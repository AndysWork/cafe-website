import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { OrderService, Order, OrderItem } from '../../services/order.service';
import { MenuService, MenuItem } from '../../services/menu.service';
import { SalesService, Sales } from '../../services/sales.service';
import { environment } from '../../../environments/environment';
import { interval, Subscription, forkJoin } from 'rxjs';
import { switchMap, startWith } from 'rxjs/operators';

interface OnlineSale {
  _id?: string;
  platform: string;
  orderId: string;
  customerName?: string;
  orderAt: Date | string;
  orderedItems: Array<{ quantity: number; itemName: string }>;
  billSubTotal: number;
  packagingCharges: number;
  discountAmount: number;
  payout: number;
  platformDeduction: number;
  rating?: number;
  review?: string;
  freebies?: number;
}

interface MonthlyTrend {
  month: string;
  offlineSales: number;
  zomatoSales: number;
  swiggySales: number;
  totalSales: number;
}

interface TopCustomer {
  customerId: string;
  customerName: string;
  totalOrders: number;
  totalSpent: number;
}

interface MostOrderedItem {
  itemName: string;
  quantity: number;
  revenue: number;
}

@Component({
  selector: 'app-admin-dashboard',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './admin-dashboard.component.html',
  styleUrls: ['./admin-dashboard.component.scss']
})
export class AdminDashboardComponent implements OnInit, OnDestroy {
  private orderService = inject(OrderService);
  private menuService = inject(MenuService);
  private salesService = inject(SalesService);
  private http = inject(HttpClient);
  private refreshSubscription?: Subscription;

  stats = [
    { label: 'Total Menu Items', value: '0', icon: '🍽️', color: '#f38181' },
    { label: 'Online Customers', value: '0', icon: '👥', color: '#95e1d3' },
    { label: 'Avg Online Rating', value: '0', icon: '⭐', color: '#ffd93d' },
    { label: 'Total Revenue', value: '₹0', icon: '💰', color: '#4ecdc4' }
  ];



  monthlyTrends: MonthlyTrend[] = [];
  topCustomers: TopCustomer[] = [];
  mostOrderedItems: MostOrderedItem[] = [];
  onlineSales: OnlineSale[] = [];
  offlineSales: Sales[] = [];
  allOrders: Order[] = [];
  menuItems: MenuItem[] = [];

  isLoading = true;

  ngOnInit(): void {
    this.loadDashboardData();
    // Auto-refresh every 30 seconds
    this.refreshSubscription = interval(30000)
      .pipe(startWith(0))
      .subscribe(() => this.loadDashboardData());
  }

  ngOnDestroy(): void {
    if (this.refreshSubscription) {
      this.refreshSubscription.unsubscribe();
    }
  }

  loadDashboardData(): void {
    this.isLoading = true;

    // Load all data in parallel
    forkJoin({
      orders: this.orderService.getAllOrders(),
      menuItems: this.menuService.getMenuItems(),
      onlineSales: this.http.get<{ success: boolean; data: OnlineSale[] }>(`${environment.apiUrl}/online-sales`),
      offlineSales: this.salesService.getAllSales()
    }).subscribe({
      next: (data) => {
        this.allOrders = data.orders;
        this.menuItems = data.menuItems;
        this.onlineSales = data.onlineSales.data || [];
        this.offlineSales = data.offlineSales;

        this.updateStats();
        this.calculateMonthlyTrends();
        this.calculateTopCustomers();
        this.calculateMostOrderedItems();

        this.isLoading = false;
      },
      error: (error) => {
        console.error('Error loading dashboard data:', error);
        this.isLoading = false;
      }
    });
  }

  updateStats(): void {
    // Total Menu Items
    this.stats[0].value = this.menuItems.length.toString();

    // Online Customers (unique customer names from online sales)
    const uniqueOnlineCustomers = new Set(
      this.onlineSales
        .filter(s => s.customerName)
        .map(s => s.customerName?.toLowerCase())
    ).size;
    this.stats[1].value = uniqueOnlineCustomers.toString();

    // Average Online Rating
    const ratingsWithValues = this.onlineSales.filter(s => s.rating && s.rating > 0);
    const avgRating = ratingsWithValues.length > 0
      ? ratingsWithValues.reduce((sum, s) => sum + (s.rating || 0), 0) / ratingsWithValues.length
      : 0;
    this.stats[2].value = avgRating > 0 ? avgRating.toFixed(1) : 'N/A';

    // Total Revenue (Online + Offline)
    const onlineRevenue = this.onlineSales.reduce((sum, s) => sum + (s.payout || 0), 0);
    const offlineRevenue = this.offlineSales.reduce((sum, s) => sum + s.totalAmount, 0);
    const orderRevenue = this.allOrders
      .filter(o => o.paymentStatus === 'paid')
      .reduce((sum, o) => sum + o.total, 0);
    const totalRevenue = onlineRevenue + offlineRevenue + orderRevenue;
    this.stats[3].value = `₹${totalRevenue.toLocaleString('en-IN', { maximumFractionDigits: 0 })}`;
  }

  calculateMonthlyTrends(): void {
    const monthlyData: { [key: string]: MonthlyTrend } = {};

    // Get last 6 months
    const now = new Date();
    for (let i = 5; i >= 0; i--) {
      const date = new Date(now.getFullYear(), now.getMonth() - i, 1);
      const monthKey = `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}`;
      const monthName = date.toLocaleDateString('en-US', { month: 'short', year: 'numeric' });

      monthlyData[monthKey] = {
        month: monthName,
        offlineSales: 0,
        zomatoSales: 0,
        swiggySales: 0,
        totalSales: 0
      };
    }

    // Add offline sales
    this.offlineSales.forEach(sale => {
      const date = new Date(sale.date);
      const monthKey = `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}`;
      if (monthlyData[monthKey]) {
        monthlyData[monthKey].offlineSales += sale.totalAmount;
      }
    });

    // Add online sales
    this.onlineSales.forEach(sale => {
      const date = new Date(sale.orderAt);
      const monthKey = `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}`;
      if (monthlyData[monthKey]) {
        const payout = sale.payout || 0;
        if (sale.platform?.toLowerCase() === 'zomato') {
          monthlyData[monthKey].zomatoSales += payout;
        } else if (sale.platform?.toLowerCase() === 'swiggy') {
          monthlyData[monthKey].swiggySales += payout;
        }
      }
    });

    // Calculate totals
    Object.values(monthlyData).forEach(data => {
      data.totalSales = data.offlineSales + data.zomatoSales + data.swiggySales;
    });

    this.monthlyTrends = Object.values(monthlyData);
  }

  calculateTopCustomers(): void {
    const customerMap: { [key: string]: TopCustomer } = {};

    // From online sales (Zomato/Swiggy) only
    this.onlineSales.forEach(sale => {
      const customerName = sale.customerName || 'Unknown';
      const key = customerName.toLowerCase();
      if (!customerMap[key]) {
        customerMap[key] = {
          customerId: key,
          customerName: customerName,
          totalOrders: 0,
          totalSpent: 0
        };
      }
      customerMap[key].totalOrders++;
      customerMap[key].totalSpent += sale.payout || 0;
    });

    // Sort by total spent and take top 5
    this.topCustomers = Object.values(customerMap)
      .filter(c => c.customerName !== 'Unknown')
      .sort((a, b) => b.totalSpent - a.totalSpent)
      .slice(0, 5);
  }

  calculateMostOrderedItems(): void {
    const itemMap: { [key: string]: MostOrderedItem } = {};

    // From online orders
    this.allOrders.forEach(order => {
      order.items.forEach(item => {
        const key = item.name;
        if (!itemMap[key]) {
          itemMap[key] = {
            itemName: item.name,
            quantity: 0,
            revenue: 0
          };
        }
        itemMap[key].quantity += item.quantity;
        itemMap[key].revenue += item.total;
      });
    });

    // From online sales (Zomato/Swiggy)
    this.onlineSales.forEach(sale => {
      sale.orderedItems?.forEach(item => {
        const key = item.itemName;
        if (!itemMap[key]) {
          itemMap[key] = {
            itemName: item.itemName,
            quantity: 0,
            revenue: 0
          };
        }
        itemMap[key].quantity += item.quantity;
      });
    });

    // From offline sales
    this.offlineSales.forEach(sale => {
      sale.items.forEach(item => {
        const key = item.itemName;
        if (!itemMap[key]) {
          itemMap[key] = {
            itemName: item.itemName,
            quantity: 0,
            revenue: 0
          };
        }
        itemMap[key].quantity += item.quantity;
        itemMap[key].revenue += item.totalPrice;
      });
    });

    // Sort by quantity and take top 5
    this.mostOrderedItems = Object.values(itemMap)
      .sort((a, b) => b.quantity - a.quantity)
      .slice(0, 5);
  }



  getMaxSales(): number {
    return Math.max(...this.monthlyTrends.map(t => t.totalSales), 1);
  }

  getBarHeight(value: number, max: number): number {
    return max > 0 ? (value / max) * 100 : 0;
  }

  getXPosition(index: number): number {
    const chartWidth = 700; // 750 - 50 margin
    const spacing = chartWidth / (this.monthlyTrends.length - 1 || 1);
    return 50 + (index * spacing);
  }

  getYPosition(value: number): number {
    const chartHeight = 250; // 300 - 50 margin
    const maxSales = this.getMaxSales();
    const ratio = maxSales > 0 ? value / maxSales : 0;
    return 300 - (ratio * chartHeight);
  }

  getLinePoints(type: 'offline' | 'zomato' | 'swiggy' | 'total'): string {
    return this.monthlyTrends
      .map((trend, index) => {
        const x = this.getXPosition(index);
        let y: number;
        switch (type) {
          case 'offline':
            y = this.getYPosition(trend.offlineSales);
            break;
          case 'zomato':
            y = this.getYPosition(trend.zomatoSales);
            break;
          case 'swiggy':
            y = this.getYPosition(trend.swiggySales);
            break;
          case 'total':
            y = this.getYPosition(trend.totalSales);
            break;
        }
        return `${x},${y}`;
      })
      .join(' ');
  }
}
