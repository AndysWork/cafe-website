import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AttendanceService, Attendance, LeaveRequest, CreateLeaveRequest } from '../../services/attendance.service';
import { StaffService } from '../../services/staff.service';
import { OutletService } from '../../services/outlet.service';
import { UIStore } from '../../store/ui.store';
import { Subscription, interval } from 'rxjs';
import { filter } from 'rxjs/operators';
import { getIstInputDate, formatIstDate } from '../../utils/date-utils';

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
  private refreshSub?: Subscription;

  todayAttendance: Attendance[] = [];
  leaveRequests: LeaveRequest[] = [];
  staffList: any[] = [];
  private staffNameById: Record<string, string> = {};
  loading = true;
  activeTab: 'attendance' | 'leave' = 'attendance';

  selectedStaffId = '';
  startDate = '';
  endDate = '';
  reportData: Attendance[] = [];

  showLeaveModal = false;
  leaveForm: CreateLeaveRequest = { staffId: '', leaveType: 'earned', startDate: '', endDate: '', isHalfDay: false, reason: '' };

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

    this.refreshSub = interval(15000).subscribe(() => {
      this.refreshAttendanceSnapshot();
    });
  }

  ngOnDestroy() {
    this.outletSub?.unsubscribe();
    this.refreshSub?.unsubscribe();
  }

  loadData() {
    this.loading = true;
    this.refreshAttendanceSnapshot(true);
    this.staffService.getAllStaff(true).subscribe({
      next: s => {
        this.staffList = s;
        this.rebuildStaffNameIndex();
        this.applyAttendanceNameFallbacks();
        this.applyLeaveNameFallbacks();
      },
      error: () => {}
    });
    this.attendanceService.getLeaveRequests().subscribe({
      next: l => {
        this.leaveRequests = l;
        this.applyLeaveNameFallbacks();
        this.loading = false;
      },
      error: () => { this.loading = false; }
    });
  }

  private refreshAttendanceSnapshot(showError = false): void {
    this.attendanceService.getTodayAttendance().subscribe({
      next: a => {
        this.todayAttendance = a;
        this.applyAttendanceNameFallbacks();
        this.loading = false;
      },
      error: () => {
        if (showError) {
          this.uiStore.error('Failed to load attendance');
        }
        this.loading = false;
      }
    });
  }

  loadReport() {
    this.attendanceService.getAttendanceReport(this.startDate, this.endDate, this.selectedStaffId || undefined).subscribe({
      next: r => {
        this.reportData = r;
        this.applyReportNameFallbacks();
      },
      error: () => this.uiStore.error('Failed to load report')
    });
  }

  openLeaveModal() {
    this.leaveForm = { staffId: '', leaveType: 'earned', startDate: '', endDate: '', isHalfDay: false, reason: '' };
    this.showLeaveModal = true;
  }

  submitLeave() {
    this.attendanceService.createLeaveRequest(this.leaveForm).subscribe({
      next: () => { this.uiStore.success('Leave request submitted'); this.showLeaveModal = false; this.loadData(); },
      error: () => this.uiStore.error('Failed to submit leave request')
    });
  }

  onLeaveStartDateChange(): void {
    if (this.leaveForm.isHalfDay) {
      this.leaveForm.endDate = this.leaveForm.startDate;
    }
  }

  onHalfDayToggle(): void {
    if (this.leaveForm.isHalfDay) {
      this.leaveForm.endDate = this.leaveForm.startDate;
    }
  }

  updateLeaveStatus(id: string, status: string) {
    this.attendanceService.updateLeaveStatus(id, status).subscribe({
      next: () => { this.uiStore.success('Leave ' + status); this.loadData(); },
      error: () => this.uiStore.error('Failed to update leave status')
    });
  }

  isStaffClockedIn(staffId: string): boolean {
    const normalizedStaffId = this.normalizeId(staffId);
    if (!normalizedStaffId) {
      return false;
    }

    return this.todayAttendance
      .filter(a => this.normalizeId(a.staffId) === normalizedStaffId)
      .some(a => this.isAttendanceInProgress(a));
  }

  getTodayAttendanceForStaff(staffId: string): Attendance | undefined {
    const normalizedStaffId = this.normalizeId(staffId);
    const records = this.todayAttendance.filter(a => this.normalizeId(a.staffId) === normalizedStaffId);
    if (records.length === 0) {
      return undefined;
    }

    return records.find(a => this.isAttendanceInProgress(a)) || records[0];
  }

  getShiftLabelForStaff(staff: any): string {
    const attendance = this.getTodayAttendanceForStaff(this.getStaffId(staff));
    if (attendance?.scheduledShiftLabel && attendance.scheduledShiftLabel.trim()) {
      return attendance.scheduledShiftLabel;
    }

    const shifts = Array.isArray(staff?.shifts) ? staff.shifts : [];
    const today = new Intl.DateTimeFormat('en-US', { weekday: 'long', timeZone: 'Asia/Kolkata' }).format(new Date());
    const todaysShifts = shifts.filter((s: any) => {
      const day = (s?.dayOfWeek || '').toString().trim().toLowerCase();
      const todayDay = today.toLowerCase();
      return day === todayDay && (s?.isActive !== false);
    });

    if (todaysShifts.length > 0) {
      return todaysShifts
        .map((s: any) => (s?.shiftName || '').toString().trim())
        .filter((name: string) => !!name)
        .join(' + ') || 'Assigned Shift';
    }

    return 'No shift configured';
  }

  getStaffId(staff: any): string {
    return staff?.id || staff?._id || '';
  }

  getStaffDisplayName(staff: any): string {
    const fromName = (staff?.name || '').toString().trim();
    if (fromName) {
      return fromName;
    }

    const first = (staff?.firstName || '').toString().trim();
    const last = (staff?.lastName || '').toString().trim();
    const combined = `${first} ${last}`.trim();
    if (combined) {
      return combined;
    }

    return staff?.employeeId || this.getStaffId(staff) || 'Unknown Staff';
  }

  getStaffRole(staff: any): string {
    return staff?.role || staff?.position || '-';
  }

  getStaffPhone(staff: any): string {
    return staff?.phone || staff?.phoneNumber || '-';
  }

  resolveAttendanceStaffName(record: Attendance): string {
    if (record.staffName && record.staffName.trim()) {
      return record.staffName;
    }

    return this.staffNameById[record.staffId] || record.staffId || 'Unknown Staff';
  }

  resolveLeaveStaffName(record: LeaveRequest): string {
    if (record.staffName && record.staffName.trim()) {
      return record.staffName;
    }

    return this.staffNameById[record.staffId] || record.staffId || 'Unknown Staff';
  }

  formatIstTime(value?: string): string {
    if (!value) {
      return '-';
    }

    return formatIstDate(value, {
      hour: '2-digit',
      minute: '2-digit',
      hour12: true
    });
  }

  private normalizeId(value?: string): string {
    return (value || '').trim().toLowerCase();
  }

  private isAttendanceInProgress(record: Attendance): boolean {
    const hasOpenSession = (record.sessions || []).some(session => {
      const status = (session.status || '').toLowerCase();
      return (status === 'in-progress') || (!!session.clockIn && !session.clockOut);
    });

    return hasOpenSession || (!!record.clockIn && !record.clockOut);
  }

  private rebuildStaffNameIndex(): void {
    const map: Record<string, string> = {};
    for (const staff of this.staffList || []) {
      const id = this.getStaffId(staff);
      if (!id) {
        continue;
      }

      map[id] = this.getStaffDisplayName(staff);
    }

    this.staffNameById = map;
  }

  private applyAttendanceNameFallbacks(): void {
    if (!this.todayAttendance?.length) {
      return;
    }

    this.todayAttendance = this.todayAttendance.map(a => ({
      ...a,
      staffName: a.staffName && a.staffName.trim() ? a.staffName : this.staffNameById[a.staffId] || a.staffName
    }));
  }

  private applyLeaveNameFallbacks(): void {
    if (!this.leaveRequests?.length) {
      return;
    }

    this.leaveRequests = this.leaveRequests.map(l => ({
      ...l,
      staffName: l.staffName && l.staffName.trim() ? l.staffName : this.staffNameById[l.staffId] || l.staffName
    }));
  }

  private applyReportNameFallbacks(): void {
    if (!this.reportData?.length) {
      return;
    }

    this.reportData = this.reportData.map(r => ({
      ...r,
      staffName: r.staffName && r.staffName.trim() ? r.staffName : this.staffNameById[r.staffId] || r.staffName
    }));
  }

  trackById(_: number, item: any) { return item?.id || item?._id || item?.staffId; }
}
