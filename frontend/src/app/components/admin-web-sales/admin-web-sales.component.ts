import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { RouterModule } from '@angular/router';
import { Subscription } from 'rxjs';
import { filter } from 'rxjs/operators';
import { environment } from '../../../environments/environment';
import { Order, OrderIssue, OrderService } from '../../services/order.service';
import { DeliveryPartner, DeliveryPartnerService } from '../../services/delivery-partner.service';
import { PaymentService } from '../../services/payment.service';
import { OutletService } from '../../services/outlet.service';
import { UIStore } from '../../store/ui.store';
import { getIstDateString, getIstInputDate, formatIstDateTime } from '../../utils/date-utils';

interface OnlineSaleSummaryItem {
  id?: string;
  orderId?: string;
  platform?: string;
  payout?: number;
  discountAmount?: number;
  platformDeduction?: number;
  orderAt?: string;
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
  issueRefunding: Record<string, boolean> = {};
  paymentRefDraft: Record<string, string> = {};

  readonly statusOptions = ['pending', 'confirmed', 'preparing', 'ready', 'out-for-delivery', 'delivered', 'cancelled'];

  constructor(
    private orderService: OrderService,
    private deliveryPartnerService: DeliveryPartnerService,
    private paymentService: PaymentService,
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
            new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime()
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
    this.initializeDrafts();
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

  get totalOrders(): number {
    return this.dashboardSummary?.totalOrders ?? this.orders.length;
  }

  get activeOrders(): number {
    return this.dashboardSummary?.activeOrders ?? this.orders.filter(o => !['delivered', 'cancelled'].includes(o.status)).length;
  }

  get deliveredOrders(): number {
    return this.dashboardSummary?.deliveredOrders ?? this.orders.filter(o => o.status === 'delivered').length;
  }

  get cancelledOrders(): number {
    return this.dashboardSummary?.cancelledOrders ?? this.orders.filter(o => o.status === 'cancelled').length;
  }

  get totalOrderValue(): number {
    return this.dashboardSummary?.grossOrderValue ?? this.orders.reduce((sum, o) => sum + (o.total || 0), 0);
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

  formatCurrency(value: number): string {
    return `₹${(value || 0).toFixed(2)}`;
  }

  formatDate(value?: string): string {
    if (!value) return '-';
    return formatIstDateTime(value);
  }

  async updateOrderStatus(order: Order): Promise<void> {
    const nextStatus = this.statusDraft[order.id];
    if (!nextStatus || nextStatus === order.status) return;

    this.saving = true;
    this.orderService.updateOrderStatus(order.id, nextStatus, 'web').subscribe({
      next: async () => {
        this.uiStore.success(`Order ${order.id.slice(-6)} updated to ${nextStatus}`);
        await this.loadDashboardData();
        this.initializeDrafts();
        this.saving = false;
      },
      error: (error) => {
        console.error('Error updating status:', error);
        this.uiStore.error(error.error?.error || 'Failed to update order status');
        this.saving = false;
      }
    });
  }

  canBypassConfirmPayment(order: Order): boolean {
    const method = (order.paymentMethod || '').toLowerCase();
    return (method === 'razorpay' || method === 'upi-qr') && order.paymentStatus === 'pending';
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
    this.deliveryPartnerService.assignDeliveryPartner({ orderId: order.id, deliveryPartnerId: partnerId }, 'web').subscribe({
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

  canRefund(order: Order): boolean {
    return order.paymentMethod === 'razorpay' && order.paymentStatus === 'paid';
  }

  processIssueRefund(order: Order, issue: OrderIssue): void {
    if (!issue.id) {
      this.uiStore.error('Invalid issue id');
      return;
    }

    if (!this.canRefund(order)) {
      this.uiStore.warning('Refund is available only for paid Razorpay orders');
      return;
    }

    const key = this.getIssueDraftKey(order.id, issue.id);
    const reason = (this.issueResolutionDraft[key] || issue.description || '').trim();

    this.issueRefunding[key] = true;
    this.paymentService.refundPayment({
      orderId: order.id,
      reason: reason || `Issue refund for order ${order.id}`
    }).subscribe({
      next: () => {
        this.issueRefundDraft[key] = true;
        this.issueResolutionDraft[key] = reason || this.issueResolutionDraft[key];
        this.uiStore.success(`Refund processed for order ${order.id.slice(-6)}`);
        this.issueRefunding[key] = false;
      },
      error: (error) => {
        console.error('Error processing refund:', error);
        this.uiStore.error(error.error?.error || 'Failed to process refund');
        this.issueRefunding[key] = false;
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
    return Math.max(0, Math.floor((Date.now() - new Date(referenceTime).getTime()) / 60000));
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
}
