import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { CartService, Cart, CartItem } from '../../services/cart.service';
import { AnalyticsTrackingService } from '../../services/analytics-tracking.service';
import { LoyaltyService } from '../../services/loyalty.service';
import { Subscription } from 'rxjs';

@Component({
  selector: 'app-cart',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './cart.component.html',
  styleUrls: ['./cart.component.scss']
})
export class CartComponent implements OnInit, OnDestroy {
  private readonly checkoutDraftStorageKey = 'checkout_draft';

  cart: Cart = {
    items: [],
    subtotal: 0,
    packagingCharges: 0,
    total: 0,
    itemCount: 0
  };
  private analyticsTracking = inject(AnalyticsTrackingService);
  private cartSub?: Subscription;
  checkoutDraft: { timestamp: string; cartItemCount: number; grandTotal: number } | null = null;
  loyaltyPointsAvailable = 0;

  constructor(
    private cartService: CartService,
    private loyaltyService: LoyaltyService,
    private router: Router
  ) {}

  ngOnInit() {
    this.analyticsTracking.trackCartView();
    this.cartSub = this.cartService.cart$.subscribe(cart => {
      this.cart = cart;
      this.loadCheckoutDraft();
    });

    this.loadLoyaltyPreview();
    this.loadCheckoutDraft();
  }

  ngOnDestroy() {
    this.cartSub?.unsubscribe();
  }

  updateQuantity(item: CartItem, quantity: number) {
    if (quantity > 0) {
      this.cartService.updateQuantity(item.menuItemId, quantity);
    }
  }

  increaseQuantity(item: CartItem) {
    this.cartService.updateQuantity(item.menuItemId, item.quantity + 1);
  }

  decreaseQuantity(item: CartItem) {
    if (item.quantity > 1) {
      this.cartService.updateQuantity(item.menuItemId, item.quantity - 1);
    }
  }

  removeItem(item: CartItem) {
    if (confirm(`Remove ${item.name} from cart?`)) {
      this.cartService.removeItem(item.menuItemId);
    }
  }

  clearCart() {
    if (confirm('Are you sure you want to clear your cart?')) {
      this.cartService.clearCart();
    }
  }

  continueShopping() {
    this.router.navigate(['/menu']);
  }

  proceedToCheckout() {
    this.router.navigate(['/checkout']);
  }

  resumeCheckoutFromDraft() {
    this.router.navigate(['/checkout']);
  }

  dismissCheckoutDraft() {
    this.checkoutDraft = null;
    localStorage.removeItem(this.checkoutDraftStorageKey);
  }

  get checkoutDraftAgeMinutes(): number {
    if (!this.checkoutDraft?.timestamp) return 0;
    const ts = new Date(this.checkoutDraft.timestamp).getTime();
    if (Number.isNaN(ts)) return 0;
    return Math.max(0, Math.floor((Date.now() - ts) / 60000));
  }

  getItemTotal(item: CartItem): number {
    return Math.round(item.price * item.quantity * 100) / 100;
  }

  getEstimatedPointsForItem(item: CartItem): number {
    return Math.max(0, Math.floor((item.price || 0) / 10) * item.quantity);
  }

  get estimatedTotalEarnPoints(): number {
    return this.cart.items.reduce((sum, item) => sum + this.getEstimatedPointsForItem(item), 0);
  }

  get redeemValuePreview(): number {
    const available = Math.floor(this.loyaltyPointsAvailable * 0.25);
    return Math.min(Math.floor(this.cart.total || 0), available);
  }

  trackByMenuItemId(index: number, item: CartItem): string { return item.menuItemId; }

  private loadLoyaltyPreview(): void {
    this.loyaltyService.getLoyaltyAccount().subscribe({
      next: (account) => {
        this.loyaltyPointsAvailable = account?.currentPoints || 0;
      },
      error: () => {
        this.loyaltyPointsAvailable = 0;
      }
    });
  }

  private loadCheckoutDraft(): void {
    const raw = localStorage.getItem(this.checkoutDraftStorageKey);
    if (!raw) {
      this.checkoutDraft = null;
      return;
    }

    try {
      const parsed = JSON.parse(raw);
      this.checkoutDraft = {
        timestamp: parsed.timestamp || new Date().toISOString(),
        cartItemCount: Number(parsed.cartItemCount || 0),
        grandTotal: Number(parsed.grandTotal || 0)
      };
    } catch {
      this.checkoutDraft = null;
      localStorage.removeItem(this.checkoutDraftStorageKey);
    }
  }
}
