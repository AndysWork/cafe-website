import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TakeawayService } from './takeaway.service';
import { OutletService } from './outlet.service';
import { environment } from '../../environments/environment';

describe('TakeawayService', () => {
  let service: TakeawayService;
  let http: HttpTestingController;
  const base = `${environment.apiUrl}/takeaway/orders`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: OutletService, useValue: { getSelectedOutletId: () => 'outlet 1' } }
      ]
    });
    service = TestBed.inject(TakeawayService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('scopes the active order list to the selected outlet', () => {
    service.getActiveOrders().subscribe();
    const request = http.expectOne(`${base}?outletId=outlet%201`);
    expect(request.request.method).toBe('GET');
    request.flush({ orders: [] });
  });

  it('adds the selected outlet to new orders', () => {
    service.createOrder({ customerName: 'Asha', items: [], paymentTiming: 'handover' }).subscribe();
    const request = http.expectOne(base);
    expect(request.request.method).toBe('POST');
    expect(request.request.body.outletId).toBe('outlet 1');
    request.flush({});
  });

  it('posts payment, handover, reprint and cancel actions to the order', () => {
    service.recordPayment('o/1', 'upi-qr', 'UPI1').subscribe();
    const payment = http.expectOne(`${base}/o%2F1/payment?outletId=outlet%201`);
    expect(payment.request.body).toEqual({ paymentMethod: 'upi-qr', upiReference: 'UPI1' });
    payment.flush({});

    service.handOver('o1').subscribe();
    http.expectOne(`${base}/o1/handover?outletId=outlet%201`).flush({});

    service.reprintKot('o1').subscribe();
    http.expectOne(`${base}/o1/kot/reprint?outletId=outlet%201`).flush({});

    service.cancelOrder('o1', 'Customer left').subscribe();
    const cancel = http.expectOne(`${base}/o1/cancel?outletId=outlet%201`);
    expect(cancel.request.body).toEqual({ reason: 'Customer left' });
    cancel.flush({});
  });
});
