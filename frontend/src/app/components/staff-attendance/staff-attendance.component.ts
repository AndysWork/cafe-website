import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AttendanceService, Attendance, LeaveRequest, MyAttendanceResponse, MyMonthlyLeaveBalanceResponse } from '../../services/attendance.service';
import { UIStore } from '../../store/ui.store';

@Component({
  selector: 'app-staff-attendance',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './staff-attendance.component.html',
  styleUrls: ['./staff-attendance.component.scss']
})
export class StaffAttendanceComponent implements OnInit {
  private attendanceService = inject(AttendanceService);
  private uiStore = inject(UIStore);

  loading = true;
  attendance: Attendance | null = null;
  shiftLabel = 'Default shift';
  scheduledHours = 0;
  shiftNames: string[] = [];
  shiftDetails: Array<{ name: string; startTime: string; endTime: string; breakDuration: number; scheduledHours: number }> = [];
  currentShiftName = '';
  currentShiftStart = '';
  currentShiftEnd = '';
  canShiftIn = false;
  canShiftOut = false;
  actionMessage = '';
  leavesLoading = false;
  leaveBalanceLoading = false;
  myLeaves: LeaveRequest[] = [];
  selectedLeaveMonth = this.getCurrentMonth();
  monthlyLeaveBalance: MyMonthlyLeaveBalanceResponse | null = null;

  leaveForm = {
    leaveType: 'earned' as 'earned',
    startDate: '',
    endDate: '',
    isHalfDay: false,
    reason: ''
  };

  ngOnInit(): void {
    const today = new Date().toISOString().slice(0, 10);
    this.leaveForm.startDate = today;
    this.leaveForm.endDate = today;
    this.loadToday();
    this.loadMyLeaves();
    this.loadMonthlyLeaveBalance();
  }

  loadToday(): void {
    this.loading = true;
    this.attendanceService.getMyTodayAttendance().subscribe({
      next: (res: MyAttendanceResponse) => {
        this.attendance = res.attendance;
        this.applySchedule(res.schedule);
        this.loading = false;
      },
      error: () => {
        this.loading = false;
        this.uiStore.error('Unable to load your attendance');
      }
    });
  }

  startShift(): void {
    this.attendanceService.startMyShift().subscribe({
      next: (res) => {
        this.attendance = res.attendance;
        this.applySchedule(res.schedule);
        this.uiStore.success('Shift started');
      },
      error: () => this.uiStore.error('Unable to start shift')
    });
  }

  endShift(): void {
    this.attendanceService.endMyShift().subscribe({
      next: (res) => {
        this.attendance = res.attendance;
        this.applySchedule(res.schedule);
        const overtime = res.summary?.overtimeHours ?? this.attendance?.overtimeHours ?? 0;
        const undertime = res.summary?.undertimeHours ?? this.attendance?.undertimeHours ?? 0;
        this.uiStore.success(`Shift ended. Worked ${this.attendance?.hoursWorked ?? 0}h | OT ${overtime}h | UT ${undertime}h`);
      },
      error: () => this.uiStore.error('Unable to end shift')
    });
  }

  loadMyLeaves(): void {
    this.leavesLoading = true;
    this.attendanceService.getMyLeaveRequests().subscribe({
      next: (leaves) => {
        this.myLeaves = leaves;
        this.leavesLoading = false;
      },
      error: () => {
        this.leavesLoading = false;
        this.uiStore.error('Unable to load leave requests');
      }
    });
  }

  loadMonthlyLeaveBalance(): void {
    this.leaveBalanceLoading = true;
    this.attendanceService.getMyMonthlyLeaveBalance(this.selectedLeaveMonth).subscribe({
      next: (balance) => {
        this.monthlyLeaveBalance = balance;
        this.leaveBalanceLoading = false;
      },
      error: () => {
        this.leaveBalanceLoading = false;
        this.monthlyLeaveBalance = null;
        this.uiStore.error('Unable to load monthly leave balance');
      }
    });
  }

  onLeaveMonthChange(): void {
    this.loadMonthlyLeaveBalance();
  }

  onHalfDayToggle(): void {
    if (this.leaveForm.isHalfDay) {
      this.leaveForm.endDate = this.leaveForm.startDate;
    }
  }

  onLeaveStartDateChange(): void {
    if (this.leaveForm.isHalfDay) {
      this.leaveForm.endDate = this.leaveForm.startDate;
    }
  }

  getMonthlyQuotaFromBalance(annualBalance?: number, available?: number): number {
    if (typeof annualBalance === 'number') {
      return Math.max(0, Math.round((annualBalance / 12) * 100) / 100);
    }

    const safeAvailable = Math.max(0, available ?? 0);
    if (safeAvailable > 12) {
      return Math.round((safeAvailable / 12) * 100) / 100;
    }

    return safeAvailable;
  }

  getRemainingMonthlyQuota(annualBalance: number | undefined, available: number | undefined, approvedThisMonth: number, pendingThisMonth: number): number {
    const monthlyQuota = this.getMonthlyQuotaFromBalance(annualBalance, available);
    const applied = (approvedThisMonth ?? 0) + (pendingThisMonth ?? 0);
    return Math.max(0, Math.round((monthlyQuota - applied) * 100) / 100);
  }

  applyLeave(): void {
    if (!this.leaveForm.reason.trim()) {
      this.uiStore.error('Please enter a leave reason');
      return;
    }

    this.attendanceService.createMyLeaveRequest(this.leaveForm).subscribe({
      next: () => {
        this.uiStore.success('Leave request submitted');
        this.leaveForm.isHalfDay = false;
        this.leaveForm.reason = '';
        this.loadMyLeaves();
        this.loadMonthlyLeaveBalance();
      },
      error: () => this.uiStore.error('Unable to submit leave request')
    });
  }

  get isInShift(): boolean {
    return !!this.attendance?.clockIn && !this.attendance?.clockOut;
  }

  get sessionHistory(): NonNullable<Attendance['sessions']> {
    return [...(this.attendance?.sessions || [])]
      .sort((a, b) => (a.clockIn || '').localeCompare(b.clockIn || ''));
  }

  private applySchedule(schedule?: MyAttendanceResponse['schedule']): void {
    if (!schedule) {
      this.shiftLabel = 'No shift configured';
      this.scheduledHours = 0;
      this.shiftNames = [];
      this.shiftDetails = [];
      this.currentShiftName = '';
      this.currentShiftStart = '';
      this.currentShiftEnd = '';
      this.canShiftIn = false;
      this.canShiftOut = false;
      this.actionMessage = 'No shift configured for today';
      return;
    }

    this.shiftLabel = schedule.shiftLabel || 'No shift configured';
    this.scheduledHours = schedule.scheduledHours ?? 0;
    this.shiftNames = schedule.shiftNames || [];
    this.shiftDetails = schedule.shifts || [];
    this.currentShiftName = schedule.currentShiftName || '';
    this.currentShiftStart = schedule.currentShiftStart || '';
    this.currentShiftEnd = schedule.currentShiftEnd || '';
    this.canShiftIn = !!schedule.canShiftIn;
    this.canShiftOut = !!schedule.canShiftOut;
    this.actionMessage = schedule.actionMessage || '';
  }

  private getCurrentMonth(): string {
    const now = new Date();
    const month = `${now.getMonth() + 1}`.padStart(2, '0');
    return `${now.getFullYear()}-${month}`;
  }
}
