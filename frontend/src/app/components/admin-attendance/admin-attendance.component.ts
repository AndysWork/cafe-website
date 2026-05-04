import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AttendanceService, Attendance, LeaveRequest } from '../../services/attendance.service';
import { StaffService } from '../../services/staff.service';
import { OutletService } from '../../services/outlet.service';
import { UIStore } from '../../store/ui.store';
import { Subscription } from 'rxjs';
import { filter } from 'rxjs/operators';
import { getIstInputDate } from '../../utils/date-utils';

@Component({
  selector: 'app-admin-attendance',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './admin-attendance.component.html',
  styleUrls: ['./admin-attendance.component.scss']
})
export class AdminAttendanceComponent implements OnInit, OnDestroy {
  private outletService = inject(OutletService);
  private uiStore = inject(UIStore);
  private outletSub?: Subscription;

  todayAttendance: Attendance[] = [];
  leaveRequests: LeaveRequest[] = [];
  staffList: any[] = [];
  loading = true;
  activeTab: 'attendance' | 'leave' = 'attendance';

  selectedStaffId = '';
  startDate = '';
  endDate = '';
  reportData: Attendance[] = [];

  showLeaveModal = false;
  leaveForm = { staffId: '', leaveType: 'casual', startDate: '', endDate: '', reason: '' };

  constructor(
    private attendanceService: AttendanceService,
    private staffService: StaffService
  ) {
    const now = new Date();
    this.startDate = getIstInputDate(new Date(now.getFullYear(), now.getMonth(), 1));
    this.endDate = getIstInputDate(now);
  }

  ngOnInit() {
    this.outletSub = this.outletService.selectedOutlet$
      .pipe(filter(o => o !== null))
      .subscribe(() => this.loadData());
    if (this.outletService.getSelectedOutlet()) this.loadData();
  }

  ngOnDestroy() { this.outletSub?.unsubscribe(); }

  loadData() {
    this.loading = true;
    this.attendanceService.getTodayAttendance().subscribe({
      next: a => { this.todayAttendance = a; this.loading = false; },
      error: () => { this.uiStore.error('Failed to load attendance'); this.loading = false; }
    });
    this.staffService.getAllStaff().subscribe({
      next: s => this.staffList = s,
      error: () => {}
    });
    this.attendanceService.getLeaveRequests().subscribe({
      next: l => this.leaveRequests = l,
      error: () => {}
    });
  }

  clockIn(staffId: string) {
    this.attendanceService.clockIn(staffId).subscribe({
      next: () => { this.uiStore.success('Clocked in'); this.loadData(); },
      error: () => this.uiStore.error('Failed to clock in')
    });
  }

  clockOut(staffId: string) {
    this.attendanceService.clockOut(staffId).subscribe({
      next: () => { this.uiStore.success('Clocked out'); this.loadData(); },
      error: () => this.uiStore.error('Failed to clock out')
    });
  }

  loadReport() {
    this.attendanceService.getAttendanceReport(this.startDate, this.endDate, this.selectedStaffId || undefined).subscribe({
      next: r => this.reportData = r,
      error: () => this.uiStore.error('Failed to load report')
    });
  }

  openLeaveModal() {
    this.leaveForm = { staffId: '', leaveType: 'casual', startDate: '', endDate: '', reason: '' };
    this.showLeaveModal = true;
  }

  submitLeave() {
    this.attendanceService.createLeaveRequest(this.leaveForm).subscribe({
      next: () => { this.uiStore.success('Leave request submitted'); this.showLeaveModal = false; this.loadData(); },
      error: () => this.uiStore.error('Failed to submit leave request')
    });
  }

  updateLeaveStatus(id: string, status: string) {
    this.attendanceService.updateLeaveStatus(id, status).subscribe({
      next: () => { this.uiStore.success('Leave ' + status); this.loadData(); },
      error: () => this.uiStore.error('Failed to update leave status')
    });
  }

  isStaffClockedIn(staffId: string): boolean {
    return this.todayAttendance.some(a => a.staffId === staffId && a.clockIn && !a.clockOut);
  }

  trackById(_: number, item: any) { return item.id; }
}
