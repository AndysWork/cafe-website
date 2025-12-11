import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule, Router } from '@angular/router';
import { AuthService, User } from '../../services/auth.service';
import { Subscription } from 'rxjs';

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
  currentUser: User | null = null;
  private authSubscription?: Subscription;

  constructor(
    private authService: AuthService,
    private router: Router
  ) {}

  ngOnInit() {
    this.authSubscription = this.authService.currentUser$.subscribe(
      user => this.currentUser = user
    );
  }

  ngOnDestroy() {
    if (this.authSubscription) {
      this.authSubscription.unsubscribe();
    }
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
    this.authService.logout();
    this.router.navigate(['/']);
    this.closeMobileMenu();
  }
}
