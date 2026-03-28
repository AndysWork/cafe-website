import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { CartService, Cart } from '../../services/cart.service';
import { OrderService, CreateOrderRequest } from '../../services/order.service';
import { PaymentService } from '../../services/payment.service';
import { Subscription } from 'rxjs';

@Component({
  selector: 'app-checkout',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './checkout.component.html',
  styleUrls: ['./checkout.component.scss']
})
export class CheckoutComponent implements OnInit, OnDestroy {
  cart: Cart = {
    items: [],
    subtotal: 0,
    packagingCharges: 0,
    total: 0,
    itemCount: 0
  };

  // Form fields
  deliveryAddress = '';
  phoneNumber = '';
  notes = '';
  paymentMethod: 'cod' | 'razorpay' = 'cod';

  isSubmitting = false;
  errorMessage = '';
  private cartSub?: Subscription;

  constructor(
    private cartService: CartService,
    private orderService: OrderService,
    private paymentService: PaymentService,
    private router: Router
  ) {}

  ngOnInit() {
    this.cartSub = this.cartService.cart$.subscribe(cart => {
      this.cart = cart;
      // Redirect to cart if empty
      if (cart.items.length === 0) {
        this.router.navigate(['/cart']);
      }
    });
  }

  ngOnDestroy() {
    this.cartSub?.unsubscribe();
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

    if (this.paymentMethod === 'razorpay') {
      this.processRazorpayPayment();
    } else {
      this.submitOrder();
    }
  }

  private processRazorpayPayment() {
    // Step 1: Create Razorpay order on backend
    this.paymentService.createPaymentOrder(this.cart.total).subscribe({
      next: (paymentOrder) => {
        // Step 2: Open Razorpay checkout modal
        this.paymentService.openRazorpayCheckout({
          orderId: paymentOrder.orderId,
          amount: paymentOrder.amount,
          currency: paymentOrder.currency,
          keyId: paymentOrder.keyId,
          customerName: this.deliveryAddress.split('\n')[0] || 'Customer',
          customerPhone: this.phoneNumber,
          description: `Order - ${this.cart.itemCount} item(s)`
        }).then((result) => {
          // Step 3: Verify payment signature on server
          this.paymentService.verifyPayment({
            razorpayOrderId: result.razorpay_order_id,
            razorpayPaymentId: result.razorpay_payment_id,
            razorpaySignature: result.razorpay_signature
          }).subscribe({
            next: (verification) => {
              if (verification.success) {
                // Step 4: Payment verified — create order with Razorpay details
                this.submitOrder(
                  result.razorpay_payment_id,
                  result.razorpay_order_id,
                  result.razorpay_signature
                );
              } else {
                this.errorMessage = 'Payment verification failed. Please contact support.';
                this.isSubmitting = false;
              }
            },
            error: (error) => {
              console.error('Payment verification failed:', error);
              this.errorMessage = 'Payment verification failed. Your payment will be refunded if charged.';
              this.isSubmitting = false;
            }
          });
        }).catch((error) => {
          this.errorMessage = error.message || 'Payment was cancelled or failed';
          this.isSubmitting = false;
        });
      },
      error: (error) => {
        console.error('Error creating payment order:', error);
        this.errorMessage = 'Failed to initiate payment. Please try again.';
        this.isSubmitting = false;
      }
    });
  }

  private submitOrder(razorpayPaymentId?: string, razorpayOrderId?: string, razorpaySignature?: string) {
    const orderRequest: CreateOrderRequest = {
      items: this.cart.items.map(item => ({
        menuItemId: item.menuItemId,
        quantity: item.quantity
      })),
      deliveryAddress: this.deliveryAddress.trim(),
      phoneNumber: this.phoneNumber.trim(),
      notes: this.notes.trim() || undefined,
      paymentMethod: this.paymentMethod,
      razorpayPaymentId,
      razorpayOrderId,
      razorpaySignature
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

  trackByName(index: number, item: any): string { return item.name; }
}
