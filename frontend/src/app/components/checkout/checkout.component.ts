import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { CartService, Cart } from '../../services/cart.service';
import { OrderService, CreateOrderRequest } from '../../services/order.service';

@Component({
  selector: 'app-checkout',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './checkout.component.html',
  styleUrls: ['./checkout.component.scss']
})
export class CheckoutComponent implements OnInit {
  cart: Cart = {
    items: [],
    subtotal: 0,
    tax: 0,
    total: 0,
    itemCount: 0
  };

  // Form fields
  deliveryAddress = '';
  phoneNumber = '';
  notes = '';

  isSubmitting = false;
  errorMessage = '';

  constructor(
    private cartService: CartService,
    private orderService: OrderService,
    private router: Router
  ) {}

  ngOnInit() {
    this.cartService.cart$.subscribe(cart => {
      this.cart = cart;
      // Redirect to cart if empty
      if (cart.items.length === 0) {
        this.router.navigate(['/cart']);
      }
    });
  }

  placeOrder() {
    // Validate form
    if (!this.deliveryAddress.trim()) {
      this.errorMessage = 'Please enter a delivery address';
      return;
    }

    if (!this.phoneNumber.trim()) {
      this.errorMessage = 'Please enter a phone number';
      return;
    }

    // Validate phone number format (basic)
    const phoneRegex = /^[0-9]{10}$/;
    if (!phoneRegex.test(this.phoneNumber.replace(/\s/g, ''))) {
      this.errorMessage = 'Please enter a valid 10-digit phone number';
      return;
    }

    this.isSubmitting = true;
    this.errorMessage = '';

    // Create order request
    const orderRequest: CreateOrderRequest = {
      items: this.cart.items.map(item => ({
        menuItemId: item.menuItemId,
        quantity: item.quantity
      })),
      deliveryAddress: this.deliveryAddress.trim(),
      phoneNumber: this.phoneNumber.trim(),
      notes: this.notes.trim() || undefined
    };

    // Submit order
    this.orderService.createOrder(orderRequest).subscribe({
      next: (order) => {
        // Clear cart
        this.cartService.clearCart();
        // Navigate to orders page with success message
        this.router.navigate(['/orders'], {
          queryParams: { orderPlaced: 'true', orderId: order.id }
        });
      },
      error: (error) => {
        console.error('Error placing order:', error);
        this.errorMessage = error.error?.error || 'Failed to place order. Please try again.';
        this.isSubmitting = false;
      }
    });
  }

  goBackToCart() {
    this.router.navigate(['/cart']);
  }
}
