import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
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
    service = jasmine.createSpyObj('PhoneOrderService', ['track']);
    service.track.and.returnValue(of(tracking));
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
});
