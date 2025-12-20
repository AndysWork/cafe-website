import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import {
  PlatformChargeService,
  PlatformCharge,
  CreatePlatformChargeRequest,
  UpdatePlatformChargeRequest,
} from '../../services/platform-charge.service';

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
  styleUrls: ['./online-sale-tracker.component.scss'],
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
  startDate = new Date(new Date().getFullYear(), 10, 1)
    .toISOString()
    .split('T')[0]; // Month is 0-indexed, 10 = November
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
  totalOrders = 0;
  itemSubtotal = 0;
  packagingCharge = 0;
  totalDiscount = 0;
  totalDeduction = 0;
  totalMonthlyCharges = 0;
  totalPayout = 0;
  averageRating = 0;

  // Expose Math to template
  Math = Math;

  // Customer name editing
  editingCustomerNameId: string | null = null;
  editingCustomerNameValue: string = '';

  // Platform Charges
  platformCharges: PlatformCharge[] = [];
  showPlatformCharges = true;
  showChargeModal = false;
  isEditingCharge = false;
  currentChargeId: string | null = null;
  chargeFormData: CreatePlatformChargeRequest = {
    platform: 'Zomato',
    month: new Date().getMonth() + 1,
    year: new Date().getFullYear(),
    charges: 0,
    chargeType: '',
    notes: '',
  };

  months = [
    { value: 1, name: 'January' },
    { value: 2, name: 'February' },
    { value: 3, name: 'March' },
    { value: 4, name: 'April' },
    { value: 5, name: 'May' },
    { value: 6, name: 'June' },
    { value: 7, name: 'July' },
    { value: 8, name: 'August' },
    { value: 9, name: 'September' },
    { value: 10, name: 'October' },
    { value: 11, name: 'November' },
    { value: 12, name: 'December' },
  ];

  constructor(
    private http: HttpClient,
    private platformChargeService: PlatformChargeService
  ) {}

  ngOnInit(): void {
    this.loadData();
    this.loadPlatformCharges();
  }

  async loadData(): Promise<void> {
    // Load both sales and daily income, then calculate summary
    await Promise.all([this.loadSales(), this.loadDailyIncome()]);
    this.calculateSummary();
  }

  onPlatformChange(): void {
    this.loadData();
  }

  onFileSelected(event: any): void {
    const file = event.target.files[0];
    if (file) {
      // Validate file type
      const validExtensions = ['.xlsx', '.xls'];
      const fileName = file.name.toLowerCase();
      const isValid = validExtensions.some((ext) => fileName.endsWith(ext));

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

      const response: any = await this.http
        .post(`${environment.apiUrl}/upload/online-sales`, formData)
        .toPromise();

      if (response.success) {
        this.uploadSuccess = true;
        let message = response.message || 'Upload successful!';

        // Add detailed info about processing
        if (response.totalRowsInFile) {
          message += `\nProcessed: ${response.salesProcessed} of ${response.totalRowsInFile} rows`;
        }

        if (
          response.hasErrors &&
          response.errors &&
          response.errors.length > 0
        ) {
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
        const fileInput = document.getElementById(
          'fileInput'
        ) as HTMLInputElement;
        if (fileInput) {
          fileInput.value = '';
        }

        // Reload data
        await this.loadData();
      } else {
        this.uploadSuccess = false;
        this.uploadMessage = response.message || 'Upload failed';
        if (response.errors && response.errors.length > 0) {
          this.uploadMessage += '\n' + response.errors.join('\n');
        }
      }
    } catch (error: any) {
      this.uploadSuccess = false;
      this.uploadMessage =
        error.error?.error || error.error?.message || 'Upload failed';
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
        endDate: this.endDate,
      });

      console.log('Loading sales with params:', {
        platform: this.selectedPlatform,
        startDate: this.startDate,
        endDate: this.endDate,
      });

      const response: any = await this.http
        .get(
          `${environment.apiUrl}/online-sales/date-range?${params.toString()}`
        )
        .toPromise();

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
        endDate: this.endDate,
      });

      const response: any = await this.http
        .get(
          `${environment.apiUrl}/online-sales/daily-income?${params.toString()}`
        )
        .toPromise();

      if (response.success) {
        this.dailyIncome = response.data || [];
        // Summary is calculated in loadSales() after sales data is loaded
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
    const platformData = this.dailyIncome.filter(
      (d) => d.platform?.toLowerCase() === this.selectedPlatform.toLowerCase()
    );

    this.totalOrders = platformData.reduce((sum, d) => sum + d.totalOrders, 0);
    this.totalDeduction = platformData.reduce(
      (sum, d) => sum + d.totalDeduction,
      0
    );
    this.totalDiscount = platformData.reduce(
      (sum, d) => sum + (d.totalDiscount || 0),
      0
    );
    this.packagingCharge = platformData.reduce(
      (sum, d) => sum + (d.totalPackaging || 0),
      0
    );

    // Calculate Item Subtotal directly from sales data (same as Analytics page)
    const platformSales = this.sales.filter(s => s.platform?.toLowerCase() === this.selectedPlatform.toLowerCase());
    this.itemSubtotal = platformSales.reduce((sum, s) => sum + (s.billSubTotal || 0), 0);

    // Calculate monthly charges from platform charges
    const currentMonth = new Date(this.startDate).getMonth() + 1;
    const currentYear = new Date(this.startDate).getFullYear();
    const endMonth = new Date(this.endDate).getMonth() + 1;
    const endYear = new Date(this.endDate).getFullYear();

    this.totalMonthlyCharges = this.platformCharges
      .filter((pc) => {
        if (pc.platform !== this.selectedPlatform) return false;
        // Include charges that fall within the date range
        if (pc.year < currentYear || pc.year > endYear) return false;
        if (pc.year === currentYear && pc.year === endYear) {
          return pc.month >= currentMonth && pc.month <= endMonth;
        }
        if (pc.year === currentYear) {
          return pc.month >= currentMonth;
        }
        if (pc.year === endYear) {
          return pc.month <= endMonth;
        }
        return true;
      })
      .reduce((sum, pc) => sum + pc.charges, 0);

    // Calculate Total Net Payout = Item Subtotal + Packaging - Discount - Deduction - Monthly Charges
    this.totalPayout = this.itemSubtotal + this.packagingCharge - this.totalDiscount - this.totalDeduction - this.totalMonthlyCharges;

    const ratingsSum = platformData.reduce(
      (sum, d) => sum + (d.averageRating || 0),
      0
    );
    const ratingsCount = platformData.filter((d) => d.averageRating > 0).length;
    this.averageRating = ratingsCount > 0 ? ratingsSum / ratingsCount : 0;
  }

  onDateRangeChange(): void {
    this.loadData();
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
    this.dailyIncome.forEach((income) => {
      const dateKey = new Date(income.date).toLocaleDateString('en-IN', {
        month: 'short',
        year: 'numeric',
      });
      if (!grouped[dateKey]) {
        grouped[dateKey] = [];
      }
      grouped[dateKey].push(income);
    });
    return grouped;
  }

  getMonthTotal(
    monthData: DailyIncome[],
    field:
      | 'totalPayout'
      | 'totalOrders'
      | 'totalDeduction'
      | 'totalDiscount'
      | 'totalPackaging'
  ): number {
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
      return items
        .map((item) => {
          if (typeof item === 'string') return item;
          return `${item.name || item.itemName || 'Item'} (${
            item.quantity || 1
          }x)`;
        })
        .join(', ');
    }

    if (typeof items === 'object') {
      return Object.entries(items)
        .map(([key, value]) => `${key}: ${value}`)
        .join(', ');
    }

    return String(items);
  }

  // Calculate Net Payout for individual order
  // Net Payout = Item Subtotal + Packaging - Discount - Deduction
  calculateOrderNetPayout(sale: OnlineSale): number {
    return sale.billSubTotal + sale.packagingCharges - (sale.discountAmount || 0) - sale.platformDeduction;
  }

  formatDate(date: Date | string): string {
    return new Date(date).toLocaleDateString('en-IN', {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
    });
  }

  formatCurrency(amount: number): string {
    return '₹' + amount.toFixed(2);
  }

  startEditingCustomerName(sale: OnlineSale): void {
    const saleId = sale._id || sale.id;
    console.log('Starting edit for sale:', saleId, sale);
    this.editingCustomerNameId = saleId || null;
    this.editingCustomerNameValue = sale.customerName || '';
    console.log('Editing state:', this.editingCustomerNameId, this.editingCustomerNameValue);
  }

  cancelEditingCustomerName(): void {
    this.editingCustomerNameId = null;
    this.editingCustomerNameValue = '';
  }

  async saveCustomerName(sale: OnlineSale): Promise<void> {
    const saleId = sale._id || sale.id;
    if (!saleId) {
      console.error('No sale ID found:', sale);
      return;
    }

    try {
      const response: any = await this.http
        .put(`${environment.apiUrl}/online-sales/id/${saleId}`, {
          customerName: this.editingCustomerNameValue
        })
        .toPromise();

      if (response.success) {
        // Update local data
        sale.customerName = this.editingCustomerNameValue;
        this.editingCustomerNameId = null;
        this.editingCustomerNameValue = '';
        console.log('Customer name updated successfully');
      } else {
        alert('Failed to update customer name: ' + response.message);
      }
    } catch (error: any) {
      alert('Error updating customer name: ' + (error.error?.message || 'Unknown error'));
      console.error('Update error:', error);
    }
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
        endDate: this.endDate,
      });

      const response: any = await this.http
        .delete(`${environment.apiUrl}/online-sales/bulk?${params.toString()}`)
        .toPromise();

      if (response.success) {
        this.uploadSuccess = true;
        this.uploadMessage = `✅ ${response.message}\n\nYou can now upload your Excel file.`;

        // Reload data to show empty state
        await this.loadData();
      } else {
        this.uploadSuccess = false;
        this.uploadMessage = `❌ Delete failed: ${response.message}`;
      }
    } catch (error: any) {
      this.uploadSuccess = false;
      this.uploadMessage = `❌ Error deleting sales: ${
        error.error?.message || 'Unknown error'
      }`;
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
      'Rating',
    ];

    const rows = this.sales.map((sale) => [
      this.formatDate(sale.orderAt),
      sale.platform,
      sale.orderId,
      sale.customerName || 'N/A',
      this.getTotalItemsCount(sale.orderedItems),
      sale.billSubTotal.toFixed(2),
      sale.payout.toFixed(2),
      sale.platformDeduction.toFixed(2),
      sale.rating || 'N/A',
    ]);

    const csvContent = [
      headers.join(','),
      ...rows.map((row) => row.join(',')),
    ].join('\n');

    const blob = new Blob([csvContent], { type: 'text/csv' });
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `${this.selectedPlatform}-sales-${this.startDate}-to-${this.endDate}.csv`;
    a.click();
    window.URL.revokeObjectURL(url);
  }

  // Platform Charges Methods
  loadPlatformCharges(): void {
    this.platformChargeService.getAllPlatformCharges().subscribe({
      next: (charges) => {
        this.platformCharges = charges;
      },
      error: (err) => {
        console.error('Error loading platform charges:', err);
      },
    });
  }

  getMonthName(month: number): string {
    const monthObj = this.months.find((m) => m.value === month);
    return monthObj ? monthObj.name : '';
  }

  getChargesByPlatform(): PlatformCharge[] {
    return this.platformCharges.filter(
      (c) => c.platform === this.selectedPlatform
    );
  }

  togglePlatformCharges(): void {
    this.showPlatformCharges = !this.showPlatformCharges;
  }

  openAddChargeModal(): void {
    this.isEditingCharge = false;
    this.currentChargeId = null;
    this.chargeFormData = {
      platform: this.selectedPlatform,
      month: new Date().getMonth() + 1,
      year: new Date().getFullYear(),
      charges: 0,
      chargeType: 'Commission',
      notes: '',
    };
    this.showChargeModal = true;
  }

  openEditChargeModal(charge: PlatformCharge): void {
    this.isEditingCharge = true;
    this.currentChargeId = charge.id!;
    this.chargeFormData = {
      platform: charge.platform,
      month: charge.month,
      year: charge.year,
      charges: charge.charges,
      chargeType: charge.chargeType,
      notes: charge.notes,
    };
    this.showChargeModal = true;
  }

  closeChargeModal(): void {
    this.showChargeModal = false;
    this.isEditingCharge = false;
    this.currentChargeId = null;
  }

  savePlatformCharge(): void {
    if (this.isEditingCharge && this.currentChargeId) {
      const updateRequest: UpdatePlatformChargeRequest = {
        charges: Number(this.chargeFormData.charges),
        chargeType: this.chargeFormData.chargeType,
        notes: this.chargeFormData.notes,
      };

      this.platformChargeService
        .updatePlatformCharge(this.currentChargeId, updateRequest)
        .subscribe({
          next: () => {
            alert('Platform charge updated successfully!');
            this.loadPlatformCharges();
            this.closeChargeModal();
          },
          error: (err) => {
            console.error('Error updating platform charge:', err);
            alert('Failed to update platform charge');
          },
        });
    } else {
      // Convert month and year to numbers (form binding makes them strings)
      const createRequest: CreatePlatformChargeRequest = {
        platform: this.chargeFormData.platform,
        month: Number(this.chargeFormData.month),
        year: Number(this.chargeFormData.year),
        charges: Number(this.chargeFormData.charges),
        chargeType: this.chargeFormData.chargeType,
        notes: this.chargeFormData.notes,
      };

      this.platformChargeService
        .createPlatformCharge(createRequest)
        .subscribe({
          next: () => {
            alert('Platform charge created successfully!');
            this.loadPlatformCharges();
            this.closeChargeModal();
          },
          error: (err) => {
            console.error('Error creating platform charge:', err);
            alert(err.error?.message || 'Failed to create platform charge');
          },
        });
    }
  }

  deletePlatformCharge(id: string): void {
    if (!confirm('Are you sure you want to delete this platform charge?'))
      return;

    this.platformChargeService.deletePlatformCharge(id).subscribe({
      next: () => {
        alert('Platform charge deleted successfully!');
        this.loadPlatformCharges();
      },
      error: (err) => {
        console.error('Error deleting platform charge:', err);
        alert('Failed to delete platform charge');
      },
    });
  }
}
