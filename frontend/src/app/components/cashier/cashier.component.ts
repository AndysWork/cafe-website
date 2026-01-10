import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { OutletService } from '../../services/outlet.service';
import { Subscription } from 'rxjs';
import { filter } from 'rxjs/operators';
import { environment } from '../../../environments/environment';

interface DailySalesSummary {
  date: Date;
  cashSales: number;
  cardSales: number;
  onlineSales: number;
  onlineOrderTotal: number;
  totalSales: number;
  totalWithOrders: number;
  cashExpenses: number;
  onlineExpenses: number;
  netCash: number;
  netOnline: number;
}

interface CashReconciliation {
  id?: string;
  date: Date;
  expectedCash: number;
  expectedCoins: number;
  expectedOnline: number;
  expectedTotal: number;
  countedCash: number;
  countedCoins: number;
  actualOnline: number;
  countedTotal: number;
  cashDeficit: number;
  coinDeficit: number;
  onlineDeficit: number;
  totalDeficit: number;
  openingCashBalance: number;
  openingCoinBalance: number;
  openingOnlineBalance: number;
  closingCashBalance: number;
  closingCoinBalance: number;
  closingOnlineBalance: number;
  notes?: string;
  isReconciled: boolean;
}

@Component({
  selector: 'app-cashier',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './cashier.component.html',
  styleUrls: ['./cashier.component.scss']
})
export class CashierComponent implements OnInit, OnDestroy {
  private outletService = inject(OutletService);
  private outletSubscription?: Subscription;

  // Date selection
  selectedDate: string = '';

  // Sales summary
  salesSummary: DailySalesSummary | null = null;

  // Reconciliation data
  reconciliation: CashReconciliation = {
    date: new Date(),
    expectedCash: 0,
    expectedCoins: 0,
    expectedOnline: 0,
    expectedTotal: 0,
    countedCash: 0,
    countedCoins: 0,
    actualOnline: 0,
    countedTotal: 0,
    cashDeficit: 0,
    coinDeficit: 0,
    onlineDeficit: 0,
    totalDeficit: 0,
    openingCashBalance: 0,
    openingCoinBalance: 0,
    openingOnlineBalance: 0,
    closingCashBalance: 0,
    closingCoinBalance: 0,
    closingOnlineBalance: 0,
    notes: '',
    isReconciled: false
  };

  // Bulk upload
  bulkData: string = '';
  showBulkUpload = false;
  selectedFile: File | null = null;
  selectedFileName: string = '';

  // Recent reconciliations
  recentReconciliations: CashReconciliation[] = [];

  // View mode
  viewMode: 'daily' | 'history' = 'daily';

  // Loading and messages
  isLoading = false;
  isSaving = false;
  successMessage = '';
  errorMessage = '';

  // Expose Math to template
  Math = Math;

  constructor(private http: HttpClient) {}

  ngOnInit(): void {
    // Set today's date
    const today = new Date();
    this.selectedDate = today.toISOString().split('T')[0];

    // Subscribe to outlet changes
    this.outletSubscription = this.outletService.selectedOutlet$
      .pipe(filter(outlet => outlet !== null))
      .subscribe(() => {
        this.loadData();
        this.loadRecentReconciliations();
      });

    // Load immediately if outlet is already selected
    if (this.outletService.getSelectedOutlet()) {
      this.loadData();
      this.loadRecentReconciliations();
    }
  }

  ngOnDestroy(): void {
    this.outletSubscription?.unsubscribe();
  }

  async loadData(): Promise<void> {
    this.isLoading = true;
    this.errorMessage = '';

    try {
      // Load sales summary
      await this.loadSalesSummary();

      // Load existing reconciliation if available
      await this.loadReconciliation();
    } catch (error: any) {
      this.errorMessage = error.error?.error || 'Failed to load data';
    } finally {
      this.isLoading = false;
    }
  }

  async loadSalesSummary(): Promise<void> {
    try {
      const response: any = await this.http.get(
        `${environment.apiUrl}/cash-reconciliation/sales-summary/${this.selectedDate}`
      ).toPromise();

      if (response.success && response.data) {
        this.salesSummary = response.data;

        // Use backend-calculated expected values which include opening balance + sales - expenses
        this.reconciliation.expectedCash = response.data.expectedCash || 0;
        this.reconciliation.expectedCoins = 0; // User splits cash into notes and coins during counting

        // Expected online = opening online + today's online - online expenses
        this.reconciliation.expectedOnline = response.data.expectedOnline || 0;
        this.calculateTotals();
      }
    } catch (error) {
      console.error('Error loading sales summary:', error);
    }
  }

  async loadReconciliation(): Promise<void> {
    try {
      const response: any = await this.http.get(
        `${environment.apiUrl}/cash-reconciliation/date/${this.selectedDate}`
      ).toPromise();

      if (response.success && response.data) {
        // Load existing reconciliation but preserve calculated expected values
        const existingData = response.data;
        this.reconciliation = {
          ...existingData,
          // Keep the auto-calculated expected values from sales
          expectedCash: this.reconciliation.expectedCash,
          expectedCoins: this.reconciliation.expectedCoins,
          expectedOnline: this.reconciliation.expectedOnline
        };
        this.calculateTotals();
      } else {
        // Reset reconciliation for new entry
        this.reconciliation.id = undefined;
        this.reconciliation.date = new Date(this.selectedDate);
        this.reconciliation.isReconciled = false;
      }
    } catch (error) {
      // No reconciliation exists, that's okay
      this.reconciliation.id = undefined;
      this.reconciliation.date = new Date(this.selectedDate);
    }
  }

  calculateTotals(): void {
    // Calculate expected totals
    this.reconciliation.expectedTotal =
      this.reconciliation.expectedCash +
      this.reconciliation.expectedCoins +
      this.reconciliation.expectedOnline;

    // Calculate counted totals
    this.reconciliation.countedTotal =
      this.reconciliation.countedCash +
      this.reconciliation.countedCoins +
      this.reconciliation.actualOnline;

    // Calculate deficits (positive = deficit, negative = surplus)
    this.reconciliation.cashDeficit =
      this.reconciliation.expectedCash - this.reconciliation.countedCash;
    this.reconciliation.coinDeficit =
      this.reconciliation.expectedCoins - this.reconciliation.countedCoins;
    this.reconciliation.onlineDeficit =
      this.reconciliation.expectedOnline - this.reconciliation.actualOnline;
    this.reconciliation.totalDeficit =
      this.reconciliation.expectedTotal - this.reconciliation.countedTotal;
  }

  async saveReconciliation(): Promise<void> {
    this.isSaving = true;
    this.errorMessage = '';
    this.successMessage = '';

    try {
      const payload = {
        date: this.selectedDate,
        expectedCash: this.reconciliation.expectedCash,
        expectedCoins: this.reconciliation.expectedCoins,
        // expectedOnline removed - user manually tracks actual online only
        countedCash: this.reconciliation.countedCash,
        countedCoins: this.reconciliation.countedCoins,
        actualOnline: this.reconciliation.actualOnline,
        notes: this.reconciliation.notes,
        isReconciled: this.reconciliation.isReconciled
      };

      let response: any;

      if (this.reconciliation.id) {
        // Update existing
        response = await this.http.put(
          `${environment.apiUrl}/cash-reconciliation/${this.reconciliation.id}`,
          payload
        ).toPromise();
      } else {
        // Create new
        response = await this.http.post(
          `${environment.apiUrl}/cash-reconciliation`,
          payload
        ).toPromise();
      }

      if (response.success) {
        this.successMessage = 'Reconciliation saved successfully!';
        this.reconciliation = response.data;
        this.loadRecentReconciliations();

        setTimeout(() => this.successMessage = '', 3000);
      }
    } catch (error: any) {
      this.errorMessage = error.error?.error || 'Failed to save reconciliation';
    } finally {
      this.isSaving = false;
    }
  }

  async loadRecentReconciliations(): Promise<void> {
    try {
      const endDate = new Date();
      const startDate = new Date();
      startDate.setDate(startDate.getDate() - 30); // Last 30 days

      const response: any = await this.http.get(
        `${environment.apiUrl}/cash-reconciliation?startDate=${startDate.toISOString().split('T')[0]}&endDate=${endDate.toISOString().split('T')[0]}`
      ).toPromise();

      if (response.success && response.data) {
        this.recentReconciliations = response.data;
      }
    } catch (error) {
      console.error('Error loading recent reconciliations:', error);
    }
  }

  async uploadBulkData(): Promise<void> {
    // Handle file upload or CSV paste
    if (this.selectedFile) {
      await this.processFile(this.selectedFile);
    } else if (this.bulkData.trim()) {
      await this.processCsvText(this.bulkData);
    } else {
      this.errorMessage = 'Please upload a file or enter bulk data';
      return;
    }
  }

  onFileSelected(event: any): void {
    const file = event.target.files[0];
    if (file) {
      this.selectedFile = file;
      this.selectedFileName = file.name;
    }
  }

  cancelBulkUpload(): void {
    this.bulkData = '';
    this.selectedFile = null;
    this.selectedFileName = '';
    this.showBulkUpload = false;
  }

  async processFile(file: File): Promise<void> {
    this.isSaving = true;
    this.errorMessage = '';
    this.successMessage = '';

    try {
      const formData = new FormData();
      formData.append('file', file);

      const response: any = await this.http.post(
        `${environment.apiUrl}/cash-reconciliation/bulk-upload`,
        formData
      ).toPromise();

      if (response.success) {
        this.successMessage = `Successfully uploaded ${response.count || response.data?.length || 0} reconciliation records!`;
        this.selectedFile = null;
        this.selectedFileName = '';
        this.showBulkUpload = false;
        this.loadRecentReconciliations();

        setTimeout(() => this.successMessage = '', 3000);
      }
    } catch (error: any) {
      this.errorMessage = error.error?.error || 'Failed to upload file';
    } finally {
      this.isSaving = false;
    }
  }

  async processExcelFile(content: string): Promise<void> {
    // No longer needed - handled by FormData upload
  }

  async processCsvText(csvText: string): Promise<void> {
    this.isSaving = true;
    this.errorMessage = '';
    this.successMessage = '';

    try {
      // Parse CSV data
      const lines = csvText.trim().split('\n').filter(line => !line.startsWith('#')); // Skip comments
      const records: any[] = [];

      for (let i = 1; i < lines.length; i++) { // Skip header
        const parts = lines[i].split(',').map(p => p.trim());
        if (parts.length >= 4) {
          records.push({
            date: parts[0],
            countedCash: parseFloat(parts[1]) || 0,
            countedCoins: parseFloat(parts[2]) || 0,
            actualOnline: parseFloat(parts[3]) || 0,
            notes: parts[4] || ''
            // Expected values will be auto-calculated by the backend from sales data
          });
        }
      }

      if (records.length === 0) {
        this.errorMessage = 'No valid records found in bulk data';
        return;
      }

      const response: any = await this.http.post(
        `${environment.apiUrl}/cash-reconciliation/bulk`,
        { records }
      ).toPromise();

      if (response.success) {
        this.successMessage = `Successfully uploaded ${response.count} reconciliation records!`;
        this.bulkData = '';
        this.showBulkUpload = false;
        this.loadRecentReconciliations();

        setTimeout(() => this.successMessage = '', 3000);
      }
    } catch (error: any) {
      this.errorMessage = error.error?.error || 'Failed to upload bulk data';
    } finally {
      this.isSaving = false;
    }
  }

  exportTemplate(): void {
    const template = `Date,Collected Cash,Collected Coins,Collected Online,Notes
${new Date().toISOString().split('T')[0]},0,0,0,Sample note
# Instructions:
# - Expected values (cash/coins/online) are AUTO-CALCULATED from sales minus expenses
# - Enter only the COLLECTED amounts from physical counting
# - Opening/Closing balances are AUTO-CALCULATED from previous day
# - Date format: YYYY-MM-DD
# - Delete these instruction lines before uploading`;

    const blob = new Blob([template], { type: 'text/csv' });
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'cash-reconciliation-template.csv';
    a.click();
    window.URL.revokeObjectURL(url);
  }

  getDeficitClass(deficit: number): string {
    if (deficit > 0) return 'deficit';
    if (deficit < 0) return 'surplus';
    return 'balanced';
  }

  formatCurrency(amount: number): string {
    return `₹${Math.abs(amount).toFixed(2)}`;
  }

  formatDate(date: any): string {
    if (!date) return '';
    return new Date(date).toLocaleDateString('en-IN');
  }

  selectReconciliation(rec: CashReconciliation): void {
    this.selectedDate = new Date(rec.date).toISOString().split('T')[0];
    this.reconciliation = { ...rec };
    this.viewMode = 'daily';
    this.loadSalesSummary();
  }
}
