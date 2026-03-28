import { Component, OnInit, OnDestroy, HostListener, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule, Router } from '@angular/router';
import { AuthService, User } from '../../services/auth.service';
import { CartService } from '../../services/cart.service';
import { AnalyticsTrackingService } from '../../services/analytics-tracking.service';
import { AuthStore, CartStore } from '../../store';

@Component({
  selector: 'app-navbar',
  standalone: true,
  imports: [CommonModule, RouterModule],
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

  get isUser(): boolean {
    return this.currentUser?.role === 'user';
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
