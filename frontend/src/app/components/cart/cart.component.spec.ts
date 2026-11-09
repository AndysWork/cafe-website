import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { of } from 'rxjs';
import { AnalyticsTrackingService } from '../../services/analytics-tracking.service';
import { AuthService } from '../../services/auth.service';
import { Cart, CartService } from '../../services/cart.service';
import { DineInService } from '../../services/dine-in.service';
import { LoyaltyService } from '../../services/loyalty.service';
import { MenuService } from '../../services/menu.service';
import { OutletService } from '../../services/outlet.service';
import { UIStore } from '../../store/ui.store';
import { CartComponent } from './cart.component';

describe('CartComponent guest access', () => {
  it('allows viewing the cart without loading loyalty and redirects before placing a dine-in order', () => {
    const loyalty = jasmine.createSpyObj('LoyaltyService', ['getLoyaltyAccount']);
    const dineIn = { activeBill$: of(null), placeDineInOrder: jasmine.createSpy('placeDineInOrder') };
    const router = jasmine.createSpyObj('Router', ['navigate']);
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: { isLoggedIn: () => false } },
        { provide: AnalyticsTrackingService, useValue: { trackCartView: () => {} } },
        { provide: CartService, useValue: { cart$: of({ items: [], subtotal: 0, packagingCharges: 0, total: 0, itemCount: 0 }) } },
        { provide: DineInService, useValue: dineIn },
        { provide: LoyaltyService, useValue: loyalty },
        { provide: MenuService, useValue: {} },
        { provide: OutletService, useValue: { getPublicOutlets: () => of([]) } },
        { provide: UIStore, useValue: {} },
        { provide: Router, useValue: router }
      ]
    });

    const component = TestBed.createComponent(CartComponent).componentInstance;
    component.ngOnInit();
    expect(loyalty.getLoyaltyAccount).not.toHaveBeenCalled();
    component.sendToKitchen();
    expect(router.navigate).toHaveBeenCalledWith(['/login'], { queryParams: { returnUrl: '/cart' } });
    expect(dineIn.placeDineInOrder).not.toHaveBeenCalled();
    component.ngOnDestroy();
  });
});

describe('Cart presentation', () => {
      let cart: Cart;
      let service: jasmine.SpyObj<CartService>;
      let router: jasmine.SpyObj<Router>;

      beforeEach(() => {
        cart = {
          items: [{ menuItemId: 'breakfast', cartLineId: 'breakfast-regular', name: 'Breakfast &amp; Coffee', categoryName: 'Breakfast', price: 160, quantity: 2, selectedVariant: { variantName: 'Regular', price: 160 } }],
          subtotal: 320, packagingCharges: 24, total: 344, itemCount: 2,
          preparationNotes: 'Less spicy'
        };
        service = jasmine.createSpyObj<CartService>('CartService', ['updateQuantity', 'removeItem', 'clearCart', 'setPreparationNotes'], { cart$: of(cart) });
        router = jasmine.createSpyObj<Router>('Router', ['navigate']);
        TestBed.configureTestingModule({
          imports: [CartComponent],
          providers: [
            { provide: AuthService, useValue: { isLoggedIn: () => false } },
            { provide: AnalyticsTrackingService, useValue: { trackCartView: () => {} } },
            { provide: CartService, useValue: service },
            { provide: DineInService, useValue: { activeBill$: of(null), currentTable: null } },
            { provide: LoyaltyService, useValue: {} },
            { provide: MenuService, useValue: {} },
            { provide: OutletService, useValue: { getPublicOutlets: () => of([]) } },
            { provide: UIStore, useValue: {} },
            { provide: Router, useValue: router }
          ]
        });
      });

      it('renders live quantities, category, prices and customization without changing totals', () => {
        const fixture = TestBed.createComponent(CartComponent);
        fixture.detectChanges();
        const root: HTMLElement = fixture.nativeElement;
        expect(root.querySelector('h1')?.textContent).toContain('Cart');
        expect(root.querySelector('.hero p')?.textContent).toContain('2 Items');
        expect(root.querySelector('.item-name')?.textContent).toBe('Breakfast & Coffee');
        expect(root.querySelector('.item-category')?.textContent).toBe('Breakfast');
        expect(root.querySelector('.item-total')?.textContent).toBe('₹320');
        expect(root.querySelector('.summary-row.total')?.textContent).toContain('₹344');
        expect(root.querySelector('.btn-edit-custom')).not.toBeNull();
        expect(root.querySelector('.delivery-notice')?.textContent).toContain('confirmed at checkout');
      });

      it('keeps quantity actions keyed to the unique cart line', () => {
        const fixture = TestBed.createComponent(CartComponent);
        fixture.detectChanges();
        const root: HTMLElement = fixture.nativeElement;
        root.querySelector<HTMLButtonElement>('[aria-label="Increase quantity"]')!.click();
        root.querySelector<HTMLButtonElement>('[aria-label="Decrease quantity"]')!.click();
        expect(service.updateQuantity.calls.allArgs()).toEqual([['breakfast-regular', 3], ['breakfast-regular', 1]]);
      });

      it('retains removal and clear-cart confirmation', () => {
        const fixture = TestBed.createComponent(CartComponent);
        fixture.detectChanges();
        const confirmation = spyOn(window, 'confirm').and.returnValue(false);
        const root: HTMLElement = fixture.nativeElement;
        root.querySelector<HTMLButtonElement>('.btn-remove')!.click();
        expect(service.removeItem).not.toHaveBeenCalled();
        confirmation.and.returnValue(true);
        root.querySelector<HTMLButtonElement>('.btn-remove')!.click();
        root.querySelector<HTMLButtonElement>('.btn-clear')!.click();
        expect(service.removeItem).toHaveBeenCalledWith('breakfast-regular');
        expect(service.clearCart).toHaveBeenCalled();
      });

      it('retains 500-character notes and checkout/menu navigation', async () => {
        const fixture = TestBed.createComponent(CartComponent);
        fixture.detectChanges();
        await fixture.whenStable();
        const root: HTMLElement = fixture.nativeElement;
        const notes = root.querySelector<HTMLTextAreaElement>('#prepNotes')!;
        expect(notes.maxLength).toBe(500);
        expect(notes.value).toBe('Less spicy');
        notes.value = 'No onion';
        notes.dispatchEvent(new Event('input'));
        expect(service.setPreparationNotes).toHaveBeenCalledWith('No onion');
        root.querySelector<HTMLButtonElement>('.btn-checkout')!.click();
        root.querySelector<HTMLButtonElement>('.btn-continue')!.click();
        expect(router.navigate.calls.allArgs()).toEqual([[['/checkout']], [['/menu']]]);
      });

      it('shows the empty state instead of checkout when no items remain', () => {
        cart.items = [];
        cart.itemCount = 0;
        const fixture = TestBed.createComponent(CartComponent);
        fixture.detectChanges();
        expect(fixture.nativeElement.querySelector('.empty-state')).not.toBeNull();
        expect(fixture.nativeElement.querySelector('.cart-content')).toBeNull();
      });
    });
