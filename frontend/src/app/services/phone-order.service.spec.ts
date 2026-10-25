import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { PhoneOrderRequest, PhoneOrderService } from './phone-order.service';
import { environment } from '../../environments/environment';

describe('PhoneOrderService', () => {
  let service: PhoneOrderService;
  let http: HttpTestingController;
  const request: PhoneOrderRequest = {
    outletId: 'outlet-1', customerName: 'Customer', phoneNumber: '9876543210',
    deliveryAddress: 'Station Road, beside the bank', deliveryLocationUrl: 'https://maps.app.goo.gl/example',
    items: [{ menuItemId: 'item-1', quantity: 2, selectedAddOnNames: [] }], paymentMethod: 'cod'
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([(req, next) => next(req.clone({
          setHeaders: { Authorization: 'Bearer staff-token', 'X-Outlet-Id': req.headers.get('X-Outlet-Id') || 'interceptor-outlet' }
        }))])),
        provideHttpClientTesting()
      ]
    });
    service = TestBed.inject(PhoneOrderService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('uses the explicit outlet and idempotency key for order creation', () => {
    service.create(request, 'request-key').subscribe();
    const req = http.expectOne(`${environment.apiUrl}/phone-orders`);
    expect(req.request.method).toBe('POST');
    expect(req.request.headers.get('X-Outlet-Id')).toBe('outlet-1');
    expect(req.request.headers.get('Idempotency-Key')).toBe('request-key');
    expect(req.request.body).toEqual(request);
    req.flush({ message: 'Created', order: { id: 'order-1' } });
  });

  it('never serves the staff phone order board from the service worker cache', () => {
    service.getOrders('outlet-1').subscribe();
    const req = http.expectOne(req => req.url.endsWith('/phone-orders'));
    expect(req.request.params.get('ngsw-bypass')).toBe('true');
    expect(req.request.headers.get('X-Outlet-Id')).toBe('outlet-1');
    req.flush({ orders: [] });
  });

  it('gets an authoritative quote without creating an order', () => {
    service.quote(request).subscribe();
    const req = http.expectOne(`${environment.apiUrl}/phone-orders/quote`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(request);
    req.flush({ subtotal: 200, deliveryFee: 30, tax: 0, platformCharge: 0, total: 230 });
  });

  it('tracks without staff auth or outlet interceptors and bypasses the service worker cache', () => {
    service.track('order-1', 'private-token').subscribe();
    const req = http.expectOne(req => req.url.endsWith('/public/phone-orders/order-1/tracking'));
    expect(req.request.headers.has('Authorization')).toBeFalse();
    expect(req.request.headers.has('X-Outlet-Id')).toBeFalse();
    expect(req.request.params.get('token')).toBe('private-token');
    expect(req.request.params.get('ngsw-bypass')).toBe('true');
    req.flush({ orderId: 'order-1', status: 'preparing' });
  });
});
