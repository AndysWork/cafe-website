import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { SalesService, Sales } from '../../services/sales.service';
import { ExpenseService, ExpenseAnalytics } from '../../services/expense.service';
import { getIstNow, getIstDateString, getIstDaysDifference, convertToIst, formatIstDate, getIstInputDate, getIstStartOfDay, getIstEndOfDay } from '../../utils/date-utils';
import { environment } from '../../../environments/environment';

interface SalesInsights {
  totalRevenue: number;
  totalTransactions: number;
  averageTransaction: number;
  topSellingItems: { name: string; quantity: number; revenue: number }[];
  paymentMethodBreakdown: { method: string; count: number; total: number }[];
  dailyAverage: number;
  weeklyTrend: { week: string; total: number }[];
  monthlyComparison: { month: string; total: number; transactions: number }[];
  peakSalesDays: { day: string; total: number }[];
  growthRate: number;
  itemCategoryBreakdown: { category: string; count: number; revenue: number }[];
}

@Component({
  selector: 'app-admin-analytics',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './admin-analytics.component.html',
  styleUrls: ['./admin-analytics.component.scss']
})
export class AdminAnalyticsComponent implements OnInit {
  loading = false;
  salesData: Sales[] = [];
  insights: SalesInsights | null = null;

  // Expense Analytics
  expenseAnalytics: ExpenseAnalytics | null = null;
  expenseLoading = false;
  selectedAnalyticsTab: 'sales' | 'expenses' | 'earnings' | 'online' = 'sales';
  expenseSource: 'All' | 'Offline' | 'Online' = 'All';

  // Earnings Analytics
  earningsData: any = null;
  earningsLoading = false;

  // Online Sales Analytics
  onlineAnalytics: any = null;
  onlineLoading = false;
  selectedPlatform: 'All' | 'Zomato' | 'Swiggy' = 'All';

  // Math for template
  Math = Math;

  // Date filters
  dateRange = {
    startDate: (() => {
      const ist = getIstNow();
      ist.setMonth(ist.getMonth() - 1);
      return getIstInputDate(ist);
    })(),
    endDate: getIstDateString()
  };

  constructor(
    private salesService: SalesService,
    private expenseService: ExpenseService,
    private http: HttpClient
  ) {}

  ngOnInit() {
    this.loadSalesInsights();
    this.loadExpenseAnalytics();
    this.loadEarningsAnalytics();
    this.loadOnlineSalesAnalytics();
  }

  switchTab(tab: 'sales' | 'expenses' | 'earnings' | 'online') {
    this.selectedAnalyticsTab = tab;
    if (tab === 'expenses' && !this.expenseAnalytics) {
      this.loadExpenseAnalytics();
    } else if (tab === 'earnings' && !this.earningsData) {
      this.loadEarningsAnalytics();
    } else if (tab === 'online' && !this.onlineAnalytics) {
      this.loadOnlineSalesAnalytics();
    }
  }

  switchExpenseSource(source: 'All' | 'Offline' | 'Online') {
    this.expenseSource = source;
    this.loadExpenseAnalytics();
  }

  loadExpenseAnalytics() {
    this.expenseLoading = true;
    this.expenseService.getExpenseAnalytics(
      this.dateRange.startDate,
      this.dateRange.endDate,
      this.expenseSource
    ).subscribe({
      next: (data) => {
        this.expenseAnalytics = data;
        this.expenseLoading = false;
      },
      error: (err) => {
        console.error('Error loading expense analytics:', err);
        this.expenseLoading = false;
      }
    });
  }

  loadSalesInsights() {
    this.loading = true;
    this.salesService.getAllSales().subscribe({
      next: (data) => {
        this.salesData = this.filterByDateRange(data);
        this.calculateInsights();
        this.loading = false;
      },
      error: (err) => {
        console.error('Error loading sales data:', err);
        this.loading = false;
      }
    });
  }

  filterByDateRange(sales: Sales[]): Sales[] {
    const start = getIstStartOfDay(this.dateRange.startDate);
    const end = getIstEndOfDay(this.dateRange.endDate);
    return sales.filter(sale => {
      const saleDate = new Date(sale.date);
      return saleDate >= start && saleDate <= end;
    });
  }

  calculateInsights() {
    if (this.salesData.length === 0) {
      this.insights = null;
      return;
    }

    // Total Revenue & Transactions
    const totalRevenue = this.salesData.reduce((sum, sale) => sum + sale.totalAmount, 0);
    const totalTransactions = this.salesData.length;
    const averageTransaction = totalRevenue / totalTransactions;

    // Top Selling Items
    const itemMap = new Map<string, { quantity: number; revenue: number }>();
    this.salesData.forEach(sale => {
      sale.items.forEach(item => {
        const existing = itemMap.get(item.itemName) || { quantity: 0, revenue: 0 };
        itemMap.set(item.itemName, {
          quantity: existing.quantity + item.quantity,
          revenue: existing.revenue + item.totalPrice
        });
      });
    });
    const topSellingItems = Array.from(itemMap.entries())
      .map(([name, data]) => ({ name, ...data }))
      .sort((a, b) => b.revenue - a.revenue)
      .slice(0, 10);

    // Payment Method Breakdown
    const paymentMap = new Map<string, { count: number; total: number }>();
    this.salesData.forEach(sale => {
      const existing = paymentMap.get(sale.paymentMethod) || { count: 0, total: 0 };
      paymentMap.set(sale.paymentMethod, {
        count: existing.count + 1,
        total: existing.total + sale.totalAmount
      });
    });
    const paymentMethodBreakdown = Array.from(paymentMap.entries())
      .map(([method, data]) => ({ method, ...data }))
      .sort((a, b) => b.total - a.total);

    // Daily Average
    const dateRange = getIstDaysDifference(new Date(this.dateRange.endDate), new Date(this.dateRange.startDate));
    const dailyAverage = totalRevenue / Math.max(dateRange, 1);

    // Weekly Trend (last 4 weeks)
    const weeklyTrend = this.calculateWeeklyTrend();

    // Monthly Comparison (last 6 months)
    const monthlyComparison = this.calculateMonthlyComparison();

    // Peak Sales Days
    const peakSalesDays = this.calculatePeakDays();

    // Growth Rate (comparing first half vs second half of date range)
    const growthRate = this.calculateGrowthRate();

    // Item Category Breakdown (Tea variants vs others)
    const itemCategoryBreakdown = this.calculateCategoryBreakdown();

    this.insights = {
      totalRevenue,
      totalTransactions,
      averageTransaction,
      topSellingItems,
      paymentMethodBreakdown,
      dailyAverage,
      weeklyTrend,
      monthlyComparison,
      peakSalesDays,
      growthRate,
      itemCategoryBreakdown
    };
  }

  calculateWeeklyTrend(): { week: string; total: number }[] {
    const weeks = new Map<string, number>();
    this.salesData.forEach(sale => {
      const date = convertToIst(new Date(sale.date));
      const weekStart = new Date(date);
      weekStart.setDate(date.getDate() - date.getDay());
      const weekKey = getIstInputDate(weekStart);
      weeks.set(weekKey, (weeks.get(weekKey) || 0) + sale.totalAmount);
    });
    return Array.from(weeks.entries())
      .map(([week, total]) => ({ week: formatIstDate(new Date(week)), total }))
      .sort((a, b) => new Date(b.week).getTime() - new Date(a.week).getTime())
      .slice(0, 4);
  }

  calculateMonthlyComparison(): { month: string; total: number; transactions: number }[] {
    const months = new Map<string, { total: number; transactions: number }>();
    this.salesData.forEach(sale => {
      const date = convertToIst(new Date(sale.date));
      const monthKey = `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}`;
      const existing = months.get(monthKey) || { total: 0, transactions: 0 };
      months.set(monthKey, {
        total: existing.total + sale.totalAmount,
        transactions: existing.transactions + 1
      });
    });
    return Array.from(months.entries())
      .map(([month, data]) => ({
        month: formatIstDate(new Date(month + '-01'), { month: 'short', year: 'numeric' }),
        ...data
      }))
      .sort((a, b) => b.month.localeCompare(a.month))
      .slice(0, 6)
      .reverse();
  }

  calculatePeakDays(): { day: string; total: number }[] {
    const days = new Map<string, number>();
    this.salesData.forEach(sale => {
      const day = sale.date.split('T')[0];
      days.set(day, (days.get(day) || 0) + sale.totalAmount);
    });
    return Array.from(days.entries())
      .map(([day, total]) => ({ day: formatIstDate(new Date(day)), total }))
      .sort((a, b) => b.total - a.total)
      .slice(0, 5);
  }

  calculateGrowthRate(): number {
    const sortedSales = [...this.salesData].sort((a, b) =>
      new Date(a.date).getTime() - new Date(b.date).getTime()
    );
    const midPoint = Math.floor(sortedSales.length / 2);
    const firstHalf = sortedSales.slice(0, midPoint);
    const secondHalf = sortedSales.slice(midPoint);

    const firstHalfTotal = firstHalf.reduce((sum, sale) => sum + sale.totalAmount, 0);
    const secondHalfTotal = secondHalf.reduce((sum, sale) => sum + sale.totalAmount, 0);

    if (firstHalfTotal === 0) return 0;
    return ((secondHalfTotal - firstHalfTotal) / firstHalfTotal) * 100;
  }

  calculateCategoryBreakdown(): { category: string; count: number; revenue: number }[] {
    const categories = new Map<string, { count: number; revenue: number }>();

    this.salesData.forEach(sale => {
      sale.items.forEach(item => {
        const category = this.categorizeItem(item.itemName);
        const existing = categories.get(category) || { count: 0, revenue: 0 };
        categories.set(category, {
          count: existing.count + item.quantity,
          revenue: existing.revenue + item.totalPrice
        });
      });
    });

    return Array.from(categories.entries())
      .map(([category, data]) => ({ category, ...data }))
      .sort((a, b) => b.revenue - a.revenue);
  }

  categorizeItem(itemName: string): string {
    if (itemName.includes('Tea -')) return 'Tea Variants';
    if (itemName.toLowerCase().includes('tea')) return 'Tea Products';
    if (itemName.toLowerCase().includes('coffee')) return 'Coffee';
    if (itemName.toLowerCase().includes('biscuit') || itemName.toLowerCase().includes('snacks')) return 'Snacks';
    if (itemName.toLowerCase().includes('water') || itemName.toLowerCase().includes('campa')) return 'Beverages';
    if (itemName.toLowerCase().includes('cigarete')) return 'Tobacco';
    return 'Others';
  }

  // Online Sales Analytics Methods
  loadOnlineSalesAnalytics() {
    this.onlineLoading = true;
    this.http.get<any>(`${environment.apiUrl}/online-sales/date-range?startDate=${this.dateRange.startDate}&endDate=${this.dateRange.endDate}`)
      .subscribe({
        next: (response) => {
          const sales = response?.data || [];
          this.calculateOnlineSalesAnalytics(sales);
          this.onlineLoading = false;
        },
        error: (err) => {
          console.error('Error loading online sales analytics:', err);
          this.onlineLoading = false;
        }
      });
  }

  switchPlatform(platform: 'All' | 'Zomato' | 'Swiggy') {
    this.selectedPlatform = platform;
  }

  calculateOnlineSalesAnalytics(sales: any[]) {
    if (!sales || sales.length === 0) {
      this.onlineAnalytics = null;
      return;
    }

    // Calculate for each platform
    const zomatoSales = sales.filter(s => s.platform === 'Zomato');
    const swiggySales = sales.filter(s => s.platform === 'Swiggy');

    this.onlineAnalytics = {
      all: this.calculatePlatformMetrics(sales, 'All'),
      zomato: this.calculatePlatformMetrics(zomatoSales, 'Zomato'),
      swiggy: this.calculatePlatformMetrics(swiggySales, 'Swiggy')
    };
  }

  calculatePlatformMetrics(sales: any[], platform: string) {
    if (sales.length === 0) {
      return {
        totalIncome: 0,
        totalOrders: 0,
        avgIncomePerOrder: 0,
        dailyAverage: 0,
        avgOrdersPerDay: 0,
        avgRating: 0,
        totalDeduction: 0,
        avgDeductionPercent: 0,
        totalDiscount: 0,
        avgDiscountPerOrder: 0,
        ordersWithDiscount: 0,
        discountUsagePercent: 0,
        totalPackaging: 0,
        avgPackagingPerOrder: 0,
        ordersWithPackaging: 0,
        packagingUsagePercent: 0,
        avgDistance: 0,
        minDistance: 0,
        maxDistance: 0,
        commonDistanceRange: 'N/A',
        topItems: [],
        dailyTrend: [],
        maxDailyIncome: 1,
        peakDays: [],
        ratingDistribution: [],
        monthlyData: []
      };
    }

    const totalIncome = sales.reduce((sum, s) => sum + (s.payout || 0), 0);
    const totalOrders = sales.length;
    const avgIncomePerOrder = totalIncome / totalOrders;
    const totalDeduction = sales.reduce((sum, s) => sum + (s.platformDeduction || 0), 0);
    const avgDeductionPercent = totalDeduction > 0 && totalIncome > 0
      ? (totalDeduction / (totalIncome + totalDeduction)) * 100
      : 0;

    // Discount metrics
    const totalDiscount = sales.reduce((sum, s) => sum + (s.discountAmount || 0), 0);
    const ordersWithDiscount = sales.filter(s => (s.discountAmount || 0) > 0).length;
    const avgDiscountPerOrder = totalDiscount / totalOrders;
    const discountUsagePercent = totalOrders > 0 ? (ordersWithDiscount / totalOrders) * 100 : 0;

    // Calculate days in range
    const dates = [...new Set(sales.map(s => new Date(s.orderAt).toDateString()))];
    const daysInRange = dates.length;
    const dailyAverage = totalIncome / Math.max(daysInRange, 1);
    const avgOrdersPerDay = totalOrders / Math.max(daysInRange, 1);

    // Average rating
    const ratingsWithValues = sales.filter(s => s.rating && s.rating > 0);
    const avgRating = ratingsWithValues.length > 0
      ? ratingsWithValues.reduce((sum, s) => sum + s.rating, 0) / ratingsWithValues.length
      : 0;

    // Distance metrics
    const salesWithDistance = sales.filter(s => s.distance && s.distance > 0);
    const avgDistance = salesWithDistance.length > 0
      ? salesWithDistance.reduce((sum, s) => sum + s.distance, 0) / salesWithDistance.length
      : 0;
    const minDistance = salesWithDistance.length > 0
      ? Math.min(...salesWithDistance.map(s => s.distance))
      : 0;
    const maxDistance = salesWithDistance.length > 0
      ? Math.max(...salesWithDistance.map(s => s.distance))
      : 0;

    // Packaging metrics
    const totalPackaging = sales.reduce((sum, s) => sum + (s.packagingCharges || 0), 0);
    const avgPackagingPerOrder = totalPackaging / totalOrders;
    const ordersWithPackaging = sales.filter(s => (s.packagingCharges || 0) > 0).length;
    const packagingUsagePercent = totalOrders > 0 ? (ordersWithPackaging / totalOrders) * 100 : 0;

    // Common distance range
    const distanceRanges = salesWithDistance.map(s => {
      const km = s.distance;
      if (km < 2) return '0-2 km';
      if (km < 5) return '2-5 km';
      if (km < 10) return '5-10 km';
      return '10+ km';
    });
    const rangeCounts = distanceRanges.reduce((acc, range) => {
      acc[range] = (acc[range] || 0) + 1;
      return acc;
    }, {} as any);
    const commonDistanceRange = Object.entries(rangeCounts)
      .sort(([,a]:any, [,b]:any) => b - a)[0]?.[0] || 'N/A';

    // Top Items
    const itemMap = new Map<string, { count: number; quantity: number }>();
    sales.forEach(sale => {
      if (sale.orderedItems) {
        try {
          const items = typeof sale.orderedItems === 'string'
            ? JSON.parse(sale.orderedItems)
            : sale.orderedItems;

          if (Array.isArray(items)) {
            items.forEach((item: any) => {
              const name = item.name || item.itemName || 'Unknown Item';
              const qty = parseInt(item.quantity || '1');
              const existing = itemMap.get(name) || { count: 0, quantity: 0 };
              itemMap.set(name, {
                count: existing.count + 1,
                quantity: existing.quantity + qty
              });
            });
          }
        } catch (e) {
          // Skip if parsing fails
        }
      }
    });
    const topItems = Array.from(itemMap.entries())
      .map(([name, data]) => ({ name, ...data }))
      .sort((a, b) => b.count - a.count)
      .slice(0, 10);

    // Daily Trend
    const dailyMap = new Map<string, { income: number; orders: number }>();
    sales.forEach(sale => {
      const dateKey = new Date(sale.orderAt).toISOString().split('T')[0];
      const existing = dailyMap.get(dateKey) || { income: 0, orders: 0 };
      dailyMap.set(dateKey, {
        income: existing.income + (sale.payout || 0),
        orders: existing.orders + 1
      });
    });
    const dailyTrend = Array.from(dailyMap.entries())
      .map(([date, data]) => ({ date, ...data }))
      .sort((a, b) => new Date(a.date).getTime() - new Date(b.date).getTime());
    const maxDailyIncome = Math.max(...dailyTrend.map(d => d.income), 1);

    // Peak Days
    const dayMap = new Map<number, { income: number; orders: number }>();
    sales.forEach(sale => {
      const dayOfWeek = new Date(sale.orderAt).getDay();
      const existing = dayMap.get(dayOfWeek) || { income: 0, orders: 0 };
      dayMap.set(dayOfWeek, {
        income: existing.income + (sale.payout || 0),
        orders: existing.orders + 1
      });
    });
    const dayNames = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
    const peakDays = Array.from(dayMap.entries())
      .map(([day, data]) => ({ dayName: dayNames[day], ...data }))
      .sort((a, b) => b.income - a.income);

    // Rating Distribution
    const ratingCounts = [5, 4, 3, 2, 1].map(stars => {
      const count = sales.filter(s => s.rating === stars).length;
      const percentage = totalOrders > 0 ? (count / totalOrders) * 100 : 0;
      return { stars, count, percentage };
    });

    // Monthly Data
    const monthlyMap = new Map<string, { income: number; orders: number; ratings: number[] }>();
    sales.forEach(sale => {
      const date = new Date(sale.orderAt);
      const monthKey = `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}`;
      const existing = monthlyMap.get(monthKey) || { income: 0, orders: 0, ratings: [] };
      monthlyMap.set(monthKey, {
        income: existing.income + (sale.payout || 0),
        orders: existing.orders + 1,
        ratings: sale.rating > 0 ? [...existing.ratings, sale.rating] : existing.ratings
      });
    });
    const monthNames = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
    const monthlyData = Array.from(monthlyMap.entries())
      .map(([key, data]) => {
        const [year, month] = key.split('-');
        const avgRating = data.ratings.length > 0
          ? data.ratings.reduce((sum, r) => sum + r, 0) / data.ratings.length
          : 0;
        return {
          month: `${monthNames[parseInt(month) - 1]} ${year}`,
          income: data.income,
          orders: data.orders,
          avgRating
        };
      })
      .sort((a, b) => a.month.localeCompare(b.month));

    return {
      totalIncome,
      totalOrders,
      avgIncomePerOrder,
      dailyAverage,
      avgOrdersPerDay,
      avgRating,
      totalDeduction,
      avgDeductionPercent,
      totalDiscount,
      avgDiscountPerOrder,
      ordersWithDiscount,
      discountUsagePercent,
      totalPackaging,
      avgPackagingPerOrder,
      ordersWithPackaging,
      packagingUsagePercent,
      avgDistance,
      minDistance,
      maxDistance,
      commonDistanceRange,
      topItems,
      dailyTrend,
      maxDailyIncome,
      peakDays,
      ratingDistribution: ratingCounts,
      monthlyData
    };
  }

  getActivePlatformData() {
    if (!this.onlineAnalytics) {
      return {
        totalIncome: 0,
        totalOrders: 0,
        avgIncomePerOrder: 0,
        dailyAverage: 0,
        avgOrdersPerDay: 0,
        avgRating: 0,
        totalDeduction: 0,
        avgDeductionPercent: 0,
        totalDiscount: 0,
        avgDiscountPerOrder: 0,
        ordersWithDiscount: 0,
        discountUsagePercent: 0,
        totalPackaging: 0,
        avgPackagingPerOrder: 0,
        ordersWithPackaging: 0,
        packagingUsagePercent: 0,
        avgDistance: 0,
        minDistance: 0,
        maxDistance: 0,
        commonDistanceRange: 'N/A',
        topItems: [],
        dailyTrend: [],
        maxDailyIncome: 1,
        peakDays: [],
        ratingDistribution: [],
        monthlyData: []
      };
    }

    if (this.selectedPlatform === 'All') return this.onlineAnalytics.all;
    if (this.selectedPlatform === 'Zomato') return this.onlineAnalytics.zomato;
    return this.onlineAnalytics.swiggy;
  }

  formatShortDate(date: string): string {
    const d = new Date(date);
    return `${d.getDate()}/${d.getMonth() + 1}`;
  }

  onDateRangeChange() {
    this.loadSalesInsights();
    this.loadExpenseAnalytics();
    this.loadEarningsAnalytics();
    this.loadOnlineSalesAnalytics();
  }

  formatCurrency(amount: number): string {
    return `₹${amount.toFixed(2)}`;
  }

  formatDate(date: Date): string {
    return formatIstDate(date, { day: 'numeric', month: 'short', year: 'numeric' });
  }

  formatPercentage(value: number): string {
    return `${value > 0 ? '+' : ''}${value.toFixed(1)}%`;
  }

  getMaxWeeklyTotal(): number {
    if (!this.insights?.weeklyTrend.length) return 1;
    return Math.max(...this.insights.weeklyTrend.map(w => w.total));
  }

  getMaxMonthlyTotal(): number {
    if (!this.insights?.monthlyComparison.length) return 1;
    return Math.max(...this.insights.monthlyComparison.map(m => m.total));
  }

  getCategoryIcon(category: string): string {
    const icons: { [key: string]: string } = {
      'Tea Variants': '🍵',
      'Tea Products': '☕',
      'Coffee': '☕',
      'Snacks': '🍪',
      'Beverages': '🥤',
      'Tobacco': '🚬',
      'Others': '📦'
    };
    return icons[category] || '📦';
  }

  getExpenseTypeIcon(type: string): string {
    const icons: { [key: string]: string } = {
      'Milk': '🥛',
      'Tea': '🍵',
      'Rent': '🏠',
      'Salary': '💰',
      'Grocerry': '🛒',
      'Electricity': '⚡',
      'Water': '💧'
    };
    return icons[type] || '💸';
  }

  getMaxExpenseWeekly(): number {
    if (!this.expenseAnalytics?.weeklyTrend.length) return 1;
    return Math.max(...this.expenseAnalytics.weeklyTrend.map(w => w.totalAmount));
  }

  getMaxExpenseMonthly(): number {
    if (!this.expenseAnalytics?.monthlyComparison.length) return 1;
    return Math.max(...this.expenseAnalytics.monthlyComparison.map(m => m.totalAmount));
  }

  loadEarningsAnalytics() {
    this.earningsLoading = true;

    // Fetch sales, expenses, cash reconciliations, and online sales for the date range
    Promise.all([
      this.salesService.getAllSales().toPromise(),
      this.http.get(`${environment.apiUrl}/expenses/range?startDate=${this.dateRange.startDate}&endDate=${this.dateRange.endDate}`).toPromise(),
      this.http.get(`${environment.apiUrl}/cash-reconciliation?startDate=${this.dateRange.startDate}&endDate=${this.dateRange.endDate}`).toPromise(),
      this.http.get(`${environment.apiUrl}/online-sales/date-range?startDate=${this.dateRange.startDate}&endDate=${this.dateRange.endDate}`).toPromise()
    ]).then(([sales, expenses, reconciliationResponse, onlineSalesResponse]: [any, any, any, any]) => {
      const filteredSales = this.filterByDateRange(sales);
      const reconciliations = reconciliationResponse?.data || [];
      const onlineSales = onlineSalesResponse?.data || [];
      // Handle expenses response - it might be wrapped in {data: []} or direct array
      const expensesData = expenses?.data || expenses || [];
      console.log('Earnings Analytics Data:', {
        salesCount: filteredSales?.length || 0,
        expensesCount: expensesData?.length || 0,
        reconciliationsCount: reconciliations?.length || 0,
        onlineSalesCount: onlineSales?.length || 0,
        filteredSales: filteredSales,
        expenses: expensesData,
        reconciliations: reconciliations,
        onlineSales: onlineSales
      });
      this.calculateEarningsInsights(filteredSales, expensesData, reconciliations, onlineSales);
      this.earningsLoading = false;
    }).catch(err => {
      console.error('Error loading earnings analytics:', err);
      this.earningsLoading = false;
    });
  }

  calculateEarningsInsights(sales: any[], expenses: any[], reconciliations: any[], onlineSales: any[]) {
    console.log('calculateEarningsInsights called with:', {
      salesLength: sales?.length,
      expensesLength: expenses?.length,
      reconciliationsLength: reconciliations?.length,
      onlineSalesLength: onlineSales?.length
    });

    // Calculate total offline sales
    const totalOfflineSales = sales.reduce((sum, s) => sum + s.totalAmount, 0);

    // Calculate online sales from payment methods (card, online, upi, etc.)
    const onlineSalesPayment = sales
      .filter(s => {
        const method = s.paymentMethod?.toLowerCase() || '';
        return method === 'card' || method === 'online' || method === 'upi' ||
               method === 'paytm' || method === 'gpay' || method === 'phonepe';
      })
      .reduce((sum, s) => sum + s.totalAmount, 0);

    // Calculate cash sales
    const cashSales = sales
      .filter(s => s.paymentMethod?.toLowerCase() === 'cash')
      .reduce((sum, s) => sum + s.totalAmount, 0);

    // Calculate Zomato/Swiggy online sales (Payout is the actual income)
    const zomatoSwiggyIncome = onlineSales.reduce((sum, s) => sum + (s.payout || 0), 0);
    const zomatoSwiggyOrders = onlineSales.length;
    const zomatoSwiggyDeductions = onlineSales.reduce((sum, s) => sum + (s.platformDeduction || 0), 0);
    const zomatoSwiggyDiscount = onlineSales.reduce((sum, s) => sum + (s.discountAmount || 0), 0);
    const zomatoSwiggyPackaging = onlineSales.reduce((sum, s) => sum + (s.packagingCharges || 0), 0);

    // Breakdown by platform
    const zomatoSales = onlineSales.filter(s => s.platform === 'Zomato');
    const swiggySales = onlineSales.filter(s => s.platform === 'Swiggy');
    const zomatoIncome = zomatoSales.reduce((sum, s) => sum + (s.payout || 0), 0);
    const swiggyIncome = swiggySales.reduce((sum, s) => sum + (s.payout || 0), 0);

    // Calculate total expenses
    const totalExpenses = expenses.reduce((sum, e) => sum + e.amount, 0);

    // Calculate offline expenses (cash payment method)
    const offlineExpenses = expenses
      .filter(e => e.paymentMethod?.toLowerCase() === 'cash')
      .reduce((sum, e) => sum + e.amount, 0);

    // Calculate online expenses (card, online, upi, bank transfer)
    const onlineExpenses = expenses
      .filter(e => {
        const method = e.paymentMethod?.toLowerCase() || '';
        return method === 'online' || method === 'card' || method === 'upi' ||
               method === 'bank transfer';
      })
      .reduce((sum, e) => sum + e.amount, 0);

    // Calculate Total Cash Collection from reconciliations
    // Sort by date
    const sortedRecs = reconciliations.sort((a, b) =>
      new Date(a.date).getTime() - new Date(b.date).getTime()
    );

    let totalCashCollection = 0;

    sortedRecs.forEach((rec, index) => {
      // Get offline expenses for this day
      const recDate = new Date(rec.date);
      const offlineExpensesForDay = expenses.filter(e => {
        const expDate = new Date(e.date);
        return expDate.toDateString() === recDate.toDateString() &&
               e.paymentMethod?.toLowerCase() === 'cash';
      }).reduce((sum, e) => sum + e.amount, 0);

      if (index === 0) {
        // First day: (CashCounted - OpeningCash) + (CoinCounted - OpeningCoin) + OfflineExpenses
        const cashDifference = (rec.countedCash || 0) - (rec.openingCashBalance || 0);
        const coinDifference = (rec.countedCoins || 0) - (rec.openingCoinBalance || 0);
        totalCashCollection += cashDifference + coinDifference + offlineExpensesForDay;
      } else {
        // Subsequent days: (CashCounted - OpeningCash) + (CoinCounted - OpeningCoin) + OfflineExpenses
        const cashDifference = (rec.countedCash || 0) - (rec.openingCashBalance || 0);
        const coinDifference = (rec.countedCoins || 0) - (rec.openingCoinBalance || 0);
        totalCashCollection += cashDifference + coinDifference + offlineExpensesForDay;
      }
    });

    // Total online collection = sum of actualOnline from reconciliations
    const totalOnlineCollection = reconciliations.reduce((sum, rec) =>
      sum + (rec.actualOnline || 0), 0
    );

    // Online platform collection = Zomato/Swiggy income
    const onlinePlatformCollection = zomatoSwiggyIncome;

    // Calculate total revenue based on actual collections
    const totalRevenue = totalCashCollection + totalOnlineCollection + onlinePlatformCollection;

    // Calculate net profit/loss
    const netProfitLoss = totalRevenue - totalExpenses;
    const profitMargin = totalRevenue > 0 ? (netProfitLoss / totalRevenue) * 100 : 0;

    this.earningsData = {
      // Offline sales
      totalOfflineSales,
      totalOnlineSalesPayment: onlineSalesPayment,
      totalCashSales: cashSales,

      // Online platform sales (Zomato/Swiggy)
      zomatoSwiggyIncome,
      zomatoSwiggyOrders,
      zomatoSwiggyDeductions,
      zomatoSwiggyDiscount,
      zomatoSwiggyPackaging,
      zomatoIncome,
      swiggyIncome,

      // Expenses
      totalExpenses,
      offlineExpenses,
      onlineExpenses,

      // Collections
      totalCashCollection,
      totalOnlineCollection,
      onlinePlatformCollection,
      totalCollection: totalCashCollection + totalOnlineCollection + onlinePlatformCollection,

      // PnL
      totalRevenue,
      netProfitLoss,
      profitMargin
    };

    console.log('Earnings Data Calculated:', this.earningsData);
  }
}

