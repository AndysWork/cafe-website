import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { OrderService, Order } from '../../services/order.service';
import { CartService } from '../../services/cart.service';
import { CustomerReviewService, CustomerReview } from '../../services/customer-review.service';
import { AuthService } from '../../services/auth.service';
import { UIStore } from '../../store/ui.store';
import { formatIstDateTime } from '../../utils/date-utils';
import { Subscription, interval } from 'rxjs';
import { switchMap } from 'rxjs/operators';

@Component({
  selector: 'app-order-detail',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './order-detail.component.html',
  styleUrls: ['./order-detail.component.scss']
})
export class OrderDetailComponent implements OnInit, OnDestroy {
  order: Order | null = null;
  isLoading = true;
  errorMessage = '';
  isAdmin = false;
  orderId = '';

  // Real-time tracking
  pollingActive = false;
  private pollSub?: Subscription;
  private routeSub?: Subscription;

  // Review state
  existingReview: CustomerReview | null = null;
  reviewRating = 0;
  reviewComment = '';
  reviewSubmitting = false;
  reviewHoverRating = 0;

  // PDF download
  downloadingPdf = false;

  // Status timeline
  statusSteps = [
    { key: 'pending', label: 'Order Placed', icon: '📋' },
    { key: 'confirmed', label: 'Confirmed', icon: '✅' },
    { key: 'preparing', label: 'Preparing', icon: '👨‍🍳' },
    { key: 'ready', label: 'Ready', icon: '🔔' },
    { key: 'delivered', label: 'Delivered', icon: '🎉' }
  ];

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private orderService: OrderService,
    private cartService: CartService,
    private reviewService: CustomerReviewService,
    private authService: AuthService,
    private uiStore: UIStore
  ) {
    this.isAdmin = this.authService.isAdmin();
  }

  ngOnInit() {
    this.routeSub = this.route.paramMap.subscribe(params => {
      this.orderId = params.get('id') || '';
      if (this.orderId) {
        this.loadOrder();
        this.startPolling();
      } else {
        this.errorMessage = 'Invalid order ID';
        this.isLoading = false;
      }
    });
  }

  ngOnDestroy() {
    this.stopPolling();
    this.routeSub?.unsubscribe();
  }

  loadOrder() {
    this.orderService.getOrderById(this.orderId).subscribe({
      next: (order) => {
        const previousStatus = this.order?.status;
        this.order = order;
        this.isLoading = false;

        // Stop polling if terminal status
        if (order.status === 'delivered' || order.status === 'cancelled') {
          this.stopPolling();
        }

        // Show toast on status change during polling
        if (previousStatus && previousStatus !== order.status) {
          this.uiStore.success(`Order status updated to ${this.orderService.getStatusDisplayText(order.status)}`);
        }

        // Load existing review for delivered orders
        if (order.status === 'delivered' && !this.existingReview) {
          this.loadReview();
        }
      },
      error: (err) => {
        this.errorMessage = err.error?.error || 'Failed to load order';
        this.isLoading = false;
        this.stopPolling();
      }
    });
  }

  startPolling() {
    this.stopPolling();
    this.pollingActive = true;
    this.pollSub = interval(15000).pipe(
      switchMap(() => this.orderService.getOrderById(this.orderId))
    ).subscribe({
      next: (order) => {
        const previousStatus = this.order?.status;
        this.order = order;

        if (previousStatus && previousStatus !== order.status) {
          this.uiStore.success(`Order status updated to ${this.orderService.getStatusDisplayText(order.status)}`);
        }

        if (order.status === 'delivered' || order.status === 'cancelled') {
          this.stopPolling();
        }
      },
      error: () => {
        // Silently fail polling errors
      }
    });
  }

  stopPolling() {
    this.pollingActive = false;
    this.pollSub?.unsubscribe();
  }

  getStepIndex(status: string): number {
    if (status === 'cancelled') return -1;
    return this.statusSteps.findIndex(s => s.key === status);
  }

  isStepComplete(stepKey: string): boolean {
    if (!this.order) return false;
    if (this.order.status === 'cancelled') return false;
    const currentIdx = this.getStepIndex(this.order.status);
    const stepIdx = this.statusSteps.findIndex(s => s.key === stepKey);
    return stepIdx <= currentIdx;
  }

  isStepCurrent(stepKey: string): boolean {
    return this.order?.status === stepKey;
  }

  canCancel(): boolean {
    return !!this.order && this.orderService.canCancelOrder(this.order.status) && !this.isAdmin;
  }

  cancelOrder() {
    if (!this.order || !confirm('Are you sure you want to cancel this order?')) return;
    this.orderService.cancelOrder(this.order.id).subscribe({
      next: () => {
        this.uiStore.success('Order cancelled successfully');
        this.loadOrder();
      },
      error: (err) => this.uiStore.error(err.error?.error || 'Failed to cancel order')
    });
  }

  reorder() {
    if (!this.order) return;
    for (const item of this.order.items) {
      this.cartService.addItem({
        menuItemId: item.menuItemId,
        name: item.name,
        description: item.description,
        categoryName: item.categoryName,
        price: item.price,
        imageUrl: undefined,
        packagingCharge: 0,
      }, item.quantity);
    }
    this.uiStore.success(`${this.order.items.length} item(s) added to cart`);
    this.router.navigate(['/cart']);
  }

  formatDate(dateString: string): string {
    return formatIstDateTime(new Date(dateString));
  }

  getStatusDisplayText(status: string): string {
    return this.orderService.getStatusDisplayText(status);
  }

  getPaymentStatusIcon(status: string): string {
    const icons: Record<string, string> = { paid: '✅', pending: '⏳', refunded: '↩️' };
    return icons[status] || '❓';
  }

  goBack() {
    this.router.navigate(['/orders']);
  }

  // Review methods
  loadReview() {
    this.reviewService.getReviewByOrder(this.orderId).subscribe({
      next: (res) => {
        if (res.exists && res.review) {
          this.existingReview = res.review;
        }
      },
      error: () => {} // silent
    });
  }

  setRating(star: number) {
    this.reviewRating = star;
  }

  submitReview() {
    if (this.reviewRating < 1) {
      this.uiStore.error('Please select a rating');
      return;
    }
    this.reviewSubmitting = true;
    this.reviewService.createReview({
      orderId: this.orderId,
      rating: this.reviewRating,
      comment: this.reviewComment.trim() || undefined
    }).subscribe({
      next: (res) => {
        this.existingReview = res.review;
        this.reviewSubmitting = false;
        this.uiStore.success('Thank you for your review!');
      },
      error: (err) => {
        this.reviewSubmitting = false;
        this.uiStore.error(err.error?.error || 'Failed to submit review');
      }
    });
  }

  trackByName(index: number, item: any): string { return item.name; }
  trackByKey(index: number, item: any): string { return item.key; }

  downloadReceipt() {
    if (!this.order || this.downloadingPdf) return;
    this.downloadingPdf = true;
    this.orderService.downloadReceiptPdf(this.order.id).subscribe({
      next: (blob) => {
        const url = window.URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `receipt-${this.order!.id.slice(-6)}.pdf`;
        a.click();
        window.URL.revokeObjectURL(url);
        this.downloadingPdf = false;
      },
      error: () => {
        this.uiStore.error('Failed to download receipt');
        this.downloadingPdf = false;
      }
    });
  }
}
