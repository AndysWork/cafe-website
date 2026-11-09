import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { AuthService, UserRole } from '../../services/auth.service';
import { CartService } from '../../services/cart.service';
import { AnalyticsTrackingService } from '../../services/analytics-tracking.service';
import { DineInService } from '../../services/dine-in.service';
import { AuthStore, CartStore } from '../../store';
import { NotificationCenterComponent } from '../notification-center/notification-center.component';
import { NavbarComponent } from './navbar.component';
import { HomePanelService } from '../../services/home-panel.service';

@Component({ selector: 'app-notification-center', standalone: true, template: '' })
class NotificationStubComponent {}

@Component({ selector: 'app-navbar-test-route', standalone: true, template: '' })
class TestRouteComponent {}

describe('NavbarComponent multi-role navigation', () => {
  let fixture: ComponentFixture<NavbarComponent>;
  let store: AuthStore;

  const setRoles = (role: UserRole, roles?: string[]) => {
    store.setUser({ username: 'staff', email: 'staff@example.com', role, roles });
    fixture.detectChanges();
  };
  const links = () => Array.from(fixture.nativeElement.querySelectorAll('a[href]') as NodeListOf<HTMLAnchorElement>)
    .map(link => link.getAttribute('href'));

  it('ignores emulated mobile hover and keeps the profile open after a tap', fakeAsync(() => {
    spyOn(window, 'matchMedia').and.returnValue(
      jasmine.createSpyObj<MediaQueryList>('media', ['addEventListener'], { matches: false })
    );
    setRoles('user');
    const component = fixture.componentInstance;
    component.toggleMobileMenu();
    component.openDropdown('customer-profile');
    expect(component.activeDropdown).toBeNull();
    fixture.nativeElement.querySelector('.customer-profile .profile-badge').click();
    component.scheduleCloseDropdown();
    tick(250);
    fixture.detectChanges();
    expect(component.activeDropdown).toBe('customer-profile');
    expect(fixture.nativeElement.querySelector('.profile-badge').getAttribute('aria-expanded')).toBe('true');
    fixture.nativeElement.querySelector('.profile-badge').click();
    expect(component.activeDropdown).toBeNull();
  }));

  beforeEach(() => {
    localStorage.removeItem('authToken');
    localStorage.removeItem('currentUser');
    TestBed.configureTestingModule({
      imports: [NavbarComponent],
      providers: [
        provideRouter([{ path: '', component: TestRouteComponent }, { path: 'home', component: TestRouteComponent }, { path: 'menu', component: TestRouteComponent }, { path: 'profile', component: TestRouteComponent }]),
        { provide: AuthService, useValue: { logout: jasmine.createSpy('logout') } },
        { provide: CartService, useValue: {} },
        { provide: CartStore, useValue: { itemCount: signal(2) } },
        { provide: AnalyticsTrackingService, useValue: {} },
        { provide: DineInService, useValue: {} }
      ]
    }).overrideComponent(NavbarComponent, {
      remove: { imports: [NotificationCenterComponent] },
      add: { imports: [NotificationStubComponent] }
    });
    store = TestBed.inject(AuthStore);
    fixture = TestBed.createComponent(NavbarComponent);
  });

  it('offers menu and cart without account or reservation navbar links to guests', () => {
    fixture.detectChanges();
    expect(links()).toContain('/menu');
    expect(links()).toContain('/cart');
    expect(links()).not.toContain('/reservations');
    expect(fixture.componentInstance.navigationGroups()).toEqual([]);
    expect(fixture.nativeElement.querySelector('.group-toggle')).toBeNull();
    expect(fixture.nativeElement.querySelector('.profile-badge')).toBeNull();
  });

  it('keeps guest links direct on other pages and in the mobile menu', async () => {
    await TestBed.inject(Router).navigateByUrl('/menu');
    fixture.detectChanges();
    fixture.componentInstance.toggleMobileMenu();
    fixture.detectChanges();
    for (const route of ['/menu', '/cart']) expect(links()).toContain(route);
    expect(links()).not.toContain('/reservations');
    expect(fixture.nativeElement.querySelector('.group-toggle')).toBeNull();
    expect(fixture.nativeElement.querySelector('.btn-login')).not.toBeNull();
  });

  it('opens home dialogs instead of linking to removed inline sections', () => {
    fixture.detectChanges();
    const open = spyOn(TestBed.inject(HomePanelService), 'open');
    const buttons: NodeListOf<HTMLButtonElement> = fixture.nativeElement.querySelectorAll('.landing-links button');
    fixture.componentInstance.toggleMobileMenu();
    buttons[0].click();
    expect(open).toHaveBeenCalledWith('story');
    expect(fixture.componentInstance.isMobileMenuOpen).toBeFalse();
    buttons[1].click();
    expect(open).toHaveBeenCalledWith('offers');
    expect(fixture.componentInstance.isMobileMenuOpen).toBeFalse();
    buttons[2].click();
    expect(open).toHaveBeenCalledWith('contact');
    expect(links()).not.toContain('#offers');
    expect(links()).not.toContain('#outlets');
  });

  it('renders all staff menus on home, not just the primary role', () => {
    expect(TestBed.inject(Router).url).toBe('/');
    setRoles('cook', ['cook', 'cashier', 'waiter']);
    for (const route of ['/kitchen/dashboard', '/kitchen/display', '/staff/cashier', '/staff/dine-in', '/staff/attendance', '/staff/payslip', '/profile']) {
      expect(links()).toContain(route);
    }
    expect(links()).not.toContain('/admin/dashboard');
    expect(links()).not.toContain('/partner/delivery');
  });

  it('simplifies customer-only navigation into one account menu', () => {
    setRoles('user', ['user']);
    expect(fixture.componentInstance.isCustomerOnly).toBeTrue();
    expect(fixture.nativeElement.querySelectorAll('.group-toggle').length).toBe(0);
    expect(fixture.nativeElement.querySelector('.customer-profile .username').textContent.trim()).toBe('Welcome staff');
    for (const route of ['/dashboard', '/orders', '/reservations', '/subscriptions', '/loyalty', '/profile']) {
      expect(fixture.nativeElement.querySelectorAll(`.customer-profile a[href="${route}"]`).length).toBe(1);
    }
    expect(fixture.nativeElement.querySelector('.customer-cart .landing-cart-count').textContent.trim()).toBe('2');
    expect(fixture.nativeElement.querySelector('app-notification-center')).not.toBeNull();
    const orderButton = fixture.nativeElement.querySelector('.landing-order');
    expect(orderButton.nextElementSibling.classList.contains('desktop-cart')).toBeTrue();
    expect(orderButton.nextElementSibling.querySelector('svg')).not.toBeNull();
    expect(links()).not.toContain('/admin/dashboard');
    fixture.componentInstance.toggleMobileMenu();
    fixture.nativeElement.querySelector('.customer-profile .profile-badge').click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.customer-profile .profile-menu').classList.contains('show')).toBeTrue();
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();
    expect(fixture.componentInstance.isMobileMenuOpen).toBeFalse();
    expect(fixture.componentInstance.activeDropdown).toBeNull();
  });

  it('keeps direct customer menu and cart access away from home', async () => {
    setRoles('user');
    await TestBed.inject(Router).navigateByUrl('/menu');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.navbar-nav .nav-link[href="/menu"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.customer-cart[href="/cart"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelectorAll('.group-toggle').length).toBe(0);
    fixture.componentInstance.toggleMobileMenu();
    fixture.componentInstance.toggleDropdown('customer-profile');
    fixture.nativeElement.querySelector('.customer-profile a[href="/profile"]').click();
    expect(fixture.componentInstance.isMobileMenuOpen).toBeFalse();
    expect(fixture.componentInstance.activeDropdown).toBeNull();
  });

  it('shows the customer name and highlights Profile only on the profile route', async () => {
    store.setUser({ username: 'customer', firstName: 'Gourab', email: 'customer@example.com', role: 'user' });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.customer-profile .username').textContent.trim()).toBe('Welcome Gourab');
    const profile: HTMLAnchorElement = fixture.nativeElement.querySelector('.primary-profile-item');
    expect(profile.classList.contains('active')).toBeFalse();
    expect(profile.getAttribute('aria-current')).toBeNull();
    await TestBed.inject(Router).navigateByUrl('/profile');
    fixture.detectChanges();
    await fixture.whenStable();
    expect(profile.classList.contains('active')).toBeTrue();
    expect(profile.getAttribute('aria-current')).toBe('page');
    await TestBed.inject(Router).navigateByUrl('/menu');
    fixture.detectChanges();
    await fixture.whenStable();
    expect(profile.classList.contains('active')).toBeFalse();
  });

  it('restores additive staff navigation when customer roles change', () => {
    setRoles('user');
    expect(fixture.componentInstance.navigationGroups()).toEqual([]);
    setRoles('user', ['user', 'cashier']);
    expect(fixture.componentInstance.isCustomerOnly).toBeFalse();
    expect(links()).toContain('/staff/cashier');
    expect(fixture.nativeElement.querySelectorAll('.group-toggle').length).toBeGreaterThan(0);
    expect(fixture.nativeElement.querySelector('.customer-profile a[href="/orders"]')).toBeNull();
  });

  it('renders manager, kitchen, and partner dashboards together', () => {
    setRoles('manager', ['manager', 'chef', 'delivery-partner']);
    for (const route of ['/manager/dashboard', '/kitchen/dashboard', '/partner/delivery', '/partner/payout', '/manager/counter-sale']) {
      expect(links()).toContain(route);
    }
    const menuLinks = Array.from(fixture.nativeElement.querySelectorAll('.navbar-nav .nav-link[href]') as NodeListOf<HTMLAnchorElement>)
      .map(link => link.getAttribute('href'));
    expect(new Set(menuLinks).size).toBe(menuLinks.length);
  });

  it('hides Home on the landing page and shows it on other pages', async () => {
    setRoles('waiter', ['waiter']);
    expect(fixture.componentInstance.isLandingPage).toBeTrue();
    expect(fixture.nativeElement.querySelector('.navbar-nav .nav-link[href="/"]')).toBeNull();

    await TestBed.inject(Router).navigateByUrl('/menu');
    fixture.detectChanges();
    expect(fixture.componentInstance.isLandingPage).toBeFalse();
    expect(fixture.nativeElement.querySelector('.navbar-nav .nav-link[href="/"]')).not.toBeNull();
  });

  it('keeps landing navigation when scrolling to a home section', async () => {
    await TestBed.inject(Router).navigateByUrl('/#offers');
    fixture.detectChanges();
    expect(fixture.componentInstance.isLandingPage).toBeTrue();
    await TestBed.inject(Router).navigateByUrl('/home?source=link#outlets');
    fixture.detectChanges();
    expect(fixture.componentInstance.isLandingPage).toBeTrue();
  });

  it('keeps customer menus and cart badge alongside secondary staff roles', () => {
    setRoles('cashier', ['cashier', 'user']);
    for (const route of ['/staff/cashier', '/dashboard', '/menu', '/cart', '/orders', '/reservations', '/subscriptions', '/loyalty']) {
      expect(links()).toContain(route);
    }
    expect(fixture.nativeElement.querySelector('.cart-badge').textContent.trim()).toBe('2');
    expect(fixture.nativeElement.querySelectorAll('.profile-badge').length).toBe(1);
  });

  it('keeps customer account links for admin plus user without duplicate profile menus', () => {
    setRoles('admin', ['admin', 'user']);
    expect(links()).toContain('/admin/outlets');
    expect(links()).toContain('/orders');
    expect(links()).toContain('/cart');
    expect(fixture.nativeElement.querySelectorAll('.profile-badge').length).toBe(1);
  });

  it('uses the same combined links when the mobile menu is open', () => {
    setRoles('waiter', ['waiter', 'cashier']);
    const before = links();
    const toggle: HTMLButtonElement = fixture.nativeElement.querySelector('.mobile-menu-toggle');
    toggle.click();
    fixture.detectChanges();
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(fixture.nativeElement.querySelector('.navbar-menu').classList.contains('active')).toBeTrue();
    expect(links()).toEqual(before);
  });

  it('opens one group at a time and closes on outside click and Escape', () => {
    setRoles('waiter', ['waiter', 'cashier']);
    const buttons: NodeListOf<HTMLButtonElement> = fixture.nativeElement.querySelectorAll('.group-toggle');
    const menus: NodeListOf<HTMLUListElement> = fixture.nativeElement.querySelectorAll('.navigation-group-menu');
    expect(menus[0].hidden).toBeTrue();
    buttons[0].click();
    fixture.detectChanges();
    expect(buttons[0].getAttribute('aria-expanded')).toBe('true');
    expect(menus[0].hidden).toBeFalse();
    buttons[1].click();
    fixture.detectChanges();
    expect(menus[0].hidden).toBeTrue();
    expect(menus[1].hidden).toBeFalse();
    document.body.click();
    fixture.detectChanges();
    expect(menus[1].hidden).toBeTrue();
    fixture.componentInstance.toggleMobileMenu();
    buttons[0].click();
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();
    expect(menus[0].hidden).toBeTrue();
    expect(fixture.componentInstance.isMobileMenuOpen).toBeFalse();
  });

  it('closes the drawer and group after selecting a destination', () => {
    setRoles('waiter');
    fixture.componentInstance.toggleMobileMenu();
    const button: HTMLButtonElement = fixture.nativeElement.querySelector('.group-toggle');
    button.click();
    fixture.detectChanges();
    const link: HTMLAnchorElement = fixture.nativeElement.querySelector('a[href="/staff/dine-in"]');
    link.addEventListener('click', event => event.preventDefault());
    link.click();
    fixture.detectChanges();
    expect(button.getAttribute('aria-expanded')).toBe('false');
    expect(fixture.componentInstance.isMobileMenuOpen).toBeFalse();
  });

  it('keeps customer links in My Account without duplicating them in profile menus', () => {
    setRoles('admin', ['admin', 'user']);
    const groups = fixture.componentInstance.navigationGroups();
    expect(groups.find(group => group.label === 'My Account')?.items.map(item => item.route))
      .toEqual(['/cart', '/orders', '/reservations', '/subscriptions', '/loyalty']);
    expect(links().filter(route => route === '/orders').length).toBe(1);
  });

  it('updates menus when roles change and clears them on logout', () => {
    setRoles('waiter', ['waiter', 'cashier']);
    expect(links()).toContain('/staff/cashier');
    setRoles('waiter');
    expect(links()).toContain('/staff/dine-in');
    expect(links()).not.toContain('/staff/cashier');
    store.setUser(null);
    fixture.detectChanges();
    expect(links()).not.toContain('/staff/dine-in');
    expect(fixture.nativeElement.querySelector('.btn-login')).not.toBeNull();
  });

  it('merges the primary role with secondary roles and accepts normalized kitchen aliases', () => {
    setRoles('waiter', ['Kitchen_Helper', ' CASHIER ']);
    for (const route of ['/staff/dine-in', '/staff/cashier', '/kitchen/dashboard', '/kitchen/display']) {
      expect(links()).toContain(route);
    }
  });
});
