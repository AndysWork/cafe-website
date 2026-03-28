import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { AnalyticsTrackingService } from './analytics-tracking.service';
import { CartStore } from '../store/cart.store';

export interface CartItem {
  menuItemId: string;
  name: string;
  description?: string;
  categoryName?: string;
  price: number;
  quantity: number;
  imageUrl?: string;
  packagingCharge?: number;
}

export interface Cart {
  items: CartItem[];
  subtotal: number;
  packagingCharges: number;
  total: number;
  itemCount: number;
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
    this.analyticsTracking.trackCartAdd(item.name, item.menuItemId);
  }

  updateQuantity(menuItemId: string, quantity: number): void {
    this.cartStore.updateQuantity(menuItemId, quantity);
  }

  removeItem(menuItemId: string): void {
    const removed = this.cartStore.removeItem(menuItemId);
    if (removed) {
      this.analyticsTracking.trackCartRemove(removed.name, removed.menuItemId);
    }
  }

  clearCart(): void {
    this.cartStore.clearCart();
  }

  getCart(): Cart {
    return this.cartStore.cart();
  }

  getItemCount(): number {
    return this.cartStore.itemCount();
  }
}
