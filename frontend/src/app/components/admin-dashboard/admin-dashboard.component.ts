import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule, Router } from '@angular/router';
import { AuthService } from '../../services/auth.service';

@Component({
  selector: 'app-admin-dashboard',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './admin-dashboard.component.html',
  styleUrls: ['./admin-dashboard.component.scss']
})
export class AdminDashboardComponent {
  currentUser$;
  isSidebarCollapsed = false;

  menuItems = [
    {
      icon: '📊',
      label: 'Dashboard',
      route: '/admin/dashboard',
      active: true
    },
    {
      icon: '🍽️',
      label: 'Menu Management',
      route: '/admin/menu',
      active: false
    },
    {
      icon: '�',
      label: 'Menu Upload',
      route: '/admin/menu/upload',
      active: false
    },
    {
      icon: '�📁',
      label: 'Category Upload',
      route: '/admin/category/upload',
      active: false
    },
    {
      icon: '✏️',
      label: 'Category CRUD',
      route: '/admin/category/crud',
      active: false
    },
    {
      icon: '📦',
      label: 'Orders',
      route: '/admin/orders',
      active: false
    },
    {
      icon: '👥',
      label: 'Customers',
      route: '/admin/customers',
      active: false
    },
    {
      icon: '📈',
      label: 'Analytics',
      route: '/admin/analytics',
      active: false
    },
    {
      icon: '⚙️',
      label: 'Settings',
      route: '/admin/settings',
      active: false
    }
  ];

  stats = [
    { label: 'Total Orders', value: '1,234', icon: '📦', color: '#ff6b6b' },
    { label: 'Revenue', value: '₹45,678', icon: '💰', color: '#4ecdc4' },
    { label: 'Customers', value: '856', icon: '👥', color: '#95e1d3' },
    { label: 'Menu Items', value: '45', icon: '🍽️', color: '#f38181' }
  ];

  recentOrders = [
    { id: '#1234', customer: 'John Doe', items: 3, total: '₹450', status: 'Delivered', time: '2 mins ago' },
    { id: '#1235', customer: 'Jane Smith', items: 2, total: '₹320', status: 'Preparing', time: '5 mins ago' },
    { id: '#1236', customer: 'Mike Johnson', items: 5, total: '₹780', status: 'On the way', time: '10 mins ago' },
    { id: '#1237', customer: 'Sarah Wilson', items: 1, total: '₹150', status: 'Pending', time: '15 mins ago' }
  ];

  constructor(
    private authService: AuthService,
    private router: Router
  ) {
    this.currentUser$ = this.authService.currentUser$;
  }

  toggleSidebar(): void {
    this.isSidebarCollapsed = !this.isSidebarCollapsed;
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

  getStatusClass(status: string): string {
    const statusMap: { [key: string]: string } = {
      'Delivered': 'status-delivered',
      'Preparing': 'status-preparing',
      'On the way': 'status-ontheway',
      'Pending': 'status-pending'
    };
    return statusMap[status] || 'status-pending';
  }
}
