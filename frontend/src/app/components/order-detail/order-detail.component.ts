import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { OrderService, Order, OrderIssue, OrderTrackingResponse } from '../../services/order.service';
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
  trackingData: OrderTrackingResponse | null = null;
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

  // Support and issue state
  supportIssueCategory: 'missing-item' | 'wrong-item' | 'damaged-item' | 'delay' | 'quality' | 'other' = 'other';
  supportIssueDescription = '';
  issueSubmitting = false;
  orderIssues: OrderIssue[] = [];

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

        this.loadTracking();
        this.loadOrderIssues();
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

        this.loadTracking();

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

  canCancelItems(): boolean {
    if (!this.order || this.isAdmin) return false;
    return this.order.status === 'pending' || this.order.status === 'confirmed' || this.order.status === 'scheduled';
  }

  cancelOrderItem(menuItemId: string, itemName: string, maxQty: number) {
    if (!this.order || !this.canCancelItems()) return;

    const input = prompt(`How many '${itemName}' do you want to cancel? (1-${maxQty})`, '1');
    if (input === null) return;

    const quantity = Number(input);
    if (!Number.isInteger(quantity) || quantity < 1 || quantity > maxQty) {
      this.uiStore.error(`Enter a valid quantity between 1 and ${maxQty}`);
      return;
    }

    this.orderService.cancelOrderItem(this.order.id, menuItemId, quantity).subscribe({
      next: (updated) => {
        this.order = updated;
        this.uiStore.success('Item updated successfully');
      },
      error: (err) => this.uiStore.error(err.error?.error || 'Failed to cancel item')
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
  trackByIssueId(index: number, item: OrderIssue): string { return item.id || `${item.category}-${index}`; }

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

  loadTracking() {
    if (!this.orderId) return;
    this.orderService.getOrderTracking(this.orderId).subscribe({
      next: (tracking) => this.trackingData = tracking,
      error: () => {}
    });
  }

  loadOrderIssues() {
    if (!this.orderId) return;
    this.orderService.getOrderIssues(this.orderId).subscribe({
      next: (issues) => this.orderIssues = issues,
      error: () => {}
    });
  }

  submitIssue() {
    if (!this.order || this.issueSubmitting) return;
    const description = this.supportIssueDescription.trim();
    if (description.length < 5) {
      this.uiStore.error('Please describe the issue with at least 5 characters');
      return;
    }

    this.issueSubmitting = true;
    this.orderService.createOrderIssue(this.order.id, {
      category: this.supportIssueCategory,
      description
    }).subscribe({
      next: (issue) => {
        this.orderIssues = [issue, ...this.orderIssues];
        this.supportIssueDescription = '';
        this.supportIssueCategory = 'other';
        this.issueSubmitting = false;
        this.uiStore.success('Issue submitted. Support will contact you soon.');
      },
      error: (err) => {
        this.issueSubmitting = false;
        this.uiStore.error(err.error?.error || 'Failed to submit issue');
      }
    });
  }

  callSupport() {
    const phone = this.trackingData?.supportPhone || '+91-9876543210';
    window.location.href = `tel:${phone}`;
  }

  emailSupport() {
    const email = this.trackingData?.supportEmail || 'support@cafemanagement.com';
    const subject = this.order ? `Support needed for order #${this.order.id.slice(-6)}` : 'Order support request';
    window.location.href = `mailto:${email}?subject=${encodeURIComponent(subject)}`;
  }

  openLiveTrackingMap() {
    if (this.trackingData?.liveLocationMapUrl) {
      window.open(this.trackingData.liveLocationMapUrl, '_blank', 'noopener');
    }
  }

  isDelayLikely(): boolean {
    if (!this.order || !this.trackingData?.estimatedDeliveryAt) return false;
    if (this.order.status === 'delivered' || this.order.status === 'cancelled') return false;

    const eta = new Date(this.trackingData.estimatedDeliveryAt).getTime();
    return Date.now() > eta;
  }

  getDelayMinutes(): number {
    if (!this.trackingData?.estimatedDeliveryAt) return 0;
    const eta = new Date(this.trackingData.estimatedDeliveryAt).getTime();
    return Math.max(0, Math.round((Date.now() - eta) / 60000));
  }

  escalateDelayIssue() {
    this.supportIssueCategory = 'delay';
    if (!this.supportIssueDescription.trim()) {
      this.supportIssueDescription = `Order appears delayed by about ${this.getDelayMinutes()} minutes. Please assist.`;
    }
    this.submitIssue();
  }
}
