import { Component, HostListener, Input, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { HttpClient, HttpParams } from '@angular/common/http';
import { OrderService, Order, OrderItem, OrderPeriodCollection } from '../../services/order.service';
import { MenuItem } from '../../services/menu.service';
import { Sales } from '../../services/sales.service';
import { Expense } from '../../services/expense.service';
import { OutletService } from '../../services/outlet.service';
import { environment } from '../../../environments/environment';
import { forkJoin, Observable, of, Subscription, timer } from 'rxjs';
import { catchError, distinctUntilChanged, filter, map } from 'rxjs/operators';
import { AuthStore } from '../../store/auth.store';
import { formatIstDate, getIstDateString, getIstInputDate, getStoredTimestampEpoch } from '../../utils/date-utils';
import { calculateDashboardInsights, calculateDashboardItemSales, calculateDashboardOverview, calculateRevenueBreakdown, DashboardDay, DashboardInsights, DashboardItemSales, DashboardOverview, DashboardRevenueBreakdown, DashboardSummary, DashboardZomatoAdjustment } from './dashboard-overview';
import { CafeTableService, CafeTableFloorView, FloorSummary } from '../../services/cafe-table.service';
import { Attendance } from '../../services/attendance.service';
import { StockAlert } from '../../services/inventory.service';
import { ManagerOpsBoardResponse, ManagerOpsService } from '../../services/manager-ops.service';
import { calculateWebSalesOverview, orderSource, WebSalesOverview } from '../admin-web-sales/web-sales-overview';
import { downloadFile, toCsv } from '../../utils/file-download';
import { Outlet } from '../../models/outlet.model';
import { OutletStore } from '../../store/outlet.store';

type ActionCategory = 'Operations' | 'Payments' | 'Inventory' | 'Staff';
interface DashboardAction {
  title: string;
  detail: string;
  owner: string;
  count: number;
  category: ActionCategory;
  severity: 'high' | 'medium' | 'low';
  icon: string;
  route: string | null;
  cta: string;
}

interface OnlineSale {
  _id?: string;
  platform: string;
  orderId: string;
  customerName?: string;
  orderAt: Date | string;
  orderedItems: Array<{ quantity: number; itemName: string }>;
  billSubTotal: number;
  packagingCharges: number;
  discountAmount: number;
  payout: number;
  platformDeduction: number;
  rating?: number;
  review?: string;
  freebies?: number;
}

interface MonthlyTrend {
  month: string;
  offlineSales: number;
  zomatoSales: number;
  swiggySales: number;
  webSales: number;
  totalSales: number;
}

interface TopCustomer {
  customerId: string;
  customerName: string;
  totalOrders: number;
  totalSpent: number;
}

interface MostOrderedItem {
  itemName: string;
  quantity: number;
  revenue: number;
}

interface DashboardAttendance {
  staffId: string;
  status: Attendance['status'];
  clockIn?: string | null;
  clockOut?: string | null;
  sessions?: { clockIn?: string | null; clockOut?: string | null }[];
}

interface DashboardReadData {
  menuItems: MenuItem[];
  onlineSales: OnlineSale[];
  offlineSales: Sales[];
  expenses: Expense[];
  attendance: DashboardAttendance[] | null;
  stockAlerts: StockAlert[] | null;
  unavailableSources: string[];
  zomatoAdjustments?: DashboardZomatoAdjustment[];
}

@Component({
  selector: 'app-admin-dashboard',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './admin-dashboard.component.html',
  styleUrls: ['./admin-dashboard.component.scss']
})
export class AdminDashboardComponent implements OnInit, OnDestroy {
  @Input() managerView = false;
  private orderService = inject(OrderService);
  private outletService = inject(OutletService);
  private http = inject(HttpClient);
  private tableService = inject(CafeTableService);
  private managerOps = inject(ManagerOpsService);
  private outletStore = inject(OutletStore);
  readonly authStore = inject(AuthStore);
  private outletSubscription?: Subscription;
  private outletListSubscription?: Subscription;
  private dataSubscription?: Subscription;
  private refreshSubscription?: Subscription;
  private collectionSubscription?: Subscription;
  private managerOutlets: Outlet[] = [];
  readonly availableOutlets$ = this.outletService.availableOutlets$.pipe(map(outlets =>
    this.managerView ? outlets.filter(outlet => this.isAssignedOutlet(outlet)) : outlets));
  periodDays = 30;
  dateRange: '1' | '7' | '30' | '90' | 'custom' = '30';
  customStartDate = '';
  customEndDate = '';
  customDateError: string | null = null;
  private customRange: { start: string; end: string } | null = null;
  showComparison = true;
  operationalErrors: string[] = [];
  collection: OrderPeriodCollection | null = null;
  collectionLoading = false;
  collectionError: string | null = null;
  operations: ManagerOpsBoardResponse | null = null;
  floor: { tables: CafeTableFloorView[]; summary: FloorSummary } | null = null;
  attendance: DashboardAttendance[] | null = null;
  stockAlerts: StockAlert[] | null = null;
  periodOrders: Order[] = [];
  appOverview: WebSalesOverview = calculateWebSalesOverview([], getIstDateString(), getIstDateString());
  itemSales: DashboardItemSales = calculateDashboardItemSales([], [], getIstDateString(), getIstDateString(), '', '', '');
  itemSearch = '';
  itemCategory = '';
  itemSource = '';
  orderSearch = '';
  orderStatus = '';
  actionCategory: 'All' | ActionCategory = 'All';
  readonly actionCategories = ['All', 'Operations', 'Payments', 'Inventory', 'Staff'] as const;
  readonly orderStatuses = ['', 'pending', 'confirmed', 'scheduled', 'preparing', 'ready', 'out-for-delivery', 'served', 'delivered', 'cancelled', 'rejected'];
  readonly itemSources = ['Dine-In', 'Takeaway', 'Website', 'Phone', 'Partner', 'Zomato', 'Swiggy', 'Other'];
  expandedPanels = new Set<string>();
  isCompact = window.innerWidth <= 767;
  itemCategoryOptions: string[] = [];

  @HostListener('window:resize')
  onResize(): void { this.isCompact = window.innerWidth <= 767; }

  @HostListener('document:visibilitychange')
  onVisibilityChange(): void {
    if (!document.hidden && !this.isLoading && !this.errorMessage) this.loadDashboardData();
  }

  dashboardRoute(workspace: 'orders' | 'tables' | 'kitchen' | 'counter'): string {
    const routes = {
      orders: this.managerView ? '/manager/operations' : '/admin/web-sales',
      tables: this.managerView ? '/staff/dine-in' : '/admin/tables',
      kitchen: this.managerView ? '/kitchen/display' : '/admin/kitchen-display',
      counter: this.managerView ? '/manager/counter-sale' : '/admin/counter-sale'
    };
    return routes[workspace];
  }

  get quickActions() {
    return this.managerView ? [
      { label: 'Create Order', icon: 'cart', route: '/manager/counter-sale' },
      { label: 'Manage Tables', icon: 'table', route: '/staff/dine-in' },
      { label: 'Assign Delivery', icon: 'delivery', route: '/manager/operations' },
      { label: 'Kitchen / KOT', icon: 'kitchen', route: '/kitchen/display' },
      { label: 'Settle Tables', icon: 'wallet', route: '/staff/cashier' },
      { label: 'Dine-In Report', icon: 'report', route: '/manager/dine-in-report' },
      { label: 'My Attendance', icon: 'staff', route: '/staff/attendance' },
      { label: 'My Payslip', icon: 'rupee', route: '/staff/payslip' }
    ] : [
      { label: 'Create Order', icon: 'cart', route: '/admin/counter-sale' },
      { label: 'Manage Tables', icon: 'table', route: '/admin/tables' },
      { label: 'Assign Delivery', icon: 'delivery', route: '/admin/web-sales' },
      { label: 'Update Inventory', icon: 'box', route: '/admin/inventory' },
      { label: 'Reconcile Payments', icon: 'wallet', route: '/admin/web-sales' },
      { label: 'Kitchen / KOT', icon: 'kitchen', route: '/admin/kitchen-display' },
      { label: 'Export Reports', icon: 'report', route: '/admin/reports' },
      { label: 'Staff Shifts', icon: 'staff', route: '/admin/attendance' }
    ];
  }

  get commandCards() {
    return [
      { label: 'Total Revenue', icon: 'rupee', tone: 'green', value: this.revenueBreakdown.total, currency: true, change: this.revenueChange, detail: 'Web Sales + Zomato net payout' },
      { label: 'Sales Records', icon: 'cart', tone: 'blue', value: this.overview.current.orders, currency: false, change: this.changeFor('orders'), detail: 'Online + offline records' },
      { label: 'Avg. Sale Value', icon: 'basket', tone: 'purple', value: this.overview.current.average, currency: true, change: this.changeFor('average'), detail: 'Per recorded sale' },
      { label: 'Gross Contribution', icon: 'chart', tone: 'green', value: null, currency: true, change: null, detail: 'Historical costs not tracked' },
      { label: 'App Discounts', icon: 'tag', tone: 'amber', value: this.appOverview.discounts, currency: true, change: null, detail: 'Coupons + loyalty discounts' },
      { label: 'App Refunds', icon: 'refund', tone: 'rose', value: this.periodRefunds, currency: true, change: null, detail: 'Refunded order value' },
      { label: 'Cancellations', icon: 'cancel', tone: 'rose', value: this.appOverview.cancelledOrders, currency: false, change: null, detail: 'App orders in this period' },
      { label: 'Paid App Orders', icon: 'wallet', tone: 'blue', value: this.appOverview.paidOrderValue, currency: true, change: null, detail: 'Excludes consolidated dine-in bills' },
      { label: 'Outstanding Dine-In', icon: 'table', tone: 'amber', value: this.tableDues, currency: true, change: null, detail: this.floor ? `${this.unpaidTables} open tables` : 'Table data unavailable' }
    ];
  }
  trendMetric: 'revenue' | 'orders' | 'average' = 'revenue';
  overview: DashboardOverview = calculateDashboardOverview([], [], 30, getIstDateString());
  zomatoAdjustments: DashboardZomatoAdjustment[] = [];
  revenueBreakdown: DashboardRevenueBreakdown = calculateRevenueBreakdown([], [], [], '', '');
  previousRevenueBreakdown: DashboardRevenueBreakdown = this.revenueBreakdown;
  get revenueChange(): number | null {
    const previous = this.previousRevenueBreakdown.total;
    return previous ? (this.revenueBreakdown.total - previous) / Math.abs(previous) * 100 : null;
  }
  insights: DashboardInsights = calculateDashboardInsights([], [], 30, getIstDateString());
  readonly overviewCards: { key: keyof DashboardSummary; label: string; icon: string; color: string; currency: boolean }[] = [
    { key: 'revenue', label: 'Total Revenue', icon: '💰', color: 'green', currency: true },
    { key: 'orders', label: 'Sales Records', icon: '🛒', color: 'blue', currency: false },
    { key: 'average', label: 'Avg. Record Value', icon: '📈', color: 'purple', currency: true },
    { key: 'customers', label: 'Online Customers', icon: '👥', color: 'orange', currency: false }
  ];

  stats = [
    { label: 'Total Menu Items', value: '0', icon: '🍽️', color: '#f38181' },
    { label: 'Online Customers', value: '0', icon: '👥', color: '#95e1d3' },
    { label: 'Avg Online Rating', value: '0', icon: '⭐', color: '#ffd93d' },
    { label: 'Total Revenue', value: '₹0', icon: '💰', color: '#4ecdc4' }
  ];



  monthlyTrends: MonthlyTrend[] = [];
  topCustomers: TopCustomer[] = [];
  onlineMostOrderedItems: MostOrderedItem[] = [];
  offlineMostOrderedItems: MostOrderedItem[] = [];
  onlineSales: OnlineSale[] = [];
  offlineSales: Sales[] = [];
  allOrders: Order[] = [];
  menuItems: MenuItem[] = [];
  expenses: Expense[] = [];

  // Online stats
  onlineStats = {
    totalOrders: 0,
    totalRevenue: 0,
    avgRating: 0,
    uniqueCustomers: 0,
    dailyAvgPayout: 0
  };

  // Offline stats
  offlineStats = {
    totalOrders: 0,
    totalRevenue: 0,
    dailyAvgSales: 0,
    dailyAvgExpenses: 0
  };

  isLoading = true;
  hasLoadedData = false;
  errorMessage: string | null = null;

  ngOnInit(): void {
    this.refreshSubscription = timer(30000, 30000).subscribe(() => {
      if (!document.hidden && !this.isLoading && !this.errorMessage) this.loadDashboardData();
    });
    if (this.managerView) {
      this.initializeManagerOutlets();
      return;
    }
    this.subscribeToOutlets();
    this.outletListSubscription = this.outletService.getActiveOutlets().subscribe();
  }

  private initializeManagerOutlets(): void {
    this.isLoading = true;
    this.errorMessage = null;
    this.outletListSubscription?.unsubscribe();
    this.outletListSubscription = this.http.get<Outlet[]>(`${environment.apiUrl}/manage/dashboard/outlets`).subscribe({
      next: outlets => {
        this.managerOutlets = outlets;
        this.outletStore.setAvailableOutlets(outlets);
        const selected = this.selectedOutlet;
        const outlet = outlets.find(item => (item._id || item.id) === (selected?._id || selected?.id)) || outlets[0];
        if (!outlet) {
          this.outletStore.clearSelectedOutlet();
          this.isLoading = false;
          this.errorMessage = 'No active outlets are assigned to your Manager account. Contact an administrator.';
          return;
        }
        this.outletService.selectOutlet(outlet);
        this.subscribeToOutlets();
      },
      error: error => {
        console.error('Unable to load Manager dashboard outlets', error);
        this.isLoading = false;
        this.errorMessage = 'Unable to load your assigned outlets. Please retry.';
      }
    });
  }

  private isAssignedOutlet(outlet: Outlet | null): boolean {
    return !!outlet && this.managerOutlets.some(item => (item._id || item.id) === (outlet._id || outlet.id));
  }

  private subscribeToOutlets(): void {
    this.outletSubscription?.unsubscribe();
    // Subscribe to outlet changes and reload data
    this.outletSubscription = this.outletService.selectedOutlet$
      .pipe(filter(outlet => outlet !== null || this.managerView),
        distinctUntilChanged((previous, current) => (previous?._id || previous?.id) === (current?._id || current?.id)))
      .subscribe(() => {
        this.hasLoadedData = false;
        this.loadDashboardData();
      });
  }

  ngOnDestroy(): void {
    this.outletSubscription?.unsubscribe();
    this.outletListSubscription?.unsubscribe();
    this.dataSubscription?.unsubscribe();
    this.refreshSubscription?.unsubscribe();
    this.collectionSubscription?.unsubscribe();
  }

  loadDashboardData(): void {
    this.dataSubscription?.unsubscribe();
    this.collectionSubscription?.unsubscribe();
    this.collection = null;
    this.collectionLoading = false;
    this.collectionError = null;
    if (this.managerView && !this.managerOutlets.length) {
      this.initializeManagerOutlets();
      return;
    }
    if (this.managerView && !this.isAssignedOutlet(this.selectedOutlet)) {
      this.hasLoadedData = false;
      this.isLoading = false;
      this.errorMessage = 'Select one of your assigned outlets to view the dashboard.';
      return;
    }
    this.isLoading = true;
    this.errorMessage = null;
    if (!this.hasLoadedData) this.operationalErrors = [];
    const loadingErrors: string[] = [];
    this.loadCollections();

    const start = this.rangeStart;
    const end = this.rangeEnd;
    const previousStart = new Date(Date.parse(`${start}T00:00:00Z`) - this.periodDays * 86400000).toISOString().slice(0, 10);
    const readData = this.http.get<DashboardReadData>(`${environment.apiUrl}/manage/dashboard/range`, {
      params: new HttpParams().set('from', previousStart).set('to', end)
    });
    this.dataSubscription = forkJoin({
      readData,
      orders: this.orderService.getDashboardOrders(start, end),
      operations: this.loadOptional(this.managerOps.getBoard(), 'Kitchen and delivery operations', loadingErrors),
      floor: this.loadOptional(this.tableService.getFloor(), 'Table occupancy and dues', loadingErrors)
    }).subscribe({
      next: (data) => {
        this.allOrders = data.orders;
        this.menuItems = data.readData.menuItems;
        this.onlineSales = data.readData.onlineSales;
        this.offlineSales = data.readData.offlineSales;
        this.zomatoAdjustments = data.readData.zomatoAdjustments || [];
        this.expenses = data.readData.expenses;
        this.operations = data.operations;
        this.floor = data.floor;
        this.attendance = data.readData.attendance;
        this.stockAlerts = data.readData.stockAlerts;
        this.operationalErrors = [...loadingErrors, ...data.readData.unavailableSources];
        this.refreshOverview();

        this.hasLoadedData = true;
        this.isLoading = false;
      },
      error: (error) => {
        console.error('Error loading dashboard data:', error);
        this.errorMessage = 'Failed to load dashboard data. Please try again.';
        this.isLoading = false;
      }
    });
  }

  setPeriod(value: string): void {
    if (value === 'custom') {
      this.dateRange = 'custom';
      this.customStartDate = this.rangeStart;
      this.customEndDate = this.rangeEnd;
      this.customDateError = null;
      return;
    }
    if (value === '1') this.periodDays = 1;
    else if (value === '7') this.periodDays = 7;
    else if (value === '30') this.periodDays = 30;
    else if (value === '90') this.periodDays = 90;
    else return;
    this.dateRange = value;
    this.customRange = null;
    this.customDateError = null;
    this.hasLoadedData = false;
    this.loadDashboardData();
  }

  applyCustomRange(): void {
    const parseDate = (value: string): number | null => {
      const epoch = Date.parse(`${value}T00:00:00Z`);
      return /^\d{4}-\d{2}-\d{2}$/.test(value) && !value.startsWith('0000')
        && Number.isFinite(epoch) && new Date(epoch).toISOString().slice(0, 10) === value ? epoch : null;
    };
    const start = parseDate(this.customStartDate);
    const end = parseDate(this.customEndDate);
    if (start === null || end === null) {
      this.customDateError = 'Choose a valid start date and end date.';
      return;
    }
    if (start > end) {
      this.customDateError = 'Start date must be on or before end date.';
      return;
    }
    const days = (end - start) / 86400000 + 1;
    if (new Date(start - days * 86400000).getUTCFullYear() < 2 || this.customEndDate === '9999-12-31') {
      this.customDateError = 'Choose dates that leave room for the previous comparison period.';
      return;
    }
    this.customRange = { start: this.customStartDate, end: this.customEndDate };
    this.periodDays = days;
    this.dateRange = 'custom';
    this.customDateError = null;
    this.hasLoadedData = false;
    this.loadDashboardData();
  }

  loadCollections(): void {
    this.collectionSubscription?.unsubscribe();
    this.collection = null;
    this.collectionError = null;
    this.collectionLoading = false;
    if (!this.selectedOutlet || (this.managerView && !this.isAssignedOutlet(this.selectedOutlet))) return;
    this.collectionLoading = true;
    this.collectionSubscription = this.orderService.getPeriodCollection(this.rangeStart, this.rangeEnd)
      .pipe(map(collection => {
        if (!collection || !['cash', 'upi', 'other', 'unallocated', 'total', 'unknownAmountOrders']
          .every(key => typeof collection[key as keyof OrderPeriodCollection] === 'number'
            && Number.isFinite(collection[key as keyof OrderPeriodCollection]))) {
          throw new Error('Invalid collection totals response');
        }
        return collection;
      })).subscribe({
        next: collection => {
          this.collection = collection;
          this.collectionLoading = false;
        },
        error: error => {
          console.error('Unable to load dashboard collections', error);
          this.collectionError = 'Cash and UPI collections are unavailable. Please retry.';
          this.collectionLoading = false;
        }
      });
  }

  setTrendMetric(value: string): void {
    if (value === 'revenue' || value === 'orders' || value === 'average') this.trendMetric = value;
  }

  refreshOverview(): void {
    const end = this.rangeEnd;
    const start = this.rangeStart;
    this.overview = calculateDashboardOverview(this.onlineSales, this.offlineSales, this.periodDays, end);
    const shift = (date: string, days: number) => new Date(Date.parse(`${date}T00:00:00Z`) + days * 86400000).toISOString().slice(0, 10);
    this.revenueBreakdown = calculateRevenueBreakdown(this.onlineSales, this.offlineSales, this.zomatoAdjustments, start, end);
    this.previousRevenueBreakdown = calculateRevenueBreakdown(
      this.onlineSales, this.offlineSales, this.zomatoAdjustments, shift(start, -this.periodDays), shift(start, -1));
    this.insights = calculateDashboardInsights(this.allOrders, this.expenses, this.periodDays, end, this.onlineSales);
    this.periodOrders = this.allOrders.filter(order => {
      const epoch = getStoredTimestampEpoch(order.createdAt, order.createdAtIsUtc);
      if (!epoch) return false;
      const date = getIstInputDate(new Date(epoch));
      return date >= start && date <= end;
    });
    this.appOverview = calculateWebSalesOverview(this.periodOrders, start, end);
    const unfilteredItems = calculateDashboardItemSales(this.periodOrders, this.menuItems, start, end, '', '', '', this.onlineSales);
    this.itemCategoryOptions = unfilteredItems.categories.map(category => category.name).sort();
    if (this.itemSearch || this.itemCategory || this.itemSource) this.refreshItemSales();
    else this.itemSales = unfilteredItems;
  }

  private loadOptional<T>(request: Observable<T>, label: string, errors: string[]): Observable<T | null> {
    return request.pipe(catchError(error => {
      console.error(`Unable to load dashboard ${label}`, error);
      errors.push(label);
      return of(null);
    }));
  }

  get selectedOutlet(): Outlet | null { return this.outletService.getSelectedOutlet(); }

  selectOutlet(id: string, outlets: Outlet[]): void {
    const outlet = outlets.find(item => (item._id || item.id) === id);
    if (outlet && (!this.managerView || this.isAssignedOutlet(outlet))) this.outletService.selectOutlet(outlet);
  }

  get rangeStart(): string {
    return this.customRange?.start
      || getIstInputDate(new Date(Date.parse(`${this.rangeEnd}T00:00:00+05:30`) - (this.periodDays - 1) * 86400000));
  }

  get rangeEnd(): string { return this.customRange?.end || getIstDateString(); }

  get rangeLabel(): string {
    if (this.customRange) return `${this.rangeStart} - ${this.rangeEnd}`;
    return this.periodDays === 1 ? 'Today' : `${this.chartDate(this.rangeStart)} - ${this.chartDate(this.rangeEnd)}`;
  }

  togglePanel(panel: string): void {
    if (this.expandedPanels.has(panel)) this.expandedPanels.delete(panel);
    else this.expandedPanels.add(panel);
  }

  sourceFor(order: Order): string { return orderSource(order); }

  statusLabel(status: string): string {
    return status ? status.replace(/-/g, ' ').replace(/\b\w/g, char => char.toUpperCase()) : 'All';
  }

  get currentKitchenOrders(): Order[] {
    return this.allOrders.filter(order => ['pending', 'confirmed', 'preparing', 'ready'].includes(order.status));
  }

  queueAge(order: Order): number | null {
    const epoch = getStoredTimestampEpoch(order.createdAt, order.createdAtIsUtc);
    return epoch ? Math.max(0, Math.floor((Date.now() - epoch) / 60000)) : null;
  }

  get kitchenBuckets(): { label: string; count: number; color: string }[] {
    const ages = this.currentKitchenOrders.map(order => this.queueAge(order)).filter((age): age is number => age !== null);
    return [
      { label: 'High wait (>30 min)', count: ages.filter(age => age > 30).length, color: '#ff5d70' },
      { label: 'Medium (20-30 min)', count: ages.filter(age => age >= 20 && age <= 30).length, color: '#f5bc16' },
      { label: 'Within 20 min', count: ages.filter(age => age < 20).length, color: '#53bd69' }
    ];
  }

  get kitchenBreaches(): number {
    return this.currentKitchenOrders.filter(order => order.status !== 'ready' && (this.queueAge(order) ?? 0) >= 20).length;
  }

  get oldestKitchenWait(): number | null {
    const ages = this.currentKitchenOrders.map(order => this.queueAge(order)).filter((age): age is number => age !== null);
    return ages.length ? Math.max(...ages) : null;
  }

  formatQueueWait(minutes: number | null): string {
    if (minutes === null) return '—';
    if (minutes < 60) return `${minutes} min`;
    if (minutes < 1440) return `${Math.floor(minutes / 60)}h ${minutes % 60}m`;
    return `${Math.floor(minutes / 1440)}d ${Math.floor(minutes % 1440 / 60)}h`;
  }

  get tableDues(): number | null {
    return this.floor ? this.floor.tables.reduce((sum, table) => sum + Math.max(0, table.amountDue), 0) : null;
  }

  get unpaidTables(): number {
    return this.floor?.tables.filter(table => table.amountDue > 0).length ?? 0;
  }

  get staffPresent(): number {
    return new Set(this.attendance?.filter(row => !!row.clockIn).map(row => row.staffId)).size;
  }

  get staffOnShift(): number {
    return new Set(this.attendance?.filter(row => row.sessions?.length
      ? row.sessions.some(session => !!session.clockIn && !session.clockOut)
      : !!row.clockIn && !row.clockOut).map(row => row.staffId)).size;
  }

  get activeDeliveryOrders(): Order[] {
    return this.allOrders.filter(order => order.orderType === 'delivery'
      && !['delivered', 'cancelled', 'rejected'].includes(order.status));
  }

  get deliveredCount(): number {
    return this.periodOrders.filter(order => order.orderType === 'delivery' && order.status === 'delivered').length;
  }

  get delayedDeliveries(): number {
    return this.activeDeliveryOrders.filter(order => order.requestedDeliveryAtUtc
      && Date.parse(order.requestedDeliveryAtUtc) < Date.now()).length;
  }

  get periodRefunds(): number {
    return this.periodOrders.filter(order => order.paymentStatus === 'refunded').reduce((sum, order) => sum + order.total, 0);
  }

  get statusBreakdown(): { status: string; count: number; color: string }[] {
    return [
      { status: 'completed', statuses: ['delivered', 'served'], color: '#49ba21' },
      { status: 'out-for-delivery', statuses: ['out-for-delivery'], color: '#39a1eb' },
      { status: 'preparing', statuses: ['preparing'], color: '#854df5' },
      { status: 'ready', statuses: ['ready'], color: '#f7be17' },
      { status: 'pending / booked', statuses: ['pending', 'confirmed', 'scheduled'], color: '#9da8b9' },
      { status: 'cancelled', statuses: ['cancelled', 'rejected'], color: '#ff5369' }
    ].map(row => ({ status: row.status, color: row.color,
      count: this.periodOrders.filter(order => row.statuses.includes(order.status)).length }));
  }

  get actions(): DashboardAction[] {
    const reconciliation = this.allOrders.filter(order => order.paymentStatus === 'pending_verification'
      && !['cancelled', 'rejected'].includes(order.status)).length;
    const stock = this.stockAlerts?.filter(alert => !alert.isResolved
      && ['LowStock', 'OutOfStock', 'ExpiredStock'].includes(alert.type)).length ?? 0;
    const inventory = stock + this.menuItems.filter(item => item.isAvailable === false).length;
    return [
      { title: 'Kitchen SLA Breach', detail: 'Orders waiting 20+ minutes in the kitchen', owner: 'Kitchen Lead',
        count: this.kitchenBreaches, category: 'Operations', severity: 'high', icon: 'kitchen', route: this.dashboardRoute('kitchen'), cta: 'View Kitchen' },
      { title: 'Unpaid / Partially Paid Tables', detail: 'Open table balances need settlement', owner: 'Floor Manager',
        count: this.unpaidTables, category: 'Payments', severity: 'medium', icon: 'table', route: '/staff/cashier', cta: 'Settle Now' },
      { title: 'Payment Reconciliation', detail: 'Orders awaiting payment verification', owner: 'Accounts',
        count: reconciliation, category: 'Payments', severity: 'medium', icon: 'wallet',
        route: this.managerView ? null : '/admin/web-sales', cta: this.managerView ? 'Admin review required' : 'Reconcile' },
      { title: 'Low Stock / Unavailable Items', detail: 'Unresolved stock alerts and unavailable menu items', owner: 'Store Manager',
        count: inventory, category: 'Inventory', severity: 'medium', icon: 'box',
        route: this.managerView ? null : stock ? '/admin/inventory' : '/admin/menu',
        cta: this.managerView ? 'Read-only · Admin review' : stock ? 'View Inventory' : 'Manage Menu' },
      { title: 'Delivery Delays', detail: 'Assigned or queued orders past their requested delivery time', owner: 'Delivery Manager',
        count: this.delayedDeliveries, category: 'Operations', severity: 'medium', icon: 'delivery', route: this.dashboardRoute('orders'), cta: 'View Deliveries' },
      { title: 'Staff / Shift Issue', detail: 'Staff marked absent or late today', owner: 'Shift Manager',
        count: this.attendance?.filter(row => ['absent', 'late'].includes(row.status)).length ?? 0,
        category: 'Staff', severity: 'low', icon: 'staff', route: this.managerView ? null : '/admin/attendance',
        cta: this.managerView ? 'Read-only · Admin review' : 'Manage Shifts' }
    ];
  }

  actionCount(category: string): number {
    return this.actions.filter(action => action.count > 0 && (category === 'All' || action.category === category)).length;
  }

  get visibleActions(): DashboardAction[] {
    return this.actions.filter(action => action.count > 0 && (this.actionCategory === 'All' || action.category === this.actionCategory));
  }

  get liveOrders(): Order[] {
    const query = this.orderSearch.trim().toLowerCase();
    return this.periodOrders.filter(order => (!this.orderStatus || order.status === this.orderStatus)
      && (!query || [order.id, order.customerName, order.username, order.tableNumber, ...order.items.map(item => item.name)]
        .some(value => value?.toLowerCase().includes(query))))
      .sort((a, b) => getStoredTimestampEpoch(b.createdAt, b.createdAtIsUtc) - getStoredTimestampEpoch(a.createdAt, a.createdAtIsUtc))
      .slice(0, 8);
  }

  orderTime(order: Order): number { return getStoredTimestampEpoch(order.createdAt, order.createdAtIsUtc); }

  statusCount(status: string): number {
    return status ? this.periodOrders.filter(order => order.status === status).length : this.periodOrders.length;
  }

  refreshItemSales(): void {
    this.itemSales = calculateDashboardItemSales(this.periodOrders, this.menuItems, this.rangeStart, this.rangeEnd,
      this.itemSearch, this.itemCategory, this.itemSource, this.onlineSales);
  }

  get topItemsByRevenue() { return [...this.itemSales.rows].sort((a, b) => b.value - a.value).slice(0, 5); }

  get itemTrendPoints(): string {
    const max = Math.max(...this.itemSales.days.map(day => day.quantity), 1);
    return this.itemSales.days.map((day, index) =>
      `${12 + index * 276 / Math.max(this.itemSales.days.length - 1, 1)},${86 - day.quantity / max * 70}`).join(' ');
  }

  exportItemSales(): void {
    const text = (value: string) => (/^[=+\-@\t\r]/.test(value) ? `'${value}` : value).replace(/"/g, '""');
    const csv = toCsv(['Item', 'Category', 'Quantity', 'Orders', 'Dine-in', 'Takeaway', 'Delivery/Other', 'Item total'],
      this.itemSales.rows.map(row => [text(row.name), text(row.category), row.quantity, row.orders, row.dineIn, row.takeaway, row.delivery, row.value]));
    downloadFile(csv, `item-sales-${this.rangeStart}-${this.rangeEnd}.csv`);
  }

  get maxHourlyOrders(): number {
    return Math.max(...this.insights.hourlyOrders.map(bucket => bucket.count), 1);
  }

  formatHour(hour: number): string {
    const format = (value: number) => `${value % 12 || 12} ${value < 12 ? 'AM' : 'PM'}`;
    return `${format(hour)}–${format((hour + 1) % 24)}`;
  }

  get expenseChange(): number | null {
    const previous = this.insights.expenses.previousTotal;
    return previous ? (this.insights.expenses.total - previous) / Math.abs(previous) * 100 : null;
  }

  get bestSalesDay(): DashboardDay | null {
    return this.overview.days.filter(day => day.orders > 0)
      .reduce<DashboardDay | null>((best, day) => !best || day.revenue > best.revenue ? day : best, null);
  }

  changeFor(key: keyof DashboardSummary): number | null {
    const previous = this.overview.previous[key];
    return previous ? (this.overview.current[key] - previous) / Math.abs(previous) * 100 : null;
  }

  trendValue(day: DashboardDay): number {
    if (this.trendMetric === 'orders') return day.orders;
    if (this.trendMetric === 'average') return day.orders ? day.revenue / day.orders : 0;
    return day.revenue;
  }

  get trendMax(): number {
    return Math.max(...this.overview.days.map(day => this.trendValue(day)), 0);
  }

  get trendMin(): number {
    return Math.min(...this.overview.days.map(day => this.trendValue(day)), 0);
  }

  chartX(index: number): number {
    return 40 + index * 530 / Math.max(this.overview.days.length - 1, 1);
  }

  chartY(day: DashboardDay): number {
    const min = this.trendMin;
    return 168 - (this.trendValue(day) - min) / (this.trendMax - min || 1) * 135;
  }

  get trendPoints(): string {
    return this.overview.days.map((day, index) => `${this.chartX(index)},${this.chartY(day)}`).join(' ');
  }

  get trendArea(): string {
    const baseline = this.chartY({ date: '', revenue: 0, orders: 0 });
    return `40,${baseline} ${this.trendPoints} 570,${baseline}`;
  }

  showDateLabel(index: number): boolean {
    const last = this.overview.days.length - 1;
    return index === 0 || index === last || (index > 0 && index < last && index % Math.ceil(last / 4) === 0);
  }

  chartDate(date: string): string {
    return formatIstDate(date, { month: 'short', day: 'numeric' });
  }

  get sourceGradient(): string {
    if (!this.overview.current.orders) return '#e8eef5';
    let position = 0;
    return `conic-gradient(${this.overview.sources.filter(source => source.count > 0).map(source => {
      const start = position;
      position += source.count / this.overview.current.orders * 100;
      return `${source.color} ${start}% ${position}%`;
    }).join(', ')})`;
  }

  itemImage(name: string): string | undefined {
    const item = this.menuItems.find(menuItem => menuItem.name?.trim().toLowerCase() === name.toLowerCase());
    return item?.imageThumbnailUrl || item?.imageUrl;
  }

  updateStats(): void {
    // Total Menu Items
    this.stats[0].value = this.menuItems.length.toString();

    // Online Stats
    this.onlineStats.totalOrders = this.onlineSales.length;
    this.onlineStats.totalRevenue = this.onlineSales.reduce((sum, s) => sum + (s.payout || 0), 0);
    const uniqueOnlineCustomers = new Set(
      this.onlineSales
        .filter(s => s.customerName)
        .map(s => s.customerName?.toLowerCase())
    ).size;
    this.onlineStats.uniqueCustomers = uniqueOnlineCustomers;
    this.stats[1].value = uniqueOnlineCustomers.toString();

    const ratingsWithValues = this.onlineSales.filter(s => s.rating && s.rating > 0);
    const avgRating = ratingsWithValues.length > 0
      ? ratingsWithValues.reduce((sum, s) => sum + (s.rating || 0), 0) / ratingsWithValues.length
      : 0;
    this.onlineStats.avgRating = avgRating;
    this.stats[2].value = avgRating > 0 ? avgRating.toFixed(1) : 'N/A';

    // Calculate daily average payout for online sales
    const uniqueOnlineDays = new Set(this.onlineSales.map(s => {
      const date = new Date(s.orderAt);
      return `${date.getFullYear()}-${date.getMonth() + 1}-${date.getDate()}`;
    })).size;
    this.onlineStats.dailyAvgPayout = uniqueOnlineDays > 0
      ? this.onlineStats.totalRevenue / uniqueOnlineDays
      : 0;

    // Offline Stats
    this.offlineStats.totalOrders = this.offlineSales.length;
    this.offlineStats.totalRevenue = this.offlineSales.reduce((sum, s) => sum + s.totalAmount, 0);

    // Calculate daily average sales
    const uniqueDays = new Set(this.offlineSales.map(s => {
      const date = new Date(s.date);
      return `${date.getFullYear()}-${date.getMonth() + 1}-${date.getDate()}`;
    })).size;
    this.offlineStats.dailyAvgSales = uniqueDays > 0
      ? this.offlineStats.totalRevenue / uniqueDays
      : 0;

    // Calculate daily average expenses
    const totalExpenses = this.expenses.reduce((sum, e) => sum + e.amount, 0);
    const uniqueExpenseDays = new Set(this.expenses.map(e => {
      const date = new Date(e.date);
      return `${date.getFullYear()}-${date.getMonth() + 1}-${date.getDate()}`;
    })).size;
    this.offlineStats.dailyAvgExpenses = uniqueExpenseDays > 0
      ? totalExpenses / uniqueExpenseDays
      : 0;

    // Total Revenue (Online + Offline). Web orders are included in online sales analytics.
    const totalRevenue = this.onlineStats.totalRevenue + this.offlineStats.totalRevenue;
    this.stats[3].value = `₹${totalRevenue.toLocaleString('en-IN', { maximumFractionDigits: 0 })}`;
  }

  calculateMonthlyTrends(): void {
    const monthlyData: { [key: string]: MonthlyTrend } = {};

    // Get last 6 months
    const now = new Date();
    for (let i = 5; i >= 0; i--) {
      const date = new Date(now.getFullYear(), now.getMonth() - i, 1);
      const monthKey = `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}`;
      const monthName = date.toLocaleDateString('en-IN', { month: 'short', year: 'numeric', timeZone: 'Asia/Kolkata' });

      monthlyData[monthKey] = {
        month: monthName,
        offlineSales: 0,
        zomatoSales: 0,
        swiggySales: 0,
        webSales: 0,
        totalSales: 0
      };
    }

    // Add offline sales
    this.offlineSales.forEach(sale => {
      const date = new Date(sale.date);
      const monthKey = `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}`;
      if (monthlyData[monthKey]) {
        monthlyData[monthKey].offlineSales += sale.totalAmount;
      }
    });

    // Add online sales
    this.onlineSales.forEach(sale => {
      const date = new Date(sale.orderAt);
      const monthKey = `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}`;
      if (monthlyData[monthKey]) {
        const payout = sale.payout || 0;
        if (sale.platform?.toLowerCase() === 'zomato') {
          monthlyData[monthKey].zomatoSales += payout;
        } else if (sale.platform?.toLowerCase() === 'swiggy') {
          monthlyData[monthKey].swiggySales += payout;
        } else if (sale.platform?.toLowerCase() === 'web sales') {
          monthlyData[monthKey].webSales += payout;
        }
      }
    });

    // Calculate totals
    Object.values(monthlyData).forEach(data => {
      data.totalSales = data.offlineSales + data.zomatoSales + data.swiggySales + data.webSales;
    });

    this.monthlyTrends = Object.values(monthlyData);
  }

  calculateTopCustomers(): void {
    const customerMap: { [key: string]: TopCustomer } = {};

    // From online sales (Zomato/Swiggy) only
    this.onlineSales.forEach(sale => {
      const customerName = sale.customerName || 'Unknown';
      const key = customerName.toLowerCase();
      if (!customerMap[key]) {
        customerMap[key] = {
          customerId: key,
          customerName: customerName,
          totalOrders: 0,
          totalSpent: 0
        };
      }
      customerMap[key].totalOrders++;
      customerMap[key].totalSpent += sale.payout || 0;
    });

    // Sort by total spent and take top 5
    this.topCustomers = Object.values(customerMap)
      .filter(c => c.customerName !== 'Unknown')
      .sort((a, b) => b.totalSpent - a.totalSpent)
      .slice(0, 5);
  }

  calculateOnlineMostOrderedItems(): void {
    const itemMap: { [key: string]: MostOrderedItem } = {};

    // From online sales (Zomato/Swiggy) only
    this.onlineSales.forEach(sale => {
      sale.orderedItems?.forEach(item => {
        const key = item.itemName;
        if (!itemMap[key]) {
          itemMap[key] = {
            itemName: item.itemName,
            quantity: 0,
            revenue: 0
          };
        }
        itemMap[key].quantity += item.quantity;
        // Calculate approximate revenue from online sales
        // Note: Individual item prices not available, so we estimate based on bill subtotal
        const itemRatio = item.quantity / sale.orderedItems.reduce((sum, i) => sum + i.quantity, 0);
        itemMap[key].revenue += (sale.billSubTotal || 0) * itemRatio;
      });
    });

    // Sort by quantity and take top 5
    this.onlineMostOrderedItems = Object.values(itemMap)
      .sort((a, b) => b.quantity - a.quantity)
      .slice(0, 5);
  }

  calculateOfflineMostOrderedItems(): void {
    const itemMap: { [key: string]: MostOrderedItem } = {};

    // From offline sales only
    this.offlineSales.forEach(sale => {
      sale.items.forEach(item => {
        const key = item.itemName;
        if (!itemMap[key]) {
          itemMap[key] = {
            itemName: item.itemName,
            quantity: 0,
            revenue: 0
          };
        }
        itemMap[key].quantity += item.quantity;
        itemMap[key].revenue += item.totalPrice;
      });
    });

    // Sort by quantity and take top 5
    this.offlineMostOrderedItems = Object.values(itemMap)
      .sort((a, b) => b.quantity - a.quantity)
      .slice(0, 5);
  }



  getMaxSales(): number {
    return Math.max(...this.monthlyTrends.map(t => t.totalSales), 1);
  }

  getBarHeight(value: number, max: number): number {
    return max > 0 ? (value / max) * 100 : 0;
  }

  getXPosition(index: number): number {
    const chartWidth = 700; // 750 - 50 margin
    const spacing = chartWidth / (this.monthlyTrends.length - 1 || 1);
    return 50 + (index * spacing);
  }

  getYPosition(value: number): number {
    const chartHeight = 250; // 300 - 50 margin
    const maxSales = this.getMaxSales();
    const ratio = maxSales > 0 ? value / maxSales : 0;
    return 300 - (ratio * chartHeight);
  }

  getLinePoints(type: 'offline' | 'zomato' | 'swiggy' | 'web' | 'total'): string {
    return this.monthlyTrends
      .map((trend, index) => {
        const x = this.getXPosition(index);
        let y: number;
        switch (type) {
          case 'offline':
            y = this.getYPosition(trend.offlineSales);
            break;
          case 'zomato':
            y = this.getYPosition(trend.zomatoSales);
            break;
          case 'swiggy':
            y = this.getYPosition(trend.swiggySales);
            break;
          case 'web':
            y = this.getYPosition(trend.webSales);
            break;
          case 'total':
            y = this.getYPosition(trend.totalSales);
            break;
        }
        return `${x},${y}`;
      })
      .join(' ');
  }

  trackByIndex(index: number): number { return index; }
}
