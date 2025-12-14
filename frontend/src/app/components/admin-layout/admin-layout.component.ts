import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule, Router } from '@angular/router';
import { AuthService } from '../../services/auth.service';

@Component({
  selector: 'app-admin-layout',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './admin-layout.component.html',
  styleUrls: ['./admin-layout.component.scss']
})
export class AdminLayoutComponent {
  currentUser$;
  isMobileMenuOpen = false;

  menuItems = [
    {
      icon: '📊',
      label: 'Dashboard',
      route: '/admin/dashboard',
      active: false
    },
    {
      icon: '🍽️',
      label: 'Menu',
      route: '/admin/menu',
      active: false
    },
    {
      icon: '🎁',
      label: 'Offers',
      route: '/admin/offers',
      active: false
    },
    {
      icon: '🏆',
      label: 'Loyalty',
      route: '/admin/loyalty',
      active: false
    },
    {
      icon: '💰',
      label: 'Sales',
      route: '/admin/sales',
      active: false
    },
    {
      icon: '💸',
      label: 'Expenses',
      route: '/admin/expenses',
      active: false
    },
    {
      icon: '📈',
      label: 'Analytics',
      route: '/admin/analytics',
      active: false
    }
  ];

  constructor(
    private authService: AuthService,
    private router: Router
  ) {
    this.currentUser$ = this.authService.currentUser$;
  }

  toggleMobileMenu(): void {
    this.isMobileMenuOpen = !this.isMobileMenuOpen;
  }

  closeMobileMenu(): void {
    this.isMobileMenuOpen = false;
  }

  onLogout(): void {
    this.authService.logout();
    this.router.navigate(['/home']);
  }

  setActiveMenu(index: number): void {
    this.menuItems.forEach((item, i) => {
      item.active = i === index;
    });
  }
}
