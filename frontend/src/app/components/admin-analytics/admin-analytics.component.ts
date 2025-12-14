import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { SalesService, Sales } from '../../services/sales.service';

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

  // Date filters
  dateRange = {
    startDate: new Date(new Date().setMonth(new Date().getMonth() - 1)).toISOString().split('T')[0],
    endDate: new Date().toISOString().split('T')[0]
  };

  constructor(private salesService: SalesService) {}

  ngOnInit() {
    this.loadSalesInsights();
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
    const start = new Date(this.dateRange.startDate);
    const end = new Date(this.dateRange.endDate);
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
    const dateRange = (new Date(this.dateRange.endDate).getTime() - new Date(this.dateRange.startDate).getTime()) / (1000 * 60 * 60 * 24);
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
      const date = new Date(sale.date);
      const weekStart = new Date(date);
      weekStart.setDate(date.getDate() - date.getDay());
      const weekKey = weekStart.toISOString().split('T')[0];
      weeks.set(weekKey, (weeks.get(weekKey) || 0) + sale.totalAmount);
    });
    return Array.from(weeks.entries())
      .map(([week, total]) => ({ week: this.formatDate(new Date(week)), total }))
      .sort((a, b) => new Date(b.week).getTime() - new Date(a.week).getTime())
      .slice(0, 4);
  }

  calculateMonthlyComparison(): { month: string; total: number; transactions: number }[] {
    const months = new Map<string, { total: number; transactions: number }>();
    this.salesData.forEach(sale => {
      const date = new Date(sale.date);
      const monthKey = `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}`;
      const existing = months.get(monthKey) || { total: 0, transactions: 0 };
      months.set(monthKey, {
        total: existing.total + sale.totalAmount,
        transactions: existing.transactions + 1
      });
    });
    return Array.from(months.entries())
      .map(([month, data]) => ({
        month: new Date(month + '-01').toLocaleString('default', { month: 'short', year: 'numeric' }),
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
      .map(([day, total]) => ({ day: this.formatDate(new Date(day)), total }))
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

  onDateRangeChange() {
    this.loadSalesInsights();
  }

  formatCurrency(amount: number): string {
    return `₹${amount.toFixed(2)}`;
  }

  formatDate(date: Date): string {
    return date.toLocaleDateString('en-IN', { day: 'numeric', month: 'short', year: 'numeric' });
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
}
