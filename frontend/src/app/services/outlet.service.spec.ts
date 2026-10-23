import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { environment } from '../../environments/environment';
import { OutletService } from './outlet.service';
import { OutletStore } from '../store/outlet.store';
import { Outlet } from '../models/outlet.model';

describe('OutletService ordering settings', () => {
  let service: OutletService;
  let http: HttpTestingController;
  let store: OutletStore;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
    service = TestBed.inject(OutletService);
    http = TestBed.inject(HttpTestingController);
    store = TestBed.inject(OutletStore);
  });

  afterEach(() => {
    http.verify();
    localStorage.clear();
  });

  function loadOutlet(settings: Record<string, boolean> = {}): Outlet {
    let result!: Outlet;
    service.getPublicOutlets().subscribe(outlets => result = outlets[0]);
    http.expectOne(`${environment.apiUrl}/public/outlets`).flush([{
      Id: 'outlet-1', OutletName: 'Main', Settings: settings
    }]);
    return result;
  }

  it('preserves enabled defaults for legacy outlets without the new counter setting', () => {
    const outlet = loadOutlet();
    expect(outlet.settings.acceptsOnlineOrders).toBeTrue();
    expect(outlet.settings.acceptsTakeaway).toBeTrue();
    expect(outlet.settings.acceptsDineIn).toBeTrue();
    expect(outlet.settings.acceptsCounterSales).toBeTrue();
  });

  it('preserves explicit false values in both API casing formats', () => {
    const outlet = loadOutlet({
      AcceptsOnlineOrders: false, acceptsTakeaway: false,
      AcceptsDineIn: false, acceptsCounterSales: false
    });
    expect(outlet.settings.acceptsOnlineOrders).toBeFalse();
    expect(outlet.settings.acceptsTakeaway).toBeFalse();
    expect(outlet.settings.acceptsDineIn).toBeFalse();
    expect(outlet.settings.acceptsCounterSales).toBeFalse();
  });

  it('refreshes selected outlet settings and persisted selection after a successful update', () => {
    const outlet = loadOutlet();
    store.selectOutlet(outlet);
    const settings = { ...outlet.settings, acceptsDineIn: false };
    service.updateOutlet('outlet-1', { settings }).subscribe();
    http.expectOne(`${environment.apiUrl}/outlets/outlet-1`).flush({ message: 'Updated' });
    expect(store.selectedOutlet()?.settings.acceptsDineIn).toBeFalse();
    expect(store.availableOutlets()[0].settings.acceptsDineIn).toBeFalse();
    expect(JSON.parse(localStorage.getItem('selectedOutlet')!).settings.acceptsDineIn).toBeFalse();
    expect(store.selectedOutlet()?.settings.acceptsOnlineOrders).toBeTrue();
  });

  it('does not change stored settings when an update fails', () => {
    const outlet = loadOutlet();
    store.selectOutlet(outlet);
    service.updateOutlet('outlet-1', {
      settings: { ...outlet.settings, acceptsCounterSales: false }
    }).subscribe({ error: () => {} });
    http.expectOne(`${environment.apiUrl}/outlets/outlet-1`).flush({}, {
      status: 403, statusText: 'Forbidden'
    });
    expect(store.selectedOutlet()?.settings.acceptsCounterSales).toBeTrue();
  });

  it('refreshes stale selected settings from public outlets', () => {
    store.selectOutlet(loadOutlet());
    loadOutlet({ acceptsTakeaway: false });
    expect(store.selectedOutlet()?.settings.acceptsTakeaway).toBeFalse();
  });

  it('marks a previously selected outlet unavailable when it is no longer active', () => {
    store.selectOutlet(loadOutlet());
    service.getPublicOutlets().subscribe();
    http.expectOne(`${environment.apiUrl}/public/outlets`).flush([]);
    expect(store.selectedOutlet()?.isActive).toBeFalse();
  });
});
