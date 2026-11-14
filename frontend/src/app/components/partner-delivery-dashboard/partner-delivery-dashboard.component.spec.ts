import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, Subject, throwError } from 'rxjs';
import { DeliveryPartnerService, PartnerAvailability, PartnerDashboard } from '../../services/delivery-partner.service';
import { WebPushService } from '../../services/web-push.service';
import { UIStore } from '../../store/ui.store';
import { PartnerDeliveryDashboardComponent } from './partner-delivery-dashboard.component';

describe('PartnerDeliveryDashboardComponent', () => {
  let fixture: ComponentFixture<PartnerDeliveryDashboardComponent>;
  let component: PartnerDeliveryDashboardComponent;
  let root: HTMLElement;
  let service: jasmine.SpyObj<DeliveryPartnerService>;
  let ui: jasmine.SpyObj<UIStore>;
  let data: PartnerDashboard;

  beforeEach(() => {
    data = {
      profile: { id: 'partner1', name: 'Test Partner', phone: '0000000000', vehicleType: 'scooter', status: 'available',
        availability: { isOnline: true, shopIsOpen: true, openingTime: '08:00', closingTime: '22:00',
          nextOpeningAtUtc: '2026-11-15T02:30:00Z', message: 'Online during shop hours.' } },
      activeShift: { id: 'shift1', partnerId: 'partner1', outletId: 'outlet1', shiftDate: '2025-11-09',
        startedAt: '2025-11-09T08:00:00', startOdometerKm: 100, totalDistanceKm: 24.8, status: 'active' },
      recentShifts: [], outletName: 'Test Outlet',
      activeOrders: [{ id: 'full-order-0123456789abcdef', customerName: 'Test Customer', deliveryPartnerId: 'partner1',
        status: 'out-for-delivery', total: 422, paymentMethod: 'cod', paymentStatus: 'pending', phoneNumber: '0000000000' }],
      pendingRequests: [
        { id: 'full-request-0123456789abcdef', status: 'ready', total: 359 },
        { id: 'phone-request', orderSource: 'phone', status: 'ready', total: 276 }
      ],
      activeParcelTasks: [], pendingParcelTasks: [], todayDistanceKm: 24.8, todayPayout: 568,
      codOutstanding: 422, averageRating: 4.8, reviewsCount: 32, recentReviews: [],
      todaySummary: { completed: 11, accepted: 12, inProgress: 1, cancelled: 0, legacyAssignmentDates: 0 },
      performance: { onTimeRate: 96, completionRate: 98, customerRating: 4.8, totalDistanceKm: 168,
        completed: 48, cancelled: 1, timedDeliveries: 25, onTimeDeliveries: 24, reviews: 12 },
      incentive: { targetDeliveries: 10, rewardAmount: 50, completedDeliveries: 11, earnedAmount: 50 },
      deliveryZones: [{ name: 'Central', minDistanceKm: 0, maxDistanceKm: 5 }],
      peakHours: [{ startHour: 12, endHour: 15, orders: 10 }]
    };
    service = jasmine.createSpyObj<DeliveryPartnerService>('partner', [
      'getPartnerDashboard', 'updateMyAvailability', 'acceptDeliveryOrder', 'pickupAssignedOrder', 'confirmMyCodCollection', 'markOrderDelivered'
    ]);
    service.getPartnerDashboard.and.returnValue(of(data));
    ui = jasmine.createSpyObj<UIStore>('ui', ['error', 'warning', 'success']);
    TestBed.configureTestingModule({
      imports: [PartnerDeliveryDashboardComponent],
      providers: [
        provideRouter([]),
        { provide: DeliveryPartnerService, useValue: service },
        { provide: UIStore, useValue: ui },
        { provide: WebPushService, useValue: { registerPartnerWebPush: () => Promise.resolve() } }
      ]
    });
    fixture = TestBed.createComponent(PartnerDeliveryDashboardComponent);
    component = fixture.componentInstance;
    root = fixture.nativeElement;
    fixture.detectChanges();
  });

  afterEach(() => fixture.destroy());

  it('renders real analytics and keeps milestone rewards separate from fuel payout', () => {
    expect(root.querySelector('.dashboard-title')?.textContent).toContain('Test Outlet');
    expect(root.querySelector('.active-panel')?.textContent).toContain('Test Customer');
    expect(root.querySelector('.payout strong')?.textContent).toBe('₹568');
    expect(root.querySelector('.incentive strong')?.textContent).toBe('₹50');
    expect(root.querySelector('.performance-panel')?.textContent).toContain('96%');
    expect(root.querySelector('.zones-panel')?.textContent).toContain('Central');
    expect(root.querySelector('[role="progressbar"]')?.getAttribute('aria-valuenow')).toBe('10');
    expect(root.querySelector('.progress-line small')?.textContent).toBe('11 / 10');
  });

  it('preserves COD input during background refresh without hiding the dashboard', () => {
    const id = data.activeOrders[0].id!;
    component.openCodForm(id);
    fixture.detectChanges();
    const input = root.querySelector('.cod-actions input');
    component.codAmountDraft[id] = '420';
    component.codReferenceDraft[id] = 'collection-reference';
    const refresh = new Subject<PartnerDashboard>();
    service.getPartnerDashboard.and.returnValue(refresh);
    component.loadDashboard();
    fixture.detectChanges();
    expect(component.loading).toBeFalse();
    expect(root.querySelector('.partner-shell')).not.toBeNull();
    refresh.next(structuredClone(data));
    fixture.detectChanges();
    expect(root.querySelector('.cod-actions input')).toBe(input);
    expect(component.codAmountDraft[id]).toBe('420');
    expect(component.codReferenceDraft[id]).toBe('collection-reference');
    expect(component.codOrderId).toBe(id);
  });

  it('ignores stale requests and preserves last successful data on refresh failure', () => {
    const first = new Subject<PartnerDashboard>();
    const second = new Subject<PartnerDashboard>();
    service.getPartnerDashboard.and.returnValues(first, second);
    component.loadDashboard();
    component.loadDashboard();
    second.next({ ...data, todayPayout: 600 });
    first.next({ ...data, todayPayout: 100 });
    expect(component.dashboard?.todayPayout).toBe(600);
    service.getPartnerDashboard.and.returnValue(throwError(() => new Error('offline')));
    component.loadDashboard();
    fixture.detectChanges();
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('last successful update');
    expect(root.querySelector('.payout strong')?.textContent).toBe('₹600');
  });

  it('uses full IDs when accepting and leaves phone orders admin-managed', () => {
    const request = data.pendingRequests[0];
    service.acceptDeliveryOrder.and.returnValue(of({
      message: 'Accepted', assigned: true, orderId: request.id!, partnerId: 'partner1'
    }));
    const buttons = root.querySelectorAll<HTMLButtonElement>('.accept-button');
    buttons[0].click();
    expect(service.acceptDeliveryOrder).toHaveBeenCalledOnceWith(request.id!);
    expect(buttons[1].disabled).toBeTrue();
    component.acceptRequest(data.pendingRequests[1]);
    expect(service.acceptDeliveryOrder).toHaveBeenCalledTimes(1);
    expect(ui.warning).toHaveBeenCalled();
  });

  it('shows assigned phone bookings with the requested IST time and offers pickup rather than self-acceptance', () => {
    data.pendingRequests = [{ id: 'booked-phone', orderSource: 'phone', deliveryPartnerId: 'partner1',
      requestedDeliveryAtUtc: '2100-01-01T18:45:00Z', customerName: 'Scheduled Customer', status: 'pending', total: 250 }];
    fixture.detectChanges();
    expect(root.querySelector('.available-panel')?.textContent).toContain('02 Jan 2100, 12:15 AM IST');
    expect(root.querySelector('.available-panel')?.textContent).toContain('Scheduled Customer');
    expect(root.querySelector<HTMLButtonElement>('.booked-pickup')?.disabled).toBeTrue();
    data.pendingRequests[0].status = 'ready';
    data.profile!.currentOrderId = 'other-delivery';
    fixture.detectChanges();
    expect(root.querySelector<HTMLButtonElement>('.booked-pickup')?.disabled).toBeTrue();
    data.profile!.currentOrderId = undefined;
    data.profile!.availability!.isOnline = false;
    service.pickupAssignedOrder.and.returnValue(of({ message: 'Picked up', status: 'out-for-delivery' }));
    fixture.detectChanges();
    root.querySelector<HTMLButtonElement>('.booked-pickup')!.click();
    expect(service.pickupAssignedOrder).toHaveBeenCalledOnceWith('booked-phone');
    expect(service.acceptDeliveryOrder).not.toHaveBeenCalled();
  });

  it('keeps pickup, COD collection and delivery gates intact in the status menu', () => {
    const order = data.activeOrders[0];
    root.querySelector<HTMLButtonElement>('.status-control > button')!.click();
    fixture.detectChanges();
    expect(root.querySelector('.status-menu')?.textContent).toContain('Confirm COD Payment');
    expect(root.querySelector('.status-menu')?.textContent).not.toContain('Mark Delivered');
    expect(component.canPickup(order)).toBeFalse();
    order.status = 'ready';
    expect(component.canPickup(order)).toBeTrue();
    expect(component.canConfirmCod(order)).toBeFalse();
    order.status = 'out-for-delivery';
    order.paymentStatus = 'paid';
    expect(component.canMarkDelivered(order)).toBeTrue();
    order.paymentStatus = 'pending';
    expect(component.canMarkDelivered(order)).toBeFalse();
  });

  it('is online during shop hours without a shift and removes the shift controls', () => {
    data.activeShift = null;
    fixture.detectChanges();
    expect(component.isOnline).toBeTrue();
    expect(root.querySelector('.availability-card')?.textContent).toContain('08:00 - 22:00 IST');
    expect(root.querySelector('.shift-actions')).toBeNull();
    expect(root.querySelector('nav')).toBeNull();
    expect(root.querySelector('app-notification-center')).toBeNull();
  });

  it('can pause with an assigned delivery, blocks duplicate changes and preserves delivery actions', () => {
    const update = new Subject<{ message: string; availability: PartnerAvailability }>();
    service.updateMyAvailability.and.returnValue(update);
    component.setAvailability(false);
    component.setAvailability(false);
    fixture.detectChanges();
    expect(root.querySelector<HTMLFieldSetElement>('.availability-options')?.disabled).toBeTrue();
    expect(service.updateMyAvailability).toHaveBeenCalledOnceWith(false);
    update.next({ message: 'You are Offline', availability: { ...data.profile!.availability!,
      isOnline: false, message: 'Go Online to resume.' } });
    fixture.detectChanges();
    expect(component.isOnline).toBeFalse();
    expect(component.dashboard?.activeOrders[0].id).toBe('full-order-0123456789abcdef');
    expect(root.querySelector<HTMLButtonElement>('.accept-button')?.disabled).toBeTrue();
    expect(component.canConfirmCod(data.activeOrders[0])).toBeTrue();
    expect(root.querySelector('.availability-card')?.textContent).not.toContain('shift');
  });

  it('rejects stale refreshes during a toggle and restores the selected radio on failure', () => {
    const stale = new Subject<PartnerDashboard>();
    const update = new Subject<{ message: string; availability: PartnerAvailability }>();
    service.getPartnerDashboard.and.returnValue(stale);
    component.loadDashboard();
    service.updateMyAvailability.and.returnValue(update);
    component.availabilityChoice = false;
    component.setAvailability(false);
    stale.next({ ...data, todayPayout: 1 });
    expect(component.dashboard?.todayPayout).toBe(568);
    update.error(new Error('Shop closed. Try again during shop hours.'));
    expect(component.availabilityChoice).toBeTrue();
    expect(component.isOnline).toBeTrue();
    expect(component.updatingAvailability).toBeFalse();
    expect(ui.error).toHaveBeenCalledWith('Shop closed. Try again during shop hours.');
  });

  it('does not allow after-hours Online overrides', () => {
    data.profile!.availability = { ...data.profile!.availability!, isOnline: false, shopIsOpen: false, message: 'Shop closed.' };
    component.loadDashboard();
    fixture.detectChanges();
    expect(root.querySelector<HTMLFieldSetElement>('.availability-options')?.disabled).toBeTrue();
    component.setAvailability(true);
    expect(service.updateMyAvailability).not.toHaveBeenCalled();
    expect(ui.warning).toHaveBeenCalledWith('Shop closed.');
  });

  it('exposes all requests while showing unavailable legacy API metrics', () => {
    data.incentive = undefined;
    data.performance = undefined;
    data.todaySummary = undefined;
    data.deliveryZones = undefined;
    data.peakHours = undefined;
    data.pendingRequests.push({ id: 'fourth-request', status: 'ready', total: 100 },
      { id: 'fifth-request', status: 'ready', total: 120 });
    component.infoModal = 'requests';
    fixture.detectChanges();
    expect(root.querySelector('.incentive strong')?.textContent).toBe('—');
    expect(root.querySelector('.zones-panel')?.textContent).toContain('unavailable');
    expect(root.querySelector('.performance-panel')?.textContent).not.toContain('0%');
    expect(root.querySelectorAll('.available-panel .available-order').length).toBe(3);
    expect(root.querySelectorAll('.info-modal .order-card').length).toBe(4);
    component.closeOverlays();
    expect(component.infoModal).toBeNull();
  });
});
