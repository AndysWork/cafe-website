import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { SalesService, Sales } from '../../services/sales.service';
import { ExpenseService, ExpenseAnalytics } from '../../services/expense.service';
import { OperationalExpenseService } from '../../services/operational-expense.service';
import { PlatformChargeService } from '../../services/platform-charge.service';
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
  selectedAnalyticsTab: 'sales' | 'expenses' | 'earnings' | 'online' | 'customers' = 'sales';
  expenseSource: 'All' | 'Offline' | 'Online' = 'All';

  // Earnings Analytics
  earningsData: any = null;
  earningsLoading = false;

  // Online Sales Analytics
  onlineAnalytics: any = null;
  onlineLoading = false;
  selectedPlatform: 'All' | 'Zomato' | 'Swiggy' = 'All';

  // Customer Analytics
  customerAnalytics: any = null;
  customerLoading = false;
  customerSearchTerm: string = '';
  customerFilterType: string = 'all';
  filteredCustomersList: any[] = [];

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
    private operationalExpenseService: OperationalExpenseService,
    private platformChargeService: PlatformChargeService,
    private http: HttpClient
  ) {}

  ngOnInit() {
    this.loadSalesInsights();
    this.loadExpenseAnalytics();
    this.loadEarningsAnalytics();
    this.loadOnlineSalesAnalytics();
    this.loadCustomerAnalytics();
  }

  switchTab(tab: 'sales' | 'expenses' | 'earnings' | 'online' | 'customers') {
    this.selectedAnalyticsTab = tab;
    if (tab === 'expenses' && !this.expenseAnalytics) {
      this.loadExpenseAnalytics();
    } else if (tab === 'earnings' && !this.earningsData) {
      this.loadEarningsAnalytics();
    } else if (tab === 'online' && !this.onlineAnalytics) {
      this.loadOnlineSalesAnalytics();
    } else if (tab === 'customers' && !this.customerAnalytics) {
      this.loadCustomerAnalytics();
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

        // Add operational expenses to the total
        if (this.expenseSource === 'All') {
          this.operationalExpenseService.getAllOperationalExpenses().subscribe({
            next: (opExpenses) => {
              const startDate = new Date(this.dateRange.startDate);
              const endDate = new Date(this.dateRange.endDate);

              let operationalTotal = 0;
              opExpenses.forEach(opExp => {
                const expDate = new Date(opExp.year, opExp.month - 1, 1);
                if (expDate >= startDate && expDate <= endDate) {
                  operationalTotal += opExp.totalOperationalCost;
                }
              });

              // Update the expense analytics with operational expenses included
              if (this.expenseAnalytics) {
                this.expenseAnalytics.summary.totalExpenses += operationalTotal;

                // Add operational expenses as a category in the breakdown
                const summary = this.expenseAnalytics.summary as any;
                if (!summary.expensesByType) {
                  summary.expensesByType = {};
                }
                summary.expensesByType['Operational Costs'] = operationalTotal;
              }

              this.expenseLoading = false;
            },
            error: (err) => {
              console.error('Error loading operational expenses:', err);
              this.expenseLoading = false;
            }
          });
        } else {
          this.expenseLoading = false;
        }
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

    // Fetch both online sales and platform charges
    Promise.all([
      this.http.get<any>(`${environment.apiUrl}/online-sales/date-range?startDate=${this.dateRange.startDate}&endDate=${this.dateRange.endDate}`).toPromise(),
      this.platformChargeService.getAllPlatformCharges().toPromise()
    ]).then(([salesResponse, platformCharges]) => {
      console.log('Online Sales Analytics Response:', salesResponse);
      const sales = salesResponse?.data || [];
      console.log('Online Sales Data:', sales);
      console.log('Platform Charges:', platformCharges);
      console.log('Zomato Sales:', sales.filter((s: any) => s.platform === 'Zomato'));
      console.log('Swiggy Sales:', sales.filter((s: any) => s.platform === 'Swiggy'));
      this.calculateOnlineSalesAnalytics(sales, platformCharges || []);
      this.onlineLoading = false;
    }).catch(err => {
      console.error('Error loading online sales analytics:', err);
      this.onlineLoading = false;
    });
  }

  switchPlatform(platform: 'All' | 'Zomato' | 'Swiggy') {
    this.selectedPlatform = platform;
  }

  calculateOnlineSalesAnalytics(sales: any[], platformCharges: any[]) {
    if (!sales || sales.length === 0) {
      this.onlineAnalytics = null;
      return;
    }

    // Calculate for each platform (case-insensitive filtering)
    const zomatoSales = sales.filter(s => s.platform?.toLowerCase() === 'zomato');
    const swiggySales = sales.filter(s => s.platform?.toLowerCase() === 'swiggy');

    console.log('Online Sales Analytics Platform Filtering:', {
      totalSales: sales.length,
      zomatoCount: zomatoSales.length,
      swiggyCount: swiggySales.length
    });

    this.onlineAnalytics = {
      all: this.calculatePlatformMetrics(sales, 'All', platformCharges),
      zomato: this.calculatePlatformMetrics(zomatoSales, 'Zomato', platformCharges),
      swiggy: this.calculatePlatformMetrics(swiggySales, 'Swiggy', platformCharges)
    };
  }

  calculatePlatformMetrics(sales: any[], platform: string, platformCharges: any[]) {
    console.log(`Calculating metrics for ${platform}:`, {
      salesCount: sales.length,
      sampleSale: sales[0],
      platformCharges
    });

    if (sales.length === 0) {
      return {
        totalIncome: 0,
        itemSubtotal: 0,
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
        totalMonthlyCharges: 0,
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

    const totalOrders = sales.length;
    const totalDeduction = sales.reduce((sum, s) => sum + (s.platformDeduction || 0), 0);

    // Calculate Item Subtotal and Packaging
    const itemSubtotal = sales.reduce((sum, s) => sum + (s.billSubTotal || 0), 0);
    const totalPackaging = sales.reduce((sum, s) => sum + (s.packagingCharges || 0), 0);
    const avgPackagingPerOrder = totalPackaging / totalOrders;
    const ordersWithPackaging = sales.filter(s => (s.packagingCharges || 0) > 0).length;
    const packagingUsagePercent = totalOrders > 0 ? (ordersWithPackaging / totalOrders) * 100 : 0;

    // Discount metrics
    const totalDiscount = sales.reduce((sum, s) => sum + (s.discountAmount || 0), 0);
    const ordersWithDiscount = sales.filter(s => (s.discountAmount || 0) > 0).length;
    const avgDiscountPerOrder = totalDiscount / totalOrders;
    const discountUsagePercent = totalOrders > 0 ? (ordersWithDiscount / totalOrders) * 100 : 0;

    // Calculate monthly charges from platform charges for this platform and date range
    const startDate = new Date(this.dateRange.startDate);
    const endDate = new Date(this.dateRange.endDate);
    const startMonth = startDate.getMonth() + 1;
    const startYear = startDate.getFullYear();
    const endMonth = endDate.getMonth() + 1;
    const endYear = endDate.getFullYear();

    const totalMonthlyCharges = platformCharges
      .filter((pc: any) => {
        // For 'All' platform, include both Zomato and Swiggy
        if (platform === 'All') {
          if (pc.platform !== 'Zomato' && pc.platform !== 'Swiggy') return false;
        } else {
          if (pc.platform !== platform) return false;
        }
        // Include charges that fall within the date range
        if (pc.year < startYear || pc.year > endYear) return false;
        if (pc.year === startYear && pc.year === endYear) {
          return pc.month >= startMonth && pc.month <= endMonth;
        }
        if (pc.year === startYear) {
          return pc.month >= startMonth;
        }
        if (pc.year === endYear) {
          return pc.month <= endMonth;
        }
        return true;
      })
      .reduce((sum: number, pc: any) => sum + (pc.charges || 0), 0);

    console.log(`${platform} Monthly Charges:`, totalMonthlyCharges);

    // Recalculate Total Net Payout = Item Subtotal + Packaging - Discount - Deduction - Monthly Charges
    const totalIncome = itemSubtotal + totalPackaging - totalDiscount - totalDeduction - totalMonthlyCharges;
    console.log(`${platform} Net Payout Calculation:`, {
      itemSubtotal,
      totalPackaging,
      totalDiscount,
      totalDeduction,
      totalMonthlyCharges,
      totalIncome
    });

    // Calculate days in range
    const dates = [...new Set(sales.map(s => new Date(s.orderAt).toDateString()))];
    const daysInRange = dates.length;
    const dailyAverage = totalIncome / Math.max(daysInRange, 1);
    const avgOrdersPerDay = totalOrders / Math.max(daysInRange, 1);
    const avgIncomePerOrder = totalIncome / totalOrders;
    const avgDeductionPercent = totalDeduction > 0 && (totalIncome + totalMonthlyCharges) > 0
      ? (totalDeduction / (totalIncome + totalMonthlyCharges + totalDeduction)) * 100
      : 0;

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
      // Net Payout = Item Subtotal + Packaging - Discount - Deduction
      const netPayout = (sale.billSubTotal || 0) + (sale.packagingCharges || 0) - (sale.discountAmount || 0) - (sale.platformDeduction || 0);
      dailyMap.set(dateKey, {
        income: existing.income + netPayout,
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
      // Peak Days based on Item Subtotal + Packaging Charges
      const grossRevenue = (sale.billSubTotal || 0) + (sale.packagingCharges || 0);
      dayMap.set(dayOfWeek, {
        income: existing.income + grossRevenue,
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
      // Net Payout = Item Subtotal + Packaging - Discount - Deduction
      const netPayout = (sale.billSubTotal || 0) + (sale.packagingCharges || 0) - (sale.discountAmount || 0) - (sale.platformDeduction || 0);
      monthlyMap.set(monthKey, {
        income: existing.income + netPayout,
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

        // Deduct monthly platform charges for this specific month and platform
        const monthCharges = platformCharges
          .filter((pc: any) => {
            if (platform === 'All') {
              return (pc.platform === 'Zomato' || pc.platform === 'Swiggy') &&
                     pc.year === parseInt(year) &&
                     pc.month === parseInt(month);
            } else {
              return pc.platform === platform &&
                     pc.year === parseInt(year) &&
                     pc.month === parseInt(month);
            }
          })
          .reduce((sum: number, pc: any) => sum + (pc.charges || 0), 0);

        return {
          month: `${monthNames[parseInt(month) - 1]} ${year}`,
          income: data.income - monthCharges,
          orders: data.orders,
          avgRating
        };
      })
      .sort((a, b) => a.month.localeCompare(b.month));

    return {
      totalIncome,
      itemSubtotal,
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
      totalMonthlyCharges,
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
        itemSubtotal: 0,
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
        totalMonthlyCharges: 0,
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
    this.loadCustomerAnalytics();
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

    // Calculate Zomato/Swiggy online sales (Net Payout = Subtotal + Packaging - Discount - Deduction)
    const zomatoSwiggyIncome = onlineSales.reduce((sum, s) => {
      const netPayout = (s.billSubTotal || 0) + (s.packagingCharges || 0) - (s.discountAmount || 0) - (s.platformDeduction || 0);
      return sum + netPayout;
    }, 0);
    const zomatoSwiggyOrders = onlineSales.length;
    const zomatoSwiggyDeductions = onlineSales.reduce((sum, s) => sum + (s.platformDeduction || 0), 0);
    const zomatoSwiggyDiscount = onlineSales.reduce((sum, s) => sum + (s.discountAmount || 0), 0);
    const zomatoSwiggyPackaging = onlineSales.reduce((sum, s) => sum + (s.packagingCharges || 0), 0);

    console.log('Zomato/Swiggy Income Calculation:', {
      totalOnlineSales: onlineSales.length,
      zomatoSwiggyIncome,
      zomatoSwiggyOrders,
      zomatoSwiggyDeductions,
      zomatoSwiggyDiscount,
      zomatoSwiggyPackaging,
      sampleSale: onlineSales[0]
    });

    // Breakdown by platform
    const zomatoSales = onlineSales.filter(s => s.platform?.toLowerCase() === 'zomato');
    const swiggySales = onlineSales.filter(s => s.platform?.toLowerCase() === 'swiggy');

    console.log('Platform Filtering:', {
      totalOnlineSales: onlineSales.length,
      zomatoCount: zomatoSales.length,
      swiggyCount: swiggySales.length,
      samplePlatforms: onlineSales.slice(0, 5).map(s => ({id: s.orderId, platform: s.platform}))
    });

    const zomatoIncome = zomatoSales.reduce((sum, s) => {
      const netPayout = (s.billSubTotal || 0) + (s.packagingCharges || 0) - (s.discountAmount || 0) - (s.platformDeduction || 0);
      return sum + netPayout;
    }, 0);
    const swiggyIncome = swiggySales.reduce((sum, s) => {
      const netPayout = (s.billSubTotal || 0) + (s.packagingCharges || 0) - (s.discountAmount || 0) - (s.platformDeduction || 0);
      return sum + netPayout;
    }, 0);

    console.log('Platform Breakdown:', {
      zomatoSalesCount: zomatoSales.length,
      zomatoIncome,
      swiggySalesCount: swiggySales.length,
      swiggyIncome
    });

    // Calculate total expenses
    const totalExpenses = expenses.reduce((sum, e) => sum + e.amount, 0);

    // Get operational expenses for the date range with proportional calculation
    let operationalExpensesTotal = 0;
    let platformChargesTotal = 0;
    let zomatoMonthlyCharges = 0;
    let swiggyMonthlyCharges = 0;
    const startDate = new Date(this.dateRange.startDate);
    const endDate = new Date(this.dateRange.endDate);

    // Fetch operational expenses and platform charges, then calculate proportionally
    Promise.all([
      this.operationalExpenseService.getAllOperationalExpenses().toPromise(),
      this.platformChargeService.getAllPlatformCharges().toPromise()
    ]).then(([opExpenses, platformCharges]) => {
      // Calculate proportional operational expenses
      opExpenses = opExpenses || [];
      opExpenses.forEach(opExp => {
        const proportionalAmount = this.calculateProportionalMonthlyExpense(
          opExp.month,
          opExp.year,
          opExp.totalOperationalCost,
          startDate,
          endDate
        );
        operationalExpensesTotal += proportionalAmount;
      });

      // Calculate proportional platform charges (separate by platform)
      platformCharges = platformCharges || [];
      platformCharges.forEach(charge => {
        const proportionalAmount = this.calculateProportionalMonthlyExpense(
          charge.month,
          charge.year,
          charge.charges,
          startDate,
          endDate
        );
        platformChargesTotal += proportionalAmount;

        // Track platform-specific charges
        if (charge.platform?.toLowerCase() === 'zomato') {
          zomatoMonthlyCharges += proportionalAmount;
        } else if (charge.platform?.toLowerCase() === 'swiggy') {
          swiggyMonthlyCharges += proportionalAmount;
        }
      });

      // Deduct monthly charges from platform income
      const zomatoIncomeAfterCharges = zomatoIncome - zomatoMonthlyCharges;
      const swiggyIncomeAfterCharges = swiggyIncome - swiggyMonthlyCharges;
      const zomatoSwiggyIncomeAfterCharges = zomatoIncomeAfterCharges + swiggyIncomeAfterCharges;

      console.log('Platform Charges Breakdown:', {
        zomatoMonthlyCharges,
        swiggyMonthlyCharges,
        totalPlatformCharges: platformChargesTotal,
        zomatoIncomeBeforeCharges: zomatoIncome,
        zomatoIncomeAfterCharges,
        swiggyIncomeBeforeCharges: swiggyIncome,
        swiggyIncomeAfterCharges
      });

      // Recalculate revenue after deducting monthly charges from platform income
      const totalRevenueAfterCharges = totalCashCollection + totalOnlineCollection + zomatoSwiggyIncomeAfterCharges;

      // Recalculate totals with proportional monthly expenses
      const totalExpensesWithMonthly = totalExpenses + operationalExpensesTotal + platformChargesTotal;
      const netProfitLoss = totalRevenueAfterCharges - totalExpensesWithMonthly;
      const profitMargin = totalRevenueAfterCharges > 0 ? (netProfitLoss / totalRevenueAfterCharges) * 100 : 0;

      this.earningsData = {
        ...this.earningsData,
        // Update with charges-deducted values
        zomatoIncome: zomatoIncomeAfterCharges,
        swiggyIncome: swiggyIncomeAfterCharges,
        zomatoSwiggyIncome: zomatoSwiggyIncomeAfterCharges,
        onlinePlatformCollection: zomatoSwiggyIncomeAfterCharges,
        totalCollection: totalCashCollection + totalOnlineCollection + zomatoSwiggyIncomeAfterCharges,
        totalRevenue: totalRevenueAfterCharges,
        totalExpenses: totalExpensesWithMonthly,
        regularExpenses: totalExpenses,
        operationalExpenses: operationalExpensesTotal,
        platformCharges: platformChargesTotal,
        netProfitLoss,
        profitMargin
      };

      console.log('Earnings Data Updated with Proportional Monthly Expenses:', {
        operationalExpenses: operationalExpensesTotal,
        platformCharges: platformChargesTotal,
        total: this.earningsData
      });
    }).catch(err => {
      console.error('Error loading monthly expenses for analytics:', err);
    });

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

      // Expenses (will be updated with operational expenses and platform charges)
      totalExpenses,
      regularExpenses: totalExpenses,
      operationalExpenses: 0,
      platformCharges: 0,
      offlineExpenses,
      onlineExpenses,

      // Collections
      totalCashCollection,
      totalOnlineCollection,
      onlinePlatformCollection,
      totalCollection: totalCashCollection + totalOnlineCollection + onlinePlatformCollection,

      // PnL (will be updated with operational expenses)
      totalRevenue,
      netProfitLoss,
      profitMargin
    };

    console.log('Earnings Data Calculated:', this.earningsData);
  }

  /**
   * Calculate proportional amount for monthly expenses based on date range overlap
   * @param month - Month number (1-12)
   * @param year - Year
   * @param monthlyAmount - Total monthly expense amount
   * @param rangeStart - Start date of analytics range
   * @param rangeEnd - End date of analytics range
   * @returns Proportional amount for the date range
   */
  calculateProportionalMonthlyExpense(
    month: number,
    year: number,
    monthlyAmount: number,
    rangeStart: Date,
    rangeEnd: Date
  ): number {
    // Get the first and last day of the expense month
    const monthStart = new Date(year, month - 1, 1);
    const monthEnd = new Date(year, month, 0); // Last day of the month

    // Check if there's any overlap between the month and the date range
    const overlapStart = new Date(Math.max(monthStart.getTime(), rangeStart.getTime()));
    const overlapEnd = new Date(Math.min(monthEnd.getTime(), rangeEnd.getTime()));

    // No overlap
    if (overlapStart > overlapEnd) {
      return 0;
    }

    // Calculate days in overlap
    const daysInOverlap = Math.ceil((overlapEnd.getTime() - overlapStart.getTime()) / (1000 * 60 * 60 * 24)) + 1;

    // Calculate total days in the month
    const daysInMonth = monthEnd.getDate();

    // Calculate proportional amount
    const proportionalAmount = (monthlyAmount / daysInMonth) * daysInOverlap;

    console.log('Proportional Calculation:', {
      month,
      year,
      monthlyAmount,
      daysInMonth,
      daysInOverlap,
      proportionalAmount,
      rangeStart: rangeStart.toISOString().split('T')[0],
      rangeEnd: rangeEnd.toISOString().split('T')[0]
    });

    return proportionalAmount;
  }

  // Customer Analytics Methods
  loadCustomerAnalytics() {
    this.customerLoading = true;

    // Load online sales for the date range
    this.http.get(`${environment.apiUrl}/online-sales/date-range?startDate=${this.dateRange.startDate}&endDate=${this.dateRange.endDate}`)
      .toPromise()
      .then((response: any) => {
        const sales = response?.data || [];
        this.calculateCustomerAnalytics(sales);
        this.customerLoading = false;
      })
      .catch(err => {
        console.error('Error loading customer analytics:', err);
        this.customerLoading = false;
      });
  }

  calculateCustomerAnalytics(sales: any[]) {
    // Filter sales with customer names
    const salesWithCustomers = sales.filter(s => s.customerName && s.customerName.trim() !== '');

    if (salesWithCustomers.length === 0) {
      this.customerAnalytics = null;
      return;
    }

    // Group by customer name
    const customerMap = new Map<string, {
      displayName: string; // Keep original casing for display
      orders: number;
      totalSpent: number;
      totalDiscount: number;
      totalDeduction: number;
      ratings: number[];
      platforms: Set<string>;
      lastOrderDate: Date;
      items: string[];
    }>();

    salesWithCustomers.forEach(sale => {
      // Normalize customer name: lowercase and remove extra spaces
      const normalizedName = sale.customerName.trim().toLowerCase().replace(/\s+/g, ' ');
      const displayName = sale.customerName.trim().replace(/\s+/g, ' '); // Keep original case but normalize spaces

      const existing = customerMap.get(normalizedName) || {
        displayName: displayName,
        orders: 0,
        totalSpent: 0,
        totalDiscount: 0,
        totalDeduction: 0,
        ratings: [] as number[],
        platforms: new Set<string>(),
        lastOrderDate: new Date(sale.orderAt),
        items: [] as string[]
      };

      const netPayout = (sale.billSubTotal || 0) + (sale.packagingCharges || 0) - (sale.discountAmount || 0) - (sale.platformDeduction || 0);

      existing.orders += 1;
      existing.totalSpent += netPayout;
      existing.totalDiscount += sale.discountAmount || 0;
      existing.totalDeduction += sale.platformDeduction || 0;
      if (sale.rating && sale.rating > 0) {
        existing.ratings.push(sale.rating);
      }
      existing.platforms.add(sale.platform);

      // Track last order date
      const orderDate = new Date(sale.orderAt);
      if (orderDate > existing.lastOrderDate) {
        existing.lastOrderDate = orderDate;
      }

      // Track items
      if (sale.orderedItems && Array.isArray(sale.orderedItems)) {
        sale.orderedItems.forEach((item: any) => {
          existing.items.push(item.itemName || item.name);
        });
      }

      customerMap.set(normalizedName, existing);
    });

    console.log('Customer Map Keys:', Array.from(customerMap.keys()));

    // Use the end date from the selected range for calculating days since last order
    const referenceDate = new Date(this.dateRange.endDate);
    console.log('Reference Date (End of Range):', referenceDate.toISOString().split('T')[0]);

    // Convert to array and calculate metrics
    const customers = Array.from(customerMap.entries()).map(([normalizedName, data]) => ({
      name: data.displayName, // Use the display name (original casing)
      orders: data.orders,
      totalSpent: data.totalSpent,
      avgOrderValue: data.totalSpent / data.orders,
      totalDiscount: data.totalDiscount,
      totalDeduction: data.totalDeduction,
      avgRating: data.ratings.length > 0 ? data.ratings.reduce((sum, r) => sum + r, 0) / data.ratings.length : 0,
      platforms: Array.from(data.platforms),
      lastOrderDate: data.lastOrderDate,
      daysSinceLastOrder: Math.floor((referenceDate.getTime() - data.lastOrderDate.getTime()) / (1000 * 60 * 60 * 24)),
      favoriteItems: this.getTopItems(data.items, 3)
    }));

    console.log('Sample Customer Data:', customers.slice(0, 3).map(c => ({
      name: c.name,
      lastOrderDate: c.lastOrderDate.toISOString().split('T')[0],
      daysSinceLastOrder: c.daysSinceLastOrder
    })));

    // Sort by total spent
    customers.sort((a, b) => b.totalSpent - a.totalSpent);

    // Calculate summary stats
    const totalCustomers = customers.length;
    const totalOrders = salesWithCustomers.length;
    const avgOrdersPerCustomer = totalOrders / totalCustomers;
    const repeatCustomers = customers.filter(c => c.orders > 1).length;
    const repeatRate = (repeatCustomers / totalCustomers) * 100;

    // Top customers
    const topCustomersBySpending = customers.slice(0, 10);
    const topCustomersByOrders = [...customers].sort((a, b) => b.orders - a.orders).slice(0, 10);
    const topRatedCustomers = customers.filter(c => c.avgRating >= 4.5).slice(0, 10);

    // Platform preference
    const platformCounts = {
      Zomato: customers.filter(c => c.platforms.length === 1 && c.platforms.includes('Zomato')).length,
      Swiggy: customers.filter(c => c.platforms.length === 1 && c.platforms.includes('Swiggy')).length,
      Both: customers.filter(c => c.platforms.length > 1).length
    };

    // Recent vs inactive customers
    const recentCustomers = customers.filter(c => c.daysSinceLastOrder <= 30).length;
    const inactiveCustomers = customers.filter(c => c.daysSinceLastOrder > 60).length;

    console.log('Customer Classification:', {
      dateRange: `${this.dateRange.startDate} to ${this.dateRange.endDate}`,
      totalCustomers: customers.length,
      recentCustomers: recentCustomers,
      inactiveCustomers: inactiveCustomers,
      recentSample: customers.filter(c => c.daysSinceLastOrder <= 30).slice(0, 3).map(c => ({
        name: c.name,
        lastOrder: c.lastOrderDate.toISOString().split('T')[0],
        daysAgo: c.daysSinceLastOrder
      })),
      inactiveSample: customers.filter(c => c.daysSinceLastOrder > 60).slice(0, 3).map(c => ({
        name: c.name,
        lastOrder: c.lastOrderDate.toISOString().split('T')[0],
        daysAgo: c.daysSinceLastOrder
      }))
    });

    this.customerAnalytics = {
      totalCustomers,
      totalOrders,
      avgOrdersPerCustomer,
      repeatCustomers,
      repeatRate,
      topCustomersBySpending,
      topCustomersByOrders,
      topRatedCustomers,
      platformCounts,
      recentCustomers,
      inactiveCustomers,
      customers
    };

    // Initialize filtered list with all customers
    this.filteredCustomersList = [...customers];

    console.log('Customer Analytics:', this.customerAnalytics);
  }

  getTopItems(items: string[], limit: number): string[] {
    const itemCounts = items.reduce((acc, item) => {
      acc[item] = (acc[item] || 0) + 1;
      return acc;
    }, {} as any);

    return Object.entries(itemCounts)
      .sort(([,a]:any, [,b]:any) => b - a)
      .slice(0, limit)
      .map(([item]) => item);
  }

  // Get all repeat customers (more than 1 order)
  getRepeatCustomers(): any[] {
    if (!this.customerAnalytics || !this.customerAnalytics.customers) {
      return [];
    }
    return this.customerAnalytics.customers
      .filter((c: any) => c.orders > 1)
      .sort((a: any, b: any) => b.orders - a.orders);
  }

  // Filter customers based on search and filter type
  filterCustomers() {
    if (!this.customerAnalytics || !this.customerAnalytics.customers) {
      this.filteredCustomersList = [];
      return;
    }

    let filtered = [...this.customerAnalytics.customers];

    // Apply search filter
    if (this.customerSearchTerm && this.customerSearchTerm.trim() !== '') {
      const searchLower = this.customerSearchTerm.toLowerCase().trim();
      filtered = filtered.filter((c: any) =>
        c.name.toLowerCase().includes(searchLower)
      );
    }

    // Apply type filter
    switch (this.customerFilterType) {
      case 'repeat':
        filtered = filtered.filter((c: any) => c.orders > 1);
        break;
      case 'single':
        filtered = filtered.filter((c: any) => c.orders === 1);
        break;
      case 'active':
        filtered = filtered.filter((c: any) => c.daysSinceLastOrder <= 30);
        break;
      case 'inactive':
        filtered = filtered.filter((c: any) => c.daysSinceLastOrder > 60);
        break;
      default:
        // 'all' - no additional filter
        break;
    }

    this.filteredCustomersList = filtered;
  }

  // Get filtered customers list
  getFilteredCustomers(): any[] {
    if (this.filteredCustomersList.length === 0 && this.customerAnalytics && this.customerAnalytics.customers) {
      // Initialize with all customers if not filtered yet
      this.filteredCustomersList = [...this.customerAnalytics.customers];
    }
    return this.filteredCustomersList;
  }
}

