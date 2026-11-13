import { Component, HostListener, OnDestroy, OnInit, inject } from '@angular/core';
import { A11yModule } from '@angular/cdk/a11y';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { RouterModule } from '@angular/router';
import { firstValueFrom, Subscription } from 'rxjs';
import { filter } from 'rxjs/operators';
import { environment } from '../../../environments/environment';
import { AdminCreateMissedOrderRequest, AdminReconcileOrderRequest, Order, OrderIssue, OrderService, UpiReconciliationReport } from '../../services/order.service';
import { DeliveryPartner, DeliveryPartnerService } from '../../services/delivery-partner.service';
import { OutletService } from '../../services/outlet.service';
import { UIStore } from '../../store/ui.store';
import { ActiveDineInTable, DineInService } from '../../services/dine-in.service';
import { MenuItem, MenuService } from '../../services/menu.service';
import { getIstDateString, getIstFileStamp, convertToIst, getIstIsoString, getIstHour, getStoredTimestampEpoch } from '../../utils/date-utils';
import { calculateWebSalesOverview, orderSource, SalesItem, SalesTrendBucket } from './web-sales-overview';

interface WebSalesDashboardResponse {
  success: boolean;
  data?: {
    partners?: DeliveryPartner[];
  };
}

interface OrderPeriodCollection {
  upi: number;
  cash: number;
  other: number;
  unallocated: number;
  total: number;
  unknownAmountOrders: number;
  sessionMethods: Record<string, string[]>;
  sessionPayments?: Record<string, SessionCollection>;
}

interface SessionCollection {
  sessionId: string;
  tableNumber: string;
  grandTotal: number;
  paymentStatus: string;
  cash: number;
  upi: number;
  other: number;
  unallocated: number;
  total: number;
}

interface OrderSessionGroup {
  key: string;
  sessionId: string | null;
  orders: Order[];
  orderValue: number;
  payment: SessionCollection | null;
}

type ComplianceLevel = 'green' | 'amber' | 'red';

interface WorkflowCompliance {
  level: ComplianceLevel;
  label: string;
  reason: string;
}

interface WebSalesItemQuantityRow {
  name: string;
  quantity: number;
  revenue: number;
  orders: number;
  dineInQuantity: number;
  takeawayQuantity: number;
  phoneOrderQuantity: number;
}

interface OrderModeSummaryRow {
  mode: string;
  orders: number;
  quantity: number;
  revenue: number;
  delivered: number;
  cancelled: number;
  inProgress: number;
  conversionRate: number;
  cancellationRate: number;
}

interface HourlyOrderTrendRow {
  hour: number;
  label: string;
  orders: number;
  quantity: number;
  revenue: number;
  ordersShare: number;
}

interface ReconcileItemDraft {
  menuItemId: string;
  menuItemSearch?: string;
  name: string;
  description?: string;
  categoryName?: string;
  selectedVariantName?: string;
  quantity: number;
  baseUnitPrice: number;
  selectedAddOns?: { name: string; price: number }[];
}

interface ReconcileOrderDraft {
  reconcileSessionPaymentMethod: boolean;
  reason: string;
  status: string;
  paymentStatus: string;
  paymentMethod: string;
  upiReference: string;
  deliveryAddress: string;
  phoneNumber: string;
  customerName: string;
  createdAt: string;
  notes: string;
  preparationNotes: string;
  tax?: number | null;
  platformCharge?: number | null;
  deliveryFee?: number | null;
  packagingCharge?: number | null;
  discountAmount?: number | null;
  loyaltyDiscountAmount?: number | null;
  manualSubtotalOverride?: number | null;
  manualTotalOverride?: number | null;
  clearDeliveryPartnerAssignment: boolean;
  clearCouponAndLoyalty: boolean;
  items: ReconcileItemDraft[];
}

interface MissedOrderDraft {
  reason: string;
  createdAt: string;
  orderType: 'delivery' | 'pickup' | 'dine-in';
  channel: 'web' | 'shop' | 'partner';
  orderSource: string;
  status: string;
  paymentStatus: string;
  paymentMethod: string;
  username: string;
  customerName: string;
  phoneNumber: string;
  deliveryAddress: string;
  upiReference: string;
  notes: string;
  preparationNotes: string;
  tax?: number | null;
  platformCharge?: number | null;
  deliveryFee?: number | null;
  packagingCharge?: number | null;
  discountAmount?: number | null;
  loyaltyDiscountAmount?: number | null;
  manualSubtotalOverride?: number | null;
  manualTotalOverride?: number | null;
  items: ReconcileItemDraft[];
}

@Component({
  selector: 'app-admin-web-sales',
  standalone: true,
  imports: [A11yModule, CommonModule, FormsModule, RouterModule],
  templateUrl: './admin-web-sales.component.html',
  styleUrls: ['./admin-web-sales.component.scss']
})
export class AdminWebSalesComponent implements OnInit, OnDestroy {
  private outletService = inject(OutletService);
  private uiStore = inject(UIStore);
  private outletSub?: Subscription;
  private dashboardRequest = 0;
  private upiRequest = 0;
  private loadSequence = 0;
  private signalsRequest = 0;
  private pendingRequest = 0;

  loading = false;
  dashboardAvailable = false;
  collection: OrderPeriodCollection | null = null;
  collectionLoading = false;
  saving = false;

  orders: Order[] = [];
  reconciliationOrders: Order[] = [];
  collapsedSessions = new Set<string>();
  partners: DeliveryPartner[] = [];
  statusFilter = 'all';
  searchTerm = '';
  itemSearchTerm = '';

  startDate = `${getIstDateString().slice(0, 7)}-01`;
  endDate = getIstDateString();
  selectedRange = 0;
  activeSection = 'overview';
  trendMetric: 'value' | 'orders' | 'average' = 'value';
  overviewCategory = 'all';
  overviewMode = 'all';
  overview = calculateWebSalesOverview([], this.startDate, this.endDate);
  overviewItems: SalesItem[] = [];

  statusDraft: Record<string, string> = {};
  partnerDraft: Record<string, string> = {};
  expandedIssueOrderId: string | null = null;
  orderIssuesMap: Record<string, OrderIssue[]> = {};
  issuesLoading: Record<string, boolean> = {};
  issueSaving: Record<string, boolean> = {};
  issueStatusDraft: Record<string, string> = {};
  issueResolutionDraft: Record<string, string> = {};
  issueRefundDraft: Record<string, boolean> = {};
  expandedReconcileOrderId: string | null = null;
  reconcilingOrderId: string | null = null;
  reconcileDraftByOrder: Record<string, ReconcileOrderDraft> = {};
  showAddMissedOrderPanel = false;
  creatingMissedOrder = false;
  missedOrderDraft: MissedOrderDraft = this.createDefaultMissedOrderDraft();
  paymentRefDraft: Record<string, string> = {};
  upiReconLoading = false;
  upiReconciliationReport: UpiReconciliationReport | null = null;
  pendingDineInPayments: ActiveDineInTable[] = [];
  confirmingDineInSessionId: string | null = null;
  dineInSlaAlerts: any[] = [];
  dayEndOpenTables: any[] = [];
  dayEndOutstandingAmount = 0;
  slaAlertsAvailable = false;
  openTablesAvailable = false;
  menuCatalog: MenuItem[] = [];
  activeMenuSuggestionKey: string | null = null;
  activeMenuSuggestionIndex = -1;
  menuPickerOrder: Order | null = null;
  menuPickerRow: ReconcileItemDraft | null = null;
  menuPickerSearch = '';
  private menuPickerTrigger: HTMLElement | null = null;

  readonly statusOptions = ['pending', 'confirmed', 'preparing', 'ready', 'out-for-delivery', 'delivered', 'cancelled'];
  readonly reconcileStatusOptions = ['scheduled', 'pending', 'confirmed', 'preparing', 'ready', 'served', 'out-for-delivery', 'delivered', 'cancelled'];
  readonly reconcilePaymentStatusOptions = ['pending', 'paid', 'refunded', 'partially_paid', 'unpaid', 'pending_verification', 'pending_cash', 'processing'];
  readonly reconcilePaymentMethodOptions = ['cod', 'upi-qr', 'dine_in_tab', 'cash_at_counter'];

  constructor(
    private orderService: OrderService,
    private deliveryPartnerService: DeliveryPartnerService,
    private dineInService: DineInService,
    private menuService: MenuService,
    private http: HttpClient
  ) {}

  ngOnInit(): void {
    this.outletSub = this.outletService.selectedOutlet$
      .pipe(filter(outlet => outlet !== null))
      .subscribe(() => this.loadData());

  }

  ngOnDestroy(): void {
    this.outletSub?.unsubscribe();
    this.dashboardRequest++;
    this.upiRequest++;
    this.loadSequence++;
    this.signalsRequest++;
    this.pendingRequest++;
  }

  scrollToOrderManagement(): void {
    const section = document.getElementById('web-order-management');
    section?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  async loadData(): Promise<void> {
    const sequence = ++this.loadSequence;
    this.loading = true;
    this.dashboardAvailable = false;
    this.collection = null;
    try {
      await this.loadMenuCatalog();
      if (sequence !== this.loadSequence) return;
      await this.loadDashboardData();
      if (sequence !== this.loadSequence) return;
      await this.loadUpiReconciliationReport();
      if (sequence !== this.loadSequence) return;
      await this.loadPendingDineInPayments();
      if (sequence !== this.loadSequence) return;
      await this.loadDineInOperationalSignals();
      if (sequence !== this.loadSequence) return;
      this.initializeDrafts();
    } finally {
      if (sequence === this.loadSequence) this.loading = false;
    }
  }

  private async loadMenuCatalog(): Promise<void> {
    return new Promise((resolve) => {
      this.menuService.getMenuItems().subscribe({
        next: items => {
          this.menuCatalog = (items || [])
            .filter(item => !!item.id && !!item.name)
            .sort((a, b) => (a.name || '').localeCompare(b.name || ''));
          resolve();
        },
        error: error => {
          console.error('Error loading menu catalog:', error);
          this.menuCatalog = [];
          this.uiStore.warning('Menu items could not be loaded. Refresh and try again.');
          resolve();
        }
      });
    });
  }

  async loadDashboardData(): Promise<void> {
    const request = ++this.dashboardRequest;
    const outletId = this.outletService.getSelectedOutletId();
    if (!outletId) {
      this.dashboardAvailable = false;
      this.uiStore.warning('Please select an outlet to load web sales dashboard');
      return;
    }
    const startDate = this.startDate;
    const endDate = this.endDate;
    const current = () => request === this.dashboardRequest
      && outletId === this.outletService.getSelectedOutletId()
      && startDate === this.startDate && endDate === this.endDate;
    this.dashboardAvailable = false;
    this.collection = null;
    this.collectionLoading = false;
    const headers = new HttpHeaders().set('X-Outlet-Id', outletId);
    const range = new URLSearchParams({ startDate, endDate });
    try {
      const response = await firstValueFrom(this.http.get<WebSalesDashboardResponse>(
        `${environment.apiUrl}/online-sales/web-dashboard?${range.toString()}`, { headers }));
      if (!response?.success || !Array.isArray(response.data?.partners)) {
        throw new Error('Invalid web sales dashboard response');
      }
      const allOrders = new Map<string, Order>();
      const pageSize = 500;
      for (let page = 1; ; page++) {
        const params = new URLSearchParams({ startDate, endDate, page: String(page), pageSize: String(pageSize) });
        const batch = await firstValueFrom(this.http.get<Order[]>(`${environment.apiUrl}/orders?${params.toString()}`, { headers }));
        if (!current()) return;
        for (const order of batch) {
          if (!allOrders.has(order.id)) allOrders.set(order.id, order);
        }
        if (batch.length < pageSize) break;
      }
      if (!current()) return;
      this.partners = response.data.partners;
      this.orders = [...allOrders.values()].sort((a, b) =>
        this.toEpochMillis(b.createdAt, b.createdAtIsUtc) - this.toEpochMillis(a.createdAt, a.createdAtIsUtc));
      this.reconciliationOrders = this.orders.filter(order => this.canReconcileOrder(order));
      this.refreshOverview();
      this.dashboardAvailable = true;
      this.collectionLoading = true;
      try {
        const collectionRange = new URLSearchParams({ from: startDate, to: endDate });
        const collection = await firstValueFrom(this.http.get<OrderPeriodCollection>(
          `${environment.apiUrl}/manage/reports/order-period-collection?${collectionRange.toString()}`, { headers }));
        if (!current()) return;
        if (!collection || !['upi', 'cash', 'other', 'unallocated', 'total', 'unknownAmountOrders']
          .every(key => typeof collection[key as keyof OrderPeriodCollection] === 'number'
            && Number.isFinite(collection[key as keyof OrderPeriodCollection]))
          || !collection.sessionMethods || typeof collection.sessionMethods !== 'object') {
          throw new Error('Invalid order-period collection response');
        }
        this.collection = collection;
      } catch (error) {
        if (!current()) return;
        console.error('Error loading collection:', error);
        this.uiStore.warning('Recorded collection is temporarily unavailable.');
      } finally {
        if (current()) this.collectionLoading = false;
      }
    } catch (error) {
      if (!current()) return;
      console.error('Error loading dashboard:', error);
      this.uiStore.error('Failed to load web sales dashboard');
    }
  }

  initializeDrafts(): void {
    this.statusDraft = {};
    this.partnerDraft = {};
    this.paymentRefDraft = {};
    for (const order of this.reconciliationOrders) {
      this.statusDraft[order.id] = order.status;
      this.partnerDraft[order.id] = '';
      this.paymentRefDraft[order.id] = '';
    }
  }

  async onDateRangeChange(preset = false): Promise<void> {
    if (!this.startDate || !this.endDate || this.startDate > this.endDate) {
      this.dashboardAvailable = false;
      this.uiStore.warning('Choose a valid date range with the start on or before the end.');
      return;
    }
    if (!preset) this.selectedRange = 0;
    await this.loadData();
  }

  setDateRange(days: number): void {
    const today = getIstDateString();
    const start = new Date(Date.parse(`${today}T00:00:00Z`) - (days - 1) * 86400000);
    this.startDate = start.toISOString().slice(0, 10);
    this.endDate = today;
    this.selectedRange = days;
    void this.onDateRangeChange(true);
  }

  scrollToSection(id: string): void {
    this.activeSection = id;
    document.getElementById(id)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  openMissedOrder(): void {
    this.showAddMissedOrderPanel = true;
    this.scrollToSection('web-order-management');
  }

  refreshOverview(): void {
    this.overview = calculateWebSalesOverview(this.orders, this.startDate, this.endDate);
    this.refreshOverviewItems();
  }

  refreshOverviewItems(): void {
    const orders = this.overviewMode === 'all'
      ? this.orders
      : this.orders.filter(order => orderSource(order) === this.overviewMode);
    const items = this.overviewMode === 'all'
      ? this.overview.items
      : calculateWebSalesOverview(orders, this.startDate, this.endDate).items;
    const query = this.itemSearchTerm.trim().toLowerCase();
    this.overviewItems = items.filter(item =>
      (this.overviewCategory === 'all' || item.category === this.overviewCategory)
      && (!query || item.name.toLowerCase().includes(query)));
  }

  get overviewCategories(): string[] {
    return [...new Set(this.overview.items.map(item => item.category))].sort();
  }

  get topRevenueOverviewItem(): SalesItem | undefined {
    return this.overview.items.reduce<SalesItem | undefined>(
      (top, item) => !top || item.value > top.value ? item : top, undefined);
  }

  get overviewPeakHour(): string {
    const hours = new Map<number, number>();
    for (const order of this.orders) {
      if (['cancelled', 'rejected'].includes((order.status || '').toLowerCase())) continue;
      const epoch = this.toEpochMillis(order.createdAt, order.createdAtIsUtc);
      if (!epoch) continue;
      if (/^\d{4}-\d{2}-\d{2}$/.test(order.createdAt)) continue;
      const hour = getIstHour(epoch);
      hours.set(hour, (hours.get(hour) || 0) + 1);
    }
    const peak = [...hours.entries()].sort((a, b) => b[1] - a[1] || a[0] - b[0])[0];
    return peak ? `${this.formatHourRange(peak[0])} (${peak[1]} orders)` : 'No orders';
  }

  get sourceGradient(): string {
    let position = 0;
    const segments = this.overview.sources.filter(source => source.count).map(source => {
      const start = position;
      position += source.count / this.overview.orders * 100;
      return `${source.color} ${start}% ${position}%`;
    });
    return segments.length ? `conic-gradient(${segments.join(', ')})` : '#e5edf5';
  }

  trendValue(bucket: SalesTrendBucket): number {
    return this.trendMetric === 'orders' ? bucket.orders
      : this.trendMetric === 'average' ? (bucket.orders ? bucket.value / bucket.orders : 0) : bucket.value;
  }

  get trendMax(): number {
    return Math.max(1, ...this.overview.trend.map(bucket => this.trendValue(bucket)));
  }

  trendX(index: number): number {
    return 40 + (this.overview.trend.length > 1 ? index * 520 / (this.overview.trend.length - 1) : 0);
  }

  trendY(bucket: SalesTrendBucket): number {
    return 160 - this.trendValue(bucket) / this.trendMax * 125;
  }

  get trendPoints(): string {
    return this.overview.trend.map((bucket, index) => `${this.trendX(index)},${this.trendY(bucket)}`).join(' ');
  }

  get trendArea(): string {
    return this.overview.trend.length ? `40,160 ${this.trendPoints} ${this.trendX(this.overview.trend.length - 1)},160` : '';
  }

  trendDate(date: string): string {
    return new Intl.DateTimeFormat('en-IN', { day: 'numeric', month: 'short', timeZone: 'UTC' })
      .format(new Date(`${date}T00:00:00Z`));
  }

  setOrderFilter(status: string): void {
    this.statusFilter = status;
  }

  orderStatusCount(status: string): number {
    return status === 'all' ? this.reconciliationOrders.length
      : this.reconciliationOrders.filter(order => order.status === status).length;
  }

  exportOverviewItemsCsv(): void {
    if (!this.overviewItems.length) {
      this.uiStore.warning('No items available to export.');
      return;
    }
    const rows = [
      ['Item', 'Category', 'Quantity', 'Orders', 'Item value'],
      ...this.overviewItems.map(item => [item.name, item.category, item.quantity, item.orders, item.value])
    ];
    const blob = new Blob([rows.map(row => row.map(value => this.escapeCsv(value)).join(',')).join('\r\n')], { type: 'text/csv;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = `web-sales-items-${getIstFileStamp()}.csv`;
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    URL.revokeObjectURL(url);
  }

  async loadDineInOperationalSignals(): Promise<void> {
    const request = ++this.signalsRequest;
    return new Promise((resolve) => {
      const outletId = this.outletService.getSelectedOutletId();
      if (!outletId) {
        this.dineInSlaAlerts = [];
        this.dayEndOpenTables = [];
        this.dayEndOutstandingAmount = 0;
        this.slaAlertsAvailable = false;
        this.openTablesAvailable = false;
        resolve();
        return;
      }

      let pending = 2;
      const done = () => {
        pending -= 1;
        if (pending <= 0) resolve();
      };

      this.dineInService.getSlaAlerts(outletId).subscribe({
        next: (res) => {
          if (request !== this.signalsRequest || outletId !== this.outletService.getSelectedOutletId()) { done(); return; }
          this.dineInSlaAlerts = res?.alerts || [];
          this.slaAlertsAvailable = true;
          done();
        },
        error: (error) => {
          if (request !== this.signalsRequest || outletId !== this.outletService.getSelectedOutletId()) { done(); return; }
          console.error('Error loading kitchen SLA alerts:', error);
          this.dineInSlaAlerts = [];
          this.slaAlertsAvailable = false;
          this.uiStore.warning('Kitchen SLA alerts are temporarily unavailable.');
          done();
        }
      });

      this.dineInService.getDayEndOpenTables(outletId).subscribe({
        next: (res) => {
          if (request !== this.signalsRequest || outletId !== this.outletService.getSelectedOutletId()) { done(); return; }
          this.dayEndOpenTables = res?.sessions || [];
          this.dayEndOutstandingAmount = res?.totalOutstandingAmount || 0;
          this.openTablesAvailable = true;
          done();
        },
        error: (error) => {
          if (request !== this.signalsRequest || outletId !== this.outletService.getSelectedOutletId()) { done(); return; }
          console.error('Error loading open table balances:', error);
          this.dayEndOpenTables = [];
          this.dayEndOutstandingAmount = 0;
          this.openTablesAvailable = false;
          this.uiStore.warning('Open table balances are temporarily unavailable.');
          done();
        }
      });
    });
  }

  async loadUpiReconciliationReport(): Promise<void> {
    const request = ++this.upiRequest;
    const start = this.startDate;
    const end = this.endDate;
    const outlet = this.outletService.getSelectedOutletId();
    const current = () => request === this.upiRequest && start === this.startDate && end === this.endDate
      && outlet === this.outletService.getSelectedOutletId();
    this.upiReconLoading = true;
    this.upiReconciliationReport = null;
    return new Promise((resolve) => {
      this.orderService.getUpiReconciliationReport(start, end).subscribe({
        next: (report) => {
          if (!current()) { resolve(); return; }
          this.upiReconciliationReport = report;
          this.upiReconLoading = false;
          resolve();
        },
        error: (error) => {
          if (!current()) { resolve(); return; }
          console.error('Error loading UPI reconciliation report:', error);
          this.uiStore.warning('UPI reconciliation data is temporarily unavailable.');
          this.upiReconciliationReport = null;
          this.upiReconLoading = false;
          resolve();
        }
      });
    });
  }

  async loadPendingDineInPayments(): Promise<void> {
    const request = ++this.pendingRequest;
    return new Promise((resolve) => {
      const outletId = this.outletService.getSelectedOutletId();
      if (!outletId) {
        this.pendingDineInPayments = [];
        resolve();
        return;
      }

      this.dineInService.getActiveTables(outletId).subscribe({
        next: sessions => {
          if (request !== this.pendingRequest || outletId !== this.outletService.getSelectedOutletId()) { resolve(); return; }
          this.pendingDineInPayments = (sessions || []).filter(session =>
            session.paymentStatus === 'pending_verification' || session.paymentStatus === 'pending_cash'
          );
          resolve();
        },
        error: error => {
          if (request !== this.pendingRequest || outletId !== this.outletService.getSelectedOutletId()) { resolve(); return; }
          console.error('Error loading pending dine-in payments:', error);
          this.pendingDineInPayments = [];
          this.uiStore.warning('Pending dine-in payments are temporarily unavailable.');
          resolve();
        }
      });
    });
  }

  confirmDineInPayment(session: ActiveDineInTable): void {
    if (this.confirmingDineInSessionId) return;
    this.confirmingDineInSessionId = session.id;
    this.dineInService.confirmPayment(session.id).subscribe({
      next: response => {
        this.uiStore.success(response.message || `Payment confirmed for table ${session.tableNumber}`);
        this.confirmingDineInSessionId = null;
        this.loadDashboardData();
        this.loadPendingDineInPayments();
        this.loadDineInOperationalSignals();
      },
      error: error => {
        console.error('Error confirming dine-in payment:', error);
        this.uiStore.error(error.error?.error || 'Failed to confirm dine-in payment');
        this.confirmingDineInSessionId = null;
      }
    });
  }

  exportUpiReconciliationCsv(): void {
    const rows = this.upiReconciliationReport?.items || [];
    if (!rows.length) {
      this.uiStore.warning('No UPI reconciliation rows available to export.');
      return;
    }

    const header = [
      'OrderId',
      'Username',
      'UserId',
      'Total',
      'PaymentStatus',
      'UpiReference',
      'HasProof',
      'UpiProofUrl',
      'UpiConfirmedBy',
      'UpiConfirmedAt',
      'CreatedAt',
      'UpdatedAt'
    ];

    const lines = rows.map(row => [
      row.id,
      row.username,
      row.userId,
      (row.total ?? 0).toFixed(2),
      row.paymentStatus,
      row.upiReference || '',
      row.upiProofUrl ? 'yes' : 'no',
      row.upiProofUrl || '',
      row.upiConfirmedBy || '',
      row.upiConfirmedAt || '',
      row.createdAt,
      row.updatedAt
    ].map(value => this.escapeCsv(value)).join(','));

    const csv = [header.join(','), ...lines].join('\n');
    const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);

    const stamp = getIstFileStamp();
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = `upi-reconciliation-${stamp}.csv`;
    document.body.appendChild(anchor);
    anchor.click();
    document.body.removeChild(anchor);
    URL.revokeObjectURL(url);
  }

  exportItemAnalyticsCsv(): void {
    const rows = this.filteredWebSalesItemQuantities;
    if (!rows.length) {
      this.uiStore.warning('No item analytics rows available to export.');
      return;
    }

    const header = [
      'Item',
      'QuantitySold',
      'Orders',
      'DineInQuantity',
      'TakeawayQuantity',
      'PhoneOrderQuantity',
      'Revenue'
    ];

    const lines = rows.map(row => [
      row.name,
      row.quantity,
      row.orders,
      row.dineInQuantity,
      row.takeawayQuantity,
      row.phoneOrderQuantity,
      row.revenue.toFixed(2)
    ].map(value => this.escapeCsv(value)).join(','));

    const csv = [header.join(','), ...lines].join('\n');
    const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);

    const stamp = getIstFileStamp();
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = `web-sales-item-analytics-${stamp}.csv`;
    document.body.appendChild(anchor);
    anchor.click();
    document.body.removeChild(anchor);
    URL.revokeObjectURL(url);
  }

  private escapeCsv(value: unknown): string {
    const text = String(value ?? '');
    return `"${text.replace(/"/g, '""')}"`;
  }

  get filteredOrders(): Order[] {
    const query = this.searchTerm.trim().toLowerCase();
    return this.reconciliationOrders.filter(order => {
      const statusMatch = this.statusFilter === 'all' || order.status === this.statusFilter;
      const textMatch = !query ||
        order.id.toLowerCase().includes(query) ||
        (order.username || '').toLowerCase().includes(query) ||
        (order.dineInSessionId || '').toLowerCase().includes(query) ||
        (order.tableNumber || '').toLowerCase().includes(query) ||
        (order.phoneNumber || '').toLowerCase().includes(query);
      return statusMatch && textMatch;
    });
  }

  get orderSessionGroups(): OrderSessionGroup[] {
    const groups = new Map<string, OrderSessionGroup>();
    for (const order of this.filteredOrders) {
      const payment = order.dineInSessionId
        ? this.collection?.sessionPayments?.[order.dineInSessionId] || null : null;
      const sessionId = payment?.sessionId || order.dineInSessionId || null;
      const key = sessionId ? `session:${sessionId}` : `order:${order.id}`;
      let group = groups.get(key);
      if (!group) {
        group = { key, sessionId, orders: [], orderValue: 0, payment };
        groups.set(key, group);
      }
      group.orders.push(order);
      if (!['cancelled', 'rejected'].includes(order.status)) group.orderValue += order.total;
    }
    return [...groups.values()];
  }

  trackBySessionGroup(_index: number, group: OrderSessionGroup): string {
    return group.key;
  }

  toggleSessionGroup(key: string): void {
    if (this.collapsedSessions.has(key)) this.collapsedSessions.delete(key);
    else this.collapsedSessions.add(key);
  }

  get webSalesItemQuantities(): WebSalesItemQuantityRow[] {
    const itemMap = new Map<string, {
      quantity: number;
      revenue: number;
      orderIds: Set<string>;
      dineInQuantity: number;
      takeawayQuantity: number;
      phoneOrderQuantity: number;
    }>();
    for (const order of this.orders) {
      if (['cancelled', 'rejected'].includes((order.status || '').toLowerCase())) continue;
      for (const item of order.items || []) {
        const name = (item.name || 'Unknown item').trim() || 'Unknown item';
        const quantity = item.quantity || 0;
        const lineRevenue = this.resolveOrderItemRevenue(item);
        const existing = itemMap.get(name) || {
          quantity: 0,
          revenue: 0,
          orderIds: new Set<string>(),
          dineInQuantity: 0,
          takeawayQuantity: 0,
          phoneOrderQuantity: 0
        };
        existing.quantity += quantity;
        existing.revenue += lineRevenue;
        existing.orderIds.add(order.id);
        this.incrementOrderTypeQuantity(existing, order, quantity);
        itemMap.set(name, existing);
      }
    }

    return Array.from(itemMap.entries())
      .map(([name, data]) => ({
        name,
        quantity: data.quantity,
        revenue: data.revenue,
        orders: data.orderIds.size,
        dineInQuantity: data.dineInQuantity,
        takeawayQuantity: data.takeawayQuantity,
        phoneOrderQuantity: data.phoneOrderQuantity,
      }))
      .sort((a, b) => b.quantity - a.quantity || b.revenue - a.revenue || a.name.localeCompare(b.name));
  }

  get filteredWebSalesItemQuantities(): WebSalesItemQuantityRow[] {
    const query = this.itemSearchTerm.trim().toLowerCase();
    if (!query) {
      return this.webSalesItemQuantities;
    }
    return this.webSalesItemQuantities.filter(row => row.name.toLowerCase().includes(query));
  }

  get totalItemsSold(): number {
    return this.webSalesItemQuantities.reduce((sum, row) => sum + row.quantity, 0);
  }

  get matchedItemsSold(): number {
    return this.filteredWebSalesItemQuantities.reduce((sum, row) => sum + row.quantity, 0);
  }

  get matchedItemsRevenue(): number {
    return this.filteredWebSalesItemQuantities.reduce((sum, row) => sum + row.revenue, 0);
  }

  get topSellingItem(): WebSalesItemQuantityRow | null {
    return this.webSalesItemQuantities[0] || null;
  }

  get mostSoldItemValue(): number {
    return this.topSellingItem?.revenue || 0;
  }

  get topRevenueItem(): WebSalesItemQuantityRow | null {
    let top: WebSalesItemQuantityRow | null = null;
    for (const row of this.webSalesItemQuantities) {
      if (!top || row.revenue > top.revenue || (row.revenue === top.revenue && row.quantity > top.quantity)) {
        top = row;
      }
    }
    return top;
  }

  get orderModeBreakdown(): OrderModeSummaryRow[] {
    const modeMap = new Map<string, OrderModeSummaryRow>();
    for (const order of this.orders) {
      const mode = this.normalizeOrderMode(order.orderType);
      const existing = modeMap.get(mode) || {
        mode,
        orders: 0,
        quantity: 0,
        revenue: 0,
        delivered: 0,
        cancelled: 0,
        inProgress: 0,
        conversionRate: 0,
        cancellationRate: 0
      };
      existing.orders += 1;
      if (!['cancelled', 'rejected'].includes(order.status)) {
        existing.quantity += this.getOrderItemsCount(order);
        existing.revenue += order.total || 0;
      }
      if (order.status === 'delivered') {
        existing.delivered += 1;
      } else if (order.status === 'cancelled' || order.status === 'rejected') {
        existing.cancelled += 1;
      } else {
        existing.inProgress += 1;
      }
      modeMap.set(mode, existing);
    }
    return Array.from(modeMap.values())
      .map(row => ({
        ...row,
        conversionRate: row.orders > 0 ? (row.delivered / row.orders) * 100 : 0,
        cancellationRate: row.orders > 0 ? (row.cancelled / row.orders) * 100 : 0
      }))
      .sort((a, b) => b.orders - a.orders || b.revenue - a.revenue);
  }

  get dominantOrderMode(): OrderModeSummaryRow | null {
    return this.orderModeBreakdown[0] || null;
  }

  get busiestOrderHourLabel(): string {
    if (!this.orders.length) return '-';
    const hourMap = new Map<number, number>();
    for (const order of this.orders) {
      if (['cancelled', 'rejected'].includes(order.status)) continue;
      const created = this.toEpochMillis(order.createdAt, order.createdAtIsUtc);
      if (!created) continue;
      if (/^\d{4}-\d{2}-\d{2}$/.test(order.createdAt)) continue;
      const hour = getIstHour(created);
      hourMap.set(hour, (hourMap.get(hour) || 0) + 1);
    }
    if (!hourMap.size) return '-';
    const peak = Array.from(hourMap.entries()).sort((a, b) => b[1] - a[1] || a[0] - b[0])[0];
    return `${this.formatHourRange(peak[0])} (${peak[1]} orders)`;
  }

  get hourlyOrderTrend(): HourlyOrderTrendRow[] {
    if (!this.orders.length) return [];

    const hourMap = new Map<number, { orders: number; quantity: number; revenue: number }>();
    for (const order of this.orders) {
      if (['cancelled', 'rejected'].includes(order.status)) continue;
      const created = this.toEpochMillis(order.createdAt, order.createdAtIsUtc);
      if (!created) continue;
      if (/^\d{4}-\d{2}-\d{2}$/.test(order.createdAt)) continue;
      const hour = getIstHour(created);
      const existing = hourMap.get(hour) || { orders: 0, quantity: 0, revenue: 0 };
      existing.orders += 1;
      existing.quantity += this.getOrderItemsCount(order);
      existing.revenue += order.total || 0;
      hourMap.set(hour, existing);
    }

    if (!hourMap.size) return [];
    const maxOrders = Math.max(...Array.from(hourMap.values()).map(row => row.orders));
    return Array.from(hourMap.entries())
      .sort((a, b) => a[0] - b[0])
      .map(([hour, data]) => ({
        hour,
        label: this.formatHourRange(hour),
        orders: data.orders,
        quantity: data.quantity,
        revenue: data.revenue,
        ordersShare: maxOrders > 0 ? (data.orders / maxOrders) * 100 : 0
      }));
  }

  get totalOrders(): number {
    return this.orders.length;
  }

  get activeOrders(): number {
    return this.orders.filter(o => !['delivered', 'served', 'cancelled', 'rejected'].includes(o.status)).length;
  }

  get deliveredOrders(): number {
    return this.orders.filter(o => o.status === 'delivered').length;
  }

  get cancelledOrders(): number {
    return this.overview.cancelledOrders;
  }

  get totalOrderValue(): number {
    return this.overview.grossValue;
  }

  get averageOrderValue(): number {
    return this.overview.average;
  }

  get availablePartners(): DeliveryPartner[] {
    return this.partners.filter(p => p.status === 'available');
  }

  getOrderItemsSummary(order: Order): string {
    const preview = order.items.slice(0, 2).map(i => `${i.name} x${i.quantity}`).join(', ');
    return order.items.length > 2 ? `${preview}...` : preview;
  }

  formatStatusLabel(status: string): string {
    if (!status) return '-';
    return status
      .split('-')
      .map(part => part.charAt(0).toUpperCase() + part.slice(1))
      .join(' ');
  }

  getPartnerDisplayName(partnerId?: string, fallbackName?: string): string {
    if (partnerId) {
      const partner = this.partners.find(p => p.id === partnerId);
      if (partner?.name) {
        return partner.name;
      }
    }
    return fallbackName || 'Unassigned';
  }

  formatCurrency(value: number): string {
    return `₹${(value || 0).toFixed(2)}`;
  }

  formatPaymentMethod(order: Order): string {
    const label = (method: string): string => {
      switch (method.toLowerCase()) {
        case 'upi':
        case 'upi-qr': return 'UPI';
        case 'cash':
        case 'cash_at_counter': return 'Cash';
        case 'cod': return 'Cash / COD';
        case 'razorpay': return 'Online';
        case 'dine_in_tab': return 'Table tab';
        default: return method || 'Unknown';
      }
    };
    if (order.dineInSessionId) {
      const methods = this.collection?.sessionMethods[order.dineInSessionId] || [];
      return methods.length ? [...new Set(methods.map(label))].join(' + ') : 'Table tab';
    }
    return label(order.paymentMethod || '');
  }

  getGrossBeforeDiscounts(order: Order): number {
    return (order.subtotal || 0) + (order.tax || 0) + (order.platformCharge || 0) + (order.deliveryFee || 0) + (order.packagingCharge || 0);
  }

  getLoyaltyRedemptionAmount(order: Order): number {
    return order.loyaltyDiscountAmount || 0;
  }

  formatDate(value?: string | Date | number | null, isUtc?: boolean | null): string {
    const epoch = this.toEpochMillis(value, isUtc);
    if (!epoch) return '-';
    return new Intl.DateTimeFormat('en-IN', {
      timeZone: 'Asia/Kolkata',
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
      hour12: true
    }).format(new Date(epoch));
  }

  formatTime(value?: string | Date | number | null, isUtc?: boolean | null): string {
    const epoch = this.toEpochMillis(value, isUtc);
    if (!epoch) return '-';
    return new Intl.DateTimeFormat('en-IN', {
      timeZone: 'Asia/Kolkata',
      hour: '2-digit',
      minute: '2-digit',
      hour12: true
    }).format(convertToIst(new Date(epoch)));
  }

  formatOrderMode(order: Order): string {
    return this.normalizeOrderMode(order.orderType);
  }

  formatPercent(value: number): string {
    return `${(value || 0).toFixed(1)}%`;
  }

  async updateOrderStatus(order: Order): Promise<void> {
    const nextStatus = this.statusDraft[order.id];
    if (!nextStatus || nextStatus === order.status) return;

    const paymentMethod = (order.paymentMethod || '').toLowerCase();
    const needsUpiConfirmationOnAccept =
      nextStatus === 'confirmed'
      && (paymentMethod === 'upi-qr' || paymentMethod === 'upi')
      && order.paymentStatus === 'pending';

    this.saving = true;
    const orderChannel = this.resolveOrderChannel(order);
    const continueStatusUpdate = () => {
      this.orderService.updateOrderStatus(order.id, nextStatus, orderChannel).subscribe({
        next: async () => {
          this.uiStore.success(`Order ${order.id.slice(-6)} updated to ${nextStatus}`);
          await this.loadDashboardData();
          await this.loadUpiReconciliationReport();
          this.initializeDrafts();
          this.saving = false;
        },
        error: (error) => {
          console.error('Error updating status:', error);
          // Keep draft aligned with persisted value when backend rejects the change.
          this.statusDraft[order.id] = order.status;
          this.uiStore.error(error.error?.error || 'Failed to update order status');
          this.saving = false;
        }
      });
    };

    if (needsUpiConfirmationOnAccept) {
      const paymentReference = (this.paymentRefDraft[order.id] || '').trim();
      this.orderService.confirmOrderPayment(order.id, {
        paymentReference: paymentReference || undefined,
        adminNote: 'UPI verified by admin while accepting order'
      }).subscribe({
        next: () => continueStatusUpdate(),
        error: (error) => {
          console.error('Error confirming UPI payment:', error);
          this.uiStore.error(error.error?.error || 'Failed to confirm UPI payment before acceptance');
          this.saving = false;
        }
      });
      return;
    }

    continueStatusUpdate();
  }

  canBypassConfirmPayment(order: Order): boolean {
    const method = (order.paymentMethod || '').toLowerCase();
    return method === 'upi-qr' && order.paymentStatus === 'pending';
  }

  confirmPayment(order: Order): void {
    if (!this.canBypassConfirmPayment(order)) {
      return;
    }

    this.saving = true;
    const paymentReference = (this.paymentRefDraft[order.id] || '').trim();

    this.orderService.confirmOrderPayment(order.id, {
      paymentReference: paymentReference || undefined,
      adminNote: 'Manual payment confirmation bypass from Web Sales Control Center'
    }).subscribe({
      next: async () => {
        this.uiStore.success(`Payment confirmed for order ${order.id.slice(-6)}`);
        await this.loadDashboardData();
        await this.loadUpiReconciliationReport();
        this.initializeDrafts();
        this.saving = false;
      },
      error: (error) => {
        console.error('Error confirming payment:', error);
        this.uiStore.error(error.error?.error || 'Failed to confirm payment');
        this.saving = false;
      }
    });
  }

  assignPartner(order: Order): void {
    const partnerId = this.partnerDraft[order.id];
    if (!partnerId) {
      this.uiStore.warning('Select a partner first');
      return;
    }

    this.saving = true;
    const orderChannel = this.resolveOrderChannel(order);
    this.deliveryPartnerService.assignDeliveryPartner({ orderId: order.id, deliveryPartnerId: partnerId }, orderChannel).subscribe({
      next: async () => {
        this.uiStore.success('Delivery partner assigned successfully');
        await this.loadDashboardData();
        this.initializeDrafts();
        this.saving = false;
      },
      error: (error) => {
        console.error('Error assigning partner:', error);
        this.uiStore.error(error.error?.error || 'Failed to assign partner');
        this.saving = false;
      }
    });
  }

  canReconcileOrder(order: Order): boolean {
    const source = String(order.orderSource || '').trim().toLowerCase();
    if (source === 'phone') return true;

    const orderType = String(order.orderType || '').trim().toLowerCase();
    if (orderType === 'dine-in' || orderType === 'pickup') return true;

    return this.resolveOrderChannel(order) === 'web';
  }

  isReconcilePanelOpen(orderId: string): boolean {
    return this.expandedReconcileOrderId === orderId;
  }

  toggleReconcilePanel(order: Order): void {
    if (!this.canReconcileOrder(order)) {
      this.uiStore.warning('Reconciliation is allowed only for Web, Dine-In, Takeaway, Phone, and Counter Sale orders.');
      return;
    }

    if (this.expandedReconcileOrderId === order.id) {
      this.expandedReconcileOrderId = null;
      return;
    }

    this.expandedReconcileOrderId = order.id;
    this.getReconcileDraft(order);
  }

  getReconcileDraft(order: Order): ReconcileOrderDraft {
    const existing = this.reconcileDraftByOrder[order.id];
    if (existing) return existing;

    const sessionPayment = order.dineInSessionId ? this.collection?.sessionPayments?.[order.dineInSessionId] : null;
    const recordedMethods = order.dineInSessionId ? this.collection?.sessionMethods?.[order.dineInSessionId] || [] : [];
    const sessionMethod = recordedMethods.length === 1 ? recordedMethods[0] : null;
    const paidMethod = sessionMethod === 'upi' || sessionMethod === 'upi-qr' ? 'upi-qr'
      : ['cash', 'cash_at_counter', 'cod'].includes(sessionMethod || '') ? 'cash_at_counter' : null;
    const draft: ReconcileOrderDraft = {
      reconcileSessionPaymentMethod: false,
      reason: '',
      status: order.status,
      paymentStatus: order.paymentStatus,
      paymentMethod: sessionPayment?.paymentStatus === 'paid' && paidMethod ? paidMethod : order.paymentMethod,
      upiReference: order.upiReference || '',
      deliveryAddress: order.deliveryAddress || '',
      phoneNumber: order.phoneNumber || '',
      customerName: order.customerName || '',
      createdAt: this.toIstDateTimeInput(order.createdAt, order.createdAtIsUtc),
      notes: order.notes || '',
      preparationNotes: order.preparationNotes || '',
      tax: order.tax ?? 0,
      platformCharge: order.platformCharge ?? 0,
      deliveryFee: order.deliveryFee ?? 0,
      packagingCharge: order.packagingCharge ?? null,
      discountAmount: order.discountAmount ?? 0,
      loyaltyDiscountAmount: order.loyaltyDiscountAmount ?? 0,
      manualSubtotalOverride: null,
      manualTotalOverride: null,
      clearDeliveryPartnerAssignment: false,
      clearCouponAndLoyalty: false,
      items: (order.items || []).map(item => ({
        menuItemId: item.menuItemId || '',
        menuItemSearch: item.name || '',
        name: item.name || '',
        description: item.description || '',
        categoryName: item.categoryName || '',
        selectedVariantName: item.selectedVariantName || '',
        quantity: item.quantity || 1,
        baseUnitPrice: item.baseUnitPrice ?? Math.max(0, (item.price || 0) - (item.addOnTotal || 0)),
        selectedAddOns: (item.selectedAddOns || []).map(addOn => ({ name: addOn.name, price: addOn.price }))
      }))
    };

    this.reconcileDraftByOrder[order.id] = draft;
    return draft;
  }

  getFilteredMenuCatalog(search: string = '', limit = 25): MenuItem[] {
    const query = (search || '').trim().toLowerCase();
    const source = this.menuCatalog || [];
    if (!query) return source.slice(0, limit);
    return source
      .filter(item => {
        const id = (item.id || '').toLowerCase();
        const name = (item.name || '').toLowerCase();
        const category = (item.categoryName || item.category || '').toLowerCase();
        return id.includes(query) || name.includes(query) || category.includes(query);
      })
      .slice(0, limit);
  }

  isMenuSuggestionOpen(key: string, row: ReconcileItemDraft): boolean {
    return this.activeMenuSuggestionKey === key && this.getFilteredMenuCatalog(row.menuItemSearch, 8).length > 0;
  }

  onMenuSearchFocus(key: string): void {
    this.activeMenuSuggestionKey = key;
    this.activeMenuSuggestionIndex = -1;
  }

  onMenuSearchBlur(row: ReconcileItemDraft): void {
    setTimeout(() => {
      this.syncMenuSearchToSelection(row);
      this.activeMenuSuggestionKey = null;
      this.activeMenuSuggestionIndex = -1;
    }, 120);
  }

  onMenuSearchKeydown(event: KeyboardEvent, row: ReconcileItemDraft, key: string): void {
    const suggestions = this.getFilteredMenuCatalog(row.menuItemSearch, 8);
    if (!suggestions.length) return;

    this.activeMenuSuggestionKey = key;

    if (event.key === 'ArrowDown') {
      event.preventDefault();
      this.activeMenuSuggestionIndex = (this.activeMenuSuggestionIndex + 1) % suggestions.length;
      return;
    }

    if (event.key === 'ArrowUp') {
      event.preventDefault();
      this.activeMenuSuggestionIndex =
        this.activeMenuSuggestionIndex <= 0 ? suggestions.length - 1 : this.activeMenuSuggestionIndex - 1;
      return;
    }

    if (event.key === 'Enter') {
      event.preventDefault();
      const selected = this.activeMenuSuggestionIndex >= 0
        ? suggestions[this.activeMenuSuggestionIndex]
        : suggestions[0];
      this.selectMenuSuggestion(row, selected);
      return;
    }

    if (event.key === 'Escape') {
      this.activeMenuSuggestionKey = null;
      this.activeMenuSuggestionIndex = -1;
    }
  }

  selectMenuSuggestion(row: ReconcileItemDraft, item: MenuItem): void {
    this.applyMenuSelection(row, item.id);
    this.activeMenuSuggestionKey = null;
    this.activeMenuSuggestionIndex = -1;
  }

  onMenuSuggestionMouseDown(event: MouseEvent, row: ReconcileItemDraft, item: MenuItem): void {
    event.preventDefault();
    this.selectMenuSuggestion(row, item);
  }

  private syncMenuSearchToSelection(row: ReconcileItemDraft): void {
    const search = (row.menuItemSearch || '').trim().toLowerCase();
    if (!search) return;

    if (row.menuItemId && (row.name || '').trim().toLowerCase() === search) {
      return;
    }

    const exactMatch = this.menuCatalog.find(item =>
      (item.id || '').toLowerCase() === search || (item.name || '').trim().toLowerCase() === search
    );
    if (exactMatch) {
      this.applyMenuSelection(row, exactMatch.id);
    }
  }

  applyMenuSelection(row: ReconcileItemDraft, menuItemId: string): void {
    const selectedId = (menuItemId || '').trim();
    if (!selectedId) {
      row.menuItemId = '';
      row.name = '';
      row.description = '';
      row.categoryName = '';
      row.selectedVariantName = '';
      row.baseUnitPrice = 0;
      return;
    }

    const menuItem = this.menuCatalog.find(item => item.id === selectedId);
    if (!menuItem) {
      row.menuItemId = selectedId;
      return;
    }

    row.menuItemId = menuItem.id;
    row.menuItemSearch = menuItem.name;
    row.name = menuItem.name;
    row.description = menuItem.description || '';
    row.categoryName = menuItem.categoryName || menuItem.category || '';
    row.selectedVariantName = menuItem.variants?.[0]?.variantName || '';
    row.baseUnitPrice = Math.max(
      0,
      menuItem.shopSellingPrice
      ?? menuItem.webPrice
      ?? menuItem.onlinePrice
      ?? row.baseUnitPrice
      ?? 0
    );
  }

  openReconcileItemPicker(order: Order, event: Event, row: ReconcileItemDraft | null = null): void {
    this.menuPickerOrder = order;
    this.menuPickerRow = row;
    this.menuPickerSearch = '';
    this.menuPickerTrigger = event.currentTarget instanceof HTMLElement ? event.currentTarget : null;
  }

  closeReconcileItemPicker(): void {
    this.menuPickerOrder = null;
    this.menuPickerRow = null;
    this.menuPickerSearch = '';
    const trigger = this.menuPickerTrigger;
    this.menuPickerTrigger = null;
    setTimeout(() => trigger?.focus());
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.menuPickerOrder) this.closeReconcileItemPicker();
  }

  selectReconcileItem(item: MenuItem): void {
    if (!this.menuPickerOrder) return;
    const row = this.menuPickerRow ?? {
      menuItemId: '',
      menuItemSearch: '',
      name: '',
      quantity: 1,
      baseUnitPrice: 0,
      selectedAddOns: []
    };
    this.applyMenuSelection(row, item.id);
    if (!this.menuPickerRow) this.getReconcileDraft(this.menuPickerOrder).items.push(row);
    this.closeReconcileItemPicker();
  }

  removeReconcileItem(order: Order, index: number): void {
    const draft = this.getReconcileDraft(order);
    if (draft.items.length <= 1) {
      this.uiStore.warning('At least one item is required.');
      return;
    }
    draft.items.splice(index, 1);
  }

  getReconcilePreviewSubtotal(order: Order): number {
    const draft = this.getReconcileDraft(order);
    return draft.items.reduce((sum, item) => {
      const addOnTotal = (item.selectedAddOns || []).reduce((s, addOn) => s + (addOn.price || 0), 0);
      const unitPrice = (item.baseUnitPrice || 0) + addOnTotal;
      return sum + ((item.quantity || 0) * unitPrice);
    }, 0);
  }

  getReconcilePreviewTotal(order: Order): number {
    const draft = this.getReconcileDraft(order);
    if (typeof draft.manualTotalOverride === 'number' && Number.isFinite(draft.manualTotalOverride)) {
      return Math.max(0, draft.manualTotalOverride);
    }

    const subtotal = (typeof draft.manualSubtotalOverride === 'number' && Number.isFinite(draft.manualSubtotalOverride))
      ? draft.manualSubtotalOverride
      : this.getReconcilePreviewSubtotal(order);

    const tax = draft.tax || 0;
    const platformCharge = draft.platformCharge || 0;
    const deliveryFee = draft.deliveryFee || 0;
    const packagingCharge = draft.packagingCharge || 0;
    const discountAmount = draft.discountAmount || 0;
    const loyaltyDiscountAmount = draft.loyaltyDiscountAmount || 0;

    return Math.max(0, subtotal + tax + platformCharge + deliveryFee + packagingCharge - discountAmount - loyaltyDiscountAmount);
  }

  submitReconciliation(order: Order): void {
    if (!this.canReconcileOrder(order)) {
      this.uiStore.error('This order type is not eligible for reconciliation.');
      return;
    }

    const draft = this.getReconcileDraft(order);
    const reason = (draft.reason || '').trim();
    if (reason.length < 5) {
      this.uiStore.warning('Please enter a clear reconciliation reason (minimum 5 characters).');
      return;
    }

    if (!draft.items.length) {
      this.uiStore.warning('At least one order item is required.');
      return;
    }

    const hasInvalidItem = draft.items.some(item =>
      !item.menuItemId?.trim() || !item.name?.trim() || (item.quantity || 0) <= 0 || (item.baseUnitPrice || 0) < 0
    );
    if (hasInvalidItem) {
      this.uiStore.warning('Each item must have menu item ID, name, quantity, and base price.');
      return;
    }

    const payload: AdminReconcileOrderRequest = {
      reason,
      reconcileSessionPaymentMethod: !!order.dineInSessionId
        && (draft.reconcileSessionPaymentMethod || draft.paymentMethod !== order.paymentMethod)
        && ['cash_at_counter', 'cod', 'upi-qr'].includes(draft.paymentMethod),
      status: draft.status,
      paymentStatus: draft.paymentStatus,
      paymentMethod: draft.paymentMethod,
      upiReference: draft.upiReference,
      deliveryAddress: draft.deliveryAddress,
      phoneNumber: draft.phoneNumber,
      customerName: draft.customerName,
      createdAt: draft.createdAt && draft.createdAt !== this.toIstDateTimeInput(order.createdAt, order.createdAtIsUtc)
        ? getIstIsoString(draft.createdAt) : undefined,
      notes: draft.notes,
      preparationNotes: draft.preparationNotes,
      tax: this.optionalNumber(draft.tax),
      platformCharge: this.optionalNumber(draft.platformCharge),
      deliveryFee: this.optionalNumber(draft.deliveryFee),
      packagingCharge: this.optionalNumber(draft.packagingCharge),
      discountAmount: this.optionalNumber(draft.discountAmount),
      loyaltyDiscountAmount: this.optionalNumber(draft.loyaltyDiscountAmount),
      manualSubtotalOverride: this.optionalNumber(draft.manualSubtotalOverride),
      manualTotalOverride: this.optionalNumber(draft.manualTotalOverride),
      clearDeliveryPartnerAssignment: draft.clearDeliveryPartnerAssignment,
      clearCouponAndLoyalty: draft.clearCouponAndLoyalty,
      items: draft.items.map(item => ({
        menuItemId: item.menuItemId.trim(),
        name: item.name.trim(),
        description: item.description,
        categoryName: item.categoryName,
        selectedVariantName: item.selectedVariantName,
        quantity: Math.max(1, Math.trunc(item.quantity || 0)),
        baseUnitPrice: Math.max(0, item.baseUnitPrice || 0),
        selectedAddOns: (item.selectedAddOns || []).map(addOn => ({
          name: addOn.name,
          price: Math.max(0, addOn.price || 0)
        }))
      }))
    };

    this.saving = true;
    this.reconcilingOrderId = order.id;

    this.orderService.adminReconcileOrder(order.id, payload).subscribe({
      next: async () => {
        this.uiStore.success(`Order ${order.id.slice(-6)} reconciled successfully`);
        this.expandedReconcileOrderId = null;
        delete this.reconcileDraftByOrder[order.id];
        await this.loadDashboardData();
        await this.loadUpiReconciliationReport();
        await this.loadPendingDineInPayments();
        await this.loadDineInOperationalSignals();
        this.initializeDrafts();
        this.saving = false;
        this.reconcilingOrderId = null;
      },
      error: (error) => {
        console.error('Error reconciling order:', error);
        this.uiStore.error(error.error?.error || 'Failed to reconcile order');
        this.saving = false;
        this.reconcilingOrderId = null;
      }
    });
  }

  toggleAddMissedOrderPanel(): void {
    this.showAddMissedOrderPanel = !this.showAddMissedOrderPanel;
    if (this.showAddMissedOrderPanel) {
      this.missedOrderDraft = this.createDefaultMissedOrderDraft();
    }
  }

  addMissedOrderItem(): void {
    this.missedOrderDraft.items.push({
      menuItemId: '',
      menuItemSearch: '',
      name: '',
      quantity: 1,
      baseUnitPrice: 0,
      selectedAddOns: []
    });
  }

  removeMissedOrderItem(index: number): void {
    if (this.missedOrderDraft.items.length <= 1) {
      this.uiStore.warning('At least one item is required.');
      return;
    }

    this.missedOrderDraft.items.splice(index, 1);
  }

  getMissedOrderPreviewSubtotal(): number {
    return this.missedOrderDraft.items.reduce((sum, item) => {
      const addOnTotal = (item.selectedAddOns || []).reduce((s, addOn) => s + (addOn.price || 0), 0);
      const unitPrice = (item.baseUnitPrice || 0) + addOnTotal;
      return sum + ((item.quantity || 0) * unitPrice);
    }, 0);
  }

  getMissedOrderPreviewTotal(): number {
    if (typeof this.missedOrderDraft.manualTotalOverride === 'number' && Number.isFinite(this.missedOrderDraft.manualTotalOverride)) {
      return Math.max(0, this.missedOrderDraft.manualTotalOverride);
    }

    const subtotal = (typeof this.missedOrderDraft.manualSubtotalOverride === 'number' && Number.isFinite(this.missedOrderDraft.manualSubtotalOverride))
      ? this.missedOrderDraft.manualSubtotalOverride
      : this.getMissedOrderPreviewSubtotal();

    const tax = this.missedOrderDraft.tax || 0;
    const platformCharge = this.missedOrderDraft.platformCharge || 0;
    const deliveryFee = this.missedOrderDraft.deliveryFee || 0;
    const packagingCharge = this.missedOrderDraft.packagingCharge || 0;
    const discountAmount = this.missedOrderDraft.discountAmount || 0;
    const loyaltyDiscountAmount = this.missedOrderDraft.loyaltyDiscountAmount || 0;

    return Math.max(0, subtotal + tax + platformCharge + deliveryFee + packagingCharge - discountAmount - loyaltyDiscountAmount);
  }

  submitMissedOrder(): void {
    const draft = this.missedOrderDraft;
    const reason = (draft.reason || '').trim();
    if (reason.length < 5) {
      this.uiStore.warning('Please enter a clear reason (minimum 5 characters).');
      return;
    }

    if (!draft.createdAt) {
      this.uiStore.warning('Please select order date and time.');
      return;
    }

    if (!draft.items.length) {
      this.uiStore.warning('At least one order item is required.');
      return;
    }

    const hasInvalidItem = draft.items.some(item =>
      !item.menuItemId?.trim() || !item.name?.trim() || (item.quantity || 0) <= 0 || (item.baseUnitPrice || 0) < 0
    );
    if (hasInvalidItem) {
      this.uiStore.warning('Each item must have menu item ID, name, quantity, and base price.');
      return;
    }

    const payload: AdminCreateMissedOrderRequest = {
      reason,
      createdAt: getIstIsoString(draft.createdAt),
      orderType: draft.orderType,
      channel: draft.channel,
      orderSource: draft.orderSource || undefined,
      status: draft.status || undefined,
      paymentStatus: draft.paymentStatus || undefined,
      paymentMethod: draft.paymentMethod || undefined,
      username: draft.username || undefined,
      customerName: draft.customerName || undefined,
      phoneNumber: draft.phoneNumber || undefined,
      deliveryAddress: draft.deliveryAddress || undefined,
      upiReference: draft.upiReference || undefined,
      notes: draft.notes || undefined,
      preparationNotes: draft.preparationNotes || undefined,
      tax: this.optionalNumber(draft.tax),
      platformCharge: this.optionalNumber(draft.platformCharge),
      deliveryFee: this.optionalNumber(draft.deliveryFee),
      packagingCharge: this.optionalNumber(draft.packagingCharge),
      discountAmount: this.optionalNumber(draft.discountAmount),
      loyaltyDiscountAmount: this.optionalNumber(draft.loyaltyDiscountAmount),
      manualSubtotalOverride: this.optionalNumber(draft.manualSubtotalOverride),
      manualTotalOverride: this.optionalNumber(draft.manualTotalOverride),
      items: draft.items.map(item => ({
        menuItemId: item.menuItemId.trim(),
        name: item.name.trim(),
        description: item.description,
        categoryName: item.categoryName,
        selectedVariantName: item.selectedVariantName,
        quantity: Math.max(1, Math.trunc(item.quantity || 0)),
        baseUnitPrice: Math.max(0, item.baseUnitPrice || 0),
        selectedAddOns: (item.selectedAddOns || []).map(addOn => ({
          name: addOn.name,
          price: Math.max(0, addOn.price || 0)
        }))
      }))
    };

    this.creatingMissedOrder = true;
    this.orderService.adminCreateMissedOrder(payload).subscribe({
      next: async () => {
        this.uiStore.success('Missed order added successfully');
        this.showAddMissedOrderPanel = false;
        this.missedOrderDraft = this.createDefaultMissedOrderDraft();
        await this.loadDashboardData();
        await this.loadUpiReconciliationReport();
        this.initializeDrafts();
        this.creatingMissedOrder = false;
      },
      error: (error) => {
        console.error('Error adding missed order:', error);
        this.uiStore.error(error.error?.error || 'Failed to add missed order');
        this.creatingMissedOrder = false;
      }
    });
  }

  canAssignPartner(order: Order): boolean {
    return order.orderType === 'delivery' && order.status !== 'delivered' && order.status !== 'cancelled';
  }

  isIssuePanelOpen(orderId: string): boolean {
    return this.expandedIssueOrderId === orderId;
  }

  toggleIssuePanel(order: Order): void {
    if (this.expandedIssueOrderId === order.id) {
      this.expandedIssueOrderId = null;
      return;
    }

    this.expandedIssueOrderId = order.id;
    if (!this.orderIssuesMap[order.id]) {
      this.loadOrderIssues(order.id);
    }
  }

  loadOrderIssues(orderId: string): void {
    this.issuesLoading[orderId] = true;
    this.orderService.getOrderIssues(orderId).subscribe({
      next: (issues) => {
        this.orderIssuesMap[orderId] = issues;
        for (const issue of issues) {
          const key = this.getIssueDraftKey(orderId, issue.id || '');
          this.issueStatusDraft[key] = issue.status;
          this.issueResolutionDraft[key] = issue.resolutionNotes || '';
          this.issueRefundDraft[key] = !!issue.refundProcessed;
        }
        this.issuesLoading[orderId] = false;
      },
      error: (error) => {
        console.error('Error loading order issues:', error);
        this.uiStore.error(error.error?.error || 'Failed to load order issues');
        this.issuesLoading[orderId] = false;
      }
    });
  }

  getOrderIssues(orderId: string): OrderIssue[] {
    return this.orderIssuesMap[orderId] || [];
  }

  getOpenIssueCount(orderId: string): number {
    return this.getOrderIssues(orderId).filter(i => i.status === 'open' || i.status === 'in-progress').length;
  }

  getIssueDraftKey(orderId: string, issueId: string): string {
    return `${orderId}:${issueId}`;
  }

  updateIssue(order: Order, issue: OrderIssue): void {
    if (!issue.id) {
      this.uiStore.error('Invalid issue id');
      return;
    }

    const key = this.getIssueDraftKey(order.id, issue.id);
    const status = this.issueStatusDraft[key] || issue.status;
    const resolutionNotes = (this.issueResolutionDraft[key] || '').trim();
    const refundProcessed = !!this.issueRefundDraft[key];

    this.issueSaving[key] = true;
    this.orderService.updateOrderIssueStatus(order.id, issue.id, {
      status: status as 'open' | 'in-progress' | 'resolved' | 'closed',
      resolutionNotes: resolutionNotes || undefined,
      refundProcessed
    }).subscribe({
      next: () => {
        this.uiStore.success(`Issue ${issue.id?.slice(-6)} updated`);
        this.issueSaving[key] = false;
        this.loadOrderIssues(order.id);
        this.loadDashboardData();
      },
      error: (error) => {
        console.error('Error updating issue:', error);
        this.uiStore.error(error.error?.error || 'Failed to update issue');
        this.issueSaving[key] = false;
      }
    });
  }

  getLoyaltyStatus(order: Order): string {
    if (order.loyaltyPointsAwarded) {
      return `Awarded (${order.loyaltyPointsAwardedValue || 0} pts)`;
    }

    if (order.status !== 'delivered') return 'Pending delivery';
    if (this.getOpenIssueCount(order.id) > 0) return 'Waiting issue closure';
    return 'Awaiting feedback';
  }

  getWorkflowCompliance(order: Order): WorkflowCompliance {
    const ageMinutes = this.getOrderAgeMinutes(order);

    if (order.status === 'cancelled') {
      return { level: 'amber', label: 'Closed', reason: 'Order cancelled' };
    }

    if (order.status === 'pending' && ageMinutes > 20) {
      return { level: 'red', label: 'Stuck', reason: 'Pending too long' };
    }

    if (order.status === 'confirmed' && ageMinutes > 30) {
      return { level: 'red', label: 'Stuck', reason: 'Not moved to preparing' };
    }

    if (order.status === 'preparing' && ageMinutes > 45) {
      return { level: 'amber', label: 'Delayed', reason: 'Prep time above SLA' };
    }

    if (order.status === 'ready' && ageMinutes > 20) {
      return { level: 'amber', label: 'Delayed', reason: 'Awaiting dispatch/delivery' };
    }

    if (order.status === 'out-for-delivery' && !order.deliveryPartnerId && order.orderType === 'delivery') {
      return { level: 'red', label: 'Blocked', reason: 'No delivery partner assigned' };
    }

    if (order.status === 'delivered') {
      if (order.loyaltyPointsAwarded) {
        return { level: 'green', label: 'Compliant', reason: `Loyalty awarded (${order.loyaltyPointsAwardedValue || 0} pts)` };
      }

      if (this.getOpenIssueCount(order.id) > 0) {
        return { level: 'red', label: 'Blocked', reason: 'Open issue pending resolution' };
      }

      if (!this.orderIssuesMap[order.id]) {
        return { level: 'amber', label: 'Review', reason: 'Open issues not yet reviewed' };
      }

      return { level: 'amber', label: 'Review', reason: 'Awaiting feedback or loyalty closure' };
    }

    return { level: 'green', label: 'On Track', reason: 'Progressing through workflow' };
  }

  getComplianceClass(order: Order): string {
    return `compliance-${this.getWorkflowCompliance(order).level}`;
  }

  private getOrderAgeMinutes(order: Order): number {
    const referenceTime = order.updatedAt || order.createdAt;
    const referenceMs = this.toEpochMillis(referenceTime);
    return Math.max(0, Math.floor((Date.now() - referenceMs) / 60000));
  }

  private createDefaultMissedOrderDraft(): MissedOrderDraft {
    return {
      reason: '',
      createdAt: this.toIstDateTimeInput(new Date()),
      orderType: 'pickup',
      channel: 'shop',
      orderSource: '',
      status: 'delivered',
      paymentStatus: 'paid',
      paymentMethod: 'cash_at_counter',
      username: '',
      customerName: '',
      phoneNumber: '',
      deliveryAddress: '',
      upiReference: '',
      notes: '',
      preparationNotes: '',
      tax: 0,
      platformCharge: 0,
      deliveryFee: 0,
      packagingCharge: 0,
      discountAmount: 0,
      loyaltyDiscountAmount: 0,
      manualSubtotalOverride: null,
      manualTotalOverride: null,
      items: [
        {
          menuItemId: '',
          menuItemSearch: '',
          name: '',
          quantity: 1,
          baseUnitPrice: 0,
          selectedAddOns: []
        }
      ]
    };
  }

  private toEpochMillis(value?: string | Date | number | null, isUtc?: boolean | null): number {
    return getStoredTimestampEpoch(value, isUtc);
  }

  private normalizeOrderMode(orderType?: string): string {
    const normalized = (orderType || '').trim().toLowerCase();
    if (!normalized) return 'Unknown';
    if (normalized === 'pickup') return 'Takeaway';
    if (normalized === 'dine-in' || normalized === 'dinein' || normalized === 'dine in') return 'Dine-In';
    if (normalized === 'delivery') return 'Delivery';
    if (normalized === 'takeaway' || normalized === 'take-away') return 'Takeaway';
    return normalized
      .split('-')
      .map(part => part.charAt(0).toUpperCase() + part.slice(1))
      .join(' ');
  }

  private resolveOrderChannel(order: Order): 'web' | 'shop' | 'partner' {
    const channel = String(order.channel || '').trim().toLowerCase();
    if (channel === 'shop' || channel === 'partner' || channel === 'web') {
      return channel;
    }

    const source = String(order.orderSource || '').trim().toLowerCase();
    if (source === 'phone' || source === 'shop' || source === 'counter') {
      return 'shop';
    }
    if (source === 'partner') {
      return 'partner';
    }

    return 'web';
  }

  private getOrderItemsCount(order: Order): number {
    return (order.items || []).reduce((sum, item) => sum + (item.quantity || 0), 0);
  }

  private resolveOrderItemRevenue(item: Order['items'][number]): number {
    return item.total ?? (item.price || 0) * (item.quantity || 0);
  }

  private incrementOrderTypeQuantity(
    aggregate: { dineInQuantity: number; takeawayQuantity: number; phoneOrderQuantity: number },
    order: Order,
    quantity: number
  ): void {
    const source = String(order.orderSource || '').trim().toLowerCase();
    if (source === 'phone') {
      aggregate.phoneOrderQuantity += quantity;
      return;
    }

    const mode = this.normalizeOrderMode(order.orderType);
    if (mode === 'Dine-In') {
      aggregate.dineInQuantity += quantity;
      return;
    }

    if (mode === 'Takeaway') {
      aggregate.takeawayQuantity += quantity;
    }
  }

  private toIstDateTimeInput(value?: string | Date | number | null, isUtc?: boolean | null): string {
    const epoch = this.toEpochMillis(value, isUtc);
    if (!Number.isFinite(epoch)) return '';

    const date = new Date(epoch);
    const parts = new Intl.DateTimeFormat('en-IN', {
      timeZone: 'Asia/Kolkata',
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      hour12: false
    }).formatToParts(date);

    const getPart = (type: Intl.DateTimeFormatPartTypes): string =>
      parts.find(p => p.type === type)?.value || '';

    const year = getPart('year');
    const month = getPart('month');
    const day = getPart('day');
    const hour = getPart('hour');
    const minute = getPart('minute');

    if (!year || !month || !day || !hour || !minute) return '';
    return `${year}-${month}-${day}T${hour}:${minute}`;
  }

  private optionalNumber(value: number | null | undefined): number | undefined {
    return typeof value === 'number' && Number.isFinite(value) ? value : undefined;
  }

  private formatHourRange(hour: number): string {
    const start = hour % 24;
    const end = (hour + 1) % 24;
    const startLabel = this.toHourLabel(start);
    const endLabel = this.toHourLabel(end);
    return `${startLabel} - ${endLabel}`;
  }

  private toHourLabel(hour: number): string {
    const normalized = ((hour % 24) + 24) % 24;
    const period = normalized >= 12 ? 'PM' : 'AM';
    const base = normalized % 12 || 12;
    return `${base} ${period}`;
  }

  getAssignedPartnerDisplay(order: Order): string {
    const o = order as any;
    return o.deliveryPartnerName || o.deliveryPartnerId || '-';
  }

  trackById(_: number, item: Order): string {
    return item.id;
  }

  trackByPartnerId(_: number, item: DeliveryPartner): string {
    return item.id || item.phone;
  }

  trackByDineInSessionId(_: number, item: ActiveDineInTable): string {
    return item.id;
  }

  trackByItemName(_: number, item: WebSalesItemQuantityRow): string {
    return item.name;
  }

  trackByOrderMode(_: number, item: OrderModeSummaryRow): string {
    return item.mode;
  }

  trackByHour(_: number, item: HourlyOrderTrendRow): number {
    return item.hour;
  }
}
