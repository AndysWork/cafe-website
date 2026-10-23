import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { AuthService, UserRole } from '../../services/auth.service';
import { CartService } from '../../services/cart.service';
import { AnalyticsTrackingService } from '../../services/analytics-tracking.service';
import { DineInService } from '../../services/dine-in.service';
import { AuthStore, CartStore } from '../../store';
import { NotificationCenterComponent } from '../notification-center/notification-center.component';
import { NavbarComponent } from './navbar.component';

@Component({ selector: 'app-notification-center', standalone: true, template: '' })
class NotificationStubComponent {}

describe('NavbarComponent multi-role navigation', () => {
  let fixture: ComponentFixture<NavbarComponent>;
  let store: AuthStore;

  const setRoles = (role: UserRole, roles?: string[]) => {
    store.setUser({ username: 'staff', email: 'staff@example.com', role, roles });
    fixture.detectChanges();
  };
  const links = () => Array.from(fixture.nativeElement.querySelectorAll('a[href]') as NodeListOf<HTMLAnchorElement>)
    .map(link => link.getAttribute('href'));

  beforeEach(() => {
    localStorage.removeItem('authToken');
    localStorage.removeItem('currentUser');
    TestBed.configureTestingModule({
      imports: [NavbarComponent],
      providers: [
        provideRouter([]),
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

  it('renders all staff menus on home, not just the primary role', () => {
    expect(TestBed.inject(Router).url).toBe('/');
    setRoles('cook', ['cook', 'cashier', 'waiter']);
    for (const route of ['/kitchen/dashboard', '/kitchen/display', '/staff/cashier', '/staff/dine-in', '/staff/attendance', '/staff/payslip', '/profile']) {
      expect(links()).toContain(route);
    }
    expect(links()).not.toContain('/admin/dashboard');
    expect(links()).not.toContain('/partner/delivery');
  });

  it('renders manager, kitchen, and partner dashboards together', () => {
    setRoles('manager', ['manager', 'chef', 'delivery-partner']);
    for (const route of ['/manager/dashboard', '/kitchen/dashboard', '/partner/delivery', '/partner/payout', '/manager/counter-sale']) {
      expect(links()).toContain(route);
    }
    expect(new Set(links()).size).toBe(links().length);
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
