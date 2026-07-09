import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { CartService, Cart, CartItem } from '../../services/cart.service';
import { MenuService, MenuItem } from '../../services/menu.service';
import { AnalyticsTrackingService } from '../../services/analytics-tracking.service';
import { LoyaltyService } from '../../services/loyalty.service';
import { Subscription } from 'rxjs';
import { decodeHtmlEntities, resolveWebSalePrice } from '../../utils/text-utils';

@Component({
  selector: 'app-cart',
  standalone: true,
  imports: [CommonModule, FormsModule],
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

  // Edit customization modal state
  showCustomizationModal = false;
  editingCartItem: CartItem | null = null;
  customizationMenuItem: MenuItem | null = null;
  selectedVariantName = '';
  selectedAddOnNames: Set<string> = new Set();
  loadingCustomizationOptions = false;

  constructor(
    private cartService: CartService,
    private menuService: MenuService,
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
      this.cartService.updateQuantity(item.cartLineId || item.menuItemId, quantity);
    }
  }

  increaseQuantity(item: CartItem) {
    this.cartService.updateQuantity(item.cartLineId || item.menuItemId, item.quantity + 1);
  }

  decreaseQuantity(item: CartItem) {
    if (item.quantity > 1) {
      this.cartService.updateQuantity(item.cartLineId || item.menuItemId, item.quantity - 1);
    }
  }

  removeItem(item: CartItem) {
    if (confirm(`Remove ${this.getDisplayText(item.name)} from cart?`)) {
      this.cartService.removeItem(item.cartLineId || item.menuItemId);
    }
  }

  canEditCustomization(item: CartItem): boolean {
    return !!(item.selectedVariant || (item.selectedAddOns && item.selectedAddOns.length > 0));
  }

  openEditCustomization(item: CartItem): void {
    this.loadingCustomizationOptions = true;
    this.menuService.getMenuItem(item.menuItemId).subscribe({
      next: (menuItem) => {
        this.customizationMenuItem = menuItem;
        this.editingCartItem = item;
        this.selectedVariantName = item.selectedVariant?.variantName || (menuItem.variants?.[0]?.variantName || '');
        this.selectedAddOnNames = new Set((item.selectedAddOns || []).map(a => a.name));
        this.showCustomizationModal = true;
        this.loadingCustomizationOptions = false;
      },
      error: () => {
        this.loadingCustomizationOptions = false;
      }
    });
  }

  closeCustomizationModal(): void {
    this.showCustomizationModal = false;
    this.editingCartItem = null;
    this.customizationMenuItem = null;
    this.selectedVariantName = '';
    this.selectedAddOnNames = new Set();
  }

  toggleCustomizationAddOn(name: string): void {
    if (this.selectedAddOnNames.has(name)) {
      this.selectedAddOnNames.delete(name);
    } else {
      this.selectedAddOnNames.add(name);
    }
    this.selectedAddOnNames = new Set(this.selectedAddOnNames);
  }

  getCustomizationVariant(): { variantName: string; price: number; quantity?: number } | undefined {
    const item = this.customizationMenuItem;
    if (!item?.variants || item.variants.length === 0) return undefined;
    return item.variants.find(v => v.variantName === this.selectedVariantName) || item.variants[0];
  }

  getCustomizationAddOns(): { name: string; price: number }[] {
    const item = this.customizationMenuItem;
    if (!item?.addOns || item.addOns.length === 0) return [];

    return item.addOns
      .filter(a => a.isActive !== false && this.selectedAddOnNames.has(a.name))
      .map(a => ({ name: a.name, price: a.price }));
  }

  getCustomizationUnitPrice(): number {
    if (!this.customizationMenuItem) return 0;
    const variant = this.getCustomizationVariant();
    const basePrice = variant?.price ?? this.getMenuWebPrice(this.customizationMenuItem);
    const addOnPrice = this.getCustomizationAddOns().reduce((sum, a) => sum + a.price, 0);
    return basePrice + addOnPrice;
  }

  applyCustomizationChanges(): void {
    if (!this.editingCartItem || !this.customizationMenuItem) return;

    const original = this.editingCartItem;
    const selectedVariant = this.getCustomizationVariant();
    const selectedAddOns = this.getCustomizationAddOns();
    const basePrice = selectedVariant?.price ?? this.getMenuWebPrice(this.customizationMenuItem);
    const unitPrice = basePrice + selectedAddOns.reduce((sum, a) => sum + a.price, 0);

    this.cartService.removeItem(original.cartLineId || original.menuItemId);
    this.cartService.addItem({
      menuItemId: original.menuItemId,
      name: this.getDisplayText(original.name),
      description: original.description,
      categoryName: original.categoryName,
      price: unitPrice,
      basePrice,
      selectedVariant,
      selectedAddOns,
      imageUrl: original.imageUrl,
      imageThumbnailUrl: original.imageThumbnailUrl,
      packagingCharge: original.packagingCharge || 0
    }, original.quantity);

    this.closeCustomizationModal();
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

  trackByMenuItemId(index: number, item: CartItem): string { return item.cartLineId || `${item.menuItemId}-${index}`; }

  getDisplayText(value?: string | null): string {
    return decodeHtmlEntities(value);
  }

  getItemOptionSummary(item: CartItem): string {
    const parts: string[] = [];
    if (item.selectedVariant?.variantName) {
      parts.push(`Variant: ${this.getDisplayText(item.selectedVariant.variantName)}`);
    }
    if (item.selectedAddOns && item.selectedAddOns.length > 0) {
      parts.push(`Add-ons: ${item.selectedAddOns.map(a => this.getDisplayText(a.name)).join(', ')}`);
    }
    return parts.join(' | ');
  }

  private getMenuWebPrice(item: MenuItem): number {
    return resolveWebSalePrice(item.webPrice, item.shopSellingPrice, item.onlinePrice);
  }

  onPreparationNotesChange(notes: string): void {
    this.cartService.setPreparationNotes(notes || '');
  }

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
