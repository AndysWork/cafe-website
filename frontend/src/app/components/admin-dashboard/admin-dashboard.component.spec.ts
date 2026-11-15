import { fakeAsync, TestBed, tick } from '@angular/core/testing';
import { HttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { BehaviorSubject, of, Subject, throwError } from 'rxjs';
import { OrderPeriodCollection, OrderService } from '../../services/order.service';
import { MenuService } from '../../services/menu.service';
import { SalesService } from '../../services/sales.service';
import { ExpenseService } from '../../services/expense.service';
import { OutletService } from '../../services/outlet.service';
import { AuthStore } from '../../store/auth.store';
import { getIstDateString, getIstInputDate } from '../../utils/date-utils';
import { Order } from '../../services/order.service';
import { AdminDashboardComponent } from './admin-dashboard.component';
import { CafeTableService } from '../../services/cafe-table.service';
import { AttendanceService } from '../../services/attendance.service';
import { InventoryService } from '../../services/inventory.service';
import { ManagerOpsService } from '../../services/manager-ops.service';
import { OutletStore } from '../../store/outlet.store';
import { Outlet } from '../../models/outlet.model';

describe('AdminDashboardComponent overview', () => {
  const collectionTotals = (cash: number, upi: number): OrderPeriodCollection => ({
    cash, upi, total: cash + upi, other: 0, unallocated: 0, unknownAmountOrders: 0, sessionMethods: {}
  });
  const outlet = (id: string): Outlet => ({
    id, outletCode: id, outletName: id, address: '', city: '', state: '', isActive: true,
    settings: {
      openingTime: '08:00', closingTime: '23:00', acceptsOnlineOrders: true,
      acceptsDineIn: true, acceptsTakeaway: true, acceptsCounterSales: true, taxPercentage: 5
    }
  });

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AdminDashboardComponent],
      providers: [
        provideRouter([]),
        { provide: OrderService, useValue: {} },
        { provide: MenuService, useValue: {} },
        { provide: SalesService, useValue: {} },
        { provide: ExpenseService, useValue: {} },
        { provide: HttpClient, useValue: {} },
        { provide: AuthStore, useValue: { userName: () => 'Admin' } },
        { provide: OutletService, useValue: { selectedOutlet$: of(null), availableOutlets$: of([]),
          getSelectedOutlet: () => null, getActiveOutlets: () => of([]) } }
      ]
    });
  });

  it('keeps expense insights below quick actions and filters them with the main date range without legacy sections', () => {
    const fixture = TestBed.createComponent(AdminDashboardComponent);
    const component = fixture.componentInstance;
    spyOn(component, 'loadDashboardData').and.callFake(() => component.refreshOverview());
    const today = getIstDateString();
    const tenDaysAgo = getIstInputDate(new Date(Date.parse(`${today}T00:00:00+05:30`) - 10 * 86400000));
    component.onlineSales = [{
      platform: 'Zomato', orderId: 'one', customerName: 'Alex', orderAt: tenDaysAgo,
      orderedItems: [{ quantity: 1, itemName: 'Tea' }], billSubTotal: 100,
      packagingCharges: 0, discountAmount: 0, payout: 100, platformDeduction: 0
    }];
    component.allOrders = [{
      id: 'one', userId: '', username: '', items: [], subtotal: 100, tax: 0, total: 100,
      status: 'delivered', paymentStatus: 'paid', paymentMethod: 'cod',
      createdAt: `${tenDaysAgo}T13:00:00+05:30`, createdAtIsUtc: true, updatedAt: ''
    } satisfies Order];
    component.expenses = [{
      id: 'expense', date: tenDaysAgo, expenseType: 'Rent', expenseSource: 'Offline', amount: 50,
      paymentMethod: 'Cash', recordedBy: '', createdAt: '', updatedAt: ''
    }];
    component.isLoading = false;
    component.refreshOverview();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.command-kpi').textContent).toContain('₹100');
    expect(fixture.nativeElement.querySelectorAll('.supporting-insights, .dashboard-details').length).toBe(0);
    const expenses = fixture.nativeElement.querySelector('.command-sidebar .quick-panel + .expenses-panel') as HTMLElement;
    expect(expenses.textContent).toContain('Rent');
    expect(expenses.querySelector('.expense-summary')?.textContent).toContain('₹50');
    expect(expenses.querySelector('a')?.getAttribute('href')).toBe('/admin/expenses');
    const comparison = fixture.nativeElement.querySelector('[aria-label="Dashboard comparison"]') as HTMLSelectElement;
    comparison.value = 'none';
    comparison.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(expenses.querySelector('.expense-comparison')).toBeNull();
    const period = fixture.nativeElement.querySelector('[aria-label="Dashboard date range"]') as HTMLSelectElement;
    period.value = '7';
    period.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(component.overview.current.orders).toBe(0);
    expect(fixture.nativeElement.querySelector('.command-kpi').textContent).toContain('₹0');
    expect(fixture.nativeElement.querySelector('.expenses-panel').textContent).toContain('No recorded expenses');
    fixture.destroy();
  });

  const sampleOrder = (overrides: Partial<Order> = {}): Order => ({
    id: 'tea-order', userId: 'customer', username: 'Customer', customerName: 'Asha',
    items: [{ menuItemId: 'tea', name: 'Tea', categoryName: 'Beverage', quantity: 2, price: 20, total: 40 }],
    subtotal: 40, tax: 0, total: 40, status: 'ready', paymentStatus: 'paid',
    paymentMethod: 'cash_at_counter', orderType: 'pickup', createdAt: new Date().toISOString(),
    createdAtIsUtc: true, updatedAt: '', ...overrides
  });

  it('initializes branch options on direct dashboard visits and forwards a branch selection', () => {
    const outlets = [{ id: 'one', outletName: 'First Outlet' }, { id: 'two', outletName: 'Second Outlet' }];
    const getActiveOutlets = jasmine.createSpy('getActiveOutlets').and.returnValue(of(outlets));
    const selectOutlet = jasmine.createSpy('selectOutlet');
    TestBed.overrideProvider(OutletService, { useValue: {
      selectedOutlet$: of(null), availableOutlets$: of(outlets),
      getSelectedOutlet: () => outlets[0], getActiveOutlets, selectOutlet
    } });
    const fixture = TestBed.createComponent(AdminDashboardComponent);
    fixture.componentInstance.isLoading = false;
    fixture.detectChanges();
    const branch = fixture.nativeElement.querySelector('[aria-label="Dashboard branch"]') as HTMLSelectElement;
    expect(getActiveOutlets).toHaveBeenCalledTimes(1);
    expect(branch.options.length).toBe(2);
    branch.value = 'two';
    branch.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(selectOutlet).toHaveBeenCalledOnceWith(outlets[1]);
    fixture.destroy();
  });

  it('filters live orders and item analytics using the actual search and status controls', () => {
    const fixture = TestBed.createComponent(AdminDashboardComponent);
    const component = fixture.componentInstance;
    component.allOrders = [sampleOrder(), sampleOrder({ id: 'other', customerName: 'Ravi', status: 'pending',
      items: [{ menuItemId: 'roll', name: 'Roll', categoryName: 'Snacks', quantity: 1, price: 80, total: 80 }] })];
    component.isLoading = false;
    component.refreshOverview();
    fixture.detectChanges();

    const search = fixture.nativeElement.querySelector('[aria-label="Search dashboard orders"]') as HTMLInputElement;
    search.value = 'asha';
    search.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(component.liveOrders.map(order => order.id)).toEqual(['tea-order']);
    const pending = Array.from(fixture.nativeElement.querySelectorAll('.order-status-tabs button') as NodeListOf<HTMLButtonElement>)
      .find(button => button.textContent?.includes('Pending'))!;
    pending.click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.live-orders-table tbody').textContent).toContain('No orders match');
    const items = fixture.nativeElement.querySelector('[aria-label="Search item sales"]') as HTMLInputElement;
    items.value = 'Tea';
    items.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(component.itemSales.quantity).toBe(2);
    expect(component.itemSales.rows.map(row => row.name)).toEqual(['Tea']);
    expect(fixture.nativeElement.querySelector('.item-sales-table tbody').textContent).not.toContain('Roll');
    fixture.destroy();
  });

  it('expands mobile operational panels and links administrative actions only to accessible workspaces', () => {
    const fixture = TestBed.createComponent(AdminDashboardComponent);
    const component = fixture.componentInstance;
    component.isLoading = false;
    component.isCompact = true;
    component.refreshOverview();
    fixture.detectChanges();
    const toggle = fixture.nativeElement.querySelector('.kitchen-panel .panel-toggle') as HTMLButtonElement;
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    toggle.click();
    fixture.detectChanges();
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(fixture.nativeElement.querySelector('.kitchen-panel').classList.contains('expanded')).toBeTrue();
    const links = Array.from(fixture.nativeElement.querySelectorAll('.command-center a') as NodeListOf<HTMLAnchorElement>);
    expect(links.some(link => link.getAttribute('href') === '/manager/operations')).toBeFalse();
    expect(links.some(link => link.textContent?.includes('Assign Delivery') && link.getAttribute('href') === '/admin/web-sales')).toBeTrue();
    fixture.destroy();
  });

  it('shows failed operational sources as unavailable while keeping loaded sales visible', () => {
    const unavailable = () => throwError(() => new Error('Unavailable for isolated test'));
    TestBed.overrideProvider(OrderService, { useValue: { getDashboardOrders: () => of([]) } });
    TestBed.overrideProvider(MenuService, { useValue: { getMenuItems: () => of([]) } });
    TestBed.overrideProvider(SalesService, { useValue: { getAllSales: () => of([]) } });
    TestBed.overrideProvider(ExpenseService, { useValue: { getAllExpenses: () => of([]) } });
    TestBed.overrideProvider(ManagerOpsService, { useValue: { getBoard: unavailable } });
    TestBed.overrideProvider(CafeTableService, { useValue: { getFloor: unavailable } });
    TestBed.overrideProvider(AttendanceService, { useValue: { getTodayAttendance: unavailable } });
    TestBed.overrideProvider(InventoryService, { useValue: { getStockAlerts: unavailable } });
    const get = jasmine.createSpy('get').and.returnValue(of({
      menuItems: [], onlineSales: [], offlineSales: [], expenses: [], attendance: null, stockAlerts: null,
      unavailableSources: ['Staff attendance', 'Stock alerts']
    }));
    TestBed.overrideProvider(HttpClient, { useValue: { get } });
    spyOn(console, 'error');
    const fixture = TestBed.createComponent(AdminDashboardComponent);
    fixture.componentInstance.loadDashboardData();
    fixture.detectChanges();
    expect(fixture.componentInstance.errorMessage).toBeNull();
    expect(fixture.componentInstance.operationalErrors.length).toBe(4);
    expect(fixture.nativeElement.querySelector('.command-notice.warning').textContent).toContain('Table occupancy and dues');
    expect(fixture.nativeElement.querySelector('.tables-panel').textContent).toContain('Table data unavailable');
    expect(fixture.componentInstance.tableDues).toBeNull();
    expect(get.calls.allArgs().map(args => args[0])).toEqual([jasmine.stringMatching('/manage/dashboard/range$')]);
    expect(console.error).toHaveBeenCalled();
    fixture.destroy();
  });

  it('accounts for every app status and shows truthful empty attendance and long queue waits', () => {
    const fixture = TestBed.createComponent(AdminDashboardComponent);
    const component = fixture.componentInstance;
    const statuses: Order['status'][] = ['pending', 'confirmed', 'scheduled', 'preparing', 'ready',
      'out-for-delivery', 'served', 'delivered', 'cancelled', 'rejected'];
    const createdAt = new Date(Date.now() - 3 * 86400000).toISOString();
    component.allOrders = statuses.map((status, index) => sampleOrder({ id: String(index), status, createdAt }));
    component.attendance = [];
    component.menuItems = [{ id: 'tea', name: 'Tea', categoryId: 'beverage', onlinePrice: 20, isAvailable: false }];
    component.stockAlerts = [];
    component.isLoading = false;
    component.refreshOverview();
    fixture.detectChanges();

    expect(component.statusBreakdown.reduce((sum, row) => sum + row.count, 0)).toBe(statuses.length);
    expect(component.statusBreakdown.find(row => row.status === 'pending / booked')?.count).toBe(3);
    expect(fixture.nativeElement.querySelector('.staff-panel .health-pill').textContent).toBe('No records');
    expect(fixture.nativeElement.querySelector('.kitchen-panel .small-metrics').textContent).toContain('3d 0h');
    expect(component.actions.find(action => action.category === 'Inventory')?.route).toBe('/admin/menu');
    fixture.destroy();
  });

  it('loads the shared Manager view only for assigned outlets without requesting Admin data or linking Admin tools', fakeAsync(() => {
    spyOnProperty(document, 'hidden', 'get').and.returnValue(false);
    const assigned = outlet('assigned');
    const other = outlet('other');
    const selected = new BehaviorSubject<Outlet | null>(other);
    const available = new BehaviorSubject<Outlet[]>([]);
    const selectOutlet = jasmine.createSpy('selectOutlet').and.callFake((value: Outlet) => selected.next(value));
    const get = jasmine.createSpy('get').and.callFake((url: string) => of(
      url.endsWith('/outlets') ? [assigned] : {
        menuItems: [{ id: 'tea', name: 'Tea', categoryId: 'beverage', onlinePrice: 20, isAvailable: false }],
        onlineSales: [], offlineSales: [], expenses: [{
          id: 'manager-expense', date: getIstDateString(), expenseType: 'Supplies', expenseSource: 'Offline',
          amount: 75, paymentMethod: 'Cash', recordedBy: '', createdAt: '', updatedAt: ''
        }], attendance: [{ staffId: 'one', status: 'late' }],
        stockAlerts: [], unavailableSources: []
      }));
    TestBed.overrideProvider(OutletService, { useValue: {
      selectedOutlet$: selected, availableOutlets$: available,
      getSelectedOutlet: () => selected.value, selectOutlet
    } });
    TestBed.overrideProvider(OutletStore, { useValue: {
      setAvailableOutlets: (outlets: Outlet[]) => available.next(outlets),
      clearSelectedOutlet: () => selected.next(null)
    } });
    TestBed.overrideProvider(HttpClient, { useValue: { get } });
    const getPeriodCollection = jasmine.createSpy('getPeriodCollection').and.returnValue(of(collectionTotals(100, 25.5)));
    TestBed.overrideProvider(OrderService, { useValue: {
      getDashboardOrders: () => of([sampleOrder({ paymentStatus: 'pending_verification' })]), getPeriodCollection
    } });
    TestBed.overrideProvider(ManagerOpsService, { useValue: { getBoard: () => of(null) } });
    TestBed.overrideProvider(CafeTableService, { useValue: { getFloor: () => of(null) } });
    const fixture = TestBed.createComponent(AdminDashboardComponent);
    fixture.componentRef.setInput('managerView', true);
    fixture.detectChanges();

    expect(selectOutlet).toHaveBeenCalledOnceWith(assigned);
    expect(fixture.nativeElement.querySelector('#command-title').textContent).toBe('Manager Dashboard');
    expect(fixture.nativeElement.querySelectorAll('.command-kpis .command-kpi').length).toBe(9);
    expect(fixture.nativeElement.querySelectorAll('.collection-card').length).toBe(2);
    expect(fixture.nativeElement.querySelector('.collection-metrics').textContent).toContain('₹100.00');
    expect(fixture.nativeElement.querySelector('.collection-metrics').textContent).toContain('₹25.50');
    expect(getPeriodCollection).toHaveBeenCalledOnceWith(fixture.componentInstance.rangeStart, getIstDateString());
    const expenses = fixture.nativeElement.querySelector('.command-sidebar .quick-panel + .expenses-panel') as HTMLElement;
    expect(expenses.textContent).toContain('Supplies');
    expect(expenses.querySelector('.expense-summary')?.textContent).toContain('₹75');
    expect(expenses.querySelector('a')?.getAttribute('href')).toBe('/finance/expenses');
    expect(fixture.nativeElement.querySelectorAll('.supporting-insights, .dashboard-details').length).toBe(0);
    expect(fixture.nativeElement.querySelectorAll('a[href^="/admin/"]').length).toBe(0);
    expect(fixture.nativeElement.querySelectorAll('.action-card[aria-disabled="true"]').length).toBe(3);
    expect(get.calls.allArgs().map(args => args[0])).toEqual([
      jasmine.stringMatching('/manage/dashboard/outlets$'), jasmine.stringMatching('/manage/dashboard/range$')
    ]);
    getPeriodCollection.and.returnValue(of(collectionTotals(25.5, 100)));
    tick(30000);
    fixture.detectChanges();
    expect(getPeriodCollection.calls.count()).toBe(2);
    expect(fixture.nativeElement.querySelector('.collection-card').textContent).toContain('₹25.50');
    selected.next(other);
    fixture.detectChanges();
    expect(get.calls.count()).toBe(3);
    expect(fixture.nativeElement.querySelector('.command-notice.error').textContent).toContain('assigned outlets');
    expect(fixture.nativeElement.querySelector('.expenses-panel')).toBeNull();
    expect(fixture.nativeElement.querySelector('.collection-panel')).toBeNull();
    expect(fixture.componentInstance.collection).toBeNull();
    fixture.destroy();
  }));

  function configureAdminCollections() {
    const selected = new BehaviorSubject<Outlet | null>(outlet('one'));
    const getPeriodCollection = jasmine.createSpy('getPeriodCollection');
    TestBed.overrideProvider(OutletService, { useValue: {
      selectedOutlet$: selected, availableOutlets$: of([]), getSelectedOutlet: () => selected.value,
      getActiveOutlets: () => of([]), selectOutlet: (value: Outlet) => selected.next(value)
    } });
    TestBed.overrideProvider(OutletStore, { useValue: {
      setAvailableOutlets: () => undefined, clearSelectedOutlet: () => selected.next(null)
    } });
    const getDashboardOrders = jasmine.createSpy('getDashboardOrders').and.returnValue(of([]));
    TestBed.overrideProvider(OrderService, { useValue: { getDashboardOrders, getPeriodCollection } });
    TestBed.overrideProvider(MenuService, { useValue: { getMenuItems: () => of([]) } });
    TestBed.overrideProvider(SalesService, { useValue: { getAllSales: () => of([]) } });
    TestBed.overrideProvider(ExpenseService, { useValue: { getAllExpenses: () => of([]) } });
    TestBed.overrideProvider(ManagerOpsService, { useValue: { getBoard: () => of(null) } });
    TestBed.overrideProvider(CafeTableService, { useValue: { getFloor: () => of(null) } });
    TestBed.overrideProvider(AttendanceService, { useValue: { getTodayAttendance: () => of([]) } });
    TestBed.overrideProvider(InventoryService, { useValue: { getStockAlerts: () => of([]) } });
    const get = jasmine.createSpy('get').and.callFake((url: string) => of(
      url.endsWith('/manage/dashboard/outlets') ? [outlet('one'), outlet('two')]
        : url.endsWith('/manage/dashboard/range') ? {
          menuItems: [], onlineSales: [], offlineSales: [], expenses: [], attendance: [], stockAlerts: [], unavailableSources: []
        } : { data: [] }));
    TestBed.overrideProvider(HttpClient, { useValue: { get } });
    return { selected, getPeriodCollection, getDashboardOrders, get };
  }

  it('keeps data visible during refresh but hides it immediately when the outlet changes', () => {
    const { selected, getPeriodCollection } = configureAdminCollections();
    getPeriodCollection.and.returnValue(of(collectionTotals(5, 10)));
    const pending = new Subject<Order[]>();
    let loads = 0;
    TestBed.overrideProvider(OrderService, { useValue: {
      getDashboardOrders: () => ++loads === 1 ? of([]) : pending,
      getPeriodCollection
    } });
    const fixture = TestBed.createComponent(AdminDashboardComponent);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.command-kpis')).not.toBeNull();
    fixture.componentInstance.operationalErrors = ['Table occupancy and dues'];
    fixture.componentInstance.loadDashboardData();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.command-kpis')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('[role="status"]').textContent).toContain('Refreshing');
    expect(fixture.nativeElement.querySelector('.command-notice.warning').textContent).toContain('Table occupancy and dues');
    selected.next(outlet('two'));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.command-kpis')).toBeNull();
    expect(fixture.nativeElement.querySelector('[role="status"]').textContent).toContain('Loading');
    fixture.destroy();
  });

  it('discards an unfinished range load when another range is selected', () => {
    const { getPeriodCollection, getDashboardOrders } = configureAdminCollections();
    getPeriodCollection.and.returnValue(of(collectionTotals(0, 0)));
    const obsolete = new Subject<Order[]>();
    const current = new Subject<Order[]>();
    getDashboardOrders.and.returnValues(of([]), obsolete, current);
    const fixture = TestBed.createComponent(AdminDashboardComponent);
    fixture.detectChanges();
    fixture.componentInstance.setPeriod('7');
    fixture.componentInstance.setPeriod('1');
    expect(obsolete.observed).toBeFalse();
    obsolete.next([sampleOrder({ id: 'obsolete' })]);
    current.next([sampleOrder({ id: 'current' })]);
    current.complete();
    fixture.detectChanges();
    expect(fixture.componentInstance.periodOrders.map(order => order.id)).toEqual(['current']);
    fixture.destroy();
  });

  for (const managerView of [false, true]) {
    it(`applies an inclusive historical custom range consistently for ${managerView ? 'Manager' : 'Admin'} and retains it on refresh`, fakeAsync(() => {
      spyOnProperty(document, 'hidden', 'get').and.returnValue(false);
      const { selected, getPeriodCollection, getDashboardOrders, get } = configureAdminCollections();
      getPeriodCollection.and.returnValue(of(collectionTotals(50, 25)));
      const fixture = TestBed.createComponent(AdminDashboardComponent);
      fixture.componentRef.setInput('managerView', managerView);
      fixture.detectChanges();
      const component = fixture.componentInstance;
      component.onlineSales = [
        ['2024-02-27T18:29:59Z', 5], ['2024-02-27T18:30:00Z', 10],
        ['2024-03-01T18:29:59Z', 20], ['2024-03-01T18:30:00Z', 999]
      ].map(([orderAt, payout]) => ({
        platform: 'Web Sales', orderId: String(orderAt), customerName: 'Customer', orderAt: String(orderAt),
        payout: Number(payout), orderedItems: [], billSubTotal: Number(payout), packagingCharges: 0,
        discountAmount: 0, platformDeduction: 0
      }));
      component.allOrders = [
        sampleOrder({ id: 'before', createdAt: '2024-02-27T18:29:59Z' }),
        sampleOrder({ id: 'start', createdAt: '2024-02-27T18:30:00Z' }),
        sampleOrder({ id: 'end', createdAt: '2024-03-01T18:29:59Z' }),
        sampleOrder({ id: 'after', createdAt: '2024-03-01T18:30:00Z' }),
        sampleOrder({ id: 'old-active', createdAt: '2023-01-01T00:00:00Z', orderType: 'delivery',
          status: 'scheduled', paymentStatus: 'pending_verification' })
      ];
      component.expenses = [
        { date: '2024-02-27', amount: 9 }, { date: '2024-02-28', amount: 30 },
        { date: '2024-03-01', amount: 60 }, { date: '2024-03-02', amount: 999 }
      ].map(expense => ({
        ...expense, id: expense.date, expenseType: 'Supplies', expenseSource: 'Offline', paymentMethod: 'Cash',
        recordedBy: '', createdAt: '', updatedAt: ''
      }));
      getDashboardOrders.and.returnValue(of(component.allOrders));
      get.and.returnValue(of({
        menuItems: component.menuItems, onlineSales: component.onlineSales, offlineSales: component.offlineSales,
        expenses: component.expenses, attendance: [], stockAlerts: [], unavailableSources: []
      }));
      const period = fixture.nativeElement.querySelector('[aria-label="Dashboard date range"]') as HTMLSelectElement;
      period.value = 'custom';
      period.dispatchEvent(new Event('change'));
      fixture.detectChanges();
      const start = fixture.nativeElement.querySelector('[aria-label="Dashboard start date"]') as HTMLInputElement;
      const end = fixture.nativeElement.querySelector('[aria-label="Dashboard end date"]') as HTMLInputElement;
      start.value = '2024-02-28';
      start.dispatchEvent(new Event('input'));
      end.value = '2024-03-01';
      end.dispatchEvent(new Event('input'));
      expect(component.rangeEnd).toBe(getIstDateString());
      expect(getPeriodCollection.calls.count()).toBe(1);
      (fixture.nativeElement.querySelector('.custom-range') as HTMLFormElement)
        .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
      fixture.detectChanges();
      expect(component.periodDays).toBe(3);
      expect(getDashboardOrders).toHaveBeenCalledWith('2024-02-28', '2024-03-01');
      const params = get.calls.mostRecent().args[1].params;
      expect(params.get('from')).toBe('2024-02-25');
      expect(params.get('to')).toBe('2024-03-01');
      expect(component.overview.current.revenue).toBe(30);
      expect(component.overview.previous.revenue).toBe(5);
      expect(component.periodOrders.map(order => order.id)).toEqual(['start', 'end']);
      expect(component.activeDeliveryOrders.map(order => order.id)).toContain('old-active');
      expect(component.actions.find(action => action.title === 'Payment Reconciliation')?.count).toBe(1);
      expect(component.appOverview.orderValue).toBe(80);
      expect(component.itemSales.quantity).toBe(4);
      expect(component.liveOrders.map(order => order.id)).toEqual(['end', 'start']);
      expect(component.insights.expenses.total).toBe(90);
      expect(component.insights.expenses.previousTotal).toBe(9);
      expect(component.insights.expenses.dailyAverage).toBe(30);
      expect(component.insights.timedOrderCount).toBe(2);
      expect(getPeriodCollection.calls.mostRecent().args).toEqual(['2024-02-28', '2024-03-01']);
      expect(fixture.nativeElement.querySelector('[aria-label="Dashboard comparison"]').textContent).toContain('Previous 3 Days');
      expect(fixture.nativeElement.querySelector('.expenses-panel').textContent).toContain('2024-02-28 - 2024-03-01');
      let filename = '';
      spyOn(URL, 'createObjectURL').and.returnValue('blob:isolated-custom-range');
      spyOn(URL, 'revokeObjectURL');
      spyOn(HTMLAnchorElement.prototype, 'click').and.callFake(function(this: HTMLAnchorElement) { filename = this.download; });
      component.exportItemSales();
      expect(filename).toBe('item-sales-2024-02-28-2024-03-01.csv');
      tick(30000);
      selected.next(outlet('two'));
      expect(getPeriodCollection.calls.count()).toBe(4);
      expect(getPeriodCollection.calls.mostRecent().args).toEqual(['2024-02-28', '2024-03-01']);
      component.setPeriod('7');
      fixture.detectChanges();
      expect(component.rangeEnd).toBe(getIstDateString());
      expect(component.periodDays).toBe(7);
      expect(fixture.nativeElement.querySelector('.custom-range')).toBeNull();
      expect(getPeriodCollection.calls.mostRecent().args).toEqual([component.rangeStart, getIstDateString()]);
      fixture.destroy();
    }));
  }

  it('accepts a single custom day and preserves the last applied range on missing, invalid or reversed dates', () => {
    const { getPeriodCollection } = configureAdminCollections();
    getPeriodCollection.and.returnValue(of(collectionTotals(10, 20)));
    const fixture = TestBed.createComponent(AdminDashboardComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;
    component.setPeriod('custom');
    component.customStartDate = component.customEndDate = '2024-02-29';
    component.applyCustomRange();
    fixture.detectChanges();
    expect(component.periodDays).toBe(1);
    expect(component.rangeLabel).toBe('2024-02-29 - 2024-02-29');
    expect(getPeriodCollection.calls.mostRecent().args).toEqual(['2024-02-29', '2024-02-29']);
    const appliedCollection = component.collection;
    for (const [start, end] of [
      ['', '2024-03-01'], ['2024-03-02', '2024-03-01'], ['2024-02-30', '2024-03-01'],
      ['2024-02-28', ''], ['invalid', '2024-03-01'], ['0002-01-01', '0002-01-01'],
      ['9999-12-31', '9999-12-31']
    ]) {
      component.customStartDate = start;
      component.customEndDate = end;
      component.applyCustomRange();
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('.custom-range [role="alert"]')).not.toBeNull();
      expect(component.rangeStart).toBe('2024-02-29');
      expect(component.rangeEnd).toBe('2024-02-29');
      expect(component.collection).toBe(appliedCollection);
      expect(getPeriodCollection.calls.count()).toBe(2);
    }
    fixture.destroy();
  });

  it('clears and reloads collections for date and outlet changes while ignoring obsolete requests', () => {
    const { selected, getPeriodCollection } = configureAdminCollections();
    const original = new Subject<OrderPeriodCollection>();
    const weekly = new Subject<OrderPeriodCollection>();
    const otherOutlet = new Subject<OrderPeriodCollection>();
    getPeriodCollection.and.returnValues(original, weekly, otherOutlet);
    const fixture = TestBed.createComponent(AdminDashboardComponent);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.collection-panel').textContent).toContain('Loading collections');
    original.next(collectionTotals(10, 20));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.collection-card').textContent).toContain('₹10.00');
    const period = fixture.nativeElement.querySelector('[aria-label="Dashboard date range"]') as HTMLSelectElement;
    period.value = '7';
    period.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(getPeriodCollection.calls.mostRecent().args).toEqual([fixture.componentInstance.rangeStart, getIstDateString()]);
    expect(fixture.nativeElement.querySelector('.collection-metrics').textContent).not.toContain('₹10.00');
    original.next(collectionTotals(999, 999));
    expect(fixture.componentInstance.collection).toBeNull();
    weekly.next(collectionTotals(5, 15));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.collection-card').textContent).toContain('₹5.00');
    selected.next(outlet('two'));
    fixture.detectChanges();
    expect(getPeriodCollection.calls.count()).toBe(3);
    expect(fixture.componentInstance.collection).toBeNull();
    weekly.next(collectionTotals(888, 888));
    expect(fixture.componentInstance.collection).toBeNull();
    otherOutlet.next(collectionTotals(0, 0));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.collection-metrics').textContent).toContain('₹0.00');
    fixture.destroy();
    expect(otherOutlet.observed).toBeFalse();
  });

  it('refreshes corrected Admin collections periodically and on visibility return', fakeAsync(() => {
    spyOnProperty(document, 'hidden', 'get').and.returnValue(false);
    const { getPeriodCollection } = configureAdminCollections();
    getPeriodCollection.and.returnValues(of(collectionTotals(100, 0)), of(collectionTotals(0, 80)), of(collectionTotals(20, 60)));
    const fixture = TestBed.createComponent(AdminDashboardComponent);
    fixture.detectChanges();
    expect(fixture.componentInstance.collection?.cash).toBe(100);
    tick(30000);
    fixture.detectChanges();
    expect(fixture.componentInstance.collection?.cash).toBe(0);
    expect(fixture.componentInstance.collection?.upi).toBe(80);
    document.dispatchEvent(new Event('visibilitychange'));
    fixture.detectChanges();
    expect(fixture.componentInstance.collection?.cash).toBe(20);
    expect(fixture.componentInstance.collection?.upi).toBe(60);
    fixture.destroy();
    tick(30000);
    expect(getPeriodCollection.calls.count()).toBe(3);
  }));

  it('reports failed or invalid collection reads without showing stale totals or fabricated zeros and can retry', () => {
    spyOn(console, 'error');
    const { getPeriodCollection } = configureAdminCollections();
    getPeriodCollection.and.returnValue(of(collectionTotals(100, 50)));
    const fixture = TestBed.createComponent(AdminDashboardComponent);
    fixture.detectChanges();
    getPeriodCollection.and.returnValue(throwError(() => new Error('Isolated collection failure')));
    fixture.componentInstance.setPeriod('1');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.collection-panel [role="alert"]').textContent).toContain('unavailable');
    expect(fixture.nativeElement.querySelector('.collection-metrics').textContent).not.toContain('₹');
    getPeriodCollection.and.returnValue(of(collectionTotals(0, Number.NaN)));
    (fixture.nativeElement.querySelector('.collection-panel [role="alert"] button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.componentInstance.collection).toBeNull();
    expect(console.error).toHaveBeenCalled();
    getPeriodCollection.and.returnValue(of(collectionTotals(15, 25)));
    (fixture.nativeElement.querySelector('.collection-panel [role="alert"] button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.collection-panel [role="alert"]')).toBeNull();
    expect(fixture.nativeElement.querySelector('.collection-card').textContent).toContain('₹15.00');
    fixture.destroy();
  });

  it('shows missing Manager outlet assignments instead of loading unscoped dashboard data', () => {
    const get = jasmine.createSpy('get').and.returnValue(of([]));
    TestBed.overrideProvider(HttpClient, { useValue: { get } });
    TestBed.overrideProvider(OutletStore, { useValue: {
      setAvailableOutlets: () => undefined, clearSelectedOutlet: jasmine.createSpy('clearSelectedOutlet')
    } });
    const fixture = TestBed.createComponent(AdminDashboardComponent);
    fixture.componentRef.setInput('managerView', true);
    fixture.detectChanges();
    expect(get).toHaveBeenCalledTimes(1);
    expect(fixture.nativeElement.querySelector('.command-notice.error').textContent).toContain('No active outlets');
    expect(fixture.nativeElement.querySelector('.command-kpis')).toBeNull();
    fixture.destroy();
  });
});
