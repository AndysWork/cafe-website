import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';

interface OnlineSale {
  _id?: string;
  orderId: string;
  customerName: string;
  customerEmail: string;
  items: Array<{
    name: string;
    quantity: number;
    price: number;
  }>;
  total: number;
  status: string;
  date: Date | string;
  paymentMethod: string;
}

interface SalesStats {
  totalSales: number;
  totalRevenue: number;
  averageOrderValue: number;
  todaysSales: number;
  thisWeekSales: number;
  thisMonthSales: number;
}

@Component({
  selector: 'app-online-sale-tracker',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './online-sale-tracker.component.html',
  styleUrls: ['./online-sale-tracker.component.scss']
})
export class OnlineSaleTrackerComponent implements OnInit {
  sales: OnlineSale[] = [];
  filteredSales: OnlineSale[] = [];
  stats: SalesStats = {
    totalSales: 0,
    totalRevenue: 0,
    averageOrderValue: 0,
    todaysSales: 0,
    thisWeekSales: 0,
    thisMonthSales: 0
  };

  // Filters
  searchQuery = '';
  statusFilter = 'all';
  dateFilter = 'all'; // all, today, week, month
  sortBy = 'date-desc';

  isLoading = false;
  errorMessage = '';
  selectedSale: OnlineSale | null = null;

  constructor(private http: HttpClient) {}

  ngOnInit(): void {
    this.loadSales();
  }

  async loadSales(): Promise<void> {
    this.isLoading = true;
    this.errorMessage = '';

    try {
      // Fetch online orders from the API
      const response: any = await this.http.get(`${environment.apiUrl}/orders/all`).toPromise();
      this.sales = response.data || response || [];
      this.applyFilters();
      this.calculateStats();
    } catch (error: any) {
      this.errorMessage = error.error?.message || 'Failed to load sales data';
      console.error('Error loading sales:', error);
    } finally {
      this.isLoading = false;
    }
  }

  applyFilters(): void {
    let filtered = [...this.sales];

    // Search filter
    if (this.searchQuery) {
      const query = this.searchQuery.toLowerCase();
      filtered = filtered.filter(sale =>
        sale.orderId?.toLowerCase().includes(query) ||
        sale.customerName?.toLowerCase().includes(query) ||
        sale.customerEmail?.toLowerCase().includes(query)
      );
    }

    // Status filter
    if (this.statusFilter !== 'all') {
      filtered = filtered.filter(sale => sale.status === this.statusFilter);
    }

    // Date filter
    const now = new Date();
    const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
    const weekAgo = new Date(today.getTime() - 7 * 24 * 60 * 60 * 1000);
    const monthAgo = new Date(today.getTime() - 30 * 24 * 60 * 60 * 1000);

    if (this.dateFilter === 'today') {
      filtered = filtered.filter(sale => new Date(sale.date) >= today);
    } else if (this.dateFilter === 'week') {
      filtered = filtered.filter(sale => new Date(sale.date) >= weekAgo);
    } else if (this.dateFilter === 'month') {
      filtered = filtered.filter(sale => new Date(sale.date) >= monthAgo);
    }

    // Sort
    filtered.sort((a, b) => {
      switch (this.sortBy) {
        case 'date-desc':
          return new Date(b.date).getTime() - new Date(a.date).getTime();
        case 'date-asc':
          return new Date(a.date).getTime() - new Date(b.date).getTime();
        case 'amount-desc':
          return b.total - a.total;
        case 'amount-asc':
          return a.total - b.total;
        default:
          return 0;
      }
    });

    this.filteredSales = filtered;
  }

  calculateStats(): void {
    const now = new Date();
    const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
    const weekAgo = new Date(today.getTime() - 7 * 24 * 60 * 60 * 1000);
    const monthAgo = new Date(today.getTime() - 30 * 24 * 60 * 60 * 1000);

    this.stats.totalSales = this.sales.length;
    this.stats.totalRevenue = this.sales.reduce((sum, sale) => sum + sale.total, 0);
    this.stats.averageOrderValue = this.stats.totalSales > 0
      ? this.stats.totalRevenue / this.stats.totalSales
      : 0;

    this.stats.todaysSales = this.sales
      .filter(sale => new Date(sale.date) >= today)
      .reduce((sum, sale) => sum + sale.total, 0);

    this.stats.thisWeekSales = this.sales
      .filter(sale => new Date(sale.date) >= weekAgo)
      .reduce((sum, sale) => sum + sale.total, 0);

    this.stats.thisMonthSales = this.sales
      .filter(sale => new Date(sale.date) >= monthAgo)
      .reduce((sum, sale) => sum + sale.total, 0);
  }

  viewSaleDetails(sale: OnlineSale): void {
    this.selectedSale = sale;
  }

  closeDetails(): void {
    this.selectedSale = null;
  }

  onSearchChange(): void {
    this.applyFilters();
  }

  onFilterChange(): void {
    this.applyFilters();
  }

  exportToCSV(): void {
    const headers = ['Order ID', 'Customer', 'Email', 'Items', 'Total', 'Status', 'Date', 'Payment'];
    const rows = this.filteredSales.map(sale => [
      sale.orderId,
      sale.customerName,
      sale.customerEmail,
      sale.items.length,
      sale.total.toFixed(2),
      sale.status,
      new Date(sale.date).toLocaleDateString(),
      sale.paymentMethod
    ]);

    const csvContent = [
      headers.join(','),
      ...rows.map(row => row.join(','))
    ].join('\n');

    const blob = new Blob([csvContent], { type: 'text/csv' });
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `online-sales-${new Date().toISOString().split('T')[0]}.csv`;
    a.click();
    window.URL.revokeObjectURL(url);
  }

  getStatusClass(status: string): string {
    const statusMap: { [key: string]: string } = {
      'pending': 'status-pending',
      'confirmed': 'status-confirmed',
      'preparing': 'status-preparing',
      'ready': 'status-ready',
      'delivered': 'status-delivered',
      'cancelled': 'status-cancelled'
    };
    return statusMap[status.toLowerCase()] || 'status-default';
  }
}
