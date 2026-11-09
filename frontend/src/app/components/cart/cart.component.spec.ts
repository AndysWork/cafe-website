import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { of } from 'rxjs';
import { AnalyticsTrackingService } from '../../services/analytics-tracking.service';
import { AuthService } from '../../services/auth.service';
import { CartService } from '../../services/cart.service';
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
