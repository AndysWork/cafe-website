import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule, ActivatedRoute } from '@angular/router';
import { OrderService, Order } from '../../services/order.service';
import { PaymentService } from '../../services/payment.service';
import { AuthService } from '../../services/auth.service';
import { UIStore } from '../../store/ui.store';
import { formatIstDateTime } from '../../utils/date-utils';
import { Subscription } from 'rxjs';

@Component({
  selector: 'app-orders',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './orders.component.html',
  styleUrls: ['./orders.component.scss']
})
export class OrdersComponent implements OnInit, OnDestroy {
  private uiStore = inject(UIStore);
  orders: Order[] = [];
  isLoading = false;
  errorMessage = '';
  isAdmin = false;
  successMessage = '';
  expandedOrderId: string | null = null;
  activeFilter: string = 'all';
  private routeSub?: Subscription;
  private successTimeout?: ReturnType<typeof setTimeout>;

  statusFilters = [
    { key: 'all', label: 'All' },
    { key: 'active', label: 'Active' },
    { key: 'delivered', label: 'Delivered' },
    { key: 'cancelled', label: 'Cancelled' }
  ];

  constructor(
    private orderService: OrderService,
    private authService: AuthService,
    private paymentService: PaymentService,
    private route: ActivatedRoute
  ) {
    this.isAdmin = this.authService.isAdmin();
  }

  ngOnInit() {
    this.routeSub = this.route.queryParams.subscribe(params => {
      if (params['orderPlaced'] === 'true') {
        this.successMessage = 'Order placed successfully! Your order is being processed.';
        this.successTimeout = setTimeout(() => this.successMessage = '', 5000);
      }
    });

    this.loadOrders();
  }

  ngOnDestroy() {
    this.routeSub?.unsubscribe();
    if (this.successTimeout) clearTimeout(this.successTimeout);
  }

  loadOrders() {
    this.isLoading = true;
    this.errorMessage = '';

    const ordersObservable = this.isAdmin
      ? this.orderService.getAllOrders()
      : this.orderService.getMyOrders();

    ordersObservable.subscribe({
      next: (orders) => {
        this.orders = orders;
        this.isLoading = false;
      },
      error: (error) => {
        console.error('Error loading orders:', error);
        this.errorMessage = error.error?.error || 'Failed to load orders';
        this.isLoading = false;
      }
    });
  }

  get filteredOrders(): Order[] {
    if (this.activeFilter === 'all') return this.orders;
    if (this.activeFilter === 'active') {
      return this.orders.filter(o => !['delivered', 'cancelled'].includes(o.status));
    }
    return this.orders.filter(o => o.status === this.activeFilter);
  }

  getFilterCount(key: string): number {
    if (key === 'all') return this.orders.length;
    if (key === 'active') return this.orders.filter(o => !['delivered', 'cancelled'].includes(o.status)).length;
    return this.orders.filter(o => o.status === key).length;
  }

  toggleExpand(orderId: string) {
    this.expandedOrderId = this.expandedOrderId === orderId ? null : orderId;
  }

  isExpanded(orderId: string): boolean {
    return this.expandedOrderId === orderId;
  }

  cancelOrder(orderId: string) {
    if (!confirm('Are you sure you want to cancel this order?')) return;

    this.orderService.cancelOrder(orderId).subscribe({
      next: () => {
        this.successMessage = 'Order cancelled successfully';
        setTimeout(() => this.successMessage = '', 3000);
        this.loadOrders();
      },
      error: (error) => {
        console.error('Error cancelling order:', error);
        this.uiStore.error(error.error?.error || 'Failed to cancel order');
      }
    });
  }

  updateOrderStatus(orderId: string, newStatus: string) {
    this.orderService.updateOrderStatus(orderId, newStatus).subscribe({
      next: () => {
        this.successMessage = `Order status updated to ${newStatus}`;
        setTimeout(() => this.successMessage = '', 3000);
        this.loadOrders();
      },
      error: (error) => {
        console.error('Error updating order status:', error);
        this.uiStore.error(error.error?.error || 'Failed to update order status');
      }
    });
  }

  getStatusDisplayText(status: string): string {
    return this.orderService.getStatusDisplayText(status);
  }

  canCancelOrder(order: Order): boolean {
    return this.orderService.canCancelOrder(order.status);
  }

  formatDate(dateString: string): string {
    return formatIstDateTime(new Date(dateString));
  }

  getStatusIcon(status: string): string {
    const icons: Record<string, string> = {
      pending: '⏳', confirmed: '✅', preparing: '👨‍🍳',
      ready: '🔔', delivered: '🎉', cancelled: '❌'
    };
    return icons[status] || '📦';
  }

  getOrderTotal(order: Order): number {
    return order.total;
  }

  canRefundOrder(order: Order): boolean {
    return this.isAdmin && order.paymentMethod === 'razorpay' && order.paymentStatus === 'paid';
  }

  refundOrder(orderId: string) {
    const reason = prompt('Refund reason (optional):');
    if (reason === null) return; // User cancelled prompt

    this.paymentService.refundPayment({ orderId, reason: reason || undefined }).subscribe({
      next: (result) => {
        this.successMessage = `Refund of ₹${result.amount} processed successfully (ID: ${result.refundId})`;
        setTimeout(() => this.successMessage = '', 5000);
        this.loadOrders();
      },
      error: (error) => {
        console.error('Error processing refund:', error);
        this.uiStore.error(error.error?.error || 'Failed to process refund');
      }
    });
  }

  getPaymentStatusIcon(status: string): string {
    const icons: Record<string, string> = {
      paid: '✅', pending: '⏳', refunded: '↩️'
    };
    return icons[status] || '❓';
  }

  trackByKey(index: number, item: any): string { return item.key; }

  trackByIndex(index: number): number { return index; }

  trackByObjId(index: number, item: any): string { return item.id; }

  trackByName(index: number, item: any): string { return item.name; }
}
