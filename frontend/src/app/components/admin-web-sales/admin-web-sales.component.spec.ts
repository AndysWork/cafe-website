import { TestBed } from '@angular/core/testing';
import { HttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { of, Subject, throwError } from 'rxjs';
import { AdminWebSalesComponent } from './admin-web-sales.component';
import { Order, OrderService } from '../../services/order.service';
import { DeliveryPartnerService } from '../../services/delivery-partner.service';
import { DineInService } from '../../services/dine-in.service';
import { MenuItem, MenuService } from '../../services/menu.service';
import { OutletService } from '../../services/outlet.service';
import { UIStore } from '../../store/ui.store';

describe('AdminWebSalesComponent reconciliation item picker', () => {
  let component: AdminWebSalesComponent;
  let orderService: jasmine.SpyObj<OrderService>;
  let http: jasmine.SpyObj<HttpClient>;
  let dineIn: jasmine.SpyObj<DineInService>;
  const menuItem: MenuItem = {
    id: 'menu-1',
    name: 'Coffee',
    categoryId: 'drinks',
    categoryName: 'Drinks',
    onlinePrice: 100,
    shopSellingPrice: 80,
    variants: [{ variantName: 'Large', price: 90 }]
  };
  const order: Order = {
    id: 'order-1', userId: 'user-1', username: 'guest',
    items: [{ menuItemId: 'old', name: 'Tea', quantity: 2, price: 50, total: 100 }],
    subtotal: 100, tax: 0, total: 100, status: 'delivered',
    paymentStatus: 'paid', paymentMethod: 'cash_at_counter',
    createdAt: '2026-11-08T12:00:00Z', updatedAt: '2026-11-08T12:00:00Z'
  };
  const collection = { upi: 80, cash: 30, other: 0, unallocated: 0,
    total: 110, unknownAmountOrders: 0, sessionMethods: { session: ['upi-qr', 'cash_at_counter'] },
    sessionPayments: { session: { sessionId: 'session', tableNumber: 'T1', grandTotal: 110,
      paymentStatus: 'paid', cash: 30, upi: 80, other: 0, unallocated: 0, total: 110 } } };

  beforeEach(() => {
    orderService = jasmine.createSpyObj<OrderService>('OrderService', ['adminReconcileOrder']);
    http = jasmine.createSpyObj<HttpClient>('HttpClient', ['get']);
    dineIn = jasmine.createSpyObj<DineInService>('DineInService', ['confirmPayment']);
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: OrderService, useValue: orderService },
        { provide: HttpClient, useValue: http },
        { provide: DineInService, useValue: dineIn },
        { provide: DeliveryPartnerService, useValue: {} },
        { provide: MenuService, useValue: {} },
        { provide: OutletService, useValue: { getSelectedOutletId: () => 'outlet-1', selectedOutlet$: of(null) } },
        { provide: UIStore, useValue: { warning: jasmine.createSpy('warning'), success: jasmine.createSpy('success'), error: jasmine.createSpy('error') } }
      ]
    });
    component = TestBed.runInInjectionContext(() => new AdminWebSalesComponent(
      orderService, {} as DeliveryPartnerService, dineIn,
      {} as MenuService, http
    ));
    component.menuCatalog = [menuItem];
  });

  it('adds a selected item with the catalog price and variant to the draft', () => {
    component.openReconcileItemPicker(order, new Event('click'));
    expect(component.getReconcileDraft(order).items.length).toBe(1);
    component.selectReconcileItem(menuItem);

    const items = component.getReconcileDraft(order).items;
    expect(items.length).toBe(2);
    expect(items[1]).toEqual(jasmine.objectContaining({
      menuItemId: 'menu-1', name: 'Coffee', menuItemSearch: 'Coffee',
      quantity: 1, baseUnitPrice: 80, selectedVariantName: 'Large'
    }));
    expect(component.menuPickerOrder).toBeNull();
  });

  it('cancels without adding an empty row', () => {
    component.openReconcileItemPicker(order, new Event('click'));
    component.menuPickerSearch = 'Coffee';
    component.closeReconcileItemPicker();
    expect(component.getReconcileDraft(order).items.length).toBe(1);
    expect(component.menuPickerSearch).toBe('');
  });

  it('changes an existing row without changing its quantity', () => {
    const row = component.getReconcileDraft(order).items[0];
    component.openReconcileItemPicker(order, new Event('click'), row);
    component.selectReconcileItem(menuItem);
    expect(component.getReconcileDraft(order).items.length).toBe(1);
    expect(row.name).toBe('Coffee');
    expect(row.quantity).toBe(2);
    expect(row.baseUnitPrice).toBe(80);
  });

  it('filters by name, category and ID and supports no matches', () => {
    expect(component.getFilteredMenuCatalog('drink')).toEqual([menuItem]);
    expect(component.getFilteredMenuCatalog('menu-1')).toEqual([menuItem]);
    expect(component.getFilteredMenuCatalog('coffee')).toEqual([menuItem]);
    expect(component.getFilteredMenuCatalog('missing')).toEqual([]);
  });

  it('filters the overview by item category, source and search together', () => {
    component.startDate = '2026-11-08';
    component.endDate = '2026-11-08';
    component.orders = [
      { ...order, orderType: 'dine-in', createdAtIsUtc: true },
      { ...order, id: 'order-2', orderType: 'pickup', createdAtIsUtc: true,
        items: [{ menuItemId: 'coffee', name: 'Coffee', categoryName: 'Drinks', quantity: 1, price: 80, total: 80 }] }
    ];
    component.refreshOverview();
    component.overviewCategory = 'Drinks';
    component.overviewMode = 'Takeaway';
    component.itemSearchTerm = 'coffee';
    component.refreshOverviewItems();

    expect(component.overviewItems.map(item => item.name)).toEqual(['Coffee']);
    component.itemSearchTerm = 'tea';
    component.refreshOverviewItems();
    expect(component.overviewItems).toEqual([]);
  });

  it('loads all selected-period pages, including more than 5000 orders', async () => {
    component.startDate = '2026-11-08';
    component.endDate = '2026-11-08';
    const pages = Array.from({ length: 11 }, (_, page) =>
      of(Array.from({ length: page < 10 ? 500 : 1 }, (_, i) => ({
        ...order, id: `${page}-${i}`, createdAtIsUtc: true
      }))));
    http.get.and.returnValues(of({ success: true, data: { partners: [] } }), ...pages, of(collection));

    await component.loadDashboardData();

    expect(http.get.calls.count()).toBe(13);
    expect(component.overview.orders).toBe(5001);
    expect(http.get.calls.allArgs()[1][0]).toContain('startDate=2026-11-08');
    expect(component.dashboardAvailable).toBeTrue();
    expect(component.collection?.total).toBe(110);
  });

  it('marks the dashboard unavailable for a malformed response', async () => {
    spyOn(console, 'error');
    http.get.and.returnValue(of({ success: true }));

    await component.loadDashboardData();

    expect(component.dashboardAvailable).toBeFalse();
    expect(http.get.calls.count()).toBe(1);
    expect(TestBed.inject(UIStore).error).toHaveBeenCalled();
  });

  it('keeps orders visible when collection data fails to load', async () => {
    spyOn(console, 'error');
    component.startDate = component.endDate = '2026-11-08';
    http.get.and.returnValues(of({ success: true, data: { partners: [] } }),
      of([{ ...order, createdAtIsUtc: true }]), throwError(() => new Error('unavailable')));

    await component.loadDashboardData();

    expect(component.dashboardAvailable).toBeTrue();
    expect(component.overview.orders).toBe(1);
    expect(component.collection).toBeNull();
    expect(component.collectionLoading).toBeFalse();
    expect(TestBed.inject(UIStore).warning).toHaveBeenCalled();
  });

  it('displays UTC and legacy wall-clock order times in IST across midnight', () => {
    expect(component.formatDate('2026-11-08T18:35:00Z', true)).toBe('9 Nov 2026, 12:05 am');
    expect(component.formatDate('2026-11-09T00:05:00Z', false)).toBe('9 Nov 2026, 12:05 am');
  });

  it('ignores a late response from an earlier date range', async () => {
    const oldPage = new Subject<Order[]>();
    component.startDate = '2026-11-08';
    component.endDate = '2026-11-08';
    http.get.and.returnValues(of({ success: true, data: { partners: [] } }), oldPage,
      of({ success: true, data: { partners: [] } }),
      of([{ ...order, id: 'new', createdAt: '2026-11-09T12:00:00Z', createdAtIsUtc: true }]),
      of(collection));
    const first = component.loadDashboardData();
    await Promise.resolve();
    component.startDate = component.endDate = '2026-11-09';
    await component.loadDashboardData();
    oldPage.next([{ ...order, createdAtIsUtc: true }]);
    oldPage.complete();
    await first;

    expect(component.orders.map(item => item.id)).toEqual(['new']);
    expect(component.overview.orderValue).toBe(100);
  });

  it('excludes cancelled orders from detailed item totals', () => {
    component.orders = [order, { ...order, id: 'cancelled', status: 'cancelled' }];
    expect(component.totalItemsSold).toBe(2);
    expect(component.matchedItemsRevenue).toBe(100);
  });

  it('shows recorded payment types for standalone and split table payments', () => {
    component.collection = collection;
    expect(component.formatPaymentMethod({ ...order, paymentMethod: 'upi-qr' })).toBe('UPI');
    expect(component.formatPaymentMethod({ ...order, paymentMethod: 'cod' })).toBe('Cash / COD');
    expect(component.formatPaymentMethod({ ...order, dineInSessionId: 'session' })).toBe('UPI + Cash');
  });

  it('groups session rounds together while keeping standalone orders and separate table visits distinct', () => {
    component.collection = collection;
    component.reconciliationOrders = [
      { ...order, id: 'round-1', dineInSessionId: 'session', total: 40, tableNumber: 'T1' },
      { ...order, id: 'takeaway', orderType: 'pickup', total: 10 },
      { ...order, id: 'round-2', dineInSessionId: 'session', total: 70, tableNumber: 'T1' },
      { ...order, id: 'other-visit', dineInSessionId: 'another-session', tableNumber: 'T1' }
    ];

    const groups = component.orderSessionGroups;

    expect(groups.length).toBe(3);
    expect(groups[0].orders.map(item => item.id)).toEqual(['round-1', 'round-2']);
    expect(groups[0].orderValue).toBe(110);
    expect(groups[0].payment?.cash).toBe(30);
    expect(groups[0].payment?.upi).toBe(80);
    expect(groups[1].sessionId).toBeNull();
    expect(groups[2].payment).toBeNull();
    component.toggleSessionGroup(groups[0].key);
    expect(component.collapsedSessions.has(groups[0].key)).toBeTrue();
    component.toggleSessionGroup(groups[0].key);
    expect(component.collapsedSessions.has(groups[0].key)).toBeFalse();
  });

  it('groups merged sessions and preserves the full payment breakdown when orders are filtered', () => {
    const payment = collection.sessionPayments.session;
    component.collection = { ...collection, sessionPayments: { session: payment, old: payment } };
    component.reconciliationOrders = [
      { ...order, id: 'one', dineInSessionId: 'old', total: 40 },
      { ...order, id: 'two', dineInSessionId: 'session', total: 70, status: 'pending' }
    ];
    expect(component.orderSessionGroups.length).toBe(1);
    component.statusFilter = 'pending';
    const group = component.orderSessionGroups[0];
    expect(group.orders.map(item => item.id)).toEqual(['two']);
    expect(group.orderValue).toBe(70);
    expect(group.payment?.total).toBe(110);
    component.statusFilter = 'all';
    component.searchTerm = 'old';
    expect(component.orderSessionGroups[0].orders.map(item => item.id)).toEqual(['one']);
  });

  it('renders session payment amounts once and retains order actions when expanded', () => {
    const fixture = TestBed.createComponent(AdminWebSalesComponent);
    const view = fixture.componentInstance;
    view.dashboardAvailable = true;
    view.collection = collection;
    view.orders = view.reconciliationOrders = [
      { ...order, id: 'round-1', dineInSessionId: 'session' },
      { ...order, id: 'round-2', dineInSessionId: 'session' },
      { ...order, id: 'takeaway', orderType: 'pickup' }
    ];
    view.initializeDrafts();
    fixture.detectChanges();

    const element: HTMLElement = fixture.nativeElement;
    const header = element.querySelector<HTMLElement>('.session-group-row')!;
    expect(header.querySelector('.session-metric.cash')?.textContent).toContain('₹30.00');
    expect(header.querySelector('.session-metric.upi')?.textContent).toContain('₹80.00');
    const body = header.parentElement!;
    expect(body.querySelectorAll('tr').length).toBe(3);
    expect(body.textContent).toContain('Reconcile');
    header.querySelector('button')!.click();
    fixture.detectChanges();
    expect(body.querySelectorAll('tr').length).toBe(1);
    expect(element.querySelectorAll('.session-group-row').length).toBe(1);
    fixture.destroy();
  });

  it('preserves an existing packaging charge in reconciliation drafts', () => {
    expect(component.getReconcileDraft({ ...order, packagingCharge: 25 }).packagingCharge).toBe(25);
    expect(component.getReconcileDraft({ ...order, id: 'missing' }).packagingCharge).toBeNull();
  });

  it('uses the recorded payment method for a fully paid table order instead of dine-in tab', () => {
    component.collection = {
      ...collection,
      sessionMethods: { session: ['upi-qr'] }
    };
    const linkedOrder: Order = { ...order, dineInSessionId: 'session', paymentMethod: 'dine_in_tab' };
    expect(component.getReconcileDraft(linkedOrder).paymentMethod).toBe('upi-qr');
  });

  it('refreshes operational outstanding balances after confirming a table payment', () => {
    dineIn.confirmPayment.and.returnValue(of({ message: 'confirmed', session: {
      sessionId: 'session', outletId: 'outlet-1', outletName: 'Cafe', tableNumber: 'T1',
      status: 'paid', paymentStatus: 'paid', amountPaid: 100, amountDue: 0,
      paymentEntries: [], voidRequests: [], timeline: [], rounds: [], totalItemsCount: 1,
      subtotal: 100, discountAmount: 0, loyaltyPointsUsed: 0, loyaltyDiscountAmount: 0,
      taxAmount: 0, grandTotal: 100, createdAt: '', canAddItems: false, canRequestBill: false
    } }));
    const pending = spyOn(component, 'loadPendingDineInPayments').and.resolveTo();
    const signals = spyOn(component, 'loadDineInOperationalSignals').and.resolveTo();
    const dashboard = spyOn(component, 'loadDashboardData').and.resolveTo();
    component.confirmDineInPayment({ id: 'session', tableNumber: 'T1', roundsCount: 1,
      subtotal: 100, grandTotal: 100, createdAt: '', minutesActive: 0, status: 'active', paymentStatus: 'pending_cash' });
    expect(pending).toHaveBeenCalled();
    expect(signals).toHaveBeenCalled();
    expect(dashboard).toHaveBeenCalled();
  });

  it('applies inclusive date presets and opens the missed-order workflow', () => {
    spyOn(component, 'onDateRangeChange').and.resolveTo();
    component.setDateRange(7);
    const start = Date.parse(`${component.startDate}T00:00:00Z`);
    const end = Date.parse(`${component.endDate}T00:00:00Z`);
    expect((end - start) / 86400000).toBe(6);
    expect(component.onDateRangeChange).toHaveBeenCalledWith(true);

    const scroll = spyOn(component, 'scrollToSection');
    component.openMissedOrder();
    expect(component.showAddMissedOrderPanel).toBeTrue();
    expect(scroll).toHaveBeenCalledWith('web-order-management');
  });

  it('refreshes dine-in balances after reconciling an order', async () => {
    const dineInOrder: Order = { ...order, orderType: 'dine-in' };
    component.getReconcileDraft(dineInOrder).reason = 'Correct items';
    orderService.adminReconcileOrder.and.returnValue(of(dineInOrder));
    spyOn(component, 'loadDashboardData').and.resolveTo();
    spyOn(component, 'loadUpiReconciliationReport').and.resolveTo();
    const pending = spyOn(component, 'loadPendingDineInPayments').and.resolveTo();
    const signals = spyOn(component, 'loadDineInOperationalSignals').and.resolveTo();

    component.submitReconciliation(dineInOrder);
    await new Promise(resolve => setTimeout(resolve, 0));

    expect(orderService.adminReconcileOrder).toHaveBeenCalledWith(
      dineInOrder.id, jasmine.objectContaining({ createdAt: undefined })
    );
    expect(pending).toHaveBeenCalled();
    expect(signals).toHaveBeenCalled();
    expect(component.saving).toBeFalse();
  });

  it('submits the created time when it was intentionally changed', async () => {
    const dineInOrder: Order = { ...order, orderType: 'dine-in' };
    const draft = component.getReconcileDraft(dineInOrder);
    draft.reason = 'Correct time';
    draft.createdAt = '2026-11-09T12:30';
    orderService.adminReconcileOrder.and.returnValue(of(dineInOrder));
    spyOn(component, 'loadDashboardData').and.resolveTo();
    spyOn(component, 'loadUpiReconciliationReport').and.resolveTo();
    spyOn(component, 'loadPendingDineInPayments').and.resolveTo();
    spyOn(component, 'loadDineInOperationalSignals').and.resolveTo();

    component.submitReconciliation(dineInOrder);
    await new Promise(resolve => setTimeout(resolve, 0));

    expect(orderService.adminReconcileOrder.calls.mostRecent().args[1].createdAt).toBeDefined();
  });

  it('requests full-session payment correction for method changes and explicit historical repairs', () => {
    const linkedOrder = { ...order, dineInSessionId: 'session' };
    const draft = component.getReconcileDraft(linkedOrder);
    draft.reason = 'Correct historical collection method';
    orderService.adminReconcileOrder.and.returnValue(new Subject<Order>());

    component.submitReconciliation(linkedOrder);
    expect(orderService.adminReconcileOrder.calls.mostRecent().args[1].reconcileSessionPaymentMethod).toBeFalse();
    draft.paymentMethod = 'upi-qr';
    component.submitReconciliation(linkedOrder);
    expect(orderService.adminReconcileOrder.calls.mostRecent().args[1].reconcileSessionPaymentMethod).toBeTrue();
    draft.paymentMethod = order.paymentMethod;
    draft.reconcileSessionPaymentMethod = true;
    component.submitReconciliation(linkedOrder);
    expect(orderService.adminReconcileOrder.calls.mostRecent().args[1].reconcileSessionPaymentMethod).toBeTrue();
  });

  it('reloads corrected historical payment totals and session summaries after reconciliation', async () => {
    component.startDate = component.endDate = '2026-11-08';
    const linkedOrder = { ...order, dineInSessionId: 'session', createdAtIsUtc: true };
    component.collection = collection;
    const corrected = {
      ...collection, cash: 0, upi: 110,
      sessionMethods: { session: ['upi-qr'] },
      sessionPayments: { session: { ...collection.sessionPayments.session, cash: 0, upi: 110 } }
    };
    const draft = component.getReconcileDraft(linkedOrder);
    draft.reason = 'Correct historical Cash to UPI';
    draft.paymentMethod = 'upi-qr';
    orderService.adminReconcileOrder.and.returnValue(of({ ...linkedOrder, paymentMethod: 'upi-qr' }));
    http.get.and.returnValues(of({ success: true, data: { partners: [] } }),
      of([{ ...linkedOrder, paymentMethod: 'upi-qr' }]), of(corrected));
    spyOn(component, 'loadUpiReconciliationReport').and.resolveTo();
    spyOn(component, 'loadPendingDineInPayments').and.resolveTo();
    spyOn(component, 'loadDineInOperationalSignals').and.resolveTo();

    component.submitReconciliation(linkedOrder);
    await new Promise(resolve => setTimeout(resolve, 0));

    expect(component.collection?.cash).toBe(0);
    expect(component.collection?.upi).toBe(110);
    expect(component.collection?.total).toBe(110);
    expect(component.orderSessionGroups[0].payment?.upi).toBe(110);
    expect(component.formatPaymentMethod(linkedOrder)).toBe('UPI');
    expect(component.saving).toBeFalse();
  });
});
