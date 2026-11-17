import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { Subject, of, throwError } from 'rxjs';
import { CustomerJourneyComponent } from './customer-journey.component';
import { CustomerJourneyService } from '../../services/customer-journey.service';
import { OutletStore } from '../../store/outlet.store';
import { JourneyDashboard } from '../../models/customer-journey.model';

describe('Customer journey dashboard', () => {
  let fixture: ComponentFixture<CustomerJourneyComponent>;
  let component: CustomerJourneyComponent;
  let api: jasmine.SpyObj<CustomerJourneyService>;
  const outletId = signal<string | null>('222222222222222222222222');

  function snapshot(visitors = 0): JourneyDashboard {
    const now = new Date().toISOString();
    return {
      from: now, toExclusive: now, generatedAt: now, retainedSince: now,
      overview: {
        visitors, sessions: visitors, loggedInVisitors: 0, returningVisitors: 0, pageViews: 0,
        activeSeconds: 0, averageActiveSeconds: 0, orders: 0, cancelledOrders: 0, revenue: 0,
        conversionRate: 0, cartAbandonmentRate: 0, liveVisitors: 0, staffSessions: 0
      },
      daily: [], funnel: [], pages: [], categories: [], visitorTypes: [], devices: [],
      cartValues: [], deliveryCharges: [], paths: [], userSegments: [], menu: [],
      recentSessions: [], registeredAccountsAllOutlets: 0
    };
  }

  beforeEach(() => {
    outletId.set('222222222222222222222222');
    api = jasmine.createSpyObj<CustomerJourneyService>('CustomerJourneyService',
      ['dashboard', 'sessions', 'timeline', 'live', 'export']);
    api.dashboard.and.returnValue(of(snapshot()));
    api.sessions.and.returnValue(of({ sessions: [], total: 0, page: 1, pageSize: 25 }));
    api.live.and.returnValue(of({ generatedAt: new Date().toISOString(), sessions: [], recentEvents: [] }));
    TestBed.configureTestingModule({
      imports: [CustomerJourneyComponent],
      providers: [
        { provide: CustomerJourneyService, useValue: api },
        { provide: OutletStore, useValue: {
          selectedOutletId: outletId, selectedOutletName: () => 'Test outlet', hasOutletSelected: () => !!outletId(),
          selectedOutlet: () => ({ id: outletId(), name: 'Test outlet' })
        } }
      ]
    });
    fixture = TestBed.createComponent(CustomerJourneyComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  afterEach(() => fixture.destroy());

  it('renders all seven tabs and an honest empty state without fabricated activity', () => {
    const buttons: NodeListOf<HTMLButtonElement> = fixture.nativeElement.querySelectorAll('.tabs button');
    expect(buttons.length).toBe(7);
    expect(fixture.nativeElement.textContent).toContain('No consented customer journeys');
    for (const button of Array.from(buttons)) {
      button.click();
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('.tabs .active')).not.toBeNull();
    }
    expect(component.activeTab).toBe('reports');
  });

  it('validates retention boundaries before requesting a custom range', () => {
    api.dashboard.calls.reset();
    component.from = '2000-01-01';
    component.applyRange();
    expect(component.error).toContain('last 30 calendar days');
    expect(api.dashboard).not.toHaveBeenCalled();
  });

  it('applies inclusive custom dates consistently and clears the previous timeline', () => {
    component.from = component.today;
    component.to = component.today;
    component.selectedId = 'previous-session';
    component.applyRange();
    expect(api.dashboard).toHaveBeenCalledWith(component.today, component.today);
    expect(api.sessions).toHaveBeenCalledWith(component.today, component.today, 'customers', '', 1);
    expect(component.selectedId).toBe('');
    expect(component.timeline).toBeUndefined();
  });

  it('cancels the previous outlet request so late data cannot replace the selected outlet', () => {
    const previous = new Subject<JourneyDashboard>();
    api.dashboard.and.returnValue(previous);
    component.refresh();
    expect(previous.observed).toBeTrue();
    const current = new Subject<JourneyDashboard>();
    api.dashboard.and.returnValue(current);
    outletId.set('333333333333333333333333');
    fixture.detectChanges();
    expect(previous.observed).toBeFalse();
    current.next(snapshot(5));
    previous.next(snapshot(999));
    expect(component.data?.overview.visitors).toBe(5);
  });

  it('shows report failures instead of success-shaped empty data', () => {
    api.dashboard.and.returnValue(throwError(() => new HttpErrorResponse({
      status: 503, error: { error: 'Analytics unavailable.' }
    })));
    component.refresh();
    fixture.detectChanges();
    expect(component.data).toBeUndefined();
    expect(component.loading).toBeFalse();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain('Analytics unavailable.');
  });
});
