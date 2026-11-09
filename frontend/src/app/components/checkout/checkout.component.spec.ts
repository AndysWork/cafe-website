import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { CheckoutComponent } from './checkout.component';
import { CartService } from '../../services/cart.service';
import { OrderService } from '../../services/order.service';
import { PaymentService } from '../../services/payment.service';
import { AddressService } from '../../services/address.service';
import { AuthService } from '../../services/auth.service';
import { OffersService } from '../../services/offers.service';
import { LoyaltyService } from '../../services/loyalty.service';
import { DeliveryZoneService } from '../../services/delivery-zone.service';
import { OutletService } from '../../services/outlet.service';
import { DineInService } from '../../services/dine-in.service';
import { UIStore } from '../../store/ui.store';

describe('CheckoutComponent presentation', () => {
  let fixture: ComponentFixture<CheckoutComponent>;
  let component: CheckoutComponent;

  beforeEach(async () => {
    spyOn(CheckoutComponent.prototype, 'ngOnInit');
    await TestBed.configureTestingModule({
      imports: [CheckoutComponent],
      providers: [
        provideRouter([]),
        ...[CartService, OrderService, PaymentService, AddressService, AuthService,
          OffersService, LoyaltyService, DeliveryZoneService, DineInService, UIStore]
          .map(service => ({ provide: service, useValue: {} })),
        { provide: OutletService, useValue: { getSelectedOutlet: () => ({ isActive: true, settings: {} }) } }
      ]
    }).compileComponents();
    fixture = TestBed.createComponent(CheckoutComponent);
    component = fixture.componentInstance;
    component.cart = {
      items: [{ menuItemId: 'momo', name: 'Veg Steamed Momo', price: 59, quantity: 1 }],
      subtotal: 59, packagingCharges: 0, total: 59, itemCount: 1
    };
    fixture.detectChanges();
    await fixture.whenStable();
  });

  it('renders the details before the live summary and retains real charges', () => {
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelector('h1')?.textContent).toContain('Complete Your Order');
    expect(root.querySelector('.checkout-content')?.firstElementChild?.classList.contains('form-card')).toBeTrue();
    expect(root.querySelector('.t-final')?.textContent).toContain('₹61');
    expect(root.querySelectorAll('.order-type-option').length).toBe(3);
    expect(root.querySelectorAll('.payment-option').length).toBe(2);
    expect(root.querySelector('.delivery-notice')?.textContent).not.toContain('Free delivery');
  });

  it('keeps schedule controls available', async () => {
    const checkbox: HTMLInputElement = fixture.nativeElement.querySelector('[name="scheduleOrder"]');
    checkbox.click();
    fixture.detectChanges();
    await fixture.whenStable();
    expect(component.scheduleOrder).toBeTrue();
    expect(fixture.nativeElement.querySelector('[name="scheduledDate"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('[name="scheduledTime"]')).not.toBeNull();
  });

  it('shows delivery validation without opening the review step', () => {
    component.onCheckoutSubmit();
    fixture.detectChanges();
    expect(component.showReviewStep).toBeFalse();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain('delivery address');
  });

  it('opens the review for pickup without placing an order', () => {
    component.orderType = 'pickup';
    component.onCheckoutSubmit();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="dialog"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.delivery-notice')).toBeNull();
    fixture.nativeElement.querySelector('.review-actions .btn-back').click();
    fixture.detectChanges();
    expect(component.showReviewStep).toBeFalse();
  });

  it('retains the configured QR payment controls', () => {
    component.isUpiQrEnabled = true;
    component.paymentMethod = 'upi-qr';
    component.upiQrCodeDataUrl = 'data:image/png;base64,';
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[alt="UPI QR Code"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('[name="upiPaymentConfirmed"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('[name="upiTransactionRef"]')).not.toBeNull();
  });
});
