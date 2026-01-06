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
  activeDropdown: string | null = null;

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
      active: false,
      children: [
        {
          icon: '📋',
          label: 'Menu Items',
          route: '/admin/menu'
        },
        {
          icon: '📁',
          label: 'Categories',
          route: '/admin/category/crud'
        }
      ]
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
    },
    {
      icon: '📦',
      label: 'Inventory',
      route: '/admin/inventory',
      active: false
    },
    {
      icon: '🛠️',
      label: 'Tools',
      active: false,
      children: [
        {
          icon: '🧾',
          label: 'Cashier',
          route: '/admin/cashier'
        },
        {
          icon: '📊',
          label: 'Online Sales',
          route: '/admin/online-sale-tracker'
        },
        {
          icon: '💹',
          label: 'Profit Tracker',
          route: '/admin/online-profit-tracker'
        },
        {
          icon: '⏱️',
          label: 'KPT Analysis',
          route: '/admin/kpt-analysis'
        },
        {
          icon: '💲',
          label: 'Price Forecasting',
          route: '/admin/price-forecasting'
        },
        {
          icon: '🧮',
          label: 'Price Calculator',
          route: '/admin/price-calculator'
        },
         {
          icon: '🏷️',
          label: 'Discount Mapping',
          route: '/admin/discount-mapping'
        }
      ]
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

  toggleDropdown(label: string, event?: Event): void {
    if (event) {
      event.preventDefault();
      event.stopPropagation();
    }
    this.activeDropdown = this.activeDropdown === label ? null : label;
  }

  closeDropdown(): void {
    this.activeDropdown = null;
  }
}
