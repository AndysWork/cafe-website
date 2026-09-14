import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { DineInService } from '../../services/dine-in.service';
import { DineInBill, SettleDineInBillRequest } from '../../models/dine-in.model';
import { PaymentService } from '../../services/payment.service';
import { OffersService, Offer } from '../../services/offers.service';
import { LoyaltyService, LoyaltyAccount } from '../../services/loyalty.service';
import { AuthService } from '../../services/auth.service';
import { UIStore } from '../../store/ui.store';

@Component({
  selector: 'app-dine-in-bill-modal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './dine-in-bill-modal.component.html',
  styleUrls: ['./dine-in-bill-modal.component.scss']
})
export class DineInBillModalComponent implements OnInit, OnDestroy {
  public dineInService = inject(DineInService);
  private router = inject(Router);
  private paymentService = inject(PaymentService);
  private offersService = inject(OffersService);
  private loyaltyService = inject(LoyaltyService);
  public authService = inject(AuthService);
  private uiStore = inject(UIStore);

  isOpen = false;
  bill: DineInBill | null = null;
  tableNumber = '';
  loading = false;
  requestingBill = false;
  settlingBill = false;
  applyingCoupon = false;
  removingCoupon = false;

  availableOffers: Offer[] = [];
  loadingOffers = false;
  loyaltyAccount: LoyaltyAccount | null = null;
  loadingLoyalty = false;

  couponInput = '';
  couponMessage = '';
  selectedPaymentMethod: 'upi-qr' | 'razorpay' | 'cash_at_counter' = 'upi-qr';
  upiTransactionRef = '';
  qrCodeUrl = '';

  private subs: Subscription[] = [];

  ngOnInit(): void {
    this.subs.push(
      this.dineInService.isBillModalOpen$.subscribe(open => {
        this.isOpen = open;
        if (open) {
          this.refresh();
        }
      }),
      this.dineInService.activeBill$.subscribe(bill => {
        this.bill = bill;
        if (bill?.tableNumber) {
          this.tableNumber = bill.tableNumber;
        }
        if (bill?.couponCode) {
          this.couponInput = bill.couponCode;
        }
        if (bill?.upiQrString) {
          this.qrCodeUrl = `https://api.qrserver.com/v1/create-qr-code/?size=220x220&data=${encodeURIComponent(bill.upiQrString)}`;
        }
      }),
      this.dineInService.activeTable$.subscribe(table => {
        if (!this.tableNumber) {
          this.tableNumber = table;
        }
      })
    );
  }

  ngOnDestroy(): void {
    this.subs.forEach(s => s.unsubscribe());
  }

  close(): void {
    this.dineInService.closeBillModal();
  }

  get canApplyCoupon(): boolean {
    return this.bill?.status === 'active';
  }

  isOfferEligible(offer: Offer): boolean {
    return (this.bill?.subtotal || 0) >= (offer.minOrderAmount || 0);
  }

  getSpendMoreAmount(offer: Offer): number {
    return Math.max(0, (offer.minOrderAmount || 0) - (this.bill?.subtotal || 0));
  }

  get estimatedPointsToEarn(): number {
    if (this.bill?.estimatedPointsToEarn !== undefined) {
      return this.bill.estimatedPointsToEarn;
    }
    return Math.max(0, Math.floor((this.bill?.grandTotal || 0) * 0.10));
  }

  refresh(): void {
    this.loading = true;
    this.loadAvailableOffers();
    this.loadLoyaltyAccount();
    this.dineInService.refreshActiveSession().subscribe({
      next: () => {
        this.loading = false;
      },
      error: () => {
        this.loading = false;
      }
    });
  }

  loadAvailableOffers(): void {
    this.loadingOffers = true;
    this.offersService.getActiveOffers().subscribe({
      next: (offers) => {
        this.availableOffers = (offers || [])
          .filter(o => o.isActive !== false && !!o.code)
          .sort((a, b) => (a.title || '').localeCompare(b.title || ''));
        this.loadingOffers = false;
      },
      error: () => {
        this.availableOffers = [];
        this.loadingOffers = false;
      }
    });
  }

  loadLoyaltyAccount(): void {
    if (!this.authService.isLoggedIn()) {
      this.loyaltyAccount = null;
      return;
    }
    this.loadingLoyalty = true;
    this.loyaltyService.getLoyaltyAccount().subscribe({
      next: (account) => {
        this.loyaltyAccount = account;
        this.loadingLoyalty = false;
      },
      error: () => {
        this.loyaltyAccount = null;
        this.loadingLoyalty = false;
      }
    });
  }

  orderMore(): void {
    this.close();
    this.router.navigate(['/menu'], { queryParams: { table: this.tableNumber || this.dineInService.currentTable } });
  }

  onRequestFinalBill(): void {
    if (!this.bill?.sessionId) return;

    const confirmMsg = this.bill.couponCode
      ? `Request final bill for Table ${this.bill.tableNumber} with coupon ${this.bill.couponCode} applied? After requesting final bill, coupons and item ordering will be locked.`
      : `Request final bill for Table ${this.bill.tableNumber}? (Note: Coupon codes cannot be applied after final bill is requested).`;

    if (!confirm(confirmMsg)) {
      return;
    }

    this.requestingBill = true;
    this.dineInService.requestFinalBill(this.bill.sessionId).subscribe({
      next: (res) => {
        this.requestingBill = false;
        this.uiStore.success('Final bill generated! Please choose your preferred payment option.');
      },
      error: (err) => {
        this.requestingBill = false;
        this.uiStore.error(err?.error?.error || 'Failed to request final bill');
      }
    });
  }

  onSelectCoupon(code?: string): void {
    if (!code || !this.canApplyCoupon || this.applyingCoupon) return;
    if (this.bill?.couponCode === code) return;
    this.couponInput = code;
    this.applyCoupon();
  }

  applyCoupon(): void {
    if (!this.bill?.sessionId || !this.couponInput.trim()) return;

    if (!this.canApplyCoupon) {
      this.uiStore.warning('Coupons can only be applied before requesting the final bill');
      return;
    }

    this.applyingCoupon = true;
    this.couponMessage = '';
    this.dineInService.applyCoupon(this.bill.sessionId, this.couponInput.trim()).subscribe({
      next: (res) => {
        this.applyingCoupon = false;
        this.couponMessage = res.message || 'Coupon applied successfully!';
        this.uiStore.success(this.couponMessage);
      },
      error: (err) => {
        this.applyingCoupon = false;
        this.couponMessage = err?.error?.error || 'Invalid coupon code';
        this.uiStore.error(this.couponMessage);
      }
    });
  }

  onRemoveCoupon(): void {
    if (!this.bill?.sessionId || !this.canApplyCoupon) return;

    this.removingCoupon = true;
    this.dineInService.removeCoupon(this.bill.sessionId).subscribe({
      next: (res) => {
        this.removingCoupon = false;
        this.couponInput = '';
        this.couponMessage = '';
        this.uiStore.success('Coupon removed');
      },
      error: (err) => {
        this.removingCoupon = false;
        this.uiStore.error(err?.error?.error || 'Failed to remove coupon');
      }
    });
  }

  copyUpiId(): void {
    if (this.bill?.upiId && typeof navigator !== 'undefined' && navigator.clipboard) {
      navigator.clipboard.writeText(this.bill.upiId);
      this.uiStore.notify('UPI ID copied to clipboard', 'info');
    }
  }

  onSettleBill(): void {
    if (!this.bill?.sessionId) return;

    this.settlingBill = true;
    const req: SettleDineInBillRequest = {
      paymentMethod: this.selectedPaymentMethod,
      upiReference: this.selectedPaymentMethod === 'upi-qr' ? this.upiTransactionRef.trim() : undefined
    };

    if (this.selectedPaymentMethod === 'razorpay') {
      this.handleRazorpayPayment();
      return;
    }

    this.dineInService.settleBill(this.bill.sessionId, req).subscribe({
      next: (res) => {
        this.settlingBill = false;
        if (this.selectedPaymentMethod === 'cash_at_counter') {
          this.uiStore.notify('Cash settlement requested! Staff have been notified to collect payment at your table or counter.', 'info');
        } else {
          this.uiStore.success('Payment recorded successfully! Thank you for dining with us.');
        }
      },
      error: (err) => {
        this.settlingBill = false;
        this.uiStore.error(err?.error?.error || 'Failed to settle bill');
      }
    });
  }

  private handleRazorpayPayment(): void {
    if (!this.bill) return;

    const amountInPaise = Math.round(this.bill.grandTotal * 100);
    this.paymentService.createPaymentOrder(amountInPaise, `dinein_${this.bill.sessionId}`).subscribe({
      next: (razorpayOrder) => {
        const options = {
          key: razorpayOrder.keyId,
          amount: razorpayOrder.amount,
          currency: 'INR',
          name: 'Maa Tara Cafe',
          description: `Table ${this.bill?.tableNumber} Dine-In Bill`,
          order_id: razorpayOrder.orderId,
          handler: (response: any) => {
            const req: SettleDineInBillRequest = {
              paymentMethod: 'razorpay',
              razorpayOrderId: response.razorpay_order_id,
              razorpayPaymentId: response.razorpay_payment_id,
              razorpaySignature: response.razorpay_signature
            };
            this.dineInService.settleBill(this.bill!.sessionId, req).subscribe({
              next: () => {
                this.settlingBill = false;
                this.uiStore.success('Payment verified! Table bill settled.');
              },
              error: () => {
                this.settlingBill = false;
                this.uiStore.error('Payment verification failed');
              }
            });
          },
          prefill: {
            name: this.bill?.customerName || '',
            contact: this.bill?.customerPhone || ''
          },
          theme: {
            color: '#E23744'
          }
        };

        if (typeof (window as any).Razorpay !== 'undefined') {
          const rzp = new (window as any).Razorpay(options);
          rzp.open();
        } else {
          this.settlingBill = false;
          this.uiStore.warning('Razorpay SDK not loaded. Please pay using UPI QR or Cash.');
        }
      },
      error: (err) => {
        this.settlingBill = false;
        this.uiStore.error('Could not initiate online payment gateway');
      }
    });
  }

  printReceipt(): void {
    if (typeof window !== 'undefined') {
      window.print();
    }
  }

  finishDining(): void {
    this.dineInService.clearTableSession();
    this.close();
    this.router.navigate(['/']);
    this.uiStore.success('Table cleared. Have a wonderful day!');
  }
}
