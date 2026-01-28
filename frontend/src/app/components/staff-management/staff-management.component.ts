import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { StaffService } from '../../services/staff.service';
import { OutletService } from '../../services/outlet.service';
import { Staff, StaffStatistics, EMPLOYMENT_TYPES, COMMON_POSITIONS, DEPARTMENTS, DAYS_OF_WEEK, SALARY_TYPES, GENDERS, DOCUMENT_TYPES } from '../../models/staff.model';
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
    this.loadStaff();
    this.loadOutlets();
    this.loadStatistics();
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
      hireDate: new Date().toISOString().split('T')[0],
      isActive: true,
      salary: 0,
      salaryType: 'Monthly',
      outletIds: [],
      workingDays: [],
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
    this.showModal = true;
  }

  openEditModal(staff: Staff): void {
    this.modalMode = 'edit';
    this.selectedStaff = staff;
    this.staffForm = { ...staff };
    this.currentTab = 'basic';
    this.showModal = true;
  }

  openViewModal(staff: Staff): void {
    this.modalMode = 'view';
    this.selectedStaff = staff;
    this.staffForm = { ...staff };
    this.currentTab = 'basic';
    this.showModal = true;
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
}
