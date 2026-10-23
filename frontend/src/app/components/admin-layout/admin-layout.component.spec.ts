import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { AuthService } from '../../services/auth.service';
import { OutletService } from '../../services/outlet.service';
import { AuthStore } from '../../store/auth.store';
import { OutletSelectorComponent } from '../outlet-selector/outlet-selector.component';
import { AdminLayoutComponent } from './admin-layout.component';

@Component({ selector: 'app-outlet-selector', standalone: true, template: '' })
class OutletSelectorStubComponent {}

describe('AdminLayoutComponent multi-role navigation', () => {
  let fixture: ComponentFixture<AdminLayoutComponent>;
  let store: AuthStore;
  const links = () => Array.from(fixture.nativeElement.querySelectorAll('.navbar-menu a[href]') as NodeListOf<HTMLAnchorElement>)
    .map(link => link.getAttribute('href'));
  const setRoles = (...roles: string[]) => {
    store.setUser({ username: 'admin', email: 'admin@example.com', role: 'admin', roles });
    fixture.detectChanges();
  };

  beforeEach(() => {
    localStorage.removeItem('authToken');
    localStorage.removeItem('currentUser');
    TestBed.configureTestingModule({
      imports: [AdminLayoutComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useFactory: (auth: AuthStore) => ({ currentUser$: auth.user$ }), deps: [AuthStore] },
        { provide: OutletService, useValue: { selectedOutlet$: of(null) } }
      ]
    }).overrideComponent(AdminLayoutComponent, {
      remove: { imports: [OutletSelectorComponent] },
      add: { imports: [OutletSelectorStubComponent] }
    });
    store = TestBed.inject(AuthStore);
    fixture = TestBed.createComponent(AdminLayoutComponent);
  });

  it('retains admin menus and adds every assigned workspace without repeated destinations', () => {
    setRoles('admin', 'manager', 'chef', 'cashier', 'waiter', 'delivery-partner', 'user');
    for (const route of ['/admin/dashboard', '/admin/user-roles', '/admin/counter-sale', '/manager/dashboard', '/kitchen/dashboard', '/staff/dine-in', '/staff/cashier', '/partner/delivery', '/partner/payout', '/dashboard', '/menu', '/cart', '/orders']) {
      expect(links()).toContain(route);
    }
    expect(new Set(links()).size).toBe(links().length);
  });

  it('does not expose unassigned manager, partner, or customer workspaces', () => {
    setRoles('admin');
    expect(links()).not.toContain('/manager/dashboard');
    expect(links()).not.toContain('/partner/delivery');
    expect(links()).not.toContain('/cart');
    expect(links()).not.toContain('/orders');
    expect(links()).toContain('/staff/cashier');
  });

  it('opens Workspaces in the mobile menu and reacts to role changes', () => {
    setRoles('admin', 'partner');
    fixture.componentInstance.toggleMobileMenu();
    fixture.detectChanges();
    const button = Array.from(fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>)
      .find(candidate => candidate.textContent?.includes('Workspaces'))!;
    button.click();
    fixture.detectChanges();
    expect(button.getAttribute('aria-expanded')).toBe('true');
    expect(fixture.nativeElement.querySelector('.navbar-menu').classList.contains('active')).toBeTrue();
    expect(links()).toContain('/partner/delivery');
    setRoles('admin');
    expect(links()).not.toContain('/partner/delivery');
  });
});
