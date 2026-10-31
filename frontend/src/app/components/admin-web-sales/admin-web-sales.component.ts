import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { RouterModule } from '@angular/router';
import { Subscription } from 'rxjs';
import { filter } from 'rxjs/operators';
import { environment } from '../../../environments/environment';
import { Order, OrderIssue, OrderService, UpiReconciliationReport } from '../../services/order.service';
import { DeliveryPartner, DeliveryPartnerService } from '../../services/delivery-partner.service';
import { OutletService } from '../../services/outlet.service';
import { UIStore } from '../../store/ui.store';
import { ActiveDineInTable, DineInService } from '../../services/dine-in.service';
import { getIstDateString, getIstFileStamp, getIstInputDate, convertToIst } from '../../utils/date-utils';

interface OnlineSaleSummaryItem {
  id?: string;
  orderId?: string;
  platform?: string;
  payout?: number;
  billSubTotal?: number;
  discountAmount?: number;
  platformDeduction?: number;
  orderAt?: string;
  orderedItems?: Array<{
    itemName?: string;
    name?: string;
    quantity?: number;
    total?: number;
    price?: number;
  }>;
}

interface WebSalesDashboardResponse {
  success: boolean;
  data?: {
    summary?: {
      totalOrders?: number;
      activeOrders?: number;
      deliveredOrders?: number;
      cancelledOrders?: number;
      grossOrderValue?: number;
      averageOrderValue?: number;
      webSalesPayout?: number;
      webSalesDiscount?: number;
      webSalesDeductions?: number;
    };
    orders?: Order[];
    partners?: DeliveryPartner[];
    webSales?: OnlineSaleSummaryItem[];
  };
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

@Component({
  selector: 'app-admin-web-sales',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './admin-web-sales.component.html',
  styleUrls: ['./admin-web-sales.component.scss']
})
export class AdminWebSalesComponent implements OnInit, OnDestroy {
  private outletService = inject(OutletService);
  private uiStore = inject(UIStore);
  private outletSub?: Subscription;

  loading = false;
  saving = false;

  orders: Order[] = [];
  partners: DeliveryPartner[] = [];
  webSales: OnlineSaleSummaryItem[] = [];
  dashboardSummary: NonNullable<WebSalesDashboardResponse['data']>['summary'] = {
    totalOrders: 0,
    activeOrders: 0,
    deliveredOrders: 0,
    cancelledOrders: 0,
    grossOrderValue: 0,
    averageOrderValue: 0,
    webSalesPayout: 0,
    webSalesDiscount: 0,
    webSalesDeductions: 0,
  };

  statusFilter = 'all';
  searchTerm = '';
  itemSearchTerm = '';

  startDate = getIstInputDate(new Date(new Date().getFullYear(), new Date().getMonth(), 1));
  endDate = getIstDateString();

  statusDraft: Record<string, string> = {};
  partnerDraft: Record<string, string> = {};
  expandedIssueOrderId: string | null = null;
  orderIssuesMap: Record<string, OrderIssue[]> = {};
  issuesLoading: Record<string, boolean> = {};
  issueSaving: Record<string, boolean> = {};
  issueStatusDraft: Record<string, string> = {};
  issueResolutionDraft: Record<string, string> = {};
  issueRefundDraft: Record<string, boolean> = {};
  paymentRefDraft: Record<string, string> = {};
  upiReconLoading = false;
  upiReconciliationReport: UpiReconciliationReport | null = null;
  pendingDineInPayments: ActiveDineInTable[] = [];
  confirmingDineInSessionId: string | null = null;
  dineInSlaAlerts: any[] = [];
  dayEndOpenTables: any[] = [];
  dayEndOutstandingAmount = 0;
  private readonly istOffsetMs = 330 * 60 * 1000;

  readonly statusOptions = ['pending', 'confirmed', 'preparing', 'ready', 'out-for-delivery', 'delivered', 'cancelled'];

  constructor(
    private orderService: OrderService,
    private deliveryPartnerService: DeliveryPartnerService,
    private dineInService: DineInService,
    private http: HttpClient
  ) {}

  ngOnInit(): void {
    this.outletSub = this.outletService.selectedOutlet$
      .pipe(filter(outlet => outlet !== null))
      .subscribe(() => this.loadData());

    if (this.outletService.getSelectedOutlet()) {
      this.loadData();
    }
  }

  ngOnDestroy(): void {
    this.outletSub?.unsubscribe();
  }

  async loadData(): Promise<void> {
    this.loading = true;
    try {
      await this.loadDashboardData();
      await this.loadUpiReconciliationReport();
      await this.loadPendingDineInPayments();
      await this.loadDineInOperationalSignals();
      this.initializeDrafts();
    } finally {
      this.loading = false;
    }
  }

  async loadDashboardData(): Promise<void> {
    return new Promise((resolve) => {
      const outletId = this.outletService.getSelectedOutletId();
      if (!outletId) {
        this.uiStore.warning('Please select an outlet to load web sales dashboard');
        resolve();
        return;
      }

      const params = new URLSearchParams({
        startDate: this.startDate,
        endDate: this.endDate,
      });

      const headers = new HttpHeaders().set('X-Outlet-Id', outletId);
      this.http.get<WebSalesDashboardResponse>(`${environment.apiUrl}/online-sales/web-dashboard?${params.toString()}`, { headers })
        .subscribe({
        next: (response) => {
          const data = response?.data;
          this.dashboardSummary = data?.summary || this.dashboardSummary;
          this.orders = [...(data?.orders || [])].sort((a, b) =>
            this.toEpochMillis(b.createdAt) - this.toEpochMillis(a.createdAt)
          );
          this.partners = data?.partners || [];
          this.webSales = data?.webSales || [];
          resolve();
        },
        error: (error) => {
          console.error('Error loading dashboard:', error);
          this.uiStore.error('Failed to load web sales dashboard');
          resolve();
        }
      });
    });
  }

  initializeDrafts(): void {
    this.statusDraft = {};
    this.partnerDraft = {};
    this.paymentRefDraft = {};
    for (const order of this.orders) {
      this.statusDraft[order.id] = order.status;
      this.partnerDraft[order.id] = '';
      this.paymentRefDraft[order.id] = '';
    }
  }

  async onDateRangeChange(): Promise<void> {
    await this.loadDashboardData();
    await this.loadUpiReconciliationReport();
    await this.loadPendingDineInPayments();
    await this.loadDineInOperationalSignals();
    this.initializeDrafts();
  }

  async loadDineInOperationalSignals(): Promise<void> {
    return new Promise((resolve) => {
      const outletId = this.outletService.getSelectedOutletId();
      if (!outletId) {
        this.dineInSlaAlerts = [];
        this.dayEndOpenTables = [];
        this.dayEndOutstandingAmount = 0;
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
          this.dineInSlaAlerts = res?.alerts || [];
          done();
        },
        error: () => {
          this.dineInSlaAlerts = [];
          done();
        }
      });

      this.dineInService.getDayEndOpenTables(outletId).subscribe({
        next: (res) => {
          this.dayEndOpenTables = res?.sessions || [];
          this.dayEndOutstandingAmount = res?.totalOutstandingAmount || 0;
          done();
        },
        error: () => {
          this.dayEndOpenTables = [];
          this.dayEndOutstandingAmount = 0;
          done();
        }
      });
    });
  }

  async loadUpiReconciliationReport(): Promise<void> {
    this.upiReconLoading = true;
    return new Promise((resolve) => {
      this.orderService.getUpiReconciliationReport(this.startDate, this.endDate).subscribe({
        next: (report) => {
          this.upiReconciliationReport = report;
          this.upiReconLoading = false;
          resolve();
        },
        error: (error) => {
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
    return new Promise((resolve) => {
      const outletId = this.outletService.getSelectedOutletId();
      if (!outletId) {
        this.pendingDineInPayments = [];
        resolve();
        return;
      }

      this.dineInService.getActiveTables(outletId).subscribe({
        next: sessions => {
          this.pendingDineInPayments = (sessions || []).filter(session =>
            session.paymentStatus === 'pending_verification' || session.paymentStatus === 'pending_cash'
          );
          resolve();
        },
        error: error => {
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
        this.loadPendingDineInPayments();
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
    return this.orders.filter(order => {
      const statusMatch = this.statusFilter === 'all' || order.status === this.statusFilter;
      const textMatch = !query ||
        order.id.toLowerCase().includes(query) ||
        (order.username || '').toLowerCase().includes(query) ||
        (order.phoneNumber || '').toLowerCase().includes(query);
      return statusMatch && textMatch;
    });
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
    const hasOrderRecords = this.orders.length > 0;

    if (hasOrderRecords) {
      for (const order of this.orders) {
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
    } else {
      for (const sale of this.webSales) {
        const saleOrderKey = sale.orderId || sale.id || '';
        for (const item of sale.orderedItems || []) {
          const name = ((item.itemName || item.name || 'Unknown item').trim()) || 'Unknown item';
          const quantity = item.quantity || 0;
          const lineTotal = this.resolveSaleItemRevenue(item);
          const existing = itemMap.get(name) || {
            quantity: 0,
            revenue: 0,
            orderIds: new Set<string>(),
            dineInQuantity: 0,
            takeawayQuantity: 0,
            phoneOrderQuantity: 0
          };
          existing.quantity += quantity;
          existing.revenue += lineTotal || 0;
          if (saleOrderKey) {
            existing.orderIds.add(saleOrderKey);
          }
          this.incrementOrderTypeQuantityFromSale(existing, sale, quantity);
          itemMap.set(name, existing);
        }
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
      existing.quantity += this.getOrderItemsCount(order);
      existing.revenue += order.total || 0;
      if (order.status === 'delivered') {
        existing.delivered += 1;
      } else if (order.status === 'cancelled') {
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
      const created = this.toEpochMillis(order.createdAt);
      if (!created) continue;
      const hour = convertToIst(new Date(created)).getHours();
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
      const created = this.toEpochMillis(order.createdAt);
      if (!created) continue;
      const hour = convertToIst(new Date(created)).getHours();
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
    if ((this.dashboardSummary?.totalOrders || 0) > 0) {
      return this.dashboardSummary?.totalOrders || 0;
    }
    if (this.orders.length > 0) {
      return this.orders.length;
    }
    return this.webSales.length;
  }

  get activeOrders(): number {
    return this.dashboardSummary?.activeOrders ?? this.orders.filter(o => !['delivered', 'cancelled'].includes(o.status)).length;
  }

  get deliveredOrders(): number {
    if ((this.dashboardSummary?.deliveredOrders || 0) > 0) {
      return this.dashboardSummary?.deliveredOrders || 0;
    }
    if (this.orders.length > 0) {
      return this.orders.filter(o => o.status === 'delivered').length;
    }
    return this.webSales.length;
  }

  get cancelledOrders(): number {
    return this.dashboardSummary?.cancelledOrders ?? this.orders.filter(o => o.status === 'cancelled').length;
  }

  get totalOrderValue(): number {
    if ((this.dashboardSummary?.grossOrderValue || 0) > 0) {
      return this.dashboardSummary?.grossOrderValue || 0;
    }
    if (this.orders.length > 0) {
      return this.orders.reduce((sum, o) => sum + (o.total || 0), 0);
    }
    return this.webSales.reduce((sum, sale) => sum + (sale.billSubTotal || sale.payout || 0), 0);
  }

  get averageOrderValue(): number {
    return this.dashboardSummary?.averageOrderValue ?? (this.totalOrders > 0 ? this.totalOrderValue / this.totalOrders : 0);
  }

  get webSalesPayout(): number {
    return this.dashboardSummary?.webSalesPayout ?? this.webSales.reduce((sum, sale) => sum + (sale.payout || 0), 0);
  }

  get webSalesDiscount(): number {
    return this.dashboardSummary?.webSalesDiscount ?? this.webSales.reduce((sum, sale) => sum + (sale.discountAmount || 0), 0);
  }

  get webSalesDeductions(): number {
    return this.dashboardSummary?.webSalesDeductions ?? this.webSales.reduce((sum, sale) => sum + (sale.platformDeduction || 0), 0);
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

  getGrossBeforeDiscounts(order: Order): number {
    return (order.subtotal || 0) + (order.tax || 0) + (order.platformCharge || 0) + (order.deliveryFee || 0);
  }

  getLoyaltyRedemptionAmount(order: Order): number {
    return order.loyaltyDiscountAmount || 0;
  }

  getNetPaidValue(order: Order): number {
    return order.total || 0;
  }

  formatDate(value?: string | Date | number | null): string {
    const epoch = this.toEpochMillis(value);
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

  formatTime(value?: string | Date | number | null): string {
    const epoch = this.toEpochMillis(value);
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

  private toEpochMillis(value?: string | Date | number | null): number {
    if (value === null || value === undefined) return 0;

    if (typeof value === 'number') {
      if (!Number.isFinite(value) || value <= 0) return 0;
      return value < 1_000_000_000_000 ? value * 1000 : value;
    }

    if (typeof value === 'string') {
      const normalized = value.trim();
      if (!normalized || normalized.startsWith('0001-01-01')) return 0;
      const parsed = convertToIst(normalized);
      const epoch = parsed.getTime();
      if (Number.isNaN(epoch)) return 0;

      // Backend timestamps are sometimes stored in IST but serialized as UTC (Z).
      // Compensate so rendered values match business-local order time.
      if (this.isSerializedUtcTimestamp(normalized)) {
        return epoch - this.istOffsetMs;
      }

      return epoch;
    }

    const epoch = convertToIst(value).getTime();
    return Number.isNaN(epoch) ? 0 : epoch;
  }

  private isSerializedUtcTimestamp(value: string): boolean {
    return /z$/i.test(value) || /[+\-]00:00$/i.test(value) || /[+\-]0000$/i.test(value);
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
    const lineTotal = item.total || 0;
    if (lineTotal > 0) {
      return lineTotal;
    }

    const quantity = item.quantity || 0;
    if (quantity <= 0) return 0;

    const computedFromUnitPrice = (item.price || 0) * quantity;
    if (computedFromUnitPrice > 0) {
      return computedFromUnitPrice;
    }

    return 0;
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

    if (mode === 'Takeaway' || mode === 'Delivery') {
      aggregate.takeawayQuantity += quantity;
    }
  }

  private incrementOrderTypeQuantityFromSale(
    aggregate: { dineInQuantity: number; takeawayQuantity: number; phoneOrderQuantity: number },
    sale: OnlineSaleSummaryItem,
    quantity: number
  ): void {
    const platform = String(sale.platform || '').trim().toLowerCase();
    if (platform.includes('phone')) {
      aggregate.phoneOrderQuantity += quantity;
      return;
    }

    if (platform.includes('dine')) {
      aggregate.dineInQuantity += quantity;
      return;
    }

    // Uploaded/online sales rows are generally non-dine-in orders.
    aggregate.takeawayQuantity += quantity;
  }

  private resolveSaleItemRevenue(
    item: NonNullable<OnlineSaleSummaryItem['orderedItems']>[number]
  ): number {
    const quantity = item.quantity || 0;
    const lineTotal = item.total ?? ((item.price || 0) * quantity);
    if ((lineTotal || 0) > 0) {
      return lineTotal || 0;
    }

    return 0;
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
