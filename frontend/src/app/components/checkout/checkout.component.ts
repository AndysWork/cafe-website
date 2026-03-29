import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { CartService, Cart } from '../../services/cart.service';
import { OrderService, CreateOrderRequest } from '../../services/order.service';
import { PaymentService } from '../../services/payment.service';
import { AddressService, DeliveryAddress, AddAddressRequest } from '../../services/address.service';
import { AuthService } from '../../services/auth.service';
import { UIStore } from '../../store/ui.store';
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

  // Saved addresses
  savedAddresses: DeliveryAddress[] = [];
  selectedAddressId: string | null = null;
  showNewAddressForm = false;
  saveNewAddress = false;
  newAddressLabel = '';

  isSubmitting = false;
  errorMessage = '';
  private cartSub?: Subscription;

  constructor(
    private cartService: CartService,
    private orderService: OrderService,
    private paymentService: PaymentService,
    private addressService: AddressService,
    private authService: AuthService,
    private uiStore: UIStore,
    private router: Router
  ) {}

  ngOnInit() {
    this.cartSub = this.cartService.cart$.subscribe(cart => {
      this.cart = cart;
      if (cart.items.length === 0) {
        this.router.navigate(['/cart']);
      }
    });

    // Load saved addresses
    if (this.authService.isLoggedIn()) {
      this.addressService.getMyAddresses().subscribe({
        next: (addresses) => {
          this.savedAddresses = addresses;
          // Auto-select default address
          const defaultAddr = addresses.find(a => a.isDefault);
          if (defaultAddr) {
            this.selectSavedAddress(defaultAddr.id);
          }
        },
        error: () => {} // silently fail
      });
    }
  }

  ngOnDestroy() {
    this.cartSub?.unsubscribe();
  }

  selectSavedAddress(addressId: string): void {
    this.selectedAddressId = addressId;
    this.showNewAddressForm = false;
    const addr = this.savedAddresses.find(a => a.id === addressId);
    if (addr) {
      this.deliveryAddress = addr.fullAddress;
      this.phoneNumber = addr.collectorPhone;
    }
  }

  useNewAddress(): void {
    this.selectedAddressId = null;
    this.showNewAddressForm = true;
    this.deliveryAddress = '';
    this.phoneNumber = '';
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
        // Save new address if requested
        if (this.showNewAddressForm && this.saveNewAddress && this.newAddressLabel.trim()) {
          const newAddr: AddAddressRequest = {
            label: this.newAddressLabel.trim(),
            fullAddress: this.deliveryAddress.trim(),
            collectorName: '',
            collectorPhone: this.phoneNumber.trim()
          };
          this.addressService.addAddress(newAddr).subscribe({
            next: () => this.uiStore.success('Address saved for future orders'),
            error: () => {} // silently fail
          });
        }

        this.cartService.clearCart();
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
