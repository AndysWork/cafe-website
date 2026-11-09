import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Params, Router } from '@angular/router';
import { BehaviorSubject, of } from 'rxjs';
import { AuthService } from '../../services/auth.service';
import { CartService } from '../../services/cart.service';
import { CustomerReviewService } from '../../services/customer-review.service';
import { DineInService } from '../../services/dine-in.service';
import { FavoriteService } from '../../services/favorite.service';
import { LoyaltyService } from '../../services/loyalty.service';
import { MenuService } from '../../services/menu.service';
import { OutletService } from '../../services/outlet.service';
import { UIStore } from '../../store/ui.store';
import { MenuComponent } from './menu.component';

describe('MenuComponent URL search', () => {
  let params: BehaviorSubject<Params>;
  let component: MenuComponent;
  const dineIn = {
    currentTable: '',
    activeTable$: of(''),
    activeBill$: of(null),
    setTableQrToken: jasmine.createSpy('setTableQrToken'),
    setTableNumber: jasmine.createSpy('setTableNumber')
  };

  beforeEach(() => {
    params = new BehaviorSubject<Params>({ search: 'pizza' });
    dineIn.setTableQrToken.calls.reset();
    dineIn.setTableNumber.calls.reset();
    TestBed.configureTestingModule({
      providers: [
        { provide: ActivatedRoute, useValue: { queryParams: params, snapshot: { queryParams: {} } } },
        { provide: Router, useValue: { navigate: jasmine.createSpy('navigate') } },
        { provide: DineInService, useValue: dineIn },
        { provide: MenuService, useValue: {
          categoryDrawerOpen$: of(false),
          menuItemsRefresh$: of(false),
          setCategoryDrawerOpen: jasmine.createSpy('setCategoryDrawerOpen')
        } },
        { provide: CartService, useValue: { cart$: of({ items: [], subtotal: 0, packagingCharges: 0, total: 0, itemCount: 0 }) } },
        { provide: AuthService, useValue: { isLoggedIn: () => false } },
        { provide: FavoriteService, useValue: {} },
        { provide: OutletService, useValue: {} },
        { provide: CustomerReviewService, useValue: {} },
        { provide: LoyaltyService, useValue: {} },
        { provide: UIStore, useValue: {} }
      ]
    });
    component = TestBed.createComponent(MenuComponent).componentInstance;
    spyOn(component, 'initializeOutletAndMenu');
    spyOn(component, 'loadSocialProof');
    spyOn(component, 'applyFilters');
    component.ngOnInit();
  });

  afterEach(() => component.ngOnDestroy());

  it('updates and clears the search when the URL search changes', () => {
    expect(component.searchQuery).toBe('pizza');
    params.next({ search: 'momo' });
    expect(component.searchQuery).toBe('momo');
    params.next({});
    expect(component.searchQuery).toBe('');
    expect(component.applyFilters).toHaveBeenCalledTimes(3);
  });

  it('preserves a manually edited search when only table parameters change', () => {
    component.searchQuery = 'burger';
    params.next({ search: 'pizza', table: '12', qrToken: 'token' });
    expect(component.searchQuery).toBe('burger');
    expect(dineIn.setTableQrToken).toHaveBeenCalledWith('token');
    expect(dineIn.setTableNumber).toHaveBeenCalledWith('12');
    expect(component.applyFilters).toHaveBeenCalledTimes(1);
  });
});
