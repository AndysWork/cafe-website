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

  it('loads older dashboard orders beyond the first page without counting overlapping orders twice', () => {
    let ids: string[] = [];
    service.getAllOrdersWithHistory().subscribe(orders => ids = orders.map(order => order.id));
    const first = http.expectOne(req => req.url === `${environment.apiUrl}/orders` && req.params.get('page') === '1');
    expect(first.request.params.get('pageSize')).toBe('500');
    first.flush(Array.from({ length: 500 }, (_, index) => ({ id: String(index) })));
    expect(ids).toEqual([]);
    const second = http.expectOne(req => req.url === `${environment.apiUrl}/orders` && req.params.get('page') === '2');
    second.flush([{ id: '0' }, { id: 'older-order' }]);
    expect(ids.length).toBe(501);
    expect(ids).toContain('older-order');
  });

  it('fails a history read instead of returning incomplete figures when a later page fails', () => {
    spyOn(console, 'error');
    const next = jasmine.createSpy('next');
    const error = jasmine.createSpy('error');
    service.getAllOrdersWithHistory().subscribe({ next, error });
    http.expectOne(req => req.params.get('page') === '1')
      .flush(Array.from({ length: 500 }, (_, index) => ({ id: String(index) })));
    http.expectOne(req => req.params.get('page') === '2').flush({ error: 'Unavailable' }, { status: 503, statusText: 'Unavailable' });
    expect(next).not.toHaveBeenCalled();
    expect(error).toHaveBeenCalled();
  });

  it('loads all selected-range and active dashboard pages without counting overlaps twice', () => {
    let ids: string[] = [];
    service.getDashboardOrders('2024-02-28', '2024-03-01').subscribe(orders => ids = orders.map(order => order.id));
    const first = http.expectOne(req => req.url === `${environment.apiUrl}/manage/dashboard/orders` && req.params.get('page') === '1');
    expect(first.request.params.get('from')).toBe('2024-02-28');
    expect(first.request.params.get('to')).toBe('2024-03-01');
    first.flush(Array.from({ length: 500 }, (_, index) => ({ id: String(index) })));
    const second = http.expectOne(req => req.params.get('page') === '2');
    second.flush([{ id: '0' }, { id: 'old-active' }]);
    expect(ids.length).toBe(501);
    expect(ids).toContain('old-active');
  });

  it('fails dashboard reads when a later page fails', () => {
    spyOn(console, 'error');
    const next = jasmine.createSpy('next');
    const error = jasmine.createSpy('error');
    service.getDashboardOrders('2024-02-28', '2024-03-01').subscribe({ next, error });
    http.expectOne(req => req.params.get('page') === '1')
      .flush(Array.from({ length: 500 }, (_, index) => ({ id: String(index) })));
    http.expectOne(req => req.params.get('page') === '2').flush({}, { status: 503, statusText: 'Unavailable' });
    expect(next).not.toHaveBeenCalled();
    expect(error).toHaveBeenCalled();
  });

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
