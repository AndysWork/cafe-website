import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { OrderService, Order, OrderItem } from '../../services/order.service';
import { MenuService, MenuItem } from '../../services/menu.service';
import { SalesService, Sales } from '../../services/sales.service';
import { ExpenseService, Expense } from '../../services/expense.service';
import { OutletService } from '../../services/outlet.service';
import { environment } from '../../../environments/environment';
import { forkJoin, of, Subscription } from 'rxjs';
import { catchError, filter, map } from 'rxjs/operators';
import { AuthStore } from '../../store/auth.store';
import { formatIstDate, getIstDateString } from '../../utils/date-utils';
import { calculateDashboardInsights, calculateDashboardOverview, DashboardDay, DashboardInsights, DashboardOverview, DashboardSummary } from './dashboard-overview';

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
  webSales: number;
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

interface OutboxHealth {
  pendingCount: number;
  processingCount: number;
  retryingCount: number;
  deadLetterCount: number;
  completedLast15Minutes: number;
  capturedAtUtc: string;
}

interface OutboxDeadLetterItem {
  id: string;
  eventType: string;
  aggregateType: string;
  aggregateId: string;
  status?: string;
  retryCount: number;
  maxRetries: number;
  error?: string;
  createdAt?: string;
  lastAttemptAt?: string;
  deadLetteredAt?: string;
  isPushFailure?: boolean;
}

interface OutboxDeadLettersResponse {
  count: number;
  items: OutboxDeadLetterItem[];
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
  private expenseService = inject(ExpenseService);
  private outletService = inject(OutletService);
  private http = inject(HttpClient);
  readonly authStore = inject(AuthStore);
  private outletSubscription?: Subscription;
  periodDays: 7 | 30 | 90 = 30;
  trendMetric: 'revenue' | 'orders' | 'average' = 'revenue';
  overview: DashboardOverview = calculateDashboardOverview([], [], 30, getIstDateString());
  insights: DashboardInsights = calculateDashboardInsights([], [], 30, getIstDateString());
  readonly overviewCards: { key: keyof DashboardSummary; label: string; icon: string; color: string; currency: boolean }[] = [
    { key: 'revenue', label: 'Total Revenue', icon: '💰', color: 'green', currency: true },
    { key: 'orders', label: 'Sales Records', icon: '🛒', color: 'blue', currency: false },
    { key: 'average', label: 'Avg. Record Value', icon: '📈', color: 'purple', currency: true },
    { key: 'customers', label: 'Online Customers', icon: '👥', color: 'orange', currency: false }
  ];

  stats = [
    { label: 'Total Menu Items', value: '0', icon: '🍽️', color: '#f38181' },
    { label: 'Online Customers', value: '0', icon: '👥', color: '#95e1d3' },
    { label: 'Avg Online Rating', value: '0', icon: '⭐', color: '#ffd93d' },
    { label: 'Total Revenue', value: '₹0', icon: '💰', color: '#4ecdc4' }
  ];



  monthlyTrends: MonthlyTrend[] = [];
  topCustomers: TopCustomer[] = [];
  onlineMostOrderedItems: MostOrderedItem[] = [];
  offlineMostOrderedItems: MostOrderedItem[] = [];
  outboxHealth: OutboxHealth | null = null;
  outboxDeadLetters: OutboxDeadLetterItem[] = [];
  showOutboxDeadLetters = false;
  onlineSales: OnlineSale[] = [];
  offlineSales: Sales[] = [];
  allOrders: Order[] = [];
  menuItems: MenuItem[] = [];
  expenses: Expense[] = [];

  // Online stats
  onlineStats = {
    totalOrders: 0,
    totalRevenue: 0,
    avgRating: 0,
    uniqueCustomers: 0,
    dailyAvgPayout: 0
  };

  // Offline stats
  offlineStats = {
    totalOrders: 0,
    totalRevenue: 0,
    dailyAvgSales: 0,
    dailyAvgExpenses: 0
  };

  isLoading = true;
  errorMessage: string | null = null;

  ngOnInit(): void {
    // Subscribe to outlet changes and reload data
    this.outletSubscription = this.outletService.selectedOutlet$
      .pipe(filter(outlet => outlet !== null))
      .subscribe(() => {
        this.loadDashboardData();
      });

    // Load immediately if outlet is already selected
    if (this.outletService.getSelectedOutlet()) {
      this.loadDashboardData();
    }
  }

  ngOnDestroy(): void {
    this.outletSubscription?.unsubscribe();
  }

  loadDashboardData(): void {
    this.isLoading = true;
    this.errorMessage = null;

    // Load all data in parallel
    forkJoin({
      orders: this.orderService.getAllOrders(),
      menuItems: this.menuService.getMenuItems(),
      onlineSales: this.http.get<{ success: boolean; data: OnlineSale[] }>(`${environment.apiUrl}/online-sales?includeWebSales=true`),
      offlineSales: this.salesService.getAllSales(),
      expenses: this.expenseService.getAllExpenses(),
      outboxHealth: this.http.get<OutboxHealth>(`${environment.apiUrl}/manage/ops/outbox-health`).pipe(
        catchError(() => of(null))
      ),
      outboxDeadLetters: this.http.get<OutboxDeadLettersResponse>(`${environment.apiUrl}/manage/ops/notification-dead-letters`).pipe(
        // Endpoint returns a wrapper payload { count, items }.
        // Keep dashboard model unchanged by projecting to items.
        map(response => response?.items || []),
        catchError(() => of([]))
      )
    }).subscribe({
      next: (data) => {
        this.allOrders = data.orders;
        this.menuItems = data.menuItems;
        this.onlineSales = data.onlineSales.data || [];
        this.offlineSales = data.offlineSales;
        this.expenses = data.expenses;
        this.outboxHealth = data.outboxHealth;
        this.outboxDeadLetters = data.outboxDeadLetters || [];

        this.updateStats();
        this.calculateMonthlyTrends();
        this.calculateTopCustomers();
        this.calculateOnlineMostOrderedItems();
        this.calculateOfflineMostOrderedItems();
        this.refreshOverview();

        this.isLoading = false;
      },
      error: (error) => {
        console.error('Error loading dashboard data:', error);
        this.errorMessage = 'Failed to load dashboard data. Please try again.';
        this.isLoading = false;
      }
    });
  }

  setPeriod(value: string): void {
    if (value === '7') this.periodDays = 7;
    else if (value === '30') this.periodDays = 30;
    else if (value === '90') this.periodDays = 90;
    else return;
    this.refreshOverview();
  }

  setTrendMetric(value: string): void {
    if (value === 'revenue' || value === 'orders' || value === 'average') this.trendMetric = value;
  }

  refreshOverview(): void {
    const today = getIstDateString();
    this.overview = calculateDashboardOverview(this.onlineSales, this.offlineSales, this.periodDays, today);
    this.insights = calculateDashboardInsights(this.allOrders, this.expenses, this.periodDays, today);
  }

  get maxHourlyOrders(): number {
    return Math.max(...this.insights.hourlyOrders.map(bucket => bucket.count), 1);
  }

  formatHour(hour: number): string {
    const format = (value: number) => `${value % 12 || 12} ${value < 12 ? 'AM' : 'PM'}`;
    return `${format(hour)}–${format((hour + 1) % 24)}`;
  }

  get expenseChange(): number | null {
    const previous = this.insights.expenses.previousTotal;
    return previous ? (this.insights.expenses.total - previous) / Math.abs(previous) * 100 : null;
  }

  get bestSalesDay(): DashboardDay | null {
    return this.overview.days.filter(day => day.orders > 0)
      .reduce<DashboardDay | null>((best, day) => !best || day.revenue > best.revenue ? day : best, null);
  }

  changeFor(key: keyof DashboardSummary): number | null {
    const previous = this.overview.previous[key];
    return previous ? (this.overview.current[key] - previous) / Math.abs(previous) * 100 : null;
  }

  trendValue(day: DashboardDay): number {
    if (this.trendMetric === 'orders') return day.orders;
    if (this.trendMetric === 'average') return day.orders ? day.revenue / day.orders : 0;
    return day.revenue;
  }

  get trendMax(): number {
    return Math.max(...this.overview.days.map(day => this.trendValue(day)), 0);
  }

  get trendMin(): number {
    return Math.min(...this.overview.days.map(day => this.trendValue(day)), 0);
  }

  chartX(index: number): number {
    return 40 + index * 530 / Math.max(this.overview.days.length - 1, 1);
  }

  chartY(day: DashboardDay): number {
    const min = this.trendMin;
    return 168 - (this.trendValue(day) - min) / (this.trendMax - min || 1) * 135;
  }

  get trendPoints(): string {
    return this.overview.days.map((day, index) => `${this.chartX(index)},${this.chartY(day)}`).join(' ');
  }

  get trendArea(): string {
    const baseline = this.chartY({ date: '', revenue: 0, orders: 0 });
    return `40,${baseline} ${this.trendPoints} 570,${baseline}`;
  }

  showDateLabel(index: number): boolean {
    const last = this.overview.days.length - 1;
    return index === 0 || index === last || (index > 0 && index < last && index % Math.ceil(last / 4) === 0);
  }

  chartDate(date: string): string {
    return formatIstDate(date, { month: 'short', day: 'numeric' });
  }

  get sourceGradient(): string {
    if (!this.overview.current.orders) return '#e8eef5';
    let position = 0;
    return `conic-gradient(${this.overview.sources.filter(source => source.count > 0).map(source => {
      const start = position;
      position += source.count / this.overview.current.orders * 100;
      return `${source.color} ${start}% ${position}%`;
    }).join(', ')})`;
  }

  itemImage(name: string): string | undefined {
    const item = this.menuItems.find(menuItem => menuItem.name?.trim().toLowerCase() === name.toLowerCase());
    return item?.imageThumbnailUrl || item?.imageUrl;
  }

  updateStats(): void {
    // Total Menu Items
    this.stats[0].value = this.menuItems.length.toString();

    // Online Stats
    this.onlineStats.totalOrders = this.onlineSales.length;
    this.onlineStats.totalRevenue = this.onlineSales.reduce((sum, s) => sum + (s.payout || 0), 0);
    const uniqueOnlineCustomers = new Set(
      this.onlineSales
        .filter(s => s.customerName)
        .map(s => s.customerName?.toLowerCase())
    ).size;
    this.onlineStats.uniqueCustomers = uniqueOnlineCustomers;
    this.stats[1].value = uniqueOnlineCustomers.toString();

    const ratingsWithValues = this.onlineSales.filter(s => s.rating && s.rating > 0);
    const avgRating = ratingsWithValues.length > 0
      ? ratingsWithValues.reduce((sum, s) => sum + (s.rating || 0), 0) / ratingsWithValues.length
      : 0;
    this.onlineStats.avgRating = avgRating;
    this.stats[2].value = avgRating > 0 ? avgRating.toFixed(1) : 'N/A';

    // Calculate daily average payout for online sales
    const uniqueOnlineDays = new Set(this.onlineSales.map(s => {
      const date = new Date(s.orderAt);
      return `${date.getFullYear()}-${date.getMonth() + 1}-${date.getDate()}`;
    })).size;
    this.onlineStats.dailyAvgPayout = uniqueOnlineDays > 0
      ? this.onlineStats.totalRevenue / uniqueOnlineDays
      : 0;

    // Offline Stats
    this.offlineStats.totalOrders = this.offlineSales.length;
    this.offlineStats.totalRevenue = this.offlineSales.reduce((sum, s) => sum + s.totalAmount, 0);

    // Calculate daily average sales
    const uniqueDays = new Set(this.offlineSales.map(s => {
      const date = new Date(s.date);
      return `${date.getFullYear()}-${date.getMonth() + 1}-${date.getDate()}`;
    })).size;
    this.offlineStats.dailyAvgSales = uniqueDays > 0
      ? this.offlineStats.totalRevenue / uniqueDays
      : 0;

    // Calculate daily average expenses
    const totalExpenses = this.expenses.reduce((sum, e) => sum + e.amount, 0);
    const uniqueExpenseDays = new Set(this.expenses.map(e => {
      const date = new Date(e.date);
      return `${date.getFullYear()}-${date.getMonth() + 1}-${date.getDate()}`;
    })).size;
    this.offlineStats.dailyAvgExpenses = uniqueExpenseDays > 0
      ? totalExpenses / uniqueExpenseDays
      : 0;

    // Total Revenue (Online + Offline). Web orders are included in online sales analytics.
    const totalRevenue = this.onlineStats.totalRevenue + this.offlineStats.totalRevenue;
    this.stats[3].value = `₹${totalRevenue.toLocaleString('en-IN', { maximumFractionDigits: 0 })}`;
  }

  calculateMonthlyTrends(): void {
    const monthlyData: { [key: string]: MonthlyTrend } = {};

    // Get last 6 months
    const now = new Date();
    for (let i = 5; i >= 0; i--) {
      const date = new Date(now.getFullYear(), now.getMonth() - i, 1);
      const monthKey = `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}`;
      const monthName = date.toLocaleDateString('en-IN', { month: 'short', year: 'numeric', timeZone: 'Asia/Kolkata' });

      monthlyData[monthKey] = {
        month: monthName,
        offlineSales: 0,
        zomatoSales: 0,
        swiggySales: 0,
        webSales: 0,
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
        } else if (sale.platform?.toLowerCase() === 'web sales') {
          monthlyData[monthKey].webSales += payout;
        }
      }
    });

    // Calculate totals
    Object.values(monthlyData).forEach(data => {
      data.totalSales = data.offlineSales + data.zomatoSales + data.swiggySales + data.webSales;
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

  calculateOnlineMostOrderedItems(): void {
    const itemMap: { [key: string]: MostOrderedItem } = {};

    // From online sales (Zomato/Swiggy) only
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
        // Calculate approximate revenue from online sales
        // Note: Individual item prices not available, so we estimate based on bill subtotal
        const itemRatio = item.quantity / sale.orderedItems.reduce((sum, i) => sum + i.quantity, 0);
        itemMap[key].revenue += (sale.billSubTotal || 0) * itemRatio;
      });
    });

    // Sort by quantity and take top 5
    this.onlineMostOrderedItems = Object.values(itemMap)
      .sort((a, b) => b.quantity - a.quantity)
      .slice(0, 5);
  }

  calculateOfflineMostOrderedItems(): void {
    const itemMap: { [key: string]: MostOrderedItem } = {};

    // From offline sales only
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
    this.offlineMostOrderedItems = Object.values(itemMap)
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

  getLinePoints(type: 'offline' | 'zomato' | 'swiggy' | 'web' | 'total'): string {
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
          case 'web':
            y = this.getYPosition(trend.webSales);
            break;
          case 'total':
            y = this.getYPosition(trend.totalSales);
            break;
        }
        return `${x},${y}`;
      })
      .join(' ');
  }

  trackByIndex(index: number): number { return index; }

  toggleOutboxDeadLetters(): void {
    this.showOutboxDeadLetters = !this.showOutboxDeadLetters;
  }
}
