import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { DineInService } from './dine-in.service';
import { OutletService } from './outlet.service';
import { AuthService } from './auth.service';
import { UIStore } from '../store/ui.store';
import { environment } from '../../environments/environment';

describe('DineInService waiter serving', () => {
  let service: DineInService;
  let http: HttpTestingController;
  let outlets: jasmine.SpyObj<OutletService>;

  beforeEach(() => {
    localStorage.removeItem('active_dinein_table');
    outlets = jasmine.createSpyObj<OutletService>('OutletService', ['getSelectedOutletId']);
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: OutletService, useValue: outlets },
        { provide: AuthService, useValue: {} },
        { provide: UIStore, useValue: {} }
      ]
    });
    service = TestBed.inject(DineInService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends the selected outlet and item index with the served update', () => {
    outlets.getSelectedOutletId.and.returnValue('outlet-1');
    service.markDineInItemServed('order-1', 2).subscribe();
    const request = http.expectOne(`${environment.apiUrl}/kitchen/orders/order-1/items/2/status?outletId=outlet-1`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ status: 'served' });
    request.flush({ message: 'Item served', itemStatus: 'served', orderStatus: 'ready' });
  });

  it('lets the server resolve the order outlet when no outlet is selected', () => {
    outlets.getSelectedOutletId.and.returnValue(null);
    service.markDineInItemServed('order-1', 0).subscribe();
    const request = http.expectOne(`${environment.apiUrl}/kitchen/orders/order-1/items/0/status`);
    expect(request.request.body).toEqual({ status: 'served' });
    request.flush({ message: 'Item served', itemStatus: 'served', orderStatus: 'served' });
  });

  it('propagates outlet-access errors rather than reporting success', () => {
    outlets.getSelectedOutletId.and.returnValue('other-outlet');
    let failed = false;
    service.markDineInItemServed('order-1', 0).subscribe({
      next: () => fail('A rejected serving update must not report success'),
      error: error => {
        failed = true;
        expect(error.status).toBe(403);
        expect(error.error.error).toBe('User does not have access to outlet other-outlet');
      }
    });
    http.expectOne(`${environment.apiUrl}/kitchen/orders/order-1/items/0/status?outletId=other-outlet`)
      .flush({ error: 'User does not have access to outlet other-outlet' }, { status: 403, statusText: 'Forbidden' });
    expect(failed).toBeTrue();
  });
});
