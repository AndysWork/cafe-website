import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { of, Subject, throwError } from 'rxjs';
import { PhoneOrderTrackingComponent } from './phone-order-tracking.component';
import { PhoneOrderService, PhoneOrderTracking } from '../../services/phone-order.service';
import { Meta } from '@angular/platform-browser';

describe('PhoneOrderTrackingComponent', () => {
  let service: jasmine.SpyObj<PhoneOrderService>;
  let component: PhoneOrderTrackingComponent;
  const tracking: PhoneOrderTracking = {
    orderId: 'order-1', status: 'preparing', createdAt: '2026-10-25T10:00:00Z',
    updatedAt: '2026-10-25T10:05:00Z', paymentMethod: 'cod', paymentStatus: 'pending', total: 200
  };

  function setup(token: string | null = 'private-token'): void {
    service = jasmine.createSpyObj('PhoneOrderService', ['track', 'invoice']);
    service.track.and.returnValue(of(tracking));
    service.invoice.and.returnValue(of({ invoiceText: 'PHONE ORDER INVOICE\nTotal: 200' }));
    TestBed.configureTestingModule({
      providers: [
        { provide: PhoneOrderService, useValue: service },
        { provide: ActivatedRoute, useValue: {
          paramMap: of(convertToParamMap({ id: 'order-1' })),
          queryParamMap: of(convertToParamMap(token ? { token } : {}))
        } }
      ]
    });
    component = TestBed.runInInjectionContext(() => new PhoneOrderTrackingComponent());
  }

  it('loads and refreshes a public tracking link without a login', fakeAsync(() => {
    setup();
    component.ngOnInit();
    tick(0);
    expect(service.track).toHaveBeenCalledWith('order-1', 'private-token');
    expect(component.tracking?.status).toBe('preparing');
    expect(component.completed('confirmed')).toBeTrue();
    expect(component.completed('ready')).toBeFalse();
    tick(15000);
    expect(service.track.calls.count()).toBe(2);
    component.ngOnDestroy();
  }));

  it('does not request tracking without the private token', () => {
    setup(null);
    component.ngOnInit();
    expect(component.error).toContain('incomplete');
    expect(service.track).not.toHaveBeenCalled();
    component.ngOnDestroy();
  });

  it('keeps the last successful status but clearly reports a failed refresh', fakeAsync(() => {
    setup();
    component.ngOnInit();
    tick(0);
    service.track.and.returnValue(throwError(() => new HttpErrorResponse({ status: 0 })));
    tick(15000);
    expect(component.tracking?.status).toBe('preparing');
    expect(component.error).toContain('could not be refreshed');
    component.ngOnDestroy();
  }));

  it('stops requesting updates after delivery and cancellation', fakeAsync(() => {
    setup();
    service.track.and.returnValue(of({ ...tracking, status: 'delivered' }));
    component.ngOnInit();
    tick(0);
    tick(30000);
    expect(service.track.calls.count()).toBe(1);
    component.ngOnDestroy();
  }));

  it('stops polling an invalid token instead of repeatedly requesting an unavailable order', fakeAsync(() => {
    setup();
    service.track.and.returnValue(throwError(() => new HttpErrorResponse({ status: 404 })));
    component.ngOnInit();
    tick(30000);
    expect(service.track.calls.count()).toBe(1);
    expect(component.error).toContain('invalid');
    component.ngOnDestroy();
  }));

  it('uses no-referrer while the private link is open and restores the prior policy', () => {
    setup(null);
    const meta = TestBed.inject(Meta);
    const original = meta.getTag('name="referrer"')?.content;
    component.ngOnInit();
    expect(meta.getTag('name="referrer"')?.content).toBe('no-referrer');
    component.ngOnDestroy();
    expect(meta.getTag('name="referrer"')?.content).toBe(original);
  });

  it('offers the invoice after delivery and reloads payment details when reopened', fakeAsync(() => {
    setup();
    service.track.and.returnValue(of({ ...tracking, status: 'delivered' }));
    component.ngOnInit();
    tick(0);
    expect(service.invoice).not.toHaveBeenCalled();
    component.toggleInvoice();
    expect(service.invoice).toHaveBeenCalledWith('order-1', 'private-token');
    expect(component.showInvoice).toBeTrue();
    expect(component.invoiceText).toContain('Total: 200');
    component.toggleInvoice();
    expect(component.showInvoice).toBeFalse();
    service.invoice.and.returnValue(of({ invoiceText: 'Payment: paid' }));
    component.toggleInvoice();
    expect(component.invoiceText).toBe('Payment: paid');
    expect(service.invoice).toHaveBeenCalledTimes(2);
    component.ngOnDestroy();
  }));

  it('prevents duplicate invoice loads and cancels an outstanding invoice on teardown', fakeAsync(() => {
    setup();
    const response = new Subject<{ invoiceText: string }>();
    service.invoice.and.returnValue(response);
    component.ngOnInit();
    tick(0);
    component.toggleInvoice();
    component.toggleInvoice();
    expect(component.invoiceLoading).toBeTrue();
    expect(service.invoice).toHaveBeenCalledTimes(1);
    component.ngOnDestroy();
    response.next({ invoiceText: 'Late response' });
    expect(component.showInvoice).toBeFalse();
  }));

  it('shows an invoice error without removing tracking and allows retry', fakeAsync(() => {
    setup();
    service.invoice.and.returnValue(throwError(() => new HttpErrorResponse({ status: 503 })));
    component.ngOnInit();
    tick(0);
    component.toggleInvoice();
    expect(component.invoiceError).toContain('try again');
    expect(component.tracking).toEqual(tracking);
    expect(component.invoiceLoading).toBeFalse();
    expect(component.showInvoice).toBeFalse();
    service.invoice.and.returnValue(of({ invoiceText: 'Updated invoice' }));
    component.toggleInvoice();
    expect(component.invoiceError).toBe('');
    expect(component.showInvoice).toBeTrue();
    component.ngOnDestroy();
  }));

  it('does not request an invoice for an incomplete link', () => {
    setup(null);
    component.ngOnInit();
    component.toggleInvoice();
    expect(service.invoice).not.toHaveBeenCalled();
    component.ngOnDestroy();
  });

  it('renders the invoice action and displays receipt content as text, not HTML', fakeAsync(() => {
    setup();
    service.invoice.and.returnValue(of({ invoiceText: 'Invoice\nCustomer: <script>untrusted()</script>\nTotal: 200' }));
    const fixture = TestBed.createComponent(PhoneOrderTrackingComponent);
    fixture.detectChanges();
    tick(0);
    fixture.detectChanges();
    const viewButton = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    expect(viewButton.textContent).toContain('View invoice');
    viewButton.click();
    fixture.detectChanges();
    const invoice = fixture.nativeElement.querySelector('#phone-order-invoice');
    expect(invoice.textContent).toContain('<script>untrusted()</script>');
    expect(invoice.querySelector('script')).toBeNull();
    expect(viewButton.getAttribute('aria-expanded')).toBe('true');
    expect(invoice.querySelector('button').textContent).toContain('Download invoice');
    fixture.destroy();
  }));
});
