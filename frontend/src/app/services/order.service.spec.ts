import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { AdminCreateMissedOrderRequest, OrderService } from './order.service';
import { environment } from '../../environments/environment';

describe('OrderService reconciliation routes', () => {
  let service: OrderService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
    service = TestBed.inject(OrderService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('reconciles an order through the non-reserved management route', () => {
    const payload = { reason: 'Correct recorded payment', paymentStatus: 'paid' };
    service.adminReconcileOrder('order-1', payload).subscribe();

    const req = http.expectOne(`${environment.apiUrl}/manage/orders/order-1/reconcile`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(payload);
    req.flush({ id: 'order-1' });
  });

  it('creates a missed order through the non-reserved management route', () => {
    const payload: AdminCreateMissedOrderRequest = {
      reason: 'Recover missed order',
      createdAt: '2026-11-13T12:00:00Z',
      orderType: 'pickup',
      items: []
    };
    service.adminCreateMissedOrder(payload).subscribe();

    const req = http.expectOne(`${environment.apiUrl}/manage/orders/reconcile/missed`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(payload);
    req.flush({ id: 'missed-1' });
  });
});
