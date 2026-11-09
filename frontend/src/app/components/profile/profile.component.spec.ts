import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { of, Subject, throwError } from 'rxjs';
import { ProfileComponent } from './profile.component';
import { AuthService } from '../../services/auth.service';
import { AddressService } from '../../services/address.service';
import { FavoriteService } from '../../services/favorite.service';
import { MenuService } from '../../services/menu.service';
import { Order, OrderService } from '../../services/order.service';
import { LoyaltyService } from '../../services/loyalty.service';
import { CartService } from '../../services/cart.service';
import { NotificationStore } from '../../store';
import { UIStore } from '../../store/ui.store';

describe('ProfileComponent', () => {
  let component: ProfileComponent;
  let fixture: ComponentFixture<ProfileComponent>;
  let orders: jasmine.SpyObj<OrderService>;
  let rewards: jasmine.SpyObj<LoyaltyService>;
  let cart: jasmine.SpyObj<CartService>;
  let menu: jasmine.SpyObj<MenuService>;
  let addresses: jasmine.SpyObj<AddressService>;
  let favorites: jasmine.SpyObj<FavoriteService>;

  const order = (id: string, status: Order['status'], createdAt: string): Order => ({
    id, status, createdAt, updatedAt: createdAt, userId: 'customer', username: 'customer',
    items: [{ menuItemId: 'pizza', name: 'Pizza', quantity: 2, price: 99, total: 198 }],
    subtotal: 198, tax: 0, total: 198, paymentStatus: 'paid', paymentMethod: 'cod'
  });

  beforeEach(async () => {
    orders = jasmine.createSpyObj<OrderService>('OrderService', ['getMyOrders']);
    rewards = jasmine.createSpyObj<LoyaltyService>('LoyaltyService', ['getLoyaltyAccount']);
    cart = jasmine.createSpyObj<CartService>('CartService', ['addItem']);
    menu = jasmine.createSpyObj<MenuService>('MenuService', ['getMenuItems']);
    addresses = jasmine.createSpyObj<AddressService>('AddressService', ['getMyAddresses']);
    favorites = jasmine.createSpyObj<FavoriteService>('FavoriteService', ['getMyFavorites']);
    orders.getMyOrders.and.returnValue(of([]));
    rewards.getLoyaltyAccount.and.returnValue(of({
      id: 'rewards', userId: 'customer', username: 'customer', currentPoints: 1250,
      totalPointsEarned: 1250, totalPointsRedeemed: 0, tier: 'Bronze', referralCode: '',
      totalReferrals: 0, loyaltyCardNumber: '', tierMultiplier: 1, tierBenefits: [],
      expiringPoints: 0, birthdayBonusAvailable: false, birthdayBonusPoints: 0,
      createdAt: '', updatedAt: ''
    }));
    menu.getMenuItems.and.returnValue(of([{ id: 'pizza', name: 'Pizza', categoryId: 'food', onlinePrice: 99, imageUrl: '/pizza.jpg' }]));
    addresses.getMyAddresses.and.returnValue(of([]));
    favorites.getMyFavorites.and.returnValue(of([]));

    await TestBed.configureTestingModule({
      imports: [ProfileComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { getCurrentUser: () => ({ username: 'customer', firstName: 'Test', lastName: 'Customer', email: 'test@example.com', phoneNumber: '9876543210', role: 'user', gender: 'male', dateOfBirth: '1990-01-01' }) } },
        { provide: OrderService, useValue: orders },
        { provide: LoyaltyService, useValue: rewards },
        { provide: CartService, useValue: cart },
        { provide: MenuService, useValue: menu },
        { provide: AddressService, useValue: addresses },
        { provide: FavoriteService, useValue: favorites },
        { provide: NotificationStore, useValue: { loadPreferences: jasmine.createSpy('loadPreferences'), preferences: () => ({}) } },
        { provide: UIStore, useValue: jasmine.createSpyObj('UIStore', ['success', 'warning', 'error']) }
      ]
    }).compileComponents();
    fixture = TestBed.createComponent(ProfileComponent);
    component = fixture.componentInstance;
  });

  it('shows live identity and rewards with an initially collapsed editor', () => {
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.textContent).toContain('Test Customer');
    expect(root.textContent).toContain('1,250');
    expect(root.querySelector<HTMLDialogElement>('.account-editor')?.open).toBeFalse();
    expect(root.querySelector('.editor-body')).toBeNull();
    expect(root.querySelectorAll('.quick-action-grid > *').length).toBe(5);
    expect(root.querySelector('.history-empty')).not.toBeNull();
  });

  it('opens profile editing and preserves one-time field protections', fakeAsync(() => {
    fixture.detectChanges();
    fixture.nativeElement.querySelector('.edit-profile-button').click();
    fixture.detectChanges();
    tick();
    expect(component.showEditor).toBeTrue();
    expect(component.activeTab).toBe('profile');
    expect(fixture.nativeElement.querySelector('.account-editor').open).toBeTrue();
    expect(fixture.nativeElement.querySelector('.settings-tabs')).toBeNull();
    expect(component.hasProfileChanges()).toBeFalse();
    expect(component.canEditGender).toBeFalse();
    expect(component.canEditDateOfBirth).toBeFalse();
    expect(fixture.nativeElement.querySelector('input[type="date"]').disabled).toBeTrue();
  }));

  it('opens the existing favorites, addresses and account settings', fakeAsync(() => {
    fixture.detectChanges();
    const buttons = fixture.nativeElement.querySelectorAll('.quick-action-grid button');
    buttons[0].click();
    expect(component.activeTab).toBe('favorites');
    expect(favorites.getMyFavorites).toHaveBeenCalled();
    buttons[1].click();
    expect(component.activeTab).toBe('addresses');
    expect(addresses.getMyAddresses).toHaveBeenCalled();
    buttons[2].click();
    expect(component.activeTab).toBe('password');
    fixture.detectChanges();
    tick();
    expect(fixture.nativeElement.querySelector('.account-editor').open).toBeTrue();
    expect(fixture.nativeElement.querySelectorAll('.settings-tabs button').length).toBe(2);
    component.switchTab('notifications');
    expect(TestBed.inject(NotificationStore).loadPreferences).toHaveBeenCalled();
  }));

  it('locks background scrolling and restores focus and password privacy on Close', () => {
    fixture.detectChanges();
    const trigger: HTMLButtonElement = fixture.nativeElement.querySelector('.edit-profile-button');
    document.body.style.overflow = 'auto';
    const focus = spyOn(trigger, 'focus').and.callThrough();
    trigger.focus();
    focus.calls.reset();
    component.switchTab('password');
    component.currentPassword = 'temporary';
    component.newPassword = 'temporary-new';
    component.confirmPassword = 'temporary-new';
    fixture.detectChanges();
    expect(document.body.style.overflow).toBe('hidden');
    fixture.nativeElement.querySelector('.editor-close').click();
    fixture.detectChanges();
    expect(component.showEditor).toBeFalse();
    expect(fixture.nativeElement.querySelector('dialog').open).toBeFalse();
    expect(document.body.style.overflow).toBe('auto');
    expect(focus).toHaveBeenCalled();
    expect(component.currentPassword + component.newPassword + component.confirmPassword).toBe('');
    document.body.style.overflow = '';
  });

  it('dismisses by Escape and backdrop, not by clicking inside, and cleans up on destruction', () => {
    fixture.detectChanges();
    component.switchTab('addresses');
    fixture.detectChanges();
    const dialog: HTMLDialogElement = fixture.nativeElement.querySelector('dialog');
    dialog.dispatchEvent(new MouseEvent('click', { clientX: dialog.getBoundingClientRect().left + 5, clientY: dialog.getBoundingClientRect().top + 5 }));
    expect(component.showEditor).toBeTrue();
    dialog.dispatchEvent(new Event('cancel', { cancelable: true }));
    expect(component.showEditor).toBeFalse();
    component.switchTab('favorites');
    dialog.dispatchEvent(new MouseEvent('click', { clientX: -1, clientY: -1 }));
    expect(component.showEditor).toBeFalse();
    component.switchTab('profile');
    fixture.destroy();
    expect(document.body.style.overflow).not.toBe('hidden');
  });

  it('ignores a queued close event after a different popup is opened', () => {
    fixture.detectChanges();
    component.switchTab('profile');
    component.closeEditor();
    component.switchTab('favorites');
    fixture.nativeElement.querySelector('dialog').dispatchEvent(new Event('close'));
    expect(component.showEditor).toBeTrue();
    expect(component.activeTab).toBe('favorites');
  });

  it('shows the latest three orders with real totals, quantities, images and status', () => {
    orders.getMyOrders.and.returnValue(of([
      order('old', 'delivered', '2026-11-01T10:00:00Z'),
      order('new', 'preparing', '2026-11-04T10:00:00Z'),
      order('cancelled', 'cancelled', '2026-11-03T10:00:00Z'),
      order('delivered', 'delivered', '2026-11-02T10:00:00Z')
    ]));
    fixture.detectChanges();
    expect(component.recentOrders.map(value => value.id)).toEqual(['new', 'cancelled', 'delivered']);
    expect(component.lastDeliveredOrder?.id).toBe('delivered');
    expect(component.getOrderItemCount(component.recentOrders[0])).toBe(2);
    expect(component.getOrderImage(component.recentOrders[0])).toBe('/pizza.jpg');
    expect(fixture.nativeElement.querySelectorAll('.reorder-button').length).toBe(1);
    expect(fixture.nativeElement.querySelector('.order-summary').textContent).toContain('198');
  });

  it('reorders the selected delivered order using the existing cart flow', () => {
    fixture.detectChanges();
    const router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.resolveTo(true);
    component.reorderLastOrder(order('delivered', 'delivered', '2026-11-02T10:00:00Z'));
    expect(cart.addItem).toHaveBeenCalledWith(jasmine.objectContaining({ menuItemId: 'pizza' }), 2);
    expect(router.navigate).toHaveBeenCalledWith(['/cart']);
    cart.addItem.calls.reset();
    component.reorderLastOrder(order('pending', 'pending', '2026-11-02T10:00:00Z'));
    expect(cart.addItem).not.toHaveBeenCalled();
  });

  it('shows explicit errors instead of empty orders or invented rewards and supports retry', () => {
    orders.getMyOrders.and.returnValue(throwError(() => new Error('offline')));
    rewards.getLoyaltyAccount.and.returnValue(throwError(() => new Error('offline')));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Unable to load your order history.');
    expect(fixture.nativeElement.textContent).toContain('Unable to load rewards.');
    expect(fixture.nativeElement.querySelector('.history-empty')).toBeNull();
    expect(fixture.nativeElement.querySelector('.rewards-summary strong')).toBeNull();
    orders.getMyOrders.and.returnValue(of([]));
    component.loadLastDeliveredOrder();
    fixture.detectChanges();
    expect(component.ordersError).toBe('');
    expect(fixture.nativeElement.querySelector('.history-empty')).not.toBeNull();
  });

  it('shows a loading state while the orders request is pending', () => {
    orders.getMyOrders.and.returnValue(new Subject<Order[]>());
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Loading your orders...');
    expect(fixture.nativeElement.querySelector('.history-empty')).toBeNull();
  });

  it('keeps photo errors visible when profile editing is collapsed', () => {
    fixture.detectChanges();
    component.profileError = 'Failed to upload profile picture';
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain('Failed to upload profile picture');
  });

  it('keeps orders and loyalty actions linked to their existing pages', () => {
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelector('.quick-action-grid a')?.getAttribute('href')).toBe('/orders');
    expect(root.querySelector('.rewards-summary a')?.getAttribute('href')).toBe('/loyalty');
    expect(root.querySelector('.history-heading a')?.getAttribute('href')).toBe('/orders');
  });
});
