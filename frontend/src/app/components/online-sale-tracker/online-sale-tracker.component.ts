import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';

interface OrderedItem {
  quantity: number;
  itemName: string;
  menuItemId?: string;
}

interface OnlineSale {
  _id?: string;
  id?: string;
  platform: string;
  orderId: string;
  customerName?: string;
  orderAt: Date | string;
  distance: number;
  orderedItems: OrderedItem[];
  instructions?: string;
  discountCoupon?: string;
  billSubTotal: number;
  packagingCharges: number;
  discountAmount: number;
  totalCommissionable: number;
  payout: number;
  platformDeduction: number;
  investment: number;
  miscCharges: number;
  rating?: number;
  review?: string;
  kpt?: number;
  rwt?: number;
  orderMarking?: string;
  complain?: string;
  uploadedBy: string;
  createdAt: Date | string;
  updatedAt: Date | string;
}

interface DailyIncome {
  date: Date | string;
  platform: string;
  totalPayout: number;
  totalOrders: number;
  totalDeduction: number;
  totalDiscount: number;
  totalPackaging: number;
  averageRating: number;
}

@Component({
  selector: 'app-online-sale-tracker',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './online-sale-tracker.component.html',
  styleUrls: ['./online-sale-tracker.component.scss']
})
export class OnlineSaleTrackerComponent implements OnInit {
  selectedPlatform: 'Zomato' | 'Swiggy' = 'Zomato';
  selectedFile: File | null = null;
  isUploading = false;
  uploadMessage = '';
  uploadSuccess = false;
  isDeleting = false;

  sales: OnlineSale[] = [];
  dailyIncome: DailyIncome[] = [];
  isLoading = false;
  errorMessage = '';

  // Date range for filtering - Start from November 1st
  startDate = new Date(new Date().getFullYear(), 10, 1).toISOString().split('T')[0]; // Month is 0-indexed, 10 = November
  endDate = new Date().toISOString().split('T')[0];

  // View options
  showDailyIncome = true;
  showOrderDetails = true;
  collapsedMonths: Set<string> = new Set();
  ordersPerPage = 50;
  currentPage = 1;

  get paginatedSales(): OnlineSale[] {
    const startIndex = (this.currentPage - 1) * this.ordersPerPage;
    const endIndex = startIndex + this.ordersPerPage;
    return this.sales.slice(startIndex, endIndex);
  }

  get totalPages(): number {
    return Math.ceil(this.sales.length / this.ordersPerPage);
  }

  // Summary stats
  totalPayout = 0;
  totalOrders = 0;
  totalDeduction = 0;
  averageRating = 0;

  // Expose Math to template
  Math = Math;

  constructor(private http: HttpClient) {}

  ngOnInit(): void {
    this.loadDailyIncome();
    this.loadSales();
  }

  onPlatformChange(): void {
    this.loadSales();
    this.loadDailyIncome();
  }

  onFileSelected(event: any): void {
    const file = event.target.files[0];
    if (file) {
      // Validate file type
      const validExtensions = ['.xlsx', '.xls'];
      const fileName = file.name.toLowerCase();
      const isValid = validExtensions.some(ext => fileName.endsWith(ext));

      if (!isValid) {
        this.uploadMessage = 'Please select an Excel file (.xlsx or .xls)';
        this.uploadSuccess = false;
        this.selectedFile = null;
        return;
      }

      this.selectedFile = file;
      this.uploadMessage = '';
    }
  }

  async uploadFile(): Promise<void> {
    if (!this.selectedFile) {
      this.uploadMessage = 'Please select a file first';
      this.uploadSuccess = false;
      return;
    }

    this.isUploading = true;
    this.uploadMessage = '';
    this.uploadSuccess = false;

    try {
      const formData = new FormData();
      formData.append('file', this.selectedFile);
      formData.append('platform', this.selectedPlatform);

      const response: any = await this.http.post(
        `${environment.apiUrl}/upload/online-sales`,
        formData
      ).toPromise();

      if (response.success) {
        this.uploadSuccess = true;
        let message = response.message || 'Upload successful!';

        // Add detailed info about processing
        if (response.totalRowsInFile) {
          message += `\nProcessed: ${response.salesProcessed} of ${response.totalRowsInFile} rows`;
        }

        if (response.hasErrors && response.errors && response.errors.length > 0) {
          const errorCount = response.errors.length;
          message += `\n\nWarnings/Issues (${errorCount}):`;
          // Show first 10 errors to avoid overwhelming UI
          const errorsToShow = response.errors.slice(0, 10);
          message += '\n' + errorsToShow.join('\n');

          if (errorCount > 10) {
            message += `\n... and ${errorCount - 10} more issues`;
          }
        }

        this.uploadMessage = message;
        this.selectedFile = null;

        // Clear file input
        const fileInput = document.getElementById('fileInput') as HTMLInputElement;
        if (fileInput) {
          fileInput.value = '';
        }

        // Reload data
        await this.loadSales();
        await this.loadDailyIncome();
      } else {
        this.uploadSuccess = false;
        this.uploadMessage = response.message || 'Upload failed';
        if (response.errors && response.errors.length > 0) {
          this.uploadMessage += '\n' + response.errors.join('\n');
        }
      }
    } catch (error: any) {
      this.uploadSuccess = false;
      this.uploadMessage = error.error?.error || error.error?.message || 'Upload failed';
      console.error('Upload error:', error);
    } finally {
      this.isUploading = false;
    }
  }

  async loadSales(): Promise<void> {
    this.isLoading = true;
    this.errorMessage = '';

    try {
      const params = new URLSearchParams({
        platform: this.selectedPlatform,
        startDate: this.startDate,
        endDate: this.endDate
      });

      console.log('Loading sales with params:', {
        platform: this.selectedPlatform,
        startDate: this.startDate,
        endDate: this.endDate
      });

      const response: any = await this.http.get(
        `${environment.apiUrl}/online-sales/date-range?${params.toString()}`
      ).toPromise();

      if (response.success) {
        this.sales = response.data || [];
        this.currentPage = 1; // Reset to first page
        console.log(`Loaded ${this.sales.length} sales records`);
      } else {
        this.errorMessage = response.message || 'Failed to load sales';
      }
    } catch (error: any) {
      this.errorMessage = error.error?.message || 'Failed to load sales data';
      console.error('Error loading sales:', error);
    } finally {
      this.isLoading = false;
    }
  }

  async loadDailyIncome(): Promise<void> {
    this.isLoading = true;
    this.errorMessage = '';

    try {
      const params = new URLSearchParams({
        startDate: this.startDate,
        endDate: this.endDate
      });

      const response: any = await this.http.get(
        `${environment.apiUrl}/online-sales/daily-income?${params.toString()}`
      ).toPromise();

      if (response.success) {
        this.dailyIncome = response.data || [];
        this.calculateSummary();
      } else {
        this.errorMessage = response.message || 'Failed to load daily income';
      }
    } catch (error: any) {
      this.errorMessage = error.error?.message || 'Failed to load daily income';
      console.error('Error loading daily income:', error);
    } finally {
      this.isLoading = false;
    }
  }

  calculateSummary(): void {
    const platformData = this.dailyIncome.filter(d => d.platform === this.selectedPlatform);

    this.totalPayout = platformData.reduce((sum, d) => sum + d.totalPayout, 0);
    this.totalOrders = platformData.reduce((sum, d) => sum + d.totalOrders, 0);
    this.totalDeduction = platformData.reduce((sum, d) => sum + d.totalDeduction, 0);

    const ratingsSum = platformData.reduce((sum, d) => sum + (d.averageRating || 0), 0);
    const ratingsCount = platformData.filter(d => d.averageRating > 0).length;
    this.averageRating = ratingsCount > 0 ? ratingsSum / ratingsCount : 0;
  }

  onDateRangeChange(): void {
    this.loadSales();
    this.loadDailyIncome();
  }

  toggleDailyIncome(): void {
    this.showDailyIncome = !this.showDailyIncome;
  }

  toggleOrderDetails(): void {
    this.showOrderDetails = !this.showOrderDetails;
  }

  toggleMonth(monthKey: string): void {
    if (this.collapsedMonths.has(monthKey)) {
      this.collapsedMonths.delete(monthKey);
    } else {
      this.collapsedMonths.add(monthKey);
    }
  }

  isMonthCollapsed(monthKey: string): boolean {
    return this.collapsedMonths.has(monthKey);
  }

  nextPage(): void {
    if (this.currentPage < this.totalPages) {
      this.currentPage++;
      window.scrollTo({ top: 0, behavior: 'smooth' });
    }
  }

  previousPage(): void {
    if (this.currentPage > 1) {
      this.currentPage--;
      window.scrollTo({ top: 0, behavior: 'smooth' });
    }
  }

  goToPage(page: number): void {
    this.currentPage = page;
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  getGroupedDailyIncome(): { [key: string]: DailyIncome[] } {
    const grouped: { [key: string]: DailyIncome[] } = {};
    this.dailyIncome.forEach(income => {
      const dateKey = new Date(income.date).toLocaleDateString('en-IN', { month: 'short', year: 'numeric' });
      if (!grouped[dateKey]) {
        grouped[dateKey] = [];
      }
      grouped[dateKey].push(income);
    });
    return grouped;
  }

  getMonthTotal(monthData: DailyIncome[], field: 'totalPayout' | 'totalOrders' | 'totalDeduction' | 'totalDiscount' | 'totalPackaging'): number {
    return monthData.reduce((sum, item) => sum + item[field], 0);
  }

  getTotalItemsCount(items: OrderedItem[]): number {
    return items.reduce((sum, item) => sum + item.quantity, 0);
  }

formatOrderedItems(items: any): string {
  if (!items) return 'N/A';

  if (typeof items === 'string') {
    return items;
  }

  if (Array.isArray(items)) {
    return items.map(item => {
      if (typeof item === 'string') return item;
      return `${item.name || item.itemName || 'Item'} (${item.quantity || 1}x)`;
    }).join(', ');
  }

  if (typeof items === 'object') {
    return Object.entries(items)
      .map(([key, value]) => `${key}: ${value}`)
      .join(', ');
  }

  return String(items);
}

  formatDate(date: Date | string): string {
    return new Date(date).toLocaleDateString('en-IN', {
      day: '2-digit',
      month: 'short',
      year: 'numeric'
    });
  }

  formatCurrency(amount: number): string {
    return '₹' + amount.toFixed(2);
  }

  async deleteSalesData(): Promise<void> {
    const confirmMessage = `Are you sure you want to delete all ${this.selectedPlatform} sales from ${this.startDate} to ${this.endDate}?\n\nThis will delete ${this.sales.length} order(s). This action cannot be undone!`;

    if (!confirm(confirmMessage)) {
      return;
    }

    this.isDeleting = true;
    this.errorMessage = '';
    this.uploadMessage = '';

    try {
      const params = new URLSearchParams({
        platform: this.selectedPlatform,
        startDate: this.startDate,
        endDate: this.endDate
      });

      const response: any = await this.http.delete(
        `${environment.apiUrl}/online-sales/bulk?${params.toString()}`
      ).toPromise();

      if (response.success) {
        this.uploadSuccess = true;
        this.uploadMessage = `✅ ${response.message}\n\nYou can now upload your Excel file.`;

        // Reload data to show empty state
        await this.loadSales();
        await this.loadDailyIncome();
      } else {
        this.uploadSuccess = false;
        this.uploadMessage = `❌ Delete failed: ${response.message}`;
      }
    } catch (error: any) {
      this.uploadSuccess = false;
      this.uploadMessage = `❌ Error deleting sales: ${error.error?.message || 'Unknown error'}`;
      console.error('Delete error:', error);
    } finally {
      this.isDeleting = false;
    }
  }

  exportToCSV(): void {
    const headers = [
      'Date',
      'Platform',
      'Order ID',
      'Customer',
      'Items',
      'Bill Total',
      'Payout',
      'Deduction',
      'Rating'
    ];

    const rows = this.sales.map(sale => [
      this.formatDate(sale.orderAt),
      sale.platform,
      sale.orderId,
      sale.customerName || 'N/A',
      this.getTotalItemsCount(sale.orderedItems),
      sale.billSubTotal.toFixed(2),
      sale.payout.toFixed(2),
      sale.platformDeduction.toFixed(2),
      sale.rating || 'N/A'
    ]);

    const csvContent = [
      headers.join(','),
      ...rows.map(row => row.join(','))
    ].join('\n');

    const blob = new Blob([csvContent], { type: 'text/csv' });
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `${this.selectedPlatform}-sales-${this.startDate}-to-${this.endDate}.csv`;
    a.click();
    window.URL.revokeObjectURL(url);
  }
}

