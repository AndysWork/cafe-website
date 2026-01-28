import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
  DailyPerformanceService,
  DailyPerformanceEntry,
  BulkDailyPerformanceRequest
} from '../../services/daily-performance.service';
import { StaffService } from '../../services/staff.service';
import { OutletService } from '../../services/outlet.service';
import { Staff } from '../../models/staff.model';

interface StaffPerformanceRow {
  staff: Staff;
  entry: DailyPerformanceEntry;
  isEdited: boolean;
}

@Component({
  selector: 'app-daily-performance-entry',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './daily-performance-entry.component.html',
  styleUrls: ['./daily-performance-entry.component.scss']
})
export class DailyPerformanceEntryComponent implements OnInit {
  private performanceService = inject(DailyPerformanceService);
  private staffService = inject(StaffService);
  private outletService = inject(OutletService);

  staffPerformanceRows: StaffPerformanceRow[] = [];
  selectedDate: string = '';
  isLoading = false;
  isSaving = false;
  errorMessage = '';
  successMessage = '';

  // Filter options
  showActiveOnly = true;
  filterPosition = '';
  searchTerm = '';

  // Available positions (will be loaded from staff data)
  positions: string[] = [];

  ngOnInit(): void {
    this.selectedDate = this.getTodayDate();
    this.loadStaffAndPerformance();
  }

  getTodayDate(): string {
    const today = new Date();
    const year = today.getFullYear();
    const month = String(today.getMonth() + 1).padStart(2, '0');
    const day = String(today.getDate()).padStart(2, '0');
    return `${year}-${month}-${day}`;
  }

  loadStaffAndPerformance(): void {
    this.isLoading = true;
    this.errorMessage = '';

    this.staffService.getAllStaff(this.showActiveOnly).subscribe({
      next: (staff: Staff[]) => {
        // Extract unique positions
        this.positions = [...new Set(staff.map((s: Staff) => s.position).filter((p): p is string => !!p))];

        // Load performance data for selected date
        this.performanceService.getDailyPerformanceByDate(this.selectedDate).subscribe({
          next: (performanceData: DailyPerformanceEntry[]) => {
            this.buildPerformanceRows(staff, performanceData);
            this.isLoading = false;
          },
          error: (error: any) => {
            // If no data exists for this date, just show staff with empty entries
            this.buildPerformanceRows(staff, []);
            this.isLoading = false;
          }
        });
      },
      error: (error: any) => {
        this.errorMessage = 'Failed to load staff: ' + (error.error?.message || error.message);
        this.isLoading = false;
      }
    });
  }

  buildPerformanceRows(staff: Staff[], performanceData: DailyPerformanceEntry[]): void {
    this.staffPerformanceRows = staff.map((s: Staff) => {
      const existingEntry = performanceData.find((p: DailyPerformanceEntry) => p.staffId === s.id);

      return {
        staff: s,
        entry: existingEntry || this.createEmptyEntry(s.id || ''),
        isEdited: false
      };
    });

    this.applyFilters();
  }

  createEmptyEntry(staffId: string): DailyPerformanceEntry {
    return {
      staffId: staffId,
      outletId: '',
      date: this.selectedDate,
      inTime: '',
      outTime: '',
      totalOrdersPrepared: 0,
      goodOrdersCount: 0,
      badOrdersCount: 0,
      refundAmountRecovery: 0,
      workingHours: 0,
      notes: ''
    };
  }

  applyFilters(): void {
    let filtered = [...this.staffPerformanceRows];

    // Filter by position
    if (this.filterPosition) {
      filtered = filtered.filter(row => row.staff.position === this.filterPosition);
    }

    // Filter by search term
    if (this.searchTerm) {
      const term = this.searchTerm.toLowerCase();
      filtered = filtered.filter(row =>
        row.staff.firstName.toLowerCase().includes(term) ||
        row.staff.lastName.toLowerCase().includes(term) ||
        row.staff.employeeId?.toLowerCase().includes(term)
      );
    }

    // Update the display
    this.staffPerformanceRows = filtered;
  }

  onDateChange(): void {
    this.loadStaffAndPerformance();
  }

  onFilterChange(): void {
    this.loadStaffAndPerformance();
  }

  markAsEdited(row: StaffPerformanceRow): void {
    row.isEdited = true;
    this.calculateWorkingHours(row.entry);
  }

  calculateWorkingHours(entry: DailyPerformanceEntry): void {
    if (entry.inTime && entry.outTime) {
      const [inHours, inMinutes] = entry.inTime.split(':').map(Number);
      const [outHours, outMinutes] = entry.outTime.split(':').map(Number);

      const inTotalMinutes = inHours * 60 + inMinutes;
      const outTotalMinutes = outHours * 60 + outMinutes;

      let diffMinutes = outTotalMinutes - inTotalMinutes;
      if (diffMinutes < 0) {
        diffMinutes += 24 * 60; // Handle overnight shifts
      }

      entry.workingHours = Math.round((diffMinutes / 60) * 100) / 100;
    }
  }

  validateEntry(entry: DailyPerformanceEntry): boolean {
    if (!entry.inTime || !entry.outTime) {
      return false;
    }
    if (entry.goodOrdersCount < 0 || entry.badOrdersCount < 0 ||
        entry.totalOrdersPrepared < 0 || entry.refundAmountRecovery < 0) {
      return false;
    }
    if (entry.totalOrdersPrepared < (entry.goodOrdersCount + entry.badOrdersCount)) {
      return false;
    }
    return true;
  }

  saveAll(): void {
    const editedRows = this.staffPerformanceRows.filter(row => row.isEdited);

    if (editedRows.length === 0) {
      this.errorMessage = 'No changes to save';
      return;
    }

    // Validate all entries
    const invalidEntries = editedRows.filter(row => !this.validateEntry(row.entry));
    if (invalidEntries.length > 0) {
      this.errorMessage = `Please fix invalid entries for: ${invalidEntries.map(r => r.staff.firstName).join(', ')}`;
      return;
    }

    this.isSaving = true;
    this.errorMessage = '';
    this.successMessage = '';

    const bulkRequest: BulkDailyPerformanceRequest = {
      date: this.selectedDate,
      entries: editedRows.map(row => ({
        staffId: row.staff.id || '',
        inTime: row.entry.inTime,
        outTime: row.entry.outTime,
        totalOrdersPrepared: row.entry.totalOrdersPrepared,
        goodOrdersCount: row.entry.goodOrdersCount,
        badOrdersCount: row.entry.badOrdersCount,
        refundAmountRecovery: row.entry.refundAmountRecovery,
        notes: row.entry.notes
      }))
    };

    this.performanceService.bulkUpsertDailyPerformance(bulkRequest).subscribe({
      next: (result: DailyPerformanceEntry[]) => {
        this.successMessage = `Successfully saved ${result.length} performance entries!`;
        this.isSaving = false;

        // Mark all as not edited
        this.staffPerformanceRows.forEach(row => row.isEdited = false);

        // Reload data to get updated entries
        setTimeout(() => {
          this.loadStaffAndPerformance();
          this.clearMessages();
        }, 2000);
      },
      error: (error: any) => {
        this.errorMessage = 'Failed to save performance data: ' + (error.error?.message || error.message);
        this.isSaving = false;
      }
    });
  }

  saveIndividual(row: StaffPerformanceRow): void {
    if (!this.validateEntry(row.entry)) {
      this.errorMessage = 'Please fill in all required fields with valid values';
      return;
    }

    this.isSaving = true;
    this.errorMessage = '';

    const request = {
      staffId: row.staff.id || '',
      date: this.selectedDate,
      inTime: row.entry.inTime,
      outTime: row.entry.outTime,
      totalOrdersPrepared: row.entry.totalOrdersPrepared,
      goodOrdersCount: row.entry.goodOrdersCount,
      badOrdersCount: row.entry.badOrdersCount,
      refundAmountRecovery: row.entry.refundAmountRecovery,
      notes: row.entry.notes
    };

    this.performanceService.upsertDailyPerformance(request).subscribe({
      next: (result: DailyPerformanceEntry) => {
        row.entry = result;
        row.isEdited = false;
        this.successMessage = `Saved performance for ${row.staff.firstName} ${row.staff.lastName}`;
        this.isSaving = false;
        setTimeout(() => this.clearMessages(), 3000);
      },
      error: (error: any) => {
        this.errorMessage = 'Failed to save: ' + (error.error?.message || error.message);
        this.isSaving = false;
      }
    });
  }

  fillDefaults(row: StaffPerformanceRow): void {
    row.entry.inTime = '09:00';
    row.entry.outTime = '18:00';
    row.entry.totalOrdersPrepared = 0;
    row.entry.goodOrdersCount = 0;
    row.entry.badOrdersCount = 0;
    row.entry.refundAmountRecovery = 0;
    this.markAsEdited(row);
  }

  clearRow(row: StaffPerformanceRow): void {
    row.entry = this.createEmptyEntry(row.staff.id || '');
    row.isEdited = false;
  }

  clearMessages(): void {
    this.errorMessage = '';
    this.successMessage = '';
  }

  getStaffName(staff: Staff): string {
    return `${staff.firstName} ${staff.lastName}`;
  }

  getEditedCount(): number {
    return this.staffPerformanceRows.filter(row => row.isEdited).length;
  }

  exportToCSV(): void {
    if (this.staffPerformanceRows.length === 0) {
      alert('No data to export');
      return;
    }

    const headers = [
      'Employee ID',
      'Staff Name',
      'Position',
      'Date',
      'In Time',
      'Out Time',
      'Working Hours',
      'Total Orders',
      'Good Orders',
      'Bad Orders',
      'Refund Recovery (₹)',
      'Notes'
    ];

    const rows = this.staffPerformanceRows.map(row => [
      row.staff.employeeId || '',
      this.getStaffName(row.staff),
      row.staff.position || '',
      this.selectedDate,
      row.entry.inTime || '',
      row.entry.outTime || '',
      row.entry.workingHours || 0,
      row.entry.totalOrdersPrepared || 0,
      row.entry.goodOrdersCount || 0,
      row.entry.badOrdersCount || 0,
      row.entry.refundAmountRecovery || 0,
      row.entry.notes || ''
    ]);

    const csvContent = [
      headers.join(','),
      ...rows.map(row => row.map(cell => `"${cell}"`).join(','))
    ].join('\n');

    const blob = new Blob([csvContent], { type: 'text/csv' });
    const url = window.URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `daily-performance-${this.selectedDate}.csv`;
    link.click();
    window.URL.revokeObjectURL(url);
  }
}
