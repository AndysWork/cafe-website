import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';

interface ProfitData {
  totalRevenue: number;
  totalExpenses: number;
  grossProfit: number;
  profitMargin: number;
  itemsProfitability: Array<{
    itemName: string;
    revenue: number;
    cost: number;
    profit: number;
    margin: number;
    quantity: number;
  }>;
}

interface ExpenseCategory {
  category: string;
  amount: number;
  percentage: number;
}

@Component({
  selector: 'app-online-profit-tracker',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './online-profit-tracker.component.html',
  styleUrls: ['./online-profit-tracker.component.scss']
})
export class OnlineProfitTrackerComponent implements OnInit {
  // Expose Math object to template
  Math = Math;

  profitData: ProfitData = {
    totalRevenue: 0,
    totalExpenses: 0,
    grossProfit: 0,
    profitMargin: 0,
    itemsProfitability: []
  };

  expenseCategories: ExpenseCategory[] = [];

  // Filters
  dateRange = 'month'; // today, week, month, year, custom
  startDate = '';
  endDate = '';

  isLoading = false;
  errorMessage = '';

  // Chart view toggle
  viewMode: 'summary' | 'items' | 'expenses' = 'summary';

  constructor(private http: HttpClient) {}

  ngOnInit(): void {
    this.setDefaultDateRange();
    this.loadProfitData();
  }

  setDefaultDateRange(): void {
    const now = new Date();
    this.endDate = now.toISOString().split('T')[0];

    const startOfMonth = new Date(now.getFullYear(), now.getMonth(), 1);
    this.startDate = startOfMonth.toISOString().split('T')[0];
  }

  onDateRangeChange(): void {
    const now = new Date();
    this.endDate = now.toISOString().split('T')[0];

    switch (this.dateRange) {
      case 'today':
        this.startDate = now.toISOString().split('T')[0];
        break;
      case 'week':
        const weekAgo = new Date(now.getTime() - 7 * 24 * 60 * 60 * 1000);
        this.startDate = weekAgo.toISOString().split('T')[0];
        break;
      case 'month':
        const monthAgo = new Date(now.getTime() - 30 * 24 * 60 * 60 * 1000);
        this.startDate = monthAgo.toISOString().split('T')[0];
        break;
      case 'year':
        const yearAgo = new Date(now.getTime() - 365 * 24 * 60 * 60 * 1000);
        this.startDate = yearAgo.toISOString().split('T')[0];
        break;
    }

    if (this.dateRange !== 'custom') {
      this.loadProfitData();
    }
  }

  async loadProfitData(): Promise<void> {
    this.isLoading = true;
    this.errorMessage = '';

    try {
      // Fetch orders and expenses data
      const [ordersResponse, expensesResponse]: any[] = await Promise.all([
        this.http.get(`${environment.apiUrl}/orders/all`).toPromise(),
        this.http.get(`${environment.apiUrl}/expenses/all`).toPromise()
      ]);

      const orders = ordersResponse.data || ordersResponse || [];
      const expenses = expensesResponse.data || expensesResponse || [];

      this.calculateProfitData(orders, expenses);
    } catch (error: any) {
      this.errorMessage = error.error?.message || 'Failed to load profit data';
      console.error('Error loading profit data:', error);
    } finally {
      this.isLoading = false;
    }
  }

  calculateProfitData(orders: any[], expenses: any[]): void {
    const startDate = new Date(this.startDate);
    const endDate = new Date(this.endDate);
    endDate.setHours(23, 59, 59, 999);

    // Filter orders and expenses by date range
    const filteredOrders = orders.filter(order => {
      const orderDate = new Date(order.date);
      return orderDate >= startDate && orderDate <= endDate;
    });

    const filteredExpenses = expenses.filter(expense => {
      const expenseDate = new Date(expense.date);
      return expenseDate >= startDate && expenseDate <= endDate;
    });

    // Calculate total revenue
    this.profitData.totalRevenue = filteredOrders.reduce((sum, order) => {
      return sum + (order.total || 0);
    }, 0);

    // Calculate total expenses
    this.profitData.totalExpenses = filteredExpenses.reduce((sum, expense) => {
      return sum + (expense.amount || 0);
    }, 0);

    // Calculate gross profit and margin
    this.profitData.grossProfit = this.profitData.totalRevenue - this.profitData.totalExpenses;
    this.profitData.profitMargin = this.profitData.totalRevenue > 0
      ? (this.profitData.grossProfit / this.profitData.totalRevenue) * 100
      : 0;

    // Calculate item-level profitability
    this.calculateItemProfitability(filteredOrders);

    // Calculate expense categories
    this.calculateExpenseCategories(filteredExpenses);
  }

  calculateItemProfitability(orders: any[]): void {
    const itemsMap = new Map<string, {
      revenue: number;
      quantity: number;
      cost: number;
    }>();

    orders.forEach(order => {
      if (order.items && Array.isArray(order.items)) {
        order.items.forEach((item: any) => {
          const itemName = item.name || 'Unknown';
          const existing = itemsMap.get(itemName) || { revenue: 0, quantity: 0, cost: 0 };

          const itemRevenue = (item.price || 0) * (item.quantity || 0);
          // Assuming cost is 60% of price (you can adjust this or fetch real cost data)
          const itemCost = itemRevenue * 0.6;

          itemsMap.set(itemName, {
            revenue: existing.revenue + itemRevenue,
            quantity: existing.quantity + (item.quantity || 0),
            cost: existing.cost + itemCost
          });
        });
      }
    });

    this.profitData.itemsProfitability = Array.from(itemsMap.entries()).map(([itemName, data]) => ({
      itemName,
      revenue: data.revenue,
      cost: data.cost,
      profit: data.revenue - data.cost,
      margin: data.revenue > 0 ? ((data.revenue - data.cost) / data.revenue) * 100 : 0,
      quantity: data.quantity
    })).sort((a, b) => b.profit - a.profit);
  }

  calculateExpenseCategories(expenses: any[]): void {
    const categoriesMap = new Map<string, number>();

    expenses.forEach(expense => {
      const category = expense.category || expense.type || 'Other';
      const amount = expense.amount || 0;
      categoriesMap.set(category, (categoriesMap.get(category) || 0) + amount);
    });

    this.expenseCategories = Array.from(categoriesMap.entries()).map(([category, amount]) => ({
      category,
      amount,
      percentage: this.profitData.totalExpenses > 0
        ? (amount / this.profitData.totalExpenses) * 100
        : 0
    })).sort((a, b) => b.amount - a.amount);
  }

  exportReport(): void {
    const report = `
Online Profit Report
Date Range: ${this.startDate} to ${this.endDate}

SUMMARY
-------
Total Revenue: ₹${this.profitData.totalRevenue.toFixed(2)}
Total Expenses: ₹${this.profitData.totalExpenses.toFixed(2)}
Gross Profit: ₹${this.profitData.grossProfit.toFixed(2)}
Profit Margin: ${this.profitData.profitMargin.toFixed(2)}%

ITEM PROFITABILITY
------------------
${this.profitData.itemsProfitability.map(item =>
  `${item.itemName}: Revenue ₹${item.revenue.toFixed(2)}, Cost ₹${item.cost.toFixed(2)}, Profit ₹${item.profit.toFixed(2)} (${item.margin.toFixed(2)}%)`
).join('\n')}

EXPENSE CATEGORIES
------------------
${this.expenseCategories.map(cat =>
  `${cat.category}: ₹${cat.amount.toFixed(2)} (${cat.percentage.toFixed(2)}%)`
).join('\n')}
    `.trim();

    const blob = new Blob([report], { type: 'text/plain' });
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `profit-report-${this.startDate}-to-${this.endDate}.txt`;
    a.click();
    window.URL.revokeObjectURL(url);
  }

  getProfitClass(): string {
    return this.profitData.grossProfit >= 0 ? 'profit-positive' : 'profit-negative';
  }

  getMarginClass(): string {
    if (this.profitData.profitMargin >= 30) return 'margin-excellent';
    if (this.profitData.profitMargin >= 20) return 'margin-good';
    if (this.profitData.profitMargin >= 10) return 'margin-fair';
    return 'margin-poor';
  }

  getProfitPerSale(): string {
    const totalSales = this.profitData.itemsProfitability.reduce((sum, item) => sum + item.quantity, 0);
    if (totalSales === 0) {
      return '0.00';
    }
    const profitPerSale = this.profitData.grossProfit / totalSales;
    return profitPerSale.toFixed(2);
  }
}
