import { Component, OnInit, OnDestroy, HostListener, computed, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule, Router } from '@angular/router';
import { AuthService, User } from '../../services/auth.service';
import { CartService } from '../../services/cart.service';
import { AnalyticsTrackingService } from '../../services/analytics-tracking.service';
import { AuthStore, CartStore } from '../../store';
import { NotificationCenterComponent } from '../notification-center/notification-center.component';
import { DineInService } from '../../services/dine-in.service';
import { getRoleNavigationItems, groupRoleNavigationItems } from '../../utils/role-navigation';

@Component({
  selector: 'app-navbar',
  standalone: true,
  imports: [CommonModule, RouterModule, NotificationCenterComponent],
  templateUrl: './navbar.component.html',
  styleUrls: ['./navbar.component.scss']
})
export class NavbarComponent implements OnInit, OnDestroy {
  public dineInService = inject(DineInService);
  isMobileMenuOpen = false;
  activeDropdown: string | null = null;
  private closeTimeout?: ReturnType<typeof setTimeout>;
  private analyticsTracking = inject(AnalyticsTrackingService);
  private authStore = inject(AuthStore);
  private cartStore = inject(CartStore);
  readonly navigationGroups = computed(() => {
    const items = getRoleNavigationItems(this.authStore.roles());
    if (this.isNonCustomerNonAdminLoggedIn) {
      items.push({ label: 'Profile', route: '/profile', icon: '👤', group: 'My Account' });
    }
    return groupRoleNavigationItems(items);
  });

  // Signal-based state reads — no subscriptions needed, auto-updates via change detection
  get currentUser(): User | null { return this.authStore.user(); }
  get cartItemCount(): number { return this.cartStore.itemCount(); }

  constructor(
    private authService: AuthService,
    private cartService: CartService,
    private router: Router
  ) {}

  ngOnInit() {
    // No subscriptions needed — signals are read directly in getters above
  }

  ngOnDestroy() {
    this.cancelCloseDropdown();
  }

  get isAdmin(): boolean {
    return this.authStore.isAdmin();
  }

  get isCustomer(): boolean {
    return this.authStore.hasRole('user');
  }

  openDineInBill(): void {
    this.dineInService.openBillModal();
  }

  get isNonAdminLoggedIn(): boolean {
    return this.isLoggedIn && !this.isAdmin;
  }

  get isCustomerLoggedIn(): boolean {
    return this.isLoggedIn && this.isCustomer && !this.isAdmin;
  }

  get isNonCustomerNonAdminLoggedIn(): boolean {
    return this.isLoggedIn && !this.isAdmin && !this.isCustomer;
  }

  get isLoggedIn(): boolean {
    return this.currentUser !== null;
  }

  get displayName(): string {
    if (this.currentUser?.firstName) {
      return this.currentUser.firstName;
    }
    return this.currentUser?.username || '';
  }

  toggleMobileMenu() {
    this.isMobileMenuOpen = !this.isMobileMenuOpen;
  }

  toggleDropdown(dropdown: string) {
    this.cancelCloseDropdown();
    this.activeDropdown = this.activeDropdown === dropdown ? null : dropdown;
  }

  openDropdown(dropdown: string) {
    if (this.closeTimeout) {
      clearTimeout(this.closeTimeout);
    }
    this.activeDropdown = dropdown;
  }

  scheduleCloseDropdown() {
    this.closeTimeout = setTimeout(() => {
      this.activeDropdown = null;
    }, 200);
  }

  cancelCloseDropdown() {
    if (this.closeTimeout) {
      clearTimeout(this.closeTimeout);
    }
  }

  closeDropdowns() {
    this.cancelCloseDropdown();
    this.activeDropdown = null;
  }

  closeMobileMenu() {
    this.cancelCloseDropdown();
    this.isMobileMenuOpen = false;
    this.activeDropdown = null;
  }

  onLogin() {
    this.router.navigate(['/login']);
    this.closeMobileMenu();
  }

  onLogout() {
    this.analyticsTracking.trackLogout();
    this.authService.logout();
    this.router.navigate(['/']);
    this.closeMobileMenu();
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent) {
    const target = event.target as HTMLElement;
    const clickedInside = target.closest('.profile-dropdown, .navigation-group');

    if (!clickedInside && this.activeDropdown) {
      this.closeDropdowns();
    }
  }

  @HostListener('document:keydown.escape')
  onEscape() {
    this.closeMobileMenu();
  }
}
