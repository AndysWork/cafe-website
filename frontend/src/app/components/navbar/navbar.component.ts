import { Component, OnInit, OnDestroy, HostListener, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule, Router } from '@angular/router';
import { AuthService, User } from '../../services/auth.service';
import { CartService } from '../../services/cart.service';
import { AnalyticsTrackingService } from '../../services/analytics-tracking.service';
import { AuthStore, CartStore } from '../../store';
import { NotificationCenterComponent } from '../notification-center/notification-center.component';

@Component({
  selector: 'app-navbar',
  standalone: true,
  imports: [CommonModule, RouterModule, NotificationCenterComponent],
  templateUrl: './navbar.component.html',
  styleUrls: ['./navbar.component.scss']
})
export class NavbarComponent implements OnInit, OnDestroy {
  isMobileMenuOpen = false;
  activeDropdown: string | null = null;
  private closeTimeout: any;
  private analyticsTracking = inject(AnalyticsTrackingService);
  private authStore = inject(AuthStore);
  private cartStore = inject(CartStore);

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
    // No subscriptions to clean up
  }

  get isAdmin(): boolean {
    return this.currentUser?.role === 'admin';
  }

  get isCustomer(): boolean {
    return this.currentUser?.role === 'user';
  }

  get isPartnerUser(): boolean {
    return this.currentUser?.role === 'partner' || this.currentUser?.role === 'delivery-partner';
  }

  get isKitchenUser(): boolean {
    return this.currentUser?.role === 'cook' || this.currentUser?.role === 'chef' || this.currentUser?.role === 'sous-chef';
  }

  get isStaffRoleUser(): boolean {
    return this.isManager || this.isKitchenUser;
  }

  get isManager(): boolean {
    return this.currentUser?.role === 'manager';
  }

  get isNonAdminLoggedIn(): boolean {
    return this.isLoggedIn && !this.isAdmin;
  }

  get isLoggedIn(): boolean {
    return this.currentUser !== null;
  }

  get isHomeScreen(): boolean {
    const path = this.router.url.split('?')[0].split('#')[0];
    return path === '/' || path === '/home';
  }

  get dashboardRoute(): string {
    if (this.isAdmin) return '/admin/dashboard';
    if (this.isManager) return '/manager/dashboard';
    if (this.isPartnerUser) return '/partner/delivery';
    if (this.isKitchenUser) return '/kitchen/dashboard';
    return '/dashboard';
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
    this.activeDropdown = null;
  }

  closeMobileMenu() {
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
    const clickedInside = target.closest('.dropdown');

    if (!clickedInside && this.activeDropdown) {
      this.activeDropdown = null;
    }
  }
}
