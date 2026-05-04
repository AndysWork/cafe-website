import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { CartService, Cart } from '../../services/cart.service';
import { OrderService, CreateOrderRequest } from '../../services/order.service';
import { PaymentService } from '../../services/payment.service';
import { AddressService, DeliveryAddress, AddAddressRequest } from '../../services/address.service';
import { AuthService } from '../../services/auth.service';
import { OffersService, OfferValidationResponse } from '../../services/offers.service';
import { LoyaltyService, LoyaltyAccount } from '../../services/loyalty.service';
import { WalletService, WalletResponse } from '../../services/wallet.service';
import { DeliveryZoneService } from '../../services/delivery-zone.service';
import { UIStore } from '../../store/ui.store';
import { Subscription } from 'rxjs';
import { getIstInputDate } from '../../utils/date-utils';

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

  // Coupon state
  couponCode = '';
  couponDiscount = 0;
  couponMessage = '';
  couponValid = false;
  validatingCoupon = false;
  appliedCouponOfferId = '';

  // Loyalty state
  loyaltyAccount: LoyaltyAccount | null = null;
  useLoyaltyPoints = false;
  loyaltyPointsToUse = 0;
  maxLoyaltyDiscount = 0;
  loyaltyLoaded = false;

  // Order type
  orderType: 'delivery' | 'pickup' | 'dine-in' = 'delivery';
  tableNumber: number | null = null;

  // Scheduling
  scheduleOrder = false;
  scheduledDate = '';
  scheduledTime = '';
  minScheduleDate = '';
  maxScheduleDate = '';
  scheduleValidationError = '';

  // Delivery fee
  deliveryFee = 0;
  calculatingFee = false;

  // Wallet
  walletData: WalletResponse | null = null;
  useWallet = false;
  walletAmountToUse = 0;

  // Computed totals
  get taxAmount(): number {
    return Math.round(this.cart.subtotal * 0.025 * 100) / 100;
  }

  get platformChargeAmount(): number {
    return Math.round(this.cart.subtotal * 0.025 * 100) / 100;
  }

  get loyaltyDiscount(): number {
    if (!this.useLoyaltyPoints || !this.loyaltyAccount) return 0;
    return Math.round(this.loyaltyPointsToUse * 0.25 * 100) / 100;
  }

  get grandTotal(): number {
    const raw = this.cart.subtotal + this.cart.packagingCharges + this.taxAmount + this.platformChargeAmount + this.deliveryFee - this.couponDiscount - this.loyaltyDiscount - this.walletDiscount;
    return Math.round(Math.max(0, raw) * 100) / 100;
  }

  get walletDiscount(): number {
    if (!this.useWallet || !this.walletData) return 0;
    return this.walletAmountToUse;
  }

  constructor(
    private cartService: CartService,
    private orderService: OrderService,
    private paymentService: PaymentService,
    private addressService: AddressService,
    private authService: AuthService,
    private offersService: OffersService,
    private loyaltyService: LoyaltyService,
    private walletService: WalletService,
    private deliveryZoneService: DeliveryZoneService,
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

      // Load loyalty account
      this.loyaltyService.getLoyaltyAccount().subscribe({
        next: (account) => {
          this.loyaltyAccount = account;
          this.loyaltyLoaded = true;
        },
        error: () => { this.loyaltyLoaded = true; }
      });

      // Load wallet
      this.walletService.getMyWallet().subscribe({
        next: (data) => this.walletData = data,
        error: () => {}
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

    // Validate schedule if enabled
    if (!this.validateSchedule()) {
      this.errorMessage = this.scheduleValidationError;
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

  // Coupon methods
  applyCoupon() {
    if (!this.couponCode.trim()) return;
    this.validatingCoupon = true;
    this.couponMessage = '';
    this.offersService.validateOffer({
      code: this.couponCode.trim(),
      orderAmount: this.cart.subtotal,
      categories: this.cart.items.map(i => i.categoryName).filter((c): c is string => !!c)
    }).subscribe({
      next: (res: OfferValidationResponse) => {
        this.validatingCoupon = false;
        if (res.isValid) {
          this.couponValid = true;
          this.couponDiscount = res.discountAmount;
          this.couponMessage = res.message || 'Coupon applied!';
          this.appliedCouponOfferId = res.offer?.id || '';
        } else {
          this.couponValid = false;
          this.couponDiscount = 0;
          this.couponMessage = res.message || 'Invalid coupon code';
        }
      },
      error: () => {
        this.validatingCoupon = false;
        this.couponValid = false;
        this.couponDiscount = 0;
        this.couponMessage = 'Failed to validate coupon';
      }
    });
  }

  removeCoupon() {
    this.couponCode = '';
    this.couponDiscount = 0;
    this.couponMessage = '';
    this.couponValid = false;
    this.appliedCouponOfferId = '';
  }

  // Loyalty methods
  toggleLoyaltyPoints() {
    this.useLoyaltyPoints = !this.useLoyaltyPoints;
    if (this.useLoyaltyPoints && this.loyaltyAccount) {
      // Max points: use all available, capped so discount doesn't exceed (subtotal + tax - couponDiscount)
      const maxDiscount = this.cart.subtotal + this.taxAmount - this.couponDiscount;
      const maxPointsByBalance = this.loyaltyAccount.currentPoints;
      const maxPointsByTotal = Math.floor(maxDiscount / 0.25);
      this.loyaltyPointsToUse = Math.min(maxPointsByBalance, maxPointsByTotal);
      this.maxLoyaltyDiscount = this.loyaltyPointsToUse * 0.25;
    } else {
      this.loyaltyPointsToUse = 0;
    }
  }

  private processRazorpayPayment() {
    // Step 1: Create Razorpay order on backend
    this.paymentService.createPaymentOrder(this.grandTotal).subscribe({
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
      deliveryAddress: this.orderType === 'delivery' ? this.deliveryAddress.trim() : undefined,
      phoneNumber: this.phoneNumber.trim(),
      notes: this.notes.trim() || undefined,
      paymentMethod: this.paymentMethod,
      razorpayPaymentId,
      razorpayOrderId,
      razorpaySignature,
      couponCode: this.couponValid ? this.couponCode.trim() : undefined,
      loyaltyPointsUsed: this.useLoyaltyPoints ? this.loyaltyPointsToUse : undefined,
      orderType: this.orderType,
      scheduledFor: this.getScheduledDateTime(),
      deliveryFee: this.orderType === 'delivery' ? this.deliveryFee : undefined,
      walletAmountUsed: this.useWallet ? this.walletAmountToUse : undefined,
      tableNumber: this.orderType === 'dine-in' && this.tableNumber ? this.tableNumber : undefined
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
        this.router.navigate(['/orders', order.id]);
      },
      error: (error) => {
        console.error('Error placing order:', error);
        this.errorMessage = error.error?.error || 'Failed to place order. Please try again.';
        this.isSubmitting = false;
      }
    });
  }

  // Order type change
  onOrderTypeChange() {
    if (this.orderType === 'delivery') {
      this.calculateDeliveryFee();
    } else {
      this.deliveryFee = 0;
    }
  }

  calculateDeliveryFee() {
    if (this.orderType !== 'delivery' || !this.deliveryAddress.trim()) {
      this.deliveryFee = 0;
      return;
    }
    this.calculatingFee = true;
    this.deliveryZoneService.calculateDeliveryFee(0, this.cart.subtotal).subscribe({
      next: (res: any) => {
        this.deliveryFee = res.deliveryFee || 0;
        this.calculatingFee = false;
      },
      error: () => {
        this.deliveryFee = 0;
        this.calculatingFee = false;
      }
    });
  }

  toggleWallet() {
    this.useWallet = !this.useWallet;
    if (this.useWallet && this.walletData) {
      const totalBeforeWallet = this.cart.subtotal + this.cart.packagingCharges + this.taxAmount + this.platformChargeAmount + this.deliveryFee - this.couponDiscount - this.loyaltyDiscount;
      this.walletAmountToUse = Math.min(this.walletData.balance, Math.max(0, totalBeforeWallet));
    } else {
      this.walletAmountToUse = 0;
    }
  }

  onScheduleToggle() {
    if (this.scheduleOrder) {
      const now = new Date();
      this.minScheduleDate = getIstInputDate(now);
      const maxDate = new Date(now.getTime() + 7 * 24 * 60 * 60 * 1000);
      this.maxScheduleDate = getIstInputDate(maxDate);
      this.scheduledDate = '';
      this.scheduledTime = '';
      this.scheduleValidationError = '';
    } else {
      this.scheduledDate = '';
      this.scheduledTime = '';
      this.scheduleValidationError = '';
    }
  }

  validateSchedule(): boolean {
    if (!this.scheduleOrder) return true;
    if (!this.scheduledDate || !this.scheduledTime) {
      this.scheduleValidationError = 'Please select both date and time for scheduling.';
      return false;
    }
    const scheduled = new Date(`${this.scheduledDate}T${this.scheduledTime}:00`);
    const minTime = new Date(Date.now() + 30 * 60 * 1000); // 30 min from now
    if (scheduled < minTime) {
      this.scheduleValidationError = 'Scheduled time must be at least 30 minutes from now.';
      return false;
    }
    const maxTime = new Date(Date.now() + 7 * 24 * 60 * 60 * 1000);
    if (scheduled > maxTime) {
      this.scheduleValidationError = 'Cannot schedule more than 7 days in advance.';
      return false;
    }
    this.scheduleValidationError = '';
    return true;
  }

  getScheduledDateTime(): string | undefined {
    if (!this.scheduleOrder || !this.scheduledDate || !this.scheduledTime) return undefined;
    return `${this.scheduledDate}T${this.scheduledTime}:00`;
  }

  goBackToCart() {
    this.router.navigate(['/cart']);
  }

  trackByName(index: number, item: any): string { return item.name; }
}
