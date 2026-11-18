import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router } from '@angular/router';
import { KitchenDisplayService } from './kitchen-display.service';
import { AuthService } from './auth.service';
import { OfflineQueueService } from './offline-queue.service';
import { UIStore } from '../store/ui.store';
import { authInterceptor } from '../interceptors/auth.interceptor';
import { errorInterceptor } from '../interceptors/error.interceptor';
import { environment } from '../../environments/environment';

describe('Kitchen status CSRF recovery', () => {
  let service: KitchenDisplayService;
  let http: HttpTestingController;
  let previousToken: string | null;
  let notify: jasmine.Spy;
  const url = `${environment.apiUrl}/kitchen/orders/order-1/status`;

  beforeEach(() => {
    previousToken = localStorage.getItem('csrfToken');
    localStorage.setItem('csrfToken', 'old-csrf');
    notify = jasmine.createSpy('error');
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([errorInterceptor, authInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { getToken: () => 'test-jwt' } },
        { provide: Router, useValue: {} },
        { provide: UIStore, useValue: { error: notify } },
        { provide: OfflineQueueService, useValue: {} }
      ]
    });
    service = TestBed.inject(KitchenDisplayService);
    http = TestBed.inject(HttpTestingController);
    spyOn(console, 'error');
  });

  afterEach(() => {
    http.verify();
    if (previousToken === null) localStorage.removeItem('csrfToken');
    else localStorage.setItem('csrfToken', previousToken);
  });

  it('replays an expired-token rejection once with the refreshed token and unchanged mutation', () => {
    const success = jasmine.createSpy('success');
    service.updateOrderStatus('order-1', 'preparing').subscribe(success);
    const initial = http.expectOne(url);
    expect(initial.request.headers.get('X-CSRF-Token')).toBe('old-csrf');
    initial.flush({ code: 'csrf_token_invalid', error: 'Invalid or missing CSRF token', csrfToken: 'shared-csrf' },
      { status: 403, statusText: 'Forbidden' });

    const retry = http.expectOne(url);
    expect(retry.request.method).toBe('PUT');
    expect(retry.request.body).toEqual(initial.request.body);
    expect(retry.request.headers.get('Authorization')).toBe('Bearer test-jwt');
    expect(retry.request.headers.get('X-CSRF-Token')).toBe('shared-csrf');
    expect(retry.request.headers.get('X-CSRF-Retry')).toBe('1');
    retry.flush({ message: 'Status updated' });
    expect(success).toHaveBeenCalledTimes(1);
    expect(notify).not.toHaveBeenCalled();
    expect(localStorage.getItem('csrfToken')).toBe('shared-csrf');
  });

  it('does not retry a role or assignment denial', () => {
    const error = jasmine.createSpy('error');
    service.updateOrderStatus('order-1', 'preparing').subscribe({ error });
    http.expectOne(url).flush({ error: 'Kitchen access role required' },
      { status: 403, statusText: 'Forbidden' });
    http.expectNone(url);
    expect(error).toHaveBeenCalledTimes(1);
    expect(localStorage.getItem('csrfToken')).toBe('old-csrf');
  });

  it('surfaces a rejected refresh without looping or reporting success', () => {
    const next = jasmine.createSpy('next');
    const error = jasmine.createSpy('error');
    service.updateOrderStatus('order-1', 'preparing').subscribe({ next, error });
    const challenge = { code: 'csrf_token_invalid', error: 'Invalid or missing CSRF token', csrfToken: 'shared-csrf' };
    http.expectOne(url).flush(challenge, { status: 403, statusText: 'Forbidden' });
    http.expectOne(url).flush(challenge, { status: 403, statusText: 'Forbidden' });
    http.expectNone(url);
    expect(next).not.toHaveBeenCalled();
    expect(error).toHaveBeenCalledTimes(1);
  });
});
