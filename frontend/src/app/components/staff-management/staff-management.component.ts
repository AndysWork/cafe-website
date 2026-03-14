import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { StaffService } from '../../services/staff.service';
import { OutletService } from '../../services/outlet.service';
import { AuthService } from '../../services/auth.service';
import { Staff, StaffStatistics, StaffShift, EMPLOYMENT_TYPES, COMMON_POSITIONS, DEPARTMENTS, DAYS_OF_WEEK, SALARY_TYPES, GENDERS, DOCUMENT_TYPES } from '../../models/staff.model';
import { Outlet } from '../../models/outlet.model';

@Component({
  selector: 'app-staff-management',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './staff-management.component.html',
  styleUrls: ['./staff-management.component.scss']
})
export class StaffManagementComponent implements OnInit {
  private staffService = inject(StaffService);
  private outletService = inject(OutletService);
  private authService = inject(AuthService);
  private router = inject(Router);

  staff: Staff[] = [];
  outlets: Outlet[] = [];
  statistics: StaffStatistics | null = null;
  isLoading = false;
  errorMessage = '';
  successMessage = '';

  // Filters
  filterActiveOnly = true;
  filterOutlet = '';
  filterPosition = '';
  filterDepartment = '';
  searchTerm = '';

  // Modal state
  showModal = false;
  modalMode: 'create' | 'edit' | 'view' = 'create';
  selectedStaff: Staff | null = null;

  // Form data
  staffForm: Partial<Staff> = this.getEmptyForm();

  // Shift management
  currentShifts: StaffShift[] = [];
  showShiftModal = false;
  shiftModalMode: 'create' | 'edit' = 'create';
  selectedShiftIndex: number = -1;
  shiftForm: Partial<StaffShift> = this.getEmptyShiftForm();
  selectedWorkingDays: string[] = [];

  // Email notification modal
  showEmailModal = false;
  emailForm = {
    subject: '',
    message: '',
    sendWhatsApp: false
  };
  emailSending = false;

  // Constants for dropdowns
  employmentTypes = EMPLOYMENT_TYPES;
  positions = COMMON_POSITIONS;
  departments = DEPARTMENTS;
  daysOfWeek = DAYS_OF_WEEK;
  salaryTypes = SALARY_TYPES;
  genders = GENDERS;
  documentTypes = DOCUMENT_TYPES;

  // Current tab in modal
  currentTab: 'basic' | 'employment' | 'compensation' | 'schedule' | 'documents' | 'performance' = 'basic';

  ngOnInit(): void {
    // Only load data if user is authenticated
    if (this.authService.isLoggedIn()) {
      this.loadStaff();
      this.loadOutlets();
      this.loadStatistics();
    } else {
      // Redirect to login if not authenticated
      this.router.navigate(['/login']);
    }
  }

  private getEmptyForm(): Partial<Staff> {
    return {
      employeeId: '',
      firstName: '',
      lastName: '',
      email: '',
      phoneNumber: '',
      alternatePhoneNumber: '',
      dateOfBirth: undefined,
      gender: '',
      position: '',
      department: '',
      employmentType: 'Full-Time',
      hireDate: this.getTodayInIST(),
      isActive: true,
      salary: 0,
      salaryType: 'Monthly',
      outletIds: [],
      workingDays: [],
      shifts: [],
      documents: [],
      skills: [],
      annualLeaveBalance: 0,
      sickLeaveBalance: 0,
      casualLeaveBalance: 0,
      address: {
        street: '',
        city: '',
        state: '',
        postalCode: '',
        country: 'India'
      },
      emergencyContact: {
        name: '',
        relationship: '',
        phoneNumber: '',
        alternatePhoneNumber: ''
      },
      bankDetails: {
        accountHolderName: '',
        accountNumber: '',
        bankName: '',
        ifscCode: '',
        branchName: ''
      }
    };
  }

  loadStaff(): void {
    this.isLoading = true;
    this.staffService.getAllStaff(this.filterActiveOnly).subscribe({
      next: (staff) => {
        this.staff = staff;
        this.isLoading = false;
      },
      error: (error) => {
        this.errorMessage = 'Failed to load staff members';
        this.isLoading = false;
        console.error('Error loading staff:', error);
      }
    });
  }

  loadOutlets(): void {
    this.outletService.getAllOutlets().subscribe({
      next: (outlets) => {
        this.outlets = outlets;
      },
      error: (error) => {
        console.error('Error loading outlets:', error);
      }
    });
  }

  loadStatistics(): void {
    this.staffService.getStatistics().subscribe({
      next: (stats) => {
        this.statistics = stats;
      },
      error: (error) => {
        console.error('Error loading statistics:', error);
        if (error.status === 401 || error.status === 403) {
          this.errorMessage = 'Authentication required. Please log in.';
          setTimeout(() => this.router.navigate(['/login']), 2000);
        } else {
          this.errorMessage = 'Failed to load statistics';
        }
        setTimeout(() => this.errorMessage = '', 3000);
      }
    });
  }

  applyFilters(): void {
    // Reload staff with updated filter
    this.loadStaff();
  }

  searchStaff(): void {
    if (this.searchTerm.trim()) {
      this.isLoading = true;
      this.staffService.searchStaff(this.searchTerm).subscribe({
        next: (staff) => {
          this.staff = staff;
          this.isLoading = false;
        },
        error: (error) => {
          this.errorMessage = 'Search failed';
          this.isLoading = false;
        }
      });
    } else {
      this.loadStaff();
    }
  }

  clearSearch(): void {
    this.searchTerm = '';
    this.loadStaff();
  }

  openCreateModal(): void {
    this.modalMode = 'create';
    this.staffForm = this.getEmptyForm();
    this.selectedStaff = null;
    this.currentTab = 'basic';
    this.currentShifts = [];
    this.showModal = true;
  }

  openEditModal(staff: Staff): void {
    this.modalMode = 'edit';
    this.selectedStaff = staff;
    this.staffForm = { ...staff };
    // Convert dates to yyyy-MM-dd format for HTML date inputs
    this.convertDatesForForm();
    this.currentTab = 'basic';
    this.currentShifts = staff.shifts ? [...staff.shifts] : [];
    this.showModal = true;
  }

  openViewModal(staff: Staff): void {
    this.modalMode = 'view';
    this.selectedStaff = staff;
    this.staffForm = { ...staff };
    // Convert dates to yyyy-MM-dd format for HTML date inputs
    this.convertDatesForForm();
    this.currentTab = 'basic';
    this.currentShifts = staff.shifts ? [...staff.shifts] : [];
    this.showModal = true;
  }

  private convertDatesForForm(): void {
    // Convert ISO date strings to yyyy-MM-dd format for HTML date inputs
    if (this.staffForm.dateOfBirth) {
      this.staffForm.dateOfBirth = this.formatDateForInput(this.staffForm.dateOfBirth) || '';
    }
    if (this.staffForm.hireDate) {
      this.staffForm.hireDate = this.formatDateForInput(this.staffForm.hireDate) || '';
    }
    if (this.staffForm.probationEndDate) {
      this.staffForm.probationEndDate = this.formatDateForInput(this.staffForm.probationEndDate) || '';
    }
    if (this.staffForm.terminationDate) {
      this.staffForm.terminationDate = this.formatDateForInput(this.staffForm.terminationDate) || '';
    }
  }

  private formatDateForInput(date: Date | string | undefined): string | undefined {
    if (!date) return undefined;

    try {
      // If it's already in yyyy-MM-dd format, return as is
      if (typeof date === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(date)) {
        return date;
      }

      // Convert to Date object if string
      const dateObj = typeof date === 'string' ? new Date(date) : date;

      // Check if valid date
      if (isNaN(dateObj.getTime())) {
        return undefined;
      }

      // Convert to IST (Indian Standard Time - UTC+5:30) and format as yyyy-MM-dd
      const istDateString = dateObj.toLocaleString('en-CA', {
        timeZone: 'Asia/Kolkata',
        year: 'numeric',
        month: '2-digit',
        day: '2-digit'
      });

      // en-CA locale returns dates in yyyy-MM-dd format
      return istDateString.split(',')[0].trim();
    } catch (error) {
      console.error('Error formatting date:', error);
      return undefined;
    }
  }

  private getTodayInIST(): string {
    // Get current date in IST timezone
    const today = new Date();
    const istDateString = today.toLocaleString('en-CA', {
      timeZone: 'Asia/Kolkata',
      year: 'numeric',
      month: '2-digit',
      day: '2-digit'
    });

    // en-CA locale returns dates in yyyy-MM-dd format
    return istDateString.split(',')[0].trim();
  }

  closeModal(): void {
    this.showModal = false;
    this.staffForm = this.getEmptyForm();
    this.selectedStaff = null;
    this.currentTab = 'basic';
  }

  saveStaff(): void {
    if (!this.validateForm()) {
      return;
    }

    this.isLoading = true;

    // Include shifts in the form data
    this.staffForm.shifts = this.currentShifts;

    const saveObservable = this.modalMode === 'create'
      ? this.staffService.createStaff(this.staffForm)
      : this.staffService.updateStaff(this.selectedStaff!.id || this.selectedStaff!._id!, this.staffForm);

    saveObservable.subscribe({
      next: () => {
        this.successMessage = `Staff member ${this.modalMode === 'create' ? 'created' : 'updated'} successfully`;
        this.closeModal();
        this.loadStaff();
        this.loadStatistics();
        this.isLoading = false;
        setTimeout(() => this.successMessage = '', 3000);
      },
      error: (error) => {
        this.errorMessage = error.error?.error || `Failed to ${this.modalMode} staff member`;
        this.isLoading = false;
        console.error('Error saving staff:', error);
      }
    });
  }

  validateForm(): boolean {
    if (!this.staffForm.employeeId?.trim()) {
      this.errorMessage = 'Employee ID is required';
      return false;
    }
    if (!this.staffForm.firstName?.trim()) {
      this.errorMessage = 'First name is required';
      return false;
    }
    if (!this.staffForm.lastName?.trim()) {
      this.errorMessage = 'Last name is required';
      return false;
    }
    if (!this.staffForm.email?.trim()) {
      this.errorMessage = 'Email is required';
      return false;
    }
    if (!this.staffForm.phoneNumber?.trim()) {
      this.errorMessage = 'Phone number is required';
      return false;
    }
    if (!this.staffForm.position?.trim()) {
      this.errorMessage = 'Position is required';
      return false;
    }
    this.errorMessage = '';
    return true;
  }

  toggleStaffStatus(staff: Staff): void {
    const action = staff.isActive ? 'deactivate' : 'activate';
    if (confirm(`Are you sure you want to ${action} ${staff.firstName} ${staff.lastName}?`)) {
      this.isLoading = true;
      const observable = staff.isActive
        ? this.staffService.deactivateStaff(staff.id || staff._id!)
        : this.staffService.activateStaff(staff.id || staff._id!);

      observable.subscribe({
        next: () => {
          this.successMessage = `Staff member ${action}d successfully`;
          this.loadStaff();
          this.loadStatistics();
          this.isLoading = false;
          setTimeout(() => this.successMessage = '', 3000);
        },
        error: (error) => {
          this.errorMessage = `Failed to ${action} staff member`;
          this.isLoading = false;
        }
      });
    }
  }

  deleteStaff(staff: Staff): void {
    if (confirm(`Are you sure you want to permanently delete ${staff.firstName} ${staff.lastName}? This action cannot be undone.`)) {
      this.isLoading = true;
      this.staffService.deleteStaff(staff.id || staff._id!).subscribe({
        next: () => {
          this.successMessage = 'Staff member deleted successfully';
          this.loadStaff();
          this.loadStatistics();
          this.isLoading = false;
          setTimeout(() => this.successMessage = '', 3000);
        },
        error: (error) => {
          this.errorMessage = 'Failed to delete staff member';
          this.isLoading = false;
        }
      });
    }
  }

  // Tab navigation
  switchTab(tab: typeof this.currentTab): void {
    this.currentTab = tab;
  }

  // Working days toggle
  toggleWorkingDay(day: string): void {
    if (!this.staffForm.workingDays) {
      this.staffForm.workingDays = [];
    }
    const index = this.staffForm.workingDays.indexOf(day);
    if (index > -1) {
      this.staffForm.workingDays.splice(index, 1);
    } else {
      this.staffForm.workingDays.push(day);
    }
  }

  isWorkingDay(day: string): boolean {
    return this.staffForm.workingDays?.includes(day) || false;
  }

  // Helper methods
  getOutletName(outletId: string): string {
    const outlet = this.outlets.find(o => o.id === outletId || o._id === outletId);
    return outlet?.outletName || 'Unknown Outlet';
  }

  getFullName(staff: Staff): string {
    return `${staff.firstName} ${staff.lastName}`;
  }

  calculateAge(dateOfBirth: Date | string | undefined): number | null {
    if (!dateOfBirth) return null;
    const dob = new Date(dateOfBirth);
    const today = new Date();
    let age = today.getFullYear() - dob.getFullYear();
    const monthDiff = today.getMonth() - dob.getMonth();
    if (monthDiff < 0 || (monthDiff === 0 && today.getDate() < dob.getDate())) {
      age--;
    }
    return age;
  }

  formatDate(date: Date | string | undefined): string {
    if (!date) return 'N/A';
    return new Date(date).toLocaleDateString();
  }

  getStaffCount(): number {
    return this.staff.length;
  }

  exportToCSV(): void {
    // Implement CSV export functionality
    const csvData = this.staff.map(s => ({
      'Employee ID': s.employeeId,
      'Name': this.getFullName(s),
      'Email': s.email,
      'Phone': s.phoneNumber,
      'Position': s.position,
      'Department': s.department || '',
      'Employment Type': s.employmentType,
      'Salary': s.salary,
      'Outlets': this.getOutletNames(s.outletIds),
      'Status': s.isActive ? 'Active' : 'Inactive',
      'Hire Date': this.formatDate(s.hireDate)
    }));

    const csv = this.convertToCSV(csvData);
    this.downloadCSV(csv, 'staff-report.csv');
  }

  private convertToCSV(data: any[]): string {
    if (data.length === 0) return '';
    const headers = Object.keys(data[0]);
    const csvRows = [
      headers.join(','),
      ...data.map(row => headers.map(header => {
        const value = row[header];
        return typeof value === 'string' ? `"${value}"` : value;
      }).join(','))
    ];
    return csvRows.join('\n');
  }

  private downloadCSV(csv: string, filename: string): void {
    const blob = new Blob([csv], { type: 'text/csv' });
    const url = window.URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = filename;
    link.click();
    window.URL.revokeObjectURL(url);
  }

  getOutletNames(outletIds: string[]): string {
    if (!outletIds || outletIds.length === 0) return 'No outlets assigned';
    return outletIds
      .map(id => this.getOutletName(id))
      .filter(name => name !== 'Unknown')
      .join(', ') || 'No outlets assigned';
  }

  toggleOutletSelection(outletId: string): void {
    if (!this.staffForm.outletIds) {
      this.staffForm.outletIds = [];
    }

    const index = this.staffForm.outletIds.indexOf(outletId);
    if (index > -1) {
      this.staffForm.outletIds.splice(index, 1);
    } else {
      this.staffForm.outletIds.push(outletId);
    }
  }

  isOutletSelected(outletId: string): boolean {
    return this.staffForm.outletIds?.includes(outletId) || false;
  }

  // Shift Management Methods

  private getEmptyShiftForm(): Partial<StaffShift> {
    return {
      shiftName: '',
      dayOfWeek: '',
      startTime: '09:00',
      endTime: '17:00',
      breakDuration: 0,
      isActive: true,
      notes: ''
    };
  }

  openAddShiftModal(): void {
    this.shiftModalMode = 'create';
    this.shiftForm = this.getEmptyShiftForm();
    this.selectedShiftIndex = -1;
    this.selectedWorkingDays = [];
    this.showShiftModal = true;
  }

  openEditShiftModal(index: number): void {
    this.shiftModalMode = 'edit';
    this.selectedShiftIndex = index;
    this.shiftForm = { ...this.currentShifts[index] };
    this.showShiftModal = true;
  }

  closeShiftModal(): void {
    this.showShiftModal = false;
    this.shiftForm = this.getEmptyShiftForm();
    this.selectedShiftIndex = -1;
    this.selectedWorkingDays = [];
  }

  toggleShiftWorkingDay(day: string): void {
    const index = this.selectedWorkingDays.indexOf(day);
    if (index > -1) {
      this.selectedWorkingDays.splice(index, 1);
    } else {
      this.selectedWorkingDays.push(day);
    }
  }

  saveShift(): void {
    if (!this.shiftForm.startTime || !this.shiftForm.endTime) {
      this.errorMessage = 'Please fill in shift start and end time';
      return;
    }

    if (this.shiftModalMode === 'create') {
      // Validate working days selection
      if (!this.selectedWorkingDays || this.selectedWorkingDays.length === 0) {
        this.errorMessage = 'Please select at least one working day';
        return;
      }

      // Create shifts for all selected working days
      const newShifts: StaffShift[] = this.selectedWorkingDays.map(day => ({
        shiftName: this.shiftForm.shiftName || `${day} Shift`,
        dayOfWeek: day,
        startTime: this.shiftForm.startTime!,
        endTime: this.shiftForm.endTime!,
        breakDuration: this.shiftForm.breakDuration || 0,
        isActive: this.shiftForm.isActive !== undefined ? this.shiftForm.isActive : true,
        outletId: this.shiftForm.outletId || null,
        notes: this.shiftForm.notes || ''
      } as StaffShift));

      if (this.modalMode === 'create') {
        // For new staff, just add to the temporary array
        this.currentShifts.push(...newShifts);
        this.closeShiftModal();
        this.successMessage = `${newShifts.length} shift(s) added (will be saved when staff is created)`;
        setTimeout(() => this.successMessage = '', 3000);
      } else if (this.modalMode === 'edit' && this.selectedStaff?.id) {
        // For existing staff, save each shift to the API
        const staffId = this.selectedStaff.id;
        if (!staffId) {
          this.errorMessage = 'Invalid staff ID. Please refresh and try again.';
          return;
        }

        let addedCount = 0;
        let errors = 0;
        newShifts.forEach(shift => {
          this.staffService.addStaffShift(staffId, shift).subscribe({
            next: (addedShift) => {
              this.currentShifts.push(addedShift);
              addedCount++;
              if (addedCount + errors === newShifts.length) {
                this.closeShiftModal();
                if (errors === 0) {
                  this.successMessage = `${addedCount} shift(s) added successfully`;
                } else {
                  this.successMessage = `${addedCount} shift(s) added, ${errors} failed`;
                }
                setTimeout(() => this.successMessage = '', 3000);
              }
            },
            error: (error) => {
              errors++;
              console.error('Error adding shift:', error);
              if (addedCount + errors === newShifts.length) {
                this.closeShiftModal();
                this.errorMessage = `Failed to add some shifts. Added: ${addedCount}, Failed: ${errors}`;
                setTimeout(() => this.errorMessage = '', 3000);
              }
            }
          });
        });
      }
    } else if (this.shiftModalMode === 'edit') {
      // Edit mode - single day, no working days selection
      if (!this.shiftForm.dayOfWeek) {
        this.errorMessage = 'Day of week is required';
        return;
      }

      if (this.modalMode === 'create') {
        this.currentShifts[this.selectedShiftIndex] = this.shiftForm as StaffShift;
        this.closeShiftModal();
        this.successMessage = 'Shift updated (will be saved when staff is created)';
        setTimeout(() => this.successMessage = '', 3000);
      } else if (this.modalMode === 'edit' && this.selectedStaff?.id) {
        this.staffService.updateStaffShift(this.selectedStaff.id, this.currentShifts[this.selectedShiftIndex].id!, this.shiftForm).subscribe({
          next: (shift) => {
            this.currentShifts[this.selectedShiftIndex] = shift;
            this.closeShiftModal();
            this.successMessage = 'Shift updated successfully';
            setTimeout(() => this.successMessage = '', 3000);
          },
          error: (error) => {
            this.errorMessage = 'Failed to add shift';
            console.error('Error adding shift:', error);
          }
        });
      } else {
        const shiftId = this.currentShifts[this.selectedShiftIndex].id;
        if (shiftId && this.selectedStaff?.id) {
          this.staffService.updateStaffShift(this.selectedStaff.id, shiftId, this.shiftForm).subscribe({
            next: (shift) => {
              this.currentShifts[this.selectedShiftIndex] = shift;
              this.closeShiftModal();
              this.successMessage = 'Shift updated successfully';
              setTimeout(() => this.successMessage = '', 3000);
            },
            error: (error) => {
              this.errorMessage = 'Failed to update shift';
              console.error('Error updating shift:', error);
            }
          });
        }
      }
    }
  }

  deleteShift(index: number): void {
    if (confirm('Are you sure you want to delete this shift?')) {
      if (this.modalMode === 'create') {
        // For new staff, just remove from the temporary array
        this.currentShifts.splice(index, 1);
        this.successMessage = 'Shift removed';
        setTimeout(() => this.successMessage = '', 3000);
      } else if (this.selectedStaff?.id) {
        // For existing staff, delete from the API
        const shiftId = this.currentShifts[index].id;
        if (shiftId) {
          this.staffService.deleteStaffShift(this.selectedStaff.id, shiftId).subscribe({
            next: () => {
              this.currentShifts.splice(index, 1);
              this.successMessage = 'Shift deleted successfully';
              setTimeout(() => this.successMessage = '', 3000);
            },
            error: (error) => {
              this.errorMessage = 'Failed to delete shift';
              console.error('Error deleting shift:', error);
            }
          });
        }
      }
    }
  }

  getDayShortName(day: string): string {
    const dayMap: { [key: string]: string } = {
      'Monday': 'Mon',
      'Tuesday': 'Tue',
      'Wednesday': 'Wed',
      'Thursday': 'Thu',
      'Friday': 'Fri',
      'Saturday': 'Sat',
      'Sunday': 'Sun'
    };
    return dayMap[day] || day;
  }

  getWeeklySummary(): string {
    const daysCount = new Set(this.currentShifts.map(s => s.dayOfWeek)).size;
    return `${this.currentShifts.length} shift${this.currentShifts.length !== 1 ? 's' : ''} across ${daysCount} day${daysCount !== 1 ? 's' : ''}`;
  }

  // Working hours/days calculation methods
  getWorkingDaysPerWeek(staff: Staff): number {
    if (!staff.shifts || staff.shifts.length === 0) return 0;
    const uniqueDays = new Set(staff.shifts.map(s => s.dayOfWeek));
    return uniqueDays.size;
  }

  getAverageHoursPerDay(staff: Staff): number {
    if (!staff.shifts || staff.shifts.length === 0) return 0;

    let totalHours = 0;
    staff.shifts.forEach(shift => {
      const hours = this.calculateShiftHours(shift.startTime, shift.endTime, shift.breakDuration || 0);
      totalHours += hours;
    });

    const workingDays = this.getWorkingDaysPerWeek(staff);
    return workingDays > 0 ? totalHours / workingDays : 0;
  }

  getTotalHoursPerWeek(staff: Staff): number {
    if (!staff.shifts || staff.shifts.length === 0) return 0;

    let totalHours = 0;
    staff.shifts.forEach(shift => {
      const hours = this.calculateShiftHours(shift.startTime, shift.endTime, shift.breakDuration || 0);
      totalHours += hours;
    });

    return totalHours;
  }

  getTotalHoursPerMonth(staff: Staff): number {
    // Assuming 4.33 weeks per month on average
    return this.getTotalHoursPerWeek(staff) * 4.33;
  }

  private calculateShiftHours(startTime: string, endTime: string, breakDuration: number): number {
    if (!startTime || !endTime) return 0;

    const [startHour, startMinute] = startTime.split(':').map(Number);
    const [endHour, endMinute] = endTime.split(':').map(Number);

    const startInMinutes = startHour * 60 + startMinute;
    const endInMinutes = endHour * 60 + endMinute;

    let durationInMinutes = endInMinutes - startInMinutes;
    if (durationInMinutes < 0) {
      // Handle overnight shifts
      durationInMinutes += 24 * 60;
    }

    return Math.max(0, durationInMinutes / 60);
  }

  getWorkScheduleSummary(staff: Staff): string {
    const daysPerWeek = this.getWorkingDaysPerWeek(staff);
    if (daysPerWeek === 0) return 'No shifts scheduled';

    const avgHours = this.getAverageHoursPerDay(staff);
    return `${daysPerWeek} day${daysPerWeek !== 1 ? 's' : ''}/week • ${avgHours.toFixed(1)} hrs/day avg`;
  }

  // Email notification methods
  openEmailModal(): void {
    if (!this.selectedStaff) return;

    this.emailForm = {
      subject: '',
      message: '',
      sendWhatsApp: false
    };
    this.showEmailModal = true;
  }

  openEmailModalFromCard(staff: Staff): void {
    this.selectedStaff = staff;
    this.openEmailModal();
  }

  closeEmailModal(): void {
    this.showEmailModal = false;
    this.emailForm = {
      subject: '',
      message: '',
      sendWhatsApp: false
    };
  }

  sendEmail(): void {
    if (!this.selectedStaff?.id && !this.selectedStaff?._id) return;

    if (!this.emailForm.subject.trim() || !this.emailForm.message.trim()) {
      this.errorMessage = 'Subject and message are required';
      setTimeout(() => this.errorMessage = '', 3000);
      return;
    }

    const staffId = this.selectedStaff.id || this.selectedStaff._id!;
    this.emailSending = true;

    this.staffService.sendEmailToStaff(
      staffId,
      this.emailForm.subject,
      this.emailForm.message,
      this.emailForm.sendWhatsApp
    ).subscribe({
      next: () => {
        this.successMessage = 'Email sent successfully to ' + this.selectedStaff!.firstName + ' ' + this.selectedStaff!.lastName;
        this.closeEmailModal();
        this.emailSending = false;
        setTimeout(() => this.successMessage = '', 3000);
      },
      error: (error) => {
        this.errorMessage = 'Failed to send email';
        console.error('Error sending email:', error);
        this.emailSending = false;
        setTimeout(() => this.errorMessage = '', 3000);
      }
    });
  }
}
