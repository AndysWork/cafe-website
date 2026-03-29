import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import {
  StaffPerformanceService,
  StaffPerformanceRecord,
  UpsertStaffPerformanceRequest,
  BonusCalculationDetail
} from '../../services/staff-performance.service';
import { StaffService } from '../../services/staff.service';
import { OutletService } from '../../services/outlet.service';
import { UIStore } from '../../store/ui.store';
import { Staff } from '../../models/staff.model';

@Component({
  selector: 'app-staff-performance',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './staff-performance.component.html',
  styleUrls: ['./staff-performance.component.scss']
})
export class StaffPerformanceComponent implements OnInit {
  private performanceService = inject(StaffPerformanceService);
  private staffService = inject(StaffService);
  private outletService = inject(OutletService);
  private uiStore = inject(UIStore);

  performanceRecords: StaffPerformanceRecord[] = [];
  staffMembers: Staff[] = [];
  isLoading = false;
  errorMessage = '';
  successMessage = '';

  // Filters
  selectedStaffId = '';
  selectedPeriod = '';
  viewMode: 'all' | 'individual' = 'all';

  // Modal state
  showModal = false;
  showDetailsModal = false;
  modalMode: 'create' | 'edit' = 'create';
  selectedRecord: StaffPerformanceRecord | null = null;

  // Form data
  performanceForm!: UpsertStaffPerformanceRequest;

  // Available periods (last 12 months)
  availablePeriods: string[] = [];

  ngOnInit(): void {
    this.generateAvailablePeriods();
    this.selectedPeriod = this.availablePeriods[1]; // Default to last month (February) instead of current month
    this.performanceForm = this.getEmptyForm(); // Initialize form after periods are generated
    this.loadStaff();
    this.loadPerformanceRecords();
  }

  private getEmptyForm(): UpsertStaffPerformanceRequest {
    return {
      staffId: '',
      period: (this.availablePeriods && this.availablePeriods.length > 0) ? this.availablePeriods[0] : '',
      scheduledHours: 0,
      actualHours: 0,
      snacksPrepared: 0,
      badOrders: 0,
      goodRatings: 0,
      missingItemRefunds: 0,
      notes: ''
    };
  }

  generateAvailablePeriods(): void {
    const periods: string[] = [];
    const now = new Date();

    for (let i = 0; i < 12; i++) {
      const date = new Date(now.getFullYear(), now.getMonth() - i, 1);
      const year = date.getFullYear();
      const month = String(date.getMonth() + 1).padStart(2, '0');
      periods.push(`${year}-${month}`);
    }

    this.availablePeriods = periods;
  }

  loadStaff(): void {
    this.staffService.getAllStaff(true).subscribe({
      next: (data: Staff[]) => {
        this.staffMembers = data.filter((s: Staff) => s.isActive);
      },
      error: (error: any) => {
        console.error('Failed to load staff:', error);
      }
    });
  }

  loadPerformanceRecords(): void {
    this.isLoading = true;
    this.errorMessage = '';

    if (this.viewMode === 'individual' && this.selectedStaffId) {
      this.performanceService.getStaffPerformanceRecords(
        this.selectedStaffId,
        this.selectedPeriod || undefined
      ).subscribe({
        next: (data) => {
          this.performanceRecords = data;
          this.isLoading = false;
        },
        error: (error) => {
          this.errorMessage = 'Failed to load performance records: ' + (error.error?.message || error.message);
          this.isLoading = false;
        }
      });
    } else {
      this.performanceService.getOutletPerformanceRecords(
        this.selectedPeriod || undefined
      ).subscribe({
        next: (data) => {
          this.performanceRecords = data;
          this.isLoading = false;
        },
        error: (error) => {
          this.errorMessage = 'Failed to load performance records: ' + (error.error?.message || error.message);
          this.isLoading = false;
        }
      });
    }
  }

  onViewModeChange(): void {
    if (this.viewMode === 'individual' && !this.selectedStaffId && this.staffMembers.length > 0) {
      this.selectedStaffId = this.staffMembers[0].id || '';
    }
    this.loadPerformanceRecords();
  }

  onFilterChange(): void {
    this.loadPerformanceRecords();
  }

  openCreateModal(): void {
    this.modalMode = 'create';
    this.selectedRecord = null;
    this.performanceForm = this.getEmptyForm();
    if (this.viewMode === 'individual' && this.selectedStaffId) {
      this.performanceForm.staffId = this.selectedStaffId;
    }
    this.showModal = true;
  }

  openEditModal(record: StaffPerformanceRecord): void {
    this.modalMode = 'edit';
    this.selectedRecord = record;
    this.performanceForm = {
      staffId: record.staffId,
      period: record.period,
      scheduledHours: record.scheduledHours,
      actualHours: record.actualHours,
      snacksPrepared: record.snacksPrepared,
      badOrders: record.badOrders,
      goodRatings: record.goodRatings,
      missingItemRefunds: record.missingItemRefunds,
      notes: record.notes || ''
    };
    this.showModal = true;
  }

  openDetailsModal(record: StaffPerformanceRecord): void {
    this.selectedRecord = record;
    this.showDetailsModal = true;
  }

  closeModal(): void {
    this.showModal = false;
    this.showDetailsModal = false;
    this.selectedRecord = null;
    this.clearMessages();
  }

  savePerformanceRecord(): void {
    if (!this.isFormValid()) {
      this.errorMessage = 'Please fill in all required fields';
      return;
    }

    this.isLoading = true;
    this.errorMessage = '';
    this.successMessage = '';

    this.performanceService.upsertStaffPerformanceRecord(this.performanceForm).subscribe({
      next: () => {
        this.successMessage = 'Performance record saved successfully!';
        this.loadPerformanceRecords();
        setTimeout(() => this.closeModal(), 1500);
      },
      error: (error) => {
        this.errorMessage = 'Failed to save performance record: ' + (error.error?.message || error.message);
        this.isLoading = false;
      }
    });
  }

  calculateBonus(record: StaffPerformanceRecord): void {
    if (!record.id || !record.staffId) return;

    this.isLoading = true;
    this.errorMessage = '';
    this.successMessage = '';

    this.performanceService.calculateStaffBonus(record.id).subscribe({
      next: () => {
        this.successMessage = 'Bonus calculated successfully!';
        this.loadPerformanceRecords();
        setTimeout(() => this.clearMessages(), 3000);
      },
      error: (error) => {
        this.errorMessage = 'Failed to calculate bonus: ' + (error.error?.message || error.message);
        this.isLoading = false;
      }
    });
  }

  isFormValid(): boolean {
    return this.performanceForm.staffId !== '' &&
           this.performanceForm.period !== '' &&
           this.performanceForm.scheduledHours >= 0 &&
           this.performanceForm.actualHours >= 0;
  }

  clearMessages(): void {
    this.errorMessage = '';
    this.successMessage = '';
  }

  getStaffName(staffId: string): string {
    const staff = this.staffMembers.find(s => s.id === staffId);
    return staff ? `${staff.firstName} ${staff.lastName}` : 'Unknown';
  }

  formatPeriod(period: string): string {
    const [year, month] = period.split('-');
    const date = new Date(parseInt(year), parseInt(month) - 1);
    return date.toLocaleDateString('en-US', { year: 'numeric', month: 'long' });
  }

  getOvertimeHours(record: StaffPerformanceRecord): number {
    return record.overtimeHours || Math.max(0, record.actualHours - record.scheduledHours);
  }

  getUndertimeHours(record: StaffPerformanceRecord): number {
    return record.undertimeHours || Math.max(0, record.scheduledHours - record.actualHours);
  }

  getTotalBonuses(record: StaffPerformanceRecord): number {
    if (!record.bonusBreakdown || !Array.isArray(record.bonusBreakdown)) {
      return 0;
    }
    return record.bonusBreakdown
      .filter(b => b.isBonus)
      .reduce((sum, b) => sum + b.calculatedAmount, 0);
  }

  getTotalDeductions(record: StaffPerformanceRecord): number {
    if (!record.bonusBreakdown || !Array.isArray(record.bonusBreakdown)) {
      return 0;
    }
    return record.bonusBreakdown
      .filter(b => !b.isBonus)
      .reduce((sum, b) => sum + b.calculatedAmount, 0);
  }

  exportToCSV(): void {
    if (this.performanceRecords.length === 0) {
      this.uiStore.warning('No records to export');
      return;
    }

    const headers = [
      'Staff Name',
      'Period',
      'Scheduled Hours',
      'Actual Hours',
      'Overtime Hours',
      'Undertime Hours',
      'Snacks Prepared',
      'Bad Orders',
      'Good Ratings',
      'Missing Item Refunds',
      'Total Bonus',
      'Total Deductions',
      'Net Bonus Amount'
    ];

    const rows = this.performanceRecords.map(record => [
      this.getStaffName(record.staffId),
      this.formatPeriod(record.period),
      record.scheduledHours,
      record.actualHours,
      this.getOvertimeHours(record),
      this.getUndertimeHours(record),
      record.snacksPrepared,
      record.badOrders,
      record.goodRatings,
      record.missingItemRefunds,
      record.totalBonus,
      record.totalDeductions,
      record.netBonusAmount
    ]);

    const csvContent = [
      headers.join(','),
      ...rows.map(row => row.join(','))
    ].join('\n');

    const blob = new Blob([csvContent], { type: 'text/csv' });
    const url = window.URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `staff-performance-${this.selectedPeriod || 'all'}.csv`;
    link.click();
    window.URL.revokeObjectURL(url);
  }

  trackByObjId(index: number, item: any): string { return item.id; }

  trackByIndex(index: number): number { return index; }
}
