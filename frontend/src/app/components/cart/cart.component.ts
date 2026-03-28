import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { CartService, Cart, CartItem } from '../../services/cart.service';
import { AnalyticsTrackingService } from '../../services/analytics-tracking.service';
import { Subscription } from 'rxjs';

@Component({
  selector: 'app-cart',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './cart.component.html',
  styleUrls: ['./cart.component.scss']
})
export class CartComponent implements OnInit, OnDestroy {
  cart: Cart = {
    items: [],
    subtotal: 0,
    packagingCharges: 0,
    total: 0,
    itemCount: 0
  };
  private analyticsTracking = inject(AnalyticsTrackingService);
  private cartSub?: Subscription;

  constructor(
    private cartService: CartService,
    private router: Router
  ) {}

  ngOnInit() {
    this.analyticsTracking.trackCartView();
    this.cartSub = this.cartService.cart$.subscribe(cart => {
      this.cart = cart;
    });
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

  getItemTotal(item: CartItem): number {
    return Math.round(item.price * item.quantity * 100) / 100;
  }

  trackByName(index: number, item: any): string { return item.name; }
}
