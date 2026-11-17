import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { AnalyticsTrackingService } from './analytics-tracking.service';
import { CartStore } from '../store/cart.store';

export interface CartItem {
  cartLineId?: string;
  menuItemId: string;
  name: string;
  description?: string;
  categoryName?: string;
  price: number;
  quantity: number;
  basePrice?: number;
  selectedVariant?: {
    variantName: string;
    price: number;
    quantity?: number;
  };
  selectedAddOns?: {
    name: string;
    price: number;
  }[];
  imageUrl?: string;
  imageThumbnailUrl?: string;
  packagingCharge?: number;
}

export interface Cart {
  items: CartItem[];
  subtotal: number;
  packagingCharges: number;
  total: number;
  itemCount: number;
  preparationNotes?: string;
}

@Injectable({
  providedIn: 'root'
})
export class CartService {
  private cartStore = inject(CartStore);
  private analyticsTracking = inject(AnalyticsTrackingService);

  /** Observable bridge — delegates to CartStore signal via toObservable. */
  public cart$: Observable<Cart> = this.cartStore.cart$;

  addItem(item: Omit<CartItem, 'quantity'>, quantity: number = 1): void {
    this.cartStore.addItem(item, quantity);
    this.analyticsTracking.trackCartAdd(item.name, item.menuItemId, quantity);
  }

  updateQuantity(menuItemId: string, quantity: number): void {
    const previous = this.cartStore.cart().items.find(item => item.cartLineId === menuItemId || item.menuItemId === menuItemId);
    this.cartStore.updateQuantity(menuItemId, quantity);
    if (previous && quantity > previous.quantity)
      this.analyticsTracking.trackCartAdd(previous.name, previous.menuItemId, quantity - previous.quantity);
    else if (previous && quantity < previous.quantity)
      this.analyticsTracking.trackCartRemove(previous.name, previous.menuItemId, previous.quantity - Math.max(0, quantity));
  }

  removeItem(menuItemId: string): void {
    const removed = this.cartStore.removeItem(menuItemId);
    if (removed) {
      this.analyticsTracking.trackCartRemove(removed.name, removed.menuItemId, removed.quantity);
    }
  }

  clearCart(): void {
    this.cartStore.clearCart();
    this.analyticsTracking.trackCartUpdate();
  }

  setPreparationNotes(notes: string): void {
    this.cartStore.setPreparationNotes(notes);
  }

  getCart(): Cart {
    return this.cartStore.cart();
  }

  getItemCount(): number {
    return this.cartStore.itemCount();
  }
}
