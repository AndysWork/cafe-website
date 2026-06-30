import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { RouterModule } from '@angular/router';
import { Subscription } from 'rxjs';
import { filter } from 'rxjs/operators';
import { environment } from '../../../environments/environment';
import { Order, OrderService } from '../../services/order.service';
import { DeliveryPartner, DeliveryPartnerService } from '../../services/delivery-partner.service';
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

  readonly statusOptions = ['pending', 'confirmed', 'preparing', 'ready', 'delivered', 'cancelled'];

  constructor(
    private orderService: OrderService,
    private deliveryPartnerService: DeliveryPartnerService,
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
    for (const order of this.orders) {
      this.statusDraft[order.id] = order.status;
      this.partnerDraft[order.id] = '';
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
