import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { CartService, Cart, CartItem } from '../../services/cart.service';

@Component({
  selector: 'app-cart',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './cart.component.html',
  styleUrls: ['./cart.component.scss']
})
export class CartComponent implements OnInit {
  cart: Cart = {
    items: [],
    subtotal: 0,
    tax: 0,
    total: 0,
    itemCount: 0
  };

  constructor(
    private cartService: CartService,
    private router: Router
  ) {}

  ngOnInit() {
    this.cartService.cart$.subscribe(cart => {
      this.cart = cart;
    });
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
}
