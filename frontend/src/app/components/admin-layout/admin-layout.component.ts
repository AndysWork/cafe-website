import { Component, NgZone, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule, Router } from '@angular/router';
import { AuthService } from '../../services/auth.service';
import { OutletService } from '../../services/outlet.service';
import { OutletSelectorComponent } from '../outlet-selector/outlet-selector.component';

@Component({
  selector: 'app-admin-layout',
  standalone: true,
  imports: [CommonModule, RouterModule, OutletSelectorComponent],
  templateUrl: './admin-layout.component.html',
  styleUrls: ['./admin-layout.component.scss']
})
export class AdminLayoutComponent implements OnDestroy {
  currentUser$;
  selectedOutlet$;
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
      icon: '👁️',
      label: 'User Analytics',
      route: '/admin/user-analytics',
      active: false
    },
    {
      icon: '📦',
      label: 'Inventory',
      route: '/admin/inventory',
      active: false
    },
    {
      icon: '👥',
      label: 'Staff',
      active: false,
      children: [
        {
          icon: '👔',
          label: 'Staff Management',
          route: '/admin/staff'
        },
        {
          icon: '📝',
          label: 'Daily Performance Entry',
          route: '/admin/daily-performance'
        },
        {
          icon: '💰',
          label: 'Bonus Dashboard',
          route: '/admin/bonus-calculation'
        },
        {
          icon: '⚙️',
          label: 'Bonus Configuration',
          route: '/admin/bonus-configuration'
        },
        {
          icon: '📊',
          label: 'Staff Performance',
          route: '/admin/staff-performance'
        },
        {
          icon: '🕐',
          label: 'Attendance',
          route: '/admin/attendance'
        }
      ]
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
    },
    {
      icon: '🏪',
      label: 'Operations',
      active: false,
      children: [
        {
          icon: '🍳',
          label: 'Kitchen Display',
          route: '/admin/kitchen-display'
        },
        {
          icon: '📋',
          label: 'Reservations',
          route: '/admin/reservations'
        },
        {
          icon: '🗑️',
          label: 'Wastage',
          route: '/admin/wastage'
        },
        {
          icon: '🚚',
          label: 'Delivery Zones',
          route: '/admin/delivery-zones'
        },
        {
          icon: '🏍️',
          label: 'Delivery Partners',
          route: '/admin/delivery-partners'
        },
        {
          icon: '📦',
          label: 'Auto Reorder',
          route: '/admin/auto-reorder'
        }
      ]
    },
    {
      icon: '📣',
      label: 'Marketing',
      active: false,
      children: [
        {
          icon: '🍔',
          label: 'Combo Meals',
          route: '/admin/combos'
        },
        {
          icon: '🎉',
          label: 'Happy Hours',
          route: '/admin/happy-hours'
        },
        {
          icon: '📅',
          label: 'Subscriptions',
          route: '/admin/subscriptions'
        },
        {
          icon: '👤',
          label: 'Customer Segments',
          route: '/admin/customer-segments'
        }
      ]
    },
    {
      icon: '📊',
      label: 'Reports',
      active: false,
      children: [
        {
          icon: '📥',
          label: 'Export Reports',
          route: '/admin/reports'
        },
        {
          icon: '🏢',
          label: 'Branch Comparison',
          route: '/admin/branch-comparison'
        }
      ]
    }
  ];

  // Profile dropdown state
  isProfileDropdownOpen = false;
  private clickHandler: ((event: Event) => void) | null = null;

  constructor(
    private authService: AuthService,
    private outletService: OutletService,
    private router: Router,
    private ngZone: NgZone
  ) {
    this.currentUser$ = this.authService.currentUser$;
    this.selectedOutlet$ = this.outletService.selectedOutlet$;

    // Close dropdowns when clicking outside
    if (typeof document !== 'undefined') {
      this.clickHandler = (event: Event) => {
        const target = event.target as HTMLElement;
        // Don't close if clicking on dropdown button or inside dropdown
        if (!target.closest('.nav-item-dropdown') && !target.closest('.profile-dropdown-container')) {
          this.ngZone.run(() => {
            this.closeDropdown();
            this.closeProfileDropdown();
          });
        }
      };
      document.addEventListener('click', this.clickHandler);
    }
  }

  ngOnDestroy(): void {
    if (this.clickHandler) {
      document.removeEventListener('click', this.clickHandler);
    }
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

  toggleProfileDropdown(event?: Event): void {
    if (event) {
      event.preventDefault();
      event.stopPropagation();
    }
    this.isProfileDropdownOpen = !this.isProfileDropdownOpen;
  }

  closeProfileDropdown(): void {
    this.isProfileDropdownOpen = false;
  }

  trackByIndex(index: number): number { return index; }
}
