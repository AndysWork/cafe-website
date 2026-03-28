import { Component, OnInit, inject } from '@angular/core';
import { downloadFile } from '../../utils/file-download';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
  DailyPerformanceService,
  DailyPerformanceEntry,
  BulkDailyPerformanceRequest,
  PerformanceShift,
} from '../../services/daily-performance.service';
import { StaffService } from '../../services/staff.service';
import { OutletService } from '../../services/outlet.service';
import { Staff } from '../../models/staff.model';

interface StaffPerformanceRow {
  staff: Staff;
  entry: DailyPerformanceEntry;
  isEdited: boolean;
  isOnLeave: boolean;
  currentShifts: PerformanceShift[];
}

@Component({
  selector: 'app-daily-performance-entry',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './daily-performance-entry.component.html',
  styleUrls: ['./daily-performance-entry.component.scss'],
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

  // Shift management
  showShiftModal = false;
  shiftModalMode: 'create' | 'edit' = 'create';
  selectedRow: StaffPerformanceRow | null = null;
  shiftForm: PerformanceShift = this.getEmptyShiftForm();
  editingShiftIndex = -1;

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
        this.positions = [
          ...new Set(
            staff.map((s: Staff) => s.position).filter((p): p is string => !!p),
          ),
        ];

        // Load performance data for selected date
        this.performanceService
          .getDailyPerformanceByDate(this.selectedDate)
          .subscribe({
            next: (performanceData: DailyPerformanceEntry[]) => {
              this.buildPerformanceRows(staff, performanceData);
              this.isLoading = false;
            },
            error: (error: any) => {
              // If no data exists for this date, just show staff with empty entries
              this.buildPerformanceRows(staff, []);
              this.isLoading = false;
            },
          });
      },
      error: (error: any) => {
        this.errorMessage =
          'Failed to load staff: ' + (error.error?.message || error.message);
        this.isLoading = false;
      },
    });
  }

  buildPerformanceRows(
    staff: Staff[],
    performanceData: DailyPerformanceEntry[],
  ): void {
    this.staffPerformanceRows = staff.map((s: Staff) => {
      const existingEntry = performanceData.find(
        (p: DailyPerformanceEntry) => p.staffId === s.id,
      );

      const entry = existingEntry || this.createEmptyEntry(s.id || '');
      return {
        staff: s,
        entry,
        isEdited: false,
        isOnLeave: (entry.leaveHours || 0) > 0,
        currentShifts: existingEntry?.shifts ? [...existingEntry.shifts] : [],
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
      leaveHours: 0,
      notes: '',
    };
  }

  applyFilters(): void {
    let filtered = [...this.staffPerformanceRows];

    // Filter by position
    if (this.filterPosition) {
      filtered = filtered.filter(
        (row) => row.staff.position === this.filterPosition,
      );
    }

    // Filter by search term
    if (this.searchTerm) {
      const term = this.searchTerm.toLowerCase();
      filtered = filtered.filter(
        (row) =>
          row.staff.firstName.toLowerCase().includes(term) ||
          row.staff.lastName.toLowerCase().includes(term) ||
          row.staff.employeeId?.toLowerCase().includes(term),
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

  onLeaveToggle(row: StaffPerformanceRow): void {
    if (row.isOnLeave) {
      row.entry.leaveHours = this.getStaffDailyHours(row.staff);
    } else {
      row.entry.leaveHours = 0;
    }
    this.markAsEdited(row);
  }

  getStaffDailyHours(staff: Staff): number {
    if (!staff.shifts || staff.shifts.length === 0) return 8; // fallback

    const selectedDay = this.getDayOfWeek(new Date(this.selectedDate));
    let totalHours = 0;

    staff.shifts.forEach(shift => {
      if (shift.isActive && shift.dayOfWeek === selectedDay) {
        const hours = this.calculateShiftHours(shift.startTime, shift.endTime);
        totalHours += Math.max(0, hours - (shift.breakDuration / 60));
      }
    });

    return totalHours > 0 ? Math.round(totalHours * 100) / 100 : 8; // fallback to 8 if no shifts configured for the day
  }

  private getDayOfWeek(date: Date): string {
    const days = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
    return days[date.getDay()];
  }

  private calculateShiftHours(inTime: string, outTime: string): number {
    if (!inTime || !outTime) return 0;
    const [inH, inM] = inTime.split(':').map(Number);
    const [outH, outM] = outTime.split(':').map(Number);
    let inMin = inH * 60 + inM;
    let outMin = outH * 60 + outM;
    if (outMin < inMin) outMin += 24 * 60;
    return (outMin - inMin) / 60;
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
    if (
      entry.goodOrdersCount < 0 ||
      entry.badOrdersCount < 0 ||
      entry.totalOrdersPrepared < 0 ||
      entry.refundAmountRecovery < 0
    ) {
      return false;
    }
    if (
      entry.totalOrdersPrepared <
      entry.goodOrdersCount + entry.badOrdersCount
    ) {
      return false;
    }
    return true;
  }

  validateRow(row: StaffPerformanceRow): boolean {
    // If on leave, always valid (leave hours are auto-calculated)
    if (row.isOnLeave) {
      return true;
    }

    // If the row has shifts, validate shifts instead of legacy fields
    if (row.currentShifts && row.currentShifts.length > 0) {
      // Shifts are present, no need to validate legacy fields
      return true;
    }

    // No shifts, validate legacy fields
    return this.validateEntry(row.entry);
  }

  private cleanShiftsForSave(shifts: PerformanceShift[]): PerformanceShift[] {
    // Return empty array if shifts is null or undefined
    if (!shifts || !Array.isArray(shifts)) {
      return [];
    }

    // Remove temporary IDs (those starting with "temp_") before sending to API
    return shifts.map(shift => {
      const cleanedShift = { ...shift };
      if (cleanedShift.id?.startsWith('temp_')) {
        delete cleanedShift.id;
      }
      return cleanedShift;
    });
  }

  saveAll(): void {
    const editedRows = this.staffPerformanceRows.filter((row) => row.isEdited);

    if (editedRows.length === 0) {
      this.errorMessage = 'No changes to save';
      return;
    }

    // Validate all entries
    const invalidEntries = editedRows.filter(
      (row) => !this.validateRow(row),
    );
    if (invalidEntries.length > 0) {
      this.errorMessage = `Please fix invalid entries for: ${invalidEntries.map((r) => r.staff.firstName).join(', ')}`;
      return;
    }

    this.isSaving = true;
    this.errorMessage = '';
    this.successMessage = '';

    const bulkRequest: BulkDailyPerformanceRequest = {
      date: this.selectedDate,
      entries: editedRows.map((row) => ({
        staffId: row.staff.id || '',
        inTime: row.entry.inTime || '00:00',
        outTime: row.entry.outTime || '00:00',
        totalOrdersPrepared: row.entry.totalOrdersPrepared || 0,
        goodOrdersCount: row.entry.goodOrdersCount || 0,
        badOrdersCount: row.entry.badOrdersCount || 0,
        refundAmountRecovery: row.entry.refundAmountRecovery || 0,
        notes: row.entry.notes,
        leaveHours: row.entry.leaveHours || 0,
        shifts: this.cleanShiftsForSave(row.currentShifts),
      })),
    };

    this.performanceService.bulkUpsertDailyPerformance(bulkRequest).subscribe({
      next: (result: DailyPerformanceEntry[]) => {
        this.successMessage = `Successfully saved ${result.length} performance entries!`;
        this.isSaving = false;

        // Mark all as not edited
        this.staffPerformanceRows.forEach((row) => (row.isEdited = false));

        // Reload data to get updated entries
        setTimeout(() => {
          this.loadStaffAndPerformance();
          this.clearMessages();
        }, 2000);
      },
      error: (error: any) => {
        this.errorMessage =
          'Failed to save performance data: ' +
          (error.error?.message || error.message);
        this.isSaving = false;
      },
    });
  }

  saveIndividual(row: StaffPerformanceRow): void {
    if (!this.validateRow(row)) {
      this.errorMessage =
        'Please fill in all required fields with valid values';
      return;
    }

    this.isSaving = true;
    this.errorMessage = '';

    const request = {
      staffId: row.staff.id || '',
      date: this.selectedDate,
      inTime: row.entry.inTime || '00:00',
      outTime: row.entry.outTime || '00:00',
      totalOrdersPrepared: row.entry.totalOrdersPrepared || 0,
      goodOrdersCount: row.entry.goodOrdersCount || 0,
      badOrdersCount: row.entry.badOrdersCount || 0,
      refundAmountRecovery: row.entry.refundAmountRecovery || 0,
      notes: row.entry.notes,
      leaveHours: row.entry.leaveHours || 0,
      shifts: this.cleanShiftsForSave(row.currentShifts),
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
        this.errorMessage =
          'Failed to save: ' + (error.error?.message || error.message);
        this.isSaving = false;
      },
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
    if (!confirm('This will clear all data and shifts for this staff member. Continue?')) {
      return;
    }

    if (row.entry.id) {
      // Entry exists in database - delete it via API
      this.performanceService.deleteDailyPerformance(row.entry.id).subscribe({
        next: () => {
          row.entry = this.createEmptyEntry(row.staff.id || '');
          row.currentShifts = [];
          row.isEdited = false;
          row.isOnLeave = false;
          this.successMessage = 'Performance entry deleted successfully';
          setTimeout(() => this.clearMessages(), 3000);
        },
        error: (error: any) => {
          this.errorMessage =
            'Failed to delete performance entry: ' +
            (error.error?.error || error.error?.message || error.message);
          setTimeout(() => this.clearMessages(), 5000);
        },
      });
    } else {
      // No entry in database - just clear locally
      row.entry = this.createEmptyEntry(row.staff.id || '');
      row.currentShifts = [];
      row.isEdited = false;
      row.isOnLeave = false;
      this.successMessage = 'Row cleared successfully';
      setTimeout(() => this.clearMessages(), 3000);
    }
  }

  clearMessages(): void {
    this.errorMessage = '';
    this.successMessage = '';
  }

  getStaffName(staff: Staff): string {
    return `${staff.firstName} ${staff.lastName}`;
  }

  getEditedCount(): number {
    return this.staffPerformanceRows.filter((row) => row.isEdited).length;
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
      'Leave Hours',
      'Total Orders',
      'Good Orders',
      'Bad Orders',
      'Refund Recovery (₹)',
      'Notes',
    ];

    const rows = this.staffPerformanceRows.map((row) => [
      row.staff.employeeId || '',
      this.getStaffName(row.staff),
      row.staff.position || '',
      this.selectedDate,
      row.entry.inTime || '',
      row.entry.outTime || '',
      row.entry.workingHours || 0,
      row.entry.leaveHours || 0,
      row.entry.totalOrdersPrepared || 0,
      row.entry.goodOrdersCount || 0,
      row.entry.badOrdersCount || 0,
      row.entry.refundAmountRecovery || 0,
      row.entry.notes || '',
    ]);

    const csvContent = [
      headers.join(','),
      ...rows.map((row) => row.map((cell) => `"${cell}"`).join(',')),
    ].join('\n');

    downloadFile(csvContent, `daily-performance-${this.selectedDate}.csv`);
  }

  // Shift Management Methods
  getEmptyShiftForm(): PerformanceShift {
    return {
      shiftName: '',
      inTime: '',
      outTime: '',
      totalOrdersPrepared: 0,
      goodOrdersCount: 0,
      badOrdersCount: 0,
      refundAmountRecovery: 0,
      notes: '',
    };
  }

  openAddShiftModal(row: StaffPerformanceRow): void {
    this.selectedRow = row;
    this.shiftModalMode = 'create';
    this.shiftForm = this.getEmptyShiftForm();
    this.editingShiftIndex = -1;
    this.showShiftModal = true;
  }

  openEditShiftModal(row: StaffPerformanceRow, index: number): void {
    this.selectedRow = row;
    this.shiftModalMode = 'edit';
    this.editingShiftIndex = index;
    this.shiftForm = { ...row.currentShifts[index] };
    this.showShiftModal = true;
  }

  closeShiftModal(): void {
    this.showShiftModal = false;
    this.selectedRow = null;
    this.shiftForm = this.getEmptyShiftForm();
    this.editingShiftIndex = -1;
  }

  saveShift(): void {
    if (!this.selectedRow) return;

    // Validate shift form
    if (
      !this.shiftForm.shiftName ||
      !this.shiftForm.inTime ||
      !this.shiftForm.outTime
    ) {
      this.errorMessage = 'Please fill in all required shift fields';
      return;
    }

    // Calculate working hours
    this.calculateShiftWorkingHours(this.shiftForm);

    // Always handle shifts locally and save via bulk operation
    if (this.shiftModalMode === 'create') {
      // Generate a temporary ID for new shifts
      this.shiftForm.id = `temp_${Date.now()}`;
      this.selectedRow.currentShifts.push({ ...this.shiftForm });
      this.successMessage = 'Shift added. Click "Save All Changes" to persist.';
    } else {
      // Edit mode - update existing shift
      this.selectedRow.currentShifts[this.editingShiftIndex] = {
        ...this.shiftForm,
      };
      this.successMessage = 'Shift updated. Click "Save All Changes" to persist.';
    }

    this.selectedRow.isEdited = true;
    this.closeShiftModal();
    setTimeout(() => this.clearMessages(), 3000);
  }

  deleteShift(row: StaffPerformanceRow, index: number): void {
    if (!confirm('Are you sure you want to delete this shift?')) {
      return;
    }

    // Always handle shift deletion locally and save via bulk operation
    row.currentShifts.splice(index, 1);
    row.isEdited = true;
    this.successMessage = 'Shift removed. Click "Save All Changes" to persist.';
    setTimeout(() => this.clearMessages(), 3000);
  }

  calculateShiftWorkingHours(shift: PerformanceShift): void {
    if (shift.inTime && shift.outTime) {
      const [inHours, inMinutes] = shift.inTime.split(':').map(Number);
      const [outHours, outMinutes] = shift.outTime.split(':').map(Number);

      const inTotalMinutes = inHours * 60 + inMinutes;
      const outTotalMinutes = outHours * 60 + outMinutes;

      let diffMinutes = outTotalMinutes - inTotalMinutes;
      if (diffMinutes < 0) {
        diffMinutes += 24 * 60; // Handle overnight shifts
      }

      shift.workingHours = Math.round((diffMinutes / 60) * 100) / 100;
    }
  }

  getTotalShiftHours(row: StaffPerformanceRow): number {
    return row.currentShifts.reduce(
      (total, shift) => total + (shift.workingHours || 0),
      0,
    );
  }

  getTotalShiftOrders(row: StaffPerformanceRow): number {
    return row.currentShifts.reduce(
      (total, shift) => total + shift.totalOrdersPrepared,
      0,
    );
  }

  trackByIndex(index: number): number { return index; }
}
