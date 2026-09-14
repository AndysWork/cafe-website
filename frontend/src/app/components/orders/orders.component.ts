import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule, ActivatedRoute, Router } from '@angular/router';
import { OrderService, Order } from '../../services/order.service';
import { PaymentService } from '../../services/payment.service';
import { AuthService } from '../../services/auth.service';
import { CartService } from '../../services/cart.service';
import { MenuService, MenuItem } from '../../services/menu.service';
import { DineInService } from '../../services/dine-in.service';
import { TableReservationService, TableReservation } from '../../services/table-reservation.service';
import { UIStore } from '../../store/ui.store';
import { formatIstDateTime } from '../../utils/date-utils';
import { Subscription } from 'rxjs';

interface QuickReorderPreset {
  key: string;
  label: string;
  occurrenceCount: number;
  items: Order['items'];
}

@Component({
  selector: 'app-orders',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './orders.component.html',
  styleUrls: ['./orders.component.scss']
})
export class OrdersComponent implements OnInit, OnDestroy {
  private readonly pendingPaymentStorageKey = 'pending_payment_recovery';
  private uiStore = inject(UIStore);
  private dineInService = inject(DineInService);
  private reservationService = inject(TableReservationService);
  orders: Order[] = [];
  reservations: TableReservation[] = [];
  isLoading = false;
  isLoadingReservations = false;
  errorMessage = '';
  isAdmin = false;
  successMessage = '';
  expandedOrderId: string | null = null;
  activeFilter: string = 'all';
  activeOrderTypeFilter: 'all' | 'delivery' | 'dine-in' | 'pickup' | 'reservation' = 'all';
  quickReorderPresets: QuickReorderPreset[] = [];
  pendingPaymentRecovery: { amount: number; reason: string; timestamp: string } | null = null;
  private routeSub?: Subscription;
  private successTimeout?: ReturnType<typeof setTimeout>;
  private menuItemMap = new Map<string, MenuItem>();

  statusFilters = [
    { key: 'all', label: 'All Orders' },
    { key: 'active', label: 'In Progress / Active' },
    { key: 'delivered', label: 'Delivered / Completed' },
    { key: 'cancelled', label: 'Cancelled' }
  ];

  isFilterSheetOpen = false;

  constructor(
    private orderService: OrderService,
    private authService: AuthService,
    private paymentService: PaymentService,
    private cartService: CartService,
    private menuService: MenuService,
    private router: Router,
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
    this.loadReservations();
    this.prefetchMenuItems();
    this.loadPendingPaymentRecovery();
  }

  ngOnDestroy() {
    this.routeSub?.unsubscribe();
    if (this.successTimeout) clearTimeout(this.successTimeout);
  }

  retryPendingPayment(): void {
    this.router.navigate(['/checkout']);
  }

  dismissPendingPaymentRecovery(): void {
    this.pendingPaymentRecovery = null;
    localStorage.removeItem(this.pendingPaymentStorageKey);
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
        if (!this.isAdmin) {
          this.buildQuickReorderPresets();
        }
        this.isLoading = false;
      },
      error: (error) => {
        console.error('Error loading orders:', error);
        this.errorMessage = error.error?.error || 'Failed to load orders';
        this.isLoading = false;
      }
    });
  }

  loadReservations() {
    if (this.isAdmin) return;
    this.isLoadingReservations = true;
    this.reservationService.getMyReservations().subscribe({
      next: (res) => {
        this.reservations = res || [];
        this.isLoadingReservations = false;
      },
      error: (err) => {
        console.warn('Could not load reservations in orders view', err);
        this.isLoadingReservations = false;
      }
    });
  }

  // --- Order Type Categorization & Helpers ---
  getOrderType(order: Order): 'delivery' | 'dine-in' | 'pickup' {
    if (order.orderType === 'dine-in' || !!order.dineInSessionId || !!order.tableNumber) {
      return 'dine-in';
    }
    if (order.orderType === 'pickup') {
      return 'pickup';
    }
    return 'delivery';
  }

  getOrderTypeLabel(order: Order): string {
    const type = this.getOrderType(order);
    switch (type) {
      case 'dine-in':
        return order.tableNumber ? `Dine-In • Table ${order.tableNumber}` : 'Dine-In Tab';
      case 'pickup':
        return 'Takeaway / Pickup';
      case 'delivery':
      default:
        return 'Home Delivery';
    }
  }

  getOrderTypeIcon(order: Order): string {
    const type = this.getOrderType(order);
    switch (type) {
      case 'dine-in': return '🍽️';
      case 'pickup': return '🛍️';
      case 'delivery': default: return '🛵';
    }
  }

  getOrderTypeBadgeClass(order: Order): string {
    const type = this.getOrderType(order);
    return `badge-type-${type}`;
  }

  setOrderTypeFilter(type: 'all' | 'delivery' | 'dine-in' | 'pickup' | 'reservation') {
    this.activeOrderTypeFilter = type;
  }

  get filteredOrders(): Order[] {
    let list = this.orders;

    // Filter by Order Type
    if (this.activeOrderTypeFilter !== 'all' && this.activeOrderTypeFilter !== 'reservation') {
      list = list.filter(o => this.getOrderType(o) === this.activeOrderTypeFilter);
    }

    // Filter by Status
    if (this.activeFilter === 'all') return list;
    if (this.activeFilter === 'active') {
      return list.filter(o => !['delivered', 'cancelled'].includes(o.status));
    }
    return list.filter(o => o.status === this.activeFilter);
  }

  getOrderTypeCount(type: 'all' | 'delivery' | 'dine-in' | 'pickup' | 'reservation'): number {
    if (type === 'all') return this.orders.length;
    if (type === 'reservation') return this.reservations.length;
    return this.orders.filter(o => this.getOrderType(o) === type).length;
  }

  get filteredReservations(): TableReservation[] {
    if (this.activeFilter === 'all') return this.reservations;
    if (this.activeFilter === 'active') {
      return this.reservations.filter(r => r.status === 'pending' || r.status === 'confirmed' || r.status === 'seated');
    }
    if (this.activeFilter === 'delivered') {
      return this.reservations.filter(r => r.status === 'completed');
    }
    if (this.activeFilter === 'cancelled') {
      return this.reservations.filter(r => r.status === 'cancelled' || r.status === 'no-show');
    }
    return this.reservations;
  }

  get activeOrderTypeLabel(): string {
    switch (this.activeOrderTypeFilter) {
      case 'delivery': return 'Delivery';
      case 'dine-in': return 'Dine-In';
      case 'pickup': return 'Pickup';
      case 'reservation': return 'Bookings';
      default: return 'All Types';
    }
  }

  get activeFilterLabel(): string {
    const f = this.statusFilters.find(x => x.key === this.activeFilter);
    return f ? f.label : 'All';
  }

  toggleFilterSheet(): void {
    this.isFilterSheetOpen = !this.isFilterSheetOpen;
  }

  closeFilterSheet(): void {
    this.isFilterSheetOpen = false;
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
      scheduled: '⏰', pending: '⏳', confirmed: '✅', preparing: '👨‍🍳',
      ready: '🔔', 'out-for-delivery': '🛵', delivered: '🎉', cancelled: '❌'
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
      paid: '✅', pending: '⏳', refunded: '↩️', unpaid: '⏳'
    };
    return icons[status] || '❓';
  }

  getPaymentMethodDisplayText(method?: string): string {
    switch (method) {
      case 'dine_in_tab':
        return '🍽️ Dine-In Tab Settlement';
      case 'cash_at_counter':
        return '💵 Cash at Counter';
      case 'razorpay':
        return '🔒 Online';
      case 'upi-qr':
        return '📱 UPI QR';
      case 'cod':
      default:
        return '💵 Cash on Delivery';
    }
  }

  // Receipt upload
  uploadingReceiptForId: string | null = null;

  onReceiptSelected(event: Event, orderId: string) {
    const input = event.target as HTMLInputElement;
    if (!input.files?.length) return;
    const file = input.files[0];
    if (file.size > 5 * 1024 * 1024) {
      this.uiStore.error('File size must be under 5MB');
      return;
    }
    this.uploadingReceiptForId = orderId;
    this.orderService.uploadReceipt(orderId, file).subscribe({
      next: (res) => {
        const order = this.orders.find(o => o.id === orderId);
        if (order) order.receiptImageUrl = res.receiptImageUrl;
        this.uploadingReceiptForId = null;
        this.uiStore.success('Receipt uploaded successfully');
      },
      error: (err) => {
        this.uploadingReceiptForId = null;
        this.uiStore.error(err.error?.error || 'Failed to upload receipt');
      }
    });
    input.value = '';
  }

  deleteReceipt(orderId: string) {
    if (!confirm('Delete receipt image?')) return;
    this.orderService.deleteReceipt(orderId).subscribe({
      next: () => {
        const order = this.orders.find(o => o.id === orderId);
        if (order) order.receiptImageUrl = undefined;
        this.uiStore.success('Receipt deleted');
      },
      error: (err) => {
        this.uiStore.error(err.error?.error || 'Failed to delete receipt');
      }
    });
  }

  reorderItems(order: Order) {
    for (const item of order.items) {
      const latest = this.menuItemMap.get(item.menuItemId);
      const packagingCharge = latest?.packagingCharge || (item as any).packagingCharge || 0;
      const selectedVariant = item.selectedVariantName
        ? {
            variantName: item.selectedVariantName,
            price: item.selectedVariantPrice ?? item.baseUnitPrice ?? item.price
          }
        : undefined;
      const selectedAddOns = (item.selectedAddOns || []).map(a => ({ name: a.name, price: a.price }));
      const basePrice = item.baseUnitPrice ?? selectedVariant?.price ?? item.price;

      this.cartService.addItem({
        menuItemId: item.menuItemId,
        name: item.name,
        description: item.description,
        categoryName: item.categoryName,
        price: item.price,
        basePrice,
        selectedVariant,
        selectedAddOns,
        imageUrl: latest?.imageUrl,
        imageThumbnailUrl: latest?.imageThumbnailUrl,
        packagingCharge,
      }, item.quantity);
    }
    this.uiStore.success(`${order.items.length} item(s) added to cart`);
    this.router.navigate(['/cart']);
  }

  reorderPreset(preset: QuickReorderPreset) {
    for (const item of preset.items) {
      const latest = this.menuItemMap.get(item.menuItemId);
      const packagingCharge = latest?.packagingCharge || (item as any).packagingCharge || 0;
      const selectedVariant = item.selectedVariantName
        ? {
            variantName: item.selectedVariantName,
            price: item.selectedVariantPrice ?? item.baseUnitPrice ?? item.price
          }
        : undefined;
      const selectedAddOns = (item.selectedAddOns || []).map(a => ({ name: a.name, price: a.price }));
      const basePrice = item.baseUnitPrice ?? selectedVariant?.price ?? item.price;

      this.cartService.addItem({
        menuItemId: item.menuItemId,
        name: item.name,
        description: item.description,
        categoryName: item.categoryName,
        price: item.price,
        basePrice,
        selectedVariant,
        selectedAddOns,
        imageUrl: latest?.imageUrl,
        imageThumbnailUrl: latest?.imageThumbnailUrl,
        packagingCharge,
      }, item.quantity);
    }
    this.uiStore.success(`Added preset: ${preset.label}`);
    this.router.navigate(['/cart']);
  }

  private prefetchMenuItems() {
    this.menuService.getMenuItems().subscribe({
      next: (items) => {
        this.menuItemMap = new Map(items.map(item => [item.id, item]));
      },
      error: () => {
        this.menuItemMap = new Map();
      }
    });
  }

  private buildQuickReorderPresets() {
    const deliveredOrders = this.orders.filter(o => o.status === 'delivered' && o.items?.length > 0);
    const grouped = new Map<string, { count: number; items: Order['items'] }>();

    for (const order of deliveredOrders) {
      const normalized = [...order.items]
        .map(i => `${i.menuItemId}:${i.quantity}`)
        .sort()
        .join('|');

      if (!normalized) continue;

      const existing = grouped.get(normalized);
      if (existing) {
        existing.count += 1;
      } else {
        grouped.set(normalized, { count: 1, items: order.items });
      }
    }

    this.quickReorderPresets = [...grouped.entries()]
      .filter(([, v]) => v.count >= 2)
      .sort((a, b) => b[1].count - a[1].count)
      .slice(0, 4)
      .map(([key, value]) => ({
        key,
        occurrenceCount: value.count,
        items: value.items,
        label: value.items.slice(0, 2).map(i => i.name).join(' + ') + (value.items.length > 2 ? '...' : '')
      }));
  }

  viewDineInBill(order: Order) {
    if (order.dineInSessionId) {
      this.dineInService.openBillModalWithSession(order.dineInSessionId, order.tableNumber || undefined);
    } else if (order.tableNumber) {
      this.dineInService.setTableNumber(order.tableNumber);
      this.dineInService.openBillModal();
    }
  }

  viewReservationBill(reservation: TableReservation) {
    if (reservation.id) {
      this.dineInService.viewReservationBill(reservation.id, reservation.tableNumber, reservation.dineInSessionId);
    } else if (reservation.dineInSessionId) {
      this.dineInService.openBillModalWithSession(reservation.dineInSessionId, reservation.tableNumber);
    }
  }

  goToReservations() {
    this.router.navigate(['/reservations']);
  }

  trackByKey(index: number, item: any): string { return item.key; }

  trackByIndex(index: number): number { return index; }

  trackByObjId(index: number, item: any): string { return item.id; }

  trackByName(index: number, item: any): string { return item.name; }

  private loadPendingPaymentRecovery(): void {
    const raw = localStorage.getItem(this.pendingPaymentStorageKey);
    if (!raw) {
      this.pendingPaymentRecovery = null;
      return;
    }

    try {
      this.pendingPaymentRecovery = JSON.parse(raw);
    } catch {
      this.pendingPaymentRecovery = null;
      localStorage.removeItem(this.pendingPaymentStorageKey);
    }
  }
}
