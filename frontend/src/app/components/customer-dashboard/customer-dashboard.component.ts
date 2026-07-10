import { Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { Subscription } from 'rxjs';
import { Order, OrderService } from '../../services/order.service';
import { LoyaltyAccount, LoyaltyService } from '../../services/loyalty.service';
import { WalletResponse, WalletService } from '../../services/wallet.service';
import { formatIstDateTime } from '../../utils/date-utils';

@Component({
  selector: 'app-customer-dashboard',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './customer-dashboard.component.html',
  styleUrls: ['./customer-dashboard.component.scss']
})
export class CustomerDashboardComponent implements OnInit, OnDestroy {
  loading = true;
  loadingOrders = false;
  errorMessage = '';

  orders: Order[] = [];
  activeOrders: Order[] = [];
  recentOrders: Order[] = [];

  loyaltyAccount: LoyaltyAccount | null = null;
  wallet: WalletResponse | null = null;

  private refreshTimer: ReturnType<typeof setInterval> | null = null;
  private subscriptions: Subscription[] = [];

  constructor(
    private readonly orderService: OrderService,
    private readonly loyaltyService: LoyaltyService,
    private readonly walletService: WalletService
  ) {}

  ngOnInit(): void {
    this.loadDashboardData();

    // Keep status widgets fresh without forcing a full page reload.
    this.refreshTimer = setInterval(() => {
      this.refreshOrdersOnly();
    }, 30000);
  }

  ngOnDestroy(): void {
    this.subscriptions.forEach(sub => sub.unsubscribe());
    if (this.refreshTimer) {
      clearInterval(this.refreshTimer);
      this.refreshTimer = null;
    }
  }

  get totalOrders(): number {
    return this.orders.length;
  }

  get deliveredOrders(): number {
    return this.orders.filter(o => o.status === 'delivered').length;
  }

  get cancelledOrders(): number {
    return this.orders.filter(o => o.status === 'cancelled').length;
  }

  get activeOrdersCount(): number {
    return this.activeOrders.length;
  }

  get totalSpend(): number {
    return this.orders
      .filter(o => o.status !== 'cancelled')
      .reduce((sum, order) => sum + (Number(order.total) || 0), 0);
  }

  get spendLast30Days(): number {
    const cutoff = Date.now() - (30 * 24 * 60 * 60 * 1000);
    return this.orders
      .filter(o => o.status !== 'cancelled' && new Date(o.createdAt).getTime() >= cutoff)
      .reduce((sum, order) => sum + (Number(order.total) || 0), 0);
  }

  get statusSummaryText(): string {
    if (this.activeOrders.length === 0) {
      return 'No live orders right now';
    }

    const top = this.activeOrders[0];
    const status = this.orderService.getStatusDisplayText(top.status);
    return `Latest live order is ${status}`;
  }

  formatDate(value: string): string {
    return formatIstDateTime(new Date(value));
  }

  getStatusIcon(status: string): string {
    const icons: Record<string, string> = {
      scheduled: '⏰',
      pending: '⏳',
      confirmed: '✅',
      preparing: '👨‍🍳',
      ready: '🔔',
      'out-for-delivery': '🛵',
      delivered: '🎉',
      cancelled: '❌'
    };

    return icons[status] || '📦';
  }

  getStatusLabel(status: string): string {
    return this.orderService.getStatusDisplayText(status);
  }

  private loadDashboardData(): void {
    this.loading = true;
    this.errorMessage = '';

    this.refreshOrdersOnly(() => {
      this.loading = false;
    });

    const loyaltySub = this.loyaltyService.getLoyaltyAccount().subscribe({
      next: data => {
        this.loyaltyAccount = data;
      },
      error: () => {
        this.loyaltyAccount = null;
      }
    });

    const walletSub = this.walletService.getMyWallet().subscribe({
      next: data => {
        this.wallet = data;
      },
      error: () => {
        this.wallet = null;
      }
    });

    this.subscriptions.push(loyaltySub, walletSub);
  }

  private refreshOrdersOnly(onComplete?: () => void): void {
    this.loadingOrders = true;

    const ordersSub = this.orderService.getMyOrders().subscribe({
      next: orders => {
        this.orders = [...orders].sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime());
        this.activeOrders = this.orders.filter(o => !['delivered', 'cancelled'].includes(o.status)).slice(0, 4);
        this.recentOrders = this.orders.slice(0, 5);
        this.loadingOrders = false;
        onComplete?.();
      },
      error: error => {
        this.loadingOrders = false;
        this.errorMessage = error?.error?.error || 'Failed to load dashboard summary';
        onComplete?.();
      }
    });

    this.subscriptions.push(ordersSub);
  }
}
