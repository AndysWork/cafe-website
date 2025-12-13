import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule, ActivatedRoute } from '@angular/router';
import { OrderService, Order } from '../../services/order.service';
import { AuthService } from '../../services/auth.service';

@Component({
  selector: 'app-orders',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './orders.component.html',
  styleUrls: ['./orders.component.scss']
})
export class OrdersComponent implements OnInit {
  orders: Order[] = [];
  isLoading = false;
  errorMessage = '';
  isAdmin = false;
  successMessage = '';

  constructor(
    private orderService: OrderService,
    private authService: AuthService,
    private route: ActivatedRoute
  ) {
    this.isAdmin = this.authService.isAdmin();
  }

  ngOnInit() {
    // Check for success message from checkout
    this.route.queryParams.subscribe(params => {
      if (params['orderPlaced'] === 'true') {
        this.successMessage = 'Order placed successfully! Your order is being processed.';
        setTimeout(() => {
          this.successMessage = '';
        }, 5000);
      }
    });

    this.loadOrders();
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

  cancelOrder(orderId: string) {
    if (!confirm('Are you sure you want to cancel this order?')) {
      return;
    }

    this.orderService.cancelOrder(orderId).subscribe({
      next: () => {
        alert('Order cancelled successfully');
        this.loadOrders();
      },
      error: (error) => {
        console.error('Error cancelling order:', error);
        alert(error.error?.error || 'Failed to cancel order');
      }
    });
  }

  updateOrderStatus(orderId: string, newStatus: string) {
    this.orderService.updateOrderStatus(orderId, newStatus).subscribe({
      next: () => {
        alert(`Order status updated to ${newStatus}`);
        this.loadOrders();
      },
      error: (error) => {
        console.error('Error updating order status:', error);
        alert(error.error?.error || 'Failed to update order status');
      }
    });
  }

  getStatusDisplayText(status: string): string {
    return this.orderService.getStatusDisplayText(status);
  }

  getStatusColorClass(status: string): string {
    return this.orderService.getStatusColorClass(status);
  }

  canCancelOrder(order: Order): boolean {
    return this.orderService.canCancelOrder(order.status);
  }

  formatDate(dateString: string): string {
    const date = new Date(dateString);
    return date.toLocaleDateString('en-US', {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit'
    });
  }

  getOrderItemsSummary(order: Order): string {
    return order.items.map(item => `${item.name} (${item.quantity})`).join(', ');
  }
}
