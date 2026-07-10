import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { CartService, Cart } from '../../services/cart.service';
import { OrderService, CreateOrderRequest, OutletSuggestion, DeliveryRouteQuote } from '../../services/order.service';
import { PaymentService } from '../../services/payment.service';
import { AddressService, DeliveryAddress, AddAddressRequest, UpdateAddressRequest } from '../../services/address.service';
import { AuthService } from '../../services/auth.service';
import { OffersService, OfferValidationResponse } from '../../services/offers.service';
import { LoyaltyService, LoyaltyAccount } from '../../services/loyalty.service';
import { DeliveryZoneService } from '../../services/delivery-zone.service';
import { OutletService } from '../../services/outlet.service';
import { Outlet } from '../../models/outlet.model';
import { UIStore } from '../../store/ui.store';
import { Subscription } from 'rxjs';
import { getIstInputDate } from '../../utils/date-utils';
import QRCode from 'qrcode';
import { DomSanitizer, SafeUrl } from '@angular/platform-browser';

@Component({
  selector: 'app-checkout',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './checkout.component.html',
  styleUrls: ['./checkout.component.scss']
})
export class CheckoutComponent implements OnInit, OnDestroy {
  private readonly checkoutDraftStorageKey = 'checkout_draft';
  private readonly pendingPaymentStorageKey = 'pending_payment_recovery';
  isRazorpayEnabled = false;
  isUpiQrEnabled = false;
  readonly fixedPlatformCharge = 2;

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
  paymentMethod: 'cod' | 'razorpay' | 'upi-qr' = 'cod';
  upiQrCodeDataUrl = '';
  upiPaymentLink = '';
  safeUpiPaymentLink: SafeUrl | null = null;
  upiConfigMissing = false;
  upiPaymentConfirmed = false;
  upiTransactionRef = '';
  private upiId = '';
  private upiPayeeName = 'Cafe';

  // Saved addresses
  savedAddresses: DeliveryAddress[] = [];
  selectedAddressId: string | null = null;
  showNewAddressForm = false;
  saveNewAddress = false;
  newAddressLabel = '';
  editingAddressId: string | null = null;
  editAddressForm: UpdateAddressRequest = {};
  savingAddressEdit = false;

  isSubmitting = false;
  errorMessage = '';
  attemptedSubmit = false;
  showReviewStep = false;
  private cartSub?: Subscription;
  private deliveryAddressDebounce?: ReturnType<typeof setTimeout>;

  // Coupon state
  couponCode = '';
  couponDiscount = 0;
  couponMessage = '';
  couponValid = false;
  validatingCoupon = false;
  appliedCouponOfferId = '';
  private lastCouponValidationKey = '';

  // Loyalty state
  loyaltyAccount: LoyaltyAccount | null = null;
  useLoyaltyPoints = false;
  loyaltyPointsToUse = 0;
  maxLoyaltyDiscount = 0;
  loyaltyLoaded = false;

  // Order type
  orderType: 'delivery' | 'pickup' | 'dine-in' = 'delivery';
  tableNumber = '';

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
  deliveryEtaMinutes: number | null = null;
  deliveryZoneName = '';
  approximateDistanceKm: number | null = null;
  feeConfidence: 'high' | 'medium' | 'low' | null = null;
  outOfZoneDetected = false;
  outOfZoneMessage = '';
  suggestedOutlets: OutletSuggestion[] = [];
  routeQuoteLoading = false;
  routeQuote: DeliveryRouteQuote | null = null;

  // Pending payment recovery
  pendingPaymentRecovery: {
    amount: number;
    reason: string;
    timestamp: string;
  } | null = null;

  // Computed totals
  get taxAmount(): number {
    return 0;
  }

  get platformChargeAmount(): number {
    return this.cart.itemCount > 0 ? this.fixedPlatformCharge : 0;
  }

  get loyaltyDiscount(): number {
    if (!this.useLoyaltyPoints || !this.loyaltyAccount) return 0;
    return Math.round(this.loyaltyPointsToUse * 0.25 * 100) / 100;
  }

  get loyaltyBalancePoints(): number {
    return this.loyaltyAccount?.currentPoints || 0;
  }

  get loyaltyBalanceValue(): number {
    return Math.round(this.loyaltyBalancePoints * 0.25 * 100) / 100;
  }

  get canRedeemLoyalty(): boolean {
    return this.loyaltyBalancePoints > 0;
  }

  get grandTotal(): number {
    const raw = this.cart.subtotal + this.cart.packagingCharges + this.taxAmount + this.platformChargeAmount + this.deliveryFee - this.couponDiscount - this.loyaltyDiscount;
    return Math.round(Math.max(0, raw) * 100) / 100;
  }

  constructor(
    private cartService: CartService,
    private orderService: OrderService,
    private paymentService: PaymentService,
    private addressService: AddressService,
    private authService: AuthService,
    private offersService: OffersService,
    private loyaltyService: LoyaltyService,
    private deliveryZoneService: DeliveryZoneService,
    private outletService: OutletService,
    private uiStore: UIStore,
    private router: Router,
    private sanitizer: DomSanitizer
  ) {}

  ngOnInit() {
    this.loadUpiRuntimeConfig();
    this.loadPendingPaymentRecovery();
    this.loadCheckoutDraft();
    this.ensureSupportedPaymentMethod();
    this.refreshOutletSuggestions();

    this.cartSub = this.cartService.cart$.subscribe(cart => {
      this.cart = cart;
      if (cart.items.length === 0) {
        this.router.navigate(['/cart']);
      }

      this.recomputeCheckoutAdjustments();
      this.saveCheckoutDraft();
      this.revalidateCouponIfNeeded();

      if (this.orderType === 'delivery' && this.deliveryAddress.trim()) {
        this.calculateDeliveryFee();
      }

      if (this.paymentMethod === 'upi-qr') {
        this.refreshUpiQrCode();
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
        error: () => {
          this.uiStore.warning('Could not load saved addresses. You can still add a new address.');
        }
      });

      // Load loyalty account
      this.loyaltyService.getLoyaltyAccount().subscribe({
        next: (account) => {
          this.loyaltyAccount = account;
          this.loyaltyLoaded = true;
          this.recomputeCheckoutAdjustments();
        },
        error: () => {
          this.loyaltyLoaded = true;
          this.recomputeCheckoutAdjustments();
        }
      });

    }
  }

  ngOnDestroy() {
    this.cartSub?.unsubscribe();
    if (this.deliveryAddressDebounce) {
      clearTimeout(this.deliveryAddressDebounce);
    }
  }

  get deliveryAddressError(): string {
    if (this.orderType !== 'delivery' || !this.attemptedSubmit) return '';
    return !this.deliveryAddress.trim() ? 'Delivery address is required for delivery orders.' : '';
  }

  get phoneError(): string {
    if (!this.attemptedSubmit) return '';
    const normalizedPhone = this.phoneNumber.trim().replace(/\s/g, '');
    const phoneRegex = /^[0-9]{10}$/;

    if (this.orderType === 'delivery') {
      if (!normalizedPhone) return 'Phone number is required for delivery.';
      if (!phoneRegex.test(normalizedPhone)) return 'Enter a valid 10-digit phone number.';
      return '';
    }

    if (normalizedPhone && !phoneRegex.test(normalizedPhone)) {
      return 'Enter a valid 10-digit phone number.';
    }

    return '';
  }

  get tableNumberError(): string {
    if (!this.attemptedSubmit || this.orderType !== 'dine-in') return '';
    return !this.tableNumber.trim() ? 'Table number is required for dine-in orders.' : '';
  }

  get saveAddressLabelError(): string {
    if (!this.attemptedSubmit || this.orderType !== 'delivery') return '';
    if (!this.showNewAddressForm || !this.saveNewAddress) return '';
    return !this.newAddressLabel.trim() ? 'Add a label to save this address.' : '';
  }

  selectSavedAddress(addressId: string): void {
    this.selectedAddressId = addressId;
    this.showNewAddressForm = false;
    this.editingAddressId = null;
    const addr = this.savedAddresses.find(a => a.id === addressId);
    if (addr) {
      this.deliveryAddress = addr.fullAddress;
      this.phoneNumber = addr.collectorPhone;
      if (this.orderType === 'delivery') {
        this.calculateDeliveryFee();
        this.refreshDeliveryRouteQuote();
      }
    }
  }

  useNewAddress(): void {
    this.selectedAddressId = null;
    this.showNewAddressForm = true;
    this.editingAddressId = null;
    this.deliveryAddress = '';
    this.phoneNumber = '';
    this.deliveryFee = 0;
    this.deliveryEtaMinutes = null;
    this.deliveryZoneName = '';
    this.approximateDistanceKm = null;
    this.feeConfidence = null;
    this.outOfZoneDetected = false;
    this.outOfZoneMessage = '';
    this.routeQuote = null;
    this.recomputeCheckoutAdjustments();
    this.saveCheckoutDraft();
    if (this.paymentMethod === 'upi-qr') {
      this.refreshUpiQrCode();
    }
  }

  onDeliveryAddressInputChange(): void {
    if (this.orderType !== 'delivery') return;
    if (this.deliveryAddressDebounce) {
      clearTimeout(this.deliveryAddressDebounce);
    }
    this.deliveryAddressDebounce = setTimeout(() => {
      this.calculateDeliveryFee();
      this.refreshOutletSuggestions();
      this.saveCheckoutDraft();
    }, 350);
  }

  onInlineFieldChange(): void {
    this.saveCheckoutDraft();
  }

  onCheckoutSubmit(): void {
    this.attemptedSubmit = true;
    this.errorMessage = '';

    const validationError = this.getValidationError();
    if (validationError) {
      this.errorMessage = validationError;
      return;
    }

    this.showReviewStep = true;
  }

  cancelReviewStep(): void {
    this.showReviewStep = false;
  }

  confirmAndPlaceOrder(): void {
    this.showReviewStep = false;
    this.placeOrder();
  }

  startEditAddress(address: DeliveryAddress, event: Event): void {
    event.stopPropagation();
    this.editingAddressId = address.id;
    this.editAddressForm = {
      label: address.label,
      fullAddress: address.fullAddress,
      city: address.city,
      pinCode: address.pinCode,
      collectorName: address.collectorName,
      collectorPhone: address.collectorPhone,
      isDefault: address.isDefault
    };
  }

  cancelEditAddress(event?: Event): void {
    event?.stopPropagation();
    this.editingAddressId = null;
    this.editAddressForm = {};
  }

  saveAddressEdit(addressId: string, event: Event): void {
    event.stopPropagation();
    if (this.savingAddressEdit) return;

    if (!this.editAddressForm.fullAddress?.trim()) {
      this.uiStore.error('Address cannot be empty');
      return;
    }

    if (!this.editAddressForm.collectorPhone?.trim()) {
      this.uiStore.error('Collector phone is required');
      return;
    }

    this.savingAddressEdit = true;
    this.addressService.updateAddress(addressId, this.editAddressForm).subscribe({
      next: () => {
        const idx = this.savedAddresses.findIndex(a => a.id === addressId);
        if (idx >= 0) {
          this.savedAddresses[idx] = {
            ...this.savedAddresses[idx],
            ...this.editAddressForm,
            id: this.savedAddresses[idx].id,
            createdAt: this.savedAddresses[idx].createdAt
          } as DeliveryAddress;
        }

        if (this.selectedAddressId === addressId) {
          this.selectSavedAddress(addressId);
        }

        this.savingAddressEdit = false;
        this.cancelEditAddress();
        this.uiStore.success('Address updated');
      },
      error: () => {
        this.savingAddressEdit = false;
        this.uiStore.error('Failed to update address');
      }
    });
  }

  placeOrder() {
    const validationError = this.getValidationError();
    if (validationError) {
      this.errorMessage = validationError;
      return;
    }

    this.isSubmitting = true;
    this.errorMessage = '';

    if (this.paymentMethod === 'upi-qr') {
      if (this.upiConfigMissing) {
        this.errorMessage = 'UPI QR is not configured yet. Please use Pay Online or Cash on Delivery.';
        this.isSubmitting = false;
        return;
      }

      if (!this.upiPaymentConfirmed) {
        this.errorMessage = 'Please confirm UPI payment completion before placing the order.';
        this.isSubmitting = false;
        return;
      }
    }

    if (this.paymentMethod === 'razorpay') {
      this.processRazorpayPayment();
    } else {
      this.submitOrder();
    }
  }

  private getValidationError(): string {
    const normalizedPhone = this.phoneNumber.trim().replace(/\s/g, '');
    const phoneRegex = /^[0-9]{10}$/;

    if (this.orderType === 'delivery') {
      if (!this.deliveryAddress.trim()) {
        return 'Please enter a delivery address';
      }

      if (!normalizedPhone) {
        return 'Please enter a phone number';
      }

      if (!phoneRegex.test(normalizedPhone)) {
        return 'Please enter a valid 10-digit phone number';
      }
    } else {
      if (normalizedPhone && !phoneRegex.test(normalizedPhone)) {
        return 'Please enter a valid 10-digit phone number';
      }
    }

    if (this.orderType === 'dine-in' && !this.tableNumber.trim()) {
      return 'Please enter your table number';
    }

    if (this.orderType === 'delivery' && this.showNewAddressForm && this.saveNewAddress && !this.newAddressLabel.trim()) {
      return 'Please add an address label before saving this address';
    }

    if (!this.validateSchedule()) {
      return this.scheduleValidationError;
    }

    return '';
  }

  // Coupon methods
  applyCoupon() {
    const normalizedCouponCode = this.couponCode.trim().toUpperCase();
    if (!normalizedCouponCode) return;

    this.couponCode = normalizedCouponCode;
    this.validatingCoupon = true;
    this.couponMessage = '';
    this.offersService.validateOffer({
      code: normalizedCouponCode,
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
          this.appliedCouponOfferId = '';
        }

        this.lastCouponValidationKey = this.buildCouponValidationKey();

        this.recomputeCheckoutAdjustments();
        this.saveCheckoutDraft();

        if (this.paymentMethod === 'upi-qr') {
          this.refreshUpiQrCode();
        }
      },
      error: (error) => {
        this.validatingCoupon = false;
        this.couponValid = false;
        this.couponDiscount = 0;
        const serverError = this.extractServerErrorMessage(error);
        this.couponMessage = serverError || 'Failed to validate coupon';
        this.appliedCouponOfferId = '';
        this.recomputeCheckoutAdjustments();
        this.saveCheckoutDraft();

        if (this.paymentMethod === 'upi-qr') {
          this.refreshUpiQrCode();
        }
      }
    });
  }

  removeCoupon() {
    this.couponCode = '';
    this.couponDiscount = 0;
    this.couponMessage = '';
    this.couponValid = false;
    this.appliedCouponOfferId = '';
    this.lastCouponValidationKey = '';
    this.recomputeCheckoutAdjustments();
    this.saveCheckoutDraft();

    if (this.paymentMethod === 'upi-qr') {
      this.refreshUpiQrCode();
    }
  }

  // Loyalty methods
  toggleLoyaltyPoints() {
    if (this.useLoyaltyPoints && this.loyaltyAccount) {
      this.loyaltyPointsToUse = this.loyaltyAccount.currentPoints;
    } else {
      this.loyaltyPointsToUse = 0;
    }

    this.recomputeCheckoutAdjustments();
    this.saveCheckoutDraft();

    if (this.paymentMethod === 'upi-qr') {
      this.refreshUpiQrCode();
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
              this.savePendingPaymentRecovery('Payment verification failed. Please retry payment.');
              this.errorMessage = 'Payment verification failed. Your payment will be refunded if charged.';
              this.isSubmitting = false;
            }
          });
        }).catch((error) => {
          this.savePendingPaymentRecovery(error?.message || 'Payment was cancelled or failed.');
          this.errorMessage = error.message || 'Payment was cancelled or failed';
          this.isSubmitting = false;
        });
      },
      error: (error) => {
        console.error('Error creating payment order:', error);
        this.savePendingPaymentRecovery('Could not initiate online payment. Please retry.');
        this.errorMessage = 'Failed to initiate payment. Please try again.';
        this.isSubmitting = false;
      }
    });
  }

  private submitOrder(razorpayPaymentId?: string, razorpayOrderId?: string, razorpaySignature?: string) {
    const paymentMethodForOrder: 'cod' | 'razorpay' | 'upi-qr' = this.paymentMethod;

    const upiRefText = this.paymentMethod === 'upi-qr'
      ? `UPI QR payment marked complete${this.upiTransactionRef.trim() ? ` | UTR/Ref: ${this.upiTransactionRef.trim()}` : ''}`
      : '';

    const orderRequest: CreateOrderRequest = {
      items: this.cart.items.map(item => ({
        menuItemId: item.menuItemId,
        quantity: item.quantity,
        selectedVariantName: item.selectedVariant?.variantName,
        selectedAddOnNames: item.selectedAddOns?.map(a => a.name) || []
      })),
      deliveryAddress: this.orderType === 'delivery' ? this.deliveryAddress.trim() : undefined,
      phoneNumber: this.phoneNumber.trim() || undefined,
      preparationNotes: this.cart.preparationNotes?.trim() || undefined,
      notes: [this.notes.trim(), upiRefText].filter(Boolean).join(' | ') || undefined,
      paymentMethod: paymentMethodForOrder,
      razorpayPaymentId,
      razorpayOrderId,
      razorpaySignature,
      couponCode: this.couponValid ? this.couponCode.trim() : undefined,
      loyaltyPointsUsed: this.useLoyaltyPoints ? this.loyaltyPointsToUse : undefined,
      orderType: this.orderType,
      channel: 'web',
      scheduledFor: this.getScheduledDateTime(),
      deliveryFee: this.orderType === 'delivery' ? this.deliveryFee : undefined,
      upiReference: this.paymentMethod === 'upi-qr' && this.upiTransactionRef.trim()
        ? this.upiTransactionRef.trim()
        : undefined,
      tableNumber: this.orderType === 'dine-in' && this.tableNumber.trim() ? this.tableNumber.trim() : undefined,
      outletId: this.outletService.getSelectedOutletId() || undefined
    };

    // Submit order
    this.orderService.createOrder(orderRequest).subscribe({
      next: (order) => {
        this.clearPendingPaymentRecovery();
        this.clearCheckoutDraft();

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
            error: () => this.uiStore.warning('Order placed, but address could not be saved.')
          });
        }

        this.cartService.clearCart();
        this.router.navigate(['/orders', order.id]);
      },
      error: (error) => {
        console.error('Error placing order:', error);
        this.errorMessage = this.extractServerErrorMessage(error) || 'Failed to place order. Please try again.';
        this.isSubmitting = false;
      }
    });
  }

  private extractServerErrorMessage(error: any): string {
    return error?.error?.error
      || error?.originalError?.error?.error
      || error?.message
      || error?.originalError?.message
      || '';
  }

  // Order type change
  onOrderTypeChange() {
    if (this.orderType === 'delivery') {
      this.calculateDeliveryFee();
      this.refreshDeliveryRouteQuote();
    } else {
      this.deliveryFee = 0;
      this.deliveryEtaMinutes = null;
      this.deliveryZoneName = '';
      this.feeConfidence = null;
      this.outOfZoneDetected = false;
      this.outOfZoneMessage = '';
      this.routeQuote = null;
      this.recomputeCheckoutAdjustments();
    }

    this.refreshOutletSuggestions();

    this.saveCheckoutDraft();

    if (this.paymentMethod === 'upi-qr') {
      this.refreshUpiQrCode();
    }
  }

  calculateDeliveryFee() {
    if (this.orderType !== 'delivery' || !this.deliveryAddress.trim()) {
      this.deliveryFee = 0;
      this.deliveryEtaMinutes = null;
      this.deliveryZoneName = '';
      this.approximateDistanceKm = null;
      this.feeConfidence = null;
      this.outOfZoneDetected = false;
      this.outOfZoneMessage = '';
      this.suggestedOutlets = [];
      this.routeQuote = null;
      return;
    }
    this.calculatingFee = true;
    this.deliveryZoneService.calculateDeliveryFee(this.cart.subtotal, this.deliveryAddress).subscribe({
      next: (res: any) => {
        this.deliveryFee = res.deliveryFee || 0;
        this.deliveryEtaMinutes = typeof res.estimatedMinutes === 'number' ? res.estimatedMinutes : null;
        this.deliveryZoneName = res.zone || '';
        this.approximateDistanceKm = typeof res.approximateDistanceKm === 'number' ? res.approximateDistanceKm : null;
        this.outOfZoneDetected = false;
        this.outOfZoneMessage = '';
        this.feeConfidence = res.feeConfidence || this.getFeeConfidence(res.deliveryFee || 0, res.freeDeliveryAbove || 0);
        this.refreshOutletSuggestions();
        this.refreshDeliveryRouteQuote();
        this.calculatingFee = false;
        this.recomputeCheckoutAdjustments();
        this.saveCheckoutDraft();

        if (this.paymentMethod === 'upi-qr') {
          this.refreshUpiQrCode();
        }
      },
      error: () => {
        this.deliveryFee = 0;
        this.deliveryEtaMinutes = null;
        this.deliveryZoneName = '';
        this.approximateDistanceKm = null;
        this.feeConfidence = null;
        this.outOfZoneDetected = true;
        this.outOfZoneMessage = 'Delivery could not be validated for this address yet. Please review nearest outlets or choose pickup.';
        this.refreshOutletSuggestions();
        this.refreshDeliveryRouteQuote();
        this.calculatingFee = false;
        this.recomputeCheckoutAdjustments();
        this.saveCheckoutDraft();

        if (this.paymentMethod === 'upi-qr') {
          this.refreshUpiQrCode();
        }
      }
    });
  }

  onPaymentMethodChange(method: 'cod' | 'razorpay' | 'upi-qr') {
    if (method === 'razorpay' && !this.isRazorpayEnabled) {
      this.paymentMethod = 'cod';
      this.uiStore.notify('Pay Online is currently disabled. Use Pay via QR or Cash on Delivery.');
      this.saveCheckoutDraft();
      return;
    }

    if (method === 'upi-qr' && !this.isUpiQrEnabled) {
      this.paymentMethod = 'cod';
      this.uiStore.notify('Pay via QR is currently unavailable. Please use Cash on Delivery.');
      this.saveCheckoutDraft();
      return;
    }

    this.paymentMethod = method;
    this.errorMessage = '';

    if (method === 'upi-qr') {
      this.upiPaymentConfirmed = false;
      this.refreshUpiQrCode();
    }

    this.saveCheckoutDraft();
  }

  retryPendingPayment(): void {
    this.errorMessage = '';
    this.showReviewStep = false;
    this.attemptedSubmit = true;

    const validationError = this.getValidationError();
    if (validationError) {
      this.errorMessage = validationError;
      return;
    }

    this.paymentMethod = 'razorpay';
    this.isSubmitting = true;
    this.processRazorpayPayment();
  }

  private recomputeCheckoutAdjustments(): void {
    if (this.useLoyaltyPoints && this.loyaltyAccount && this.loyaltyAccount.currentPoints > 0) {
      const maxDiscount = Math.max(0, this.cart.subtotal + this.taxAmount - this.couponDiscount);
      const maxPointsByTotal = Math.floor(maxDiscount / 0.25);
      const maxPointsAllowed = Math.min(this.loyaltyAccount.currentPoints, Math.max(0, maxPointsByTotal));

      this.loyaltyPointsToUse = Math.min(this.loyaltyPointsToUse || maxPointsAllowed, maxPointsAllowed);
      this.maxLoyaltyDiscount = Math.round(this.loyaltyPointsToUse * 0.25 * 100) / 100;
    } else {
      this.useLoyaltyPoints = false;
      this.loyaltyPointsToUse = 0;
      this.maxLoyaltyDiscount = 0;
    }

  }

  private buildCouponValidationKey(): string {
    const categoriesKey = this.cart.items
      .map(i => i.categoryName || '')
      .filter(Boolean)
      .sort()
      .join('|');

    return `${this.couponCode.trim().toUpperCase()}::${this.cart.subtotal}::${categoriesKey}`;
  }

  private revalidateCouponIfNeeded(): void {
    if (!this.couponCode.trim() || this.validatingCoupon) return;

    const nextKey = this.buildCouponValidationKey();
    if (nextKey === this.lastCouponValidationKey) return;

    this.validatingCoupon = true;
    this.offersService.validateOffer({
      code: this.couponCode.trim().toUpperCase(),
      orderAmount: this.cart.subtotal,
      categories: this.cart.items.map(i => i.categoryName).filter((c): c is string => !!c)
    }).subscribe({
      next: (res: OfferValidationResponse) => {
        this.validatingCoupon = false;
        this.couponValid = res.isValid;
        this.couponDiscount = res.isValid ? res.discountAmount : 0;
        this.couponMessage = res.message || (res.isValid ? 'Coupon applied!' : 'Coupon no longer applicable for current cart');
        this.appliedCouponOfferId = res.isValid ? (res.offer?.id || '') : '';
        this.lastCouponValidationKey = this.buildCouponValidationKey();

        this.recomputeCheckoutAdjustments();
        this.saveCheckoutDraft();
      },
      error: (error) => {
        this.validatingCoupon = false;
        this.couponValid = false;
        this.couponDiscount = 0;
        const serverError = this.extractServerErrorMessage(error);
        this.couponMessage = serverError || 'Failed to validate coupon';
        this.appliedCouponOfferId = '';
        this.lastCouponValidationKey = '';

        this.recomputeCheckoutAdjustments();
        this.saveCheckoutDraft();
      }
    });
  }

  private getFeeConfidence(deliveryFee: number, freeDeliveryAbove: number): 'high' | 'medium' | 'low' {
    if (deliveryFee <= 0) return 'high';
    if (freeDeliveryAbove > 0 && this.cart.subtotal >= freeDeliveryAbove * 0.8) return 'medium';
    return 'low';
  }

  selectSuggestedOutlet(suggestion: OutletSuggestion): void {
    const outlet: Outlet = {
      id: suggestion.outletId,
      outletCode: suggestion.outletCode,
      outletName: suggestion.outletName,
      address: suggestion.address,
      city: suggestion.city,
      state: suggestion.state,
      isActive: true,
      settings: {
        openingTime: '08:00',
        closingTime: '22:00',
        acceptsOnlineOrders: true,
        acceptsDineIn: true,
        acceptsTakeaway: true,
        taxPercentage: 5
      }
    };

    this.outletService.selectOutlet(outlet);
    this.outOfZoneDetected = false;
    this.outOfZoneMessage = `Selected ${suggestion.outletName}. Delivery details are being recalculated.`;
    this.calculateDeliveryFee();
    this.saveCheckoutDraft();
  }

  private refreshOutletSuggestions(): void {
    this.orderService.getOutletSuggestions(this.orderType, this.deliveryAddress, this.cart.subtotal).subscribe({
      next: (suggestions) => {
        this.suggestedOutlets = suggestions || [];
      },
      error: () => {
        this.suggestedOutlets = [];
      }
    });
  }

  private refreshDeliveryRouteQuote(): void {
    const address = this.deliveryAddress.trim();
    if (this.orderType !== 'delivery' || !address || address.length < 10) {
      this.routeQuote = null;
      this.routeQuoteLoading = false;
      return;
    }

    const outletId = this.outletService.getSelectedOutletId() || undefined;
    this.routeQuoteLoading = true;

    this.orderService.getDeliveryRouteQuote(address, outletId).subscribe({
      next: (quote) => {
        this.routeQuote = quote;
        if (typeof quote.distanceKm === 'number') {
          this.approximateDistanceKm = quote.distanceKm;
        }
        if (typeof quote.etaMinutes === 'number') {
          this.deliveryEtaMinutes = quote.etaMinutes;
        }
        this.routeQuoteLoading = false;
      },
      error: () => {
        this.routeQuote = null;
        this.routeQuoteLoading = false;
      }
    });
  }

  private savePendingPaymentRecovery(reason: string): void {
    const snapshot = {
      amount: this.grandTotal,
      reason,
      timestamp: new Date().toISOString()
    };
    this.pendingPaymentRecovery = snapshot;
    localStorage.setItem(this.pendingPaymentStorageKey, JSON.stringify(snapshot));
  }

  private loadPendingPaymentRecovery(): void {
    const raw = localStorage.getItem(this.pendingPaymentStorageKey);
    if (!raw) return;
    try {
      this.pendingPaymentRecovery = JSON.parse(raw);
    } catch {
      this.pendingPaymentRecovery = null;
      localStorage.removeItem(this.pendingPaymentStorageKey);
    }
  }

  private clearPendingPaymentRecovery(): void {
    this.pendingPaymentRecovery = null;
    localStorage.removeItem(this.pendingPaymentStorageKey);
  }

  private saveCheckoutDraft(): void {
    if (!this.cart.items.length) {
      this.clearCheckoutDraft();
      return;
    }

    const draft = {
      deliveryAddress: this.deliveryAddress,
      phoneNumber: this.phoneNumber,
      notes: this.notes,
      orderType: this.orderType,
      tableNumber: this.tableNumber,
      scheduleOrder: this.scheduleOrder,
      scheduledDate: this.scheduledDate,
      scheduledTime: this.scheduledTime,
      selectedAddressId: this.selectedAddressId,
      showNewAddressForm: this.showNewAddressForm,
      saveNewAddress: this.saveNewAddress,
      newAddressLabel: this.newAddressLabel,
      couponCode: this.couponCode,
      couponDiscount: this.couponDiscount,
      couponValid: this.couponValid,
      useLoyaltyPoints: this.useLoyaltyPoints,
      paymentMethod: this.paymentMethod,
      cartItemCount: this.cart.itemCount,
      timestamp: new Date().toISOString(),
      grandTotal: this.grandTotal
    };

    localStorage.setItem(this.checkoutDraftStorageKey, JSON.stringify(draft));
  }

  private loadCheckoutDraft(): void {
    const raw = localStorage.getItem(this.checkoutDraftStorageKey);
    if (!raw) return;

    try {
      const draft = JSON.parse(raw);
      this.deliveryAddress = draft.deliveryAddress || '';
      this.phoneNumber = draft.phoneNumber || '';
      this.notes = draft.notes || '';
      this.orderType = draft.orderType || 'delivery';
      this.tableNumber = draft.tableNumber || '';
      this.scheduleOrder = !!draft.scheduleOrder;
      this.scheduledDate = draft.scheduledDate || '';
      this.scheduledTime = draft.scheduledTime || '';
      this.selectedAddressId = draft.selectedAddressId || null;
      this.showNewAddressForm = !!draft.showNewAddressForm;
      this.saveNewAddress = !!draft.saveNewAddress;
      this.newAddressLabel = draft.newAddressLabel || '';
      this.couponCode = draft.couponCode || '';
      this.couponDiscount = Number(draft.couponDiscount || 0);
      this.couponValid = !!draft.couponValid;
      this.useLoyaltyPoints = !!draft.useLoyaltyPoints;
      this.paymentMethod = draft.paymentMethod || 'cod';
      this.ensureSupportedPaymentMethod();
    } catch {
      this.clearCheckoutDraft();
    }
  }

  private ensureSupportedPaymentMethod(): void {
    if (this.paymentMethod === 'razorpay' && !this.isRazorpayEnabled) {
      this.paymentMethod = 'cod';
      return;
    }

    if (this.paymentMethod === 'upi-qr' && !this.isUpiQrEnabled) {
      this.paymentMethod = 'cod';
    }
  }

  private clearCheckoutDraft(): void {
    localStorage.removeItem(this.checkoutDraftStorageKey);
  }

  openUpiApp(): void {
    if (!this.upiPaymentLink) {
      this.uiStore.warning('UPI payment link is unavailable right now. Please retry in a moment.');
      return;
    }

    const userAgent = typeof navigator !== 'undefined' ? navigator.userAgent.toLowerCase() : '';
    const isMobile = /android|iphone|ipad|ipod|mobile/.test(userAgent);

    if (!isMobile) {
      this.uiStore.notify('UPI app launch works on mobile devices. Please scan the QR with a UPI app on your phone.');
      return;
    }

    try {
      window.location.href = this.upiPaymentLink;
    } catch {
      this.uiStore.warning('Could not open UPI app automatically. Please scan the QR manually.');
    }
  }

  private async refreshUpiQrCode() {
    if (!this.upiId || !this.upiId.includes('@')) {
      this.upiConfigMissing = true;
      this.upiPaymentLink = '';
      this.safeUpiPaymentLink = null;
      this.upiQrCodeDataUrl = '';
      return;
    }

    this.upiConfigMissing = false;
    const amount = this.grandTotal.toFixed(2);
    const transactionRef = `CAFE-${Date.now()}`;
    const note = `Cafe order payment`;

    const upiUrl = `upi://pay?pa=${encodeURIComponent(this.upiId)}&pn=${encodeURIComponent(this.upiPayeeName)}&tr=${encodeURIComponent(transactionRef)}&tn=${encodeURIComponent(note)}&am=${encodeURIComponent(amount)}&cu=INR`;
    this.upiPaymentLink = upiUrl;
    this.safeUpiPaymentLink = this.sanitizer.bypassSecurityTrustUrl(upiUrl);

    try {
      this.upiQrCodeDataUrl = await QRCode.toDataURL(upiUrl, {
        width: 240,
        margin: 1,
        errorCorrectionLevel: 'M'
      });
    } catch {
      this.upiQrCodeDataUrl = '';
      this.upiConfigMissing = true;
    }
  }

  private loadUpiRuntimeConfig() {
    this.paymentService.getUpiConfig().subscribe({
      next: (config) => {
        const normalizedUpiId = (config.upiId || '').trim();
        const hasValidUpiId = normalizedUpiId.includes('@');

        this.upiId = normalizedUpiId;
        this.upiPayeeName = (config.payeeName || 'Cafe').trim() || 'Cafe';
        this.isRazorpayEnabled = !!config.razorpayEnabled;
        this.isUpiQrEnabled = !!config.upiQrEnabled;
        this.upiConfigMissing = this.isUpiQrEnabled ? !(!!config.configured || hasValidUpiId) : true;
        this.ensureSupportedPaymentMethod();

        if (this.paymentMethod === 'upi-qr' && this.isUpiQrEnabled && !this.upiConfigMissing) {
          this.refreshUpiQrCode();
        }
      },
      error: () => {
        this.upiId = '';
        this.upiPayeeName = 'Cafe';
        this.isRazorpayEnabled = false;
        this.isUpiQrEnabled = false;
        this.upiConfigMissing = true;
        this.ensureSupportedPaymentMethod();
      }
    });
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
