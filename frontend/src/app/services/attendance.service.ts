import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { handleServiceError } from '../utils/error-handler';

export interface Attendance {
  id?: string;
  staffId: string;
  staffName?: string;
  outletId?: string;
  clockIn?: string;
  clockOut?: string;
  hoursWorked?: number;
  scheduledHours?: number;
  overtimeHours?: number;
  undertimeHours?: number;
  scheduledShiftLabel?: string;
  sessions?: AttendanceSession[];
  status: 'present' | 'absent' | 'late' | 'half-day' | 'leave';
  date?: string;
}

export interface AttendanceSession {
  sessionId: string;
  shiftKey: string;
  shiftName: string;
  shiftStartTime?: string;
  shiftEndTime?: string;
  clockIn?: string;
  clockOut?: string;
  scheduledHours: number;
  hoursWorked: number;
  overtimeHours: number;
  undertimeHours: number;
  status: 'pending' | 'in-progress' | 'completed';
}

export interface MyAttendanceResponse {
  attendance: Attendance | null;
  schedule: {
    shiftLabel: string;
    scheduledHours: number;
    shiftNames?: string[];
    shifts?: Array<{
      name: string;
      startTime: string;
      endTime: string;
      breakDuration: number;
      scheduledHours: number;
    }>;
    currentShiftName?: string;
    currentShiftKey?: string;
    currentShiftStart?: string;
    currentShiftEnd?: string;
    currentShiftScheduledHours?: number;
    canShiftIn?: boolean;
    canShiftOut?: boolean;
    actionMessage?: string;
  };
}

export interface MyMonthlyLeaveTypeBalance {
  available?: number;
  annualBalance?: number;
  approvedThisMonth: number;
  pendingThisMonth: number;
  appliedThisMonth?: number;
  remainingAfterMonthUsage?: number;
}

export interface MyMonthlyLeaveBalanceResponse {
  period: string;
  from: string;
  to: string;
  balances: {
    sick: MyMonthlyLeaveTypeBalance;
    casual: MyMonthlyLeaveTypeBalance;
    earned: MyMonthlyLeaveTypeBalance;
    unpaid: MyMonthlyLeaveTypeBalance;
  };
  totals: {
    approvedThisMonth: number;
    pendingThisMonth: number;
  };
}

export interface MyShiftActionResponse {
  message: string;
  attendance: Attendance;
  schedule?: {
    shiftLabel: string;
    scheduledHours: number;
    shiftNames?: string[];
    shifts?: Array<{
      name: string;
      startTime: string;
      endTime: string;
      breakDuration: number;
      scheduledHours: number;
    }>;
    currentShiftName?: string;
    currentShiftKey?: string;
    currentShiftStart?: string;
    currentShiftEnd?: string;
    currentShiftScheduledHours?: number;
    canShiftIn?: boolean;
    canShiftOut?: boolean;
    actionMessage?: string;
  };
  summary?: {
    hoursWorked: number;
    scheduledHours: number;
    overtimeHours: number;
    undertimeHours: number;
    shiftLabel?: string;
  };
}

export interface LeaveRequest {
  id?: string;
  staffId: string;
  staffName?: string;
  leaveType: 'casual' | 'sick' | 'earned' | 'unpaid';
  startDate: string;
  endDate: string;
  reason: string;
  status: 'pending' | 'approved' | 'rejected';
  outletId?: string;
  createdAt?: string;
}

export interface CreateLeaveRequest {
  staffId: string;
  leaveType: string;
  startDate: string;
  endDate: string;
  reason: string;
}

export interface CreateMyLeaveRequest {
  leaveType: 'casual' | 'sick' | 'earned' | 'unpaid';
  startDate: string;
  endDate: string;
  reason: string;
}

export interface MyPayslipHistoryEntry {
  period: string;
  workedHours: number;
  workedDays: number;
  baseEarnings: number;
  bonusAmount: number;
  totalEarnings: number;
}

export interface MyPayslipResponse {
  staff: {
    id?: string;
    name: string;
    position: string;
    salary: number;
    salaryType: string;
    employeeId?: string;
  };
  current: {
    period: string;
    from: string;
    to: string;
    isEstimated: boolean;
    workedHours: number;
    workedDays: number;
    baseEarnings: number;
    bonusAmount: number;
    estimatedTotalEarnings: number;
    eligibleForBonus: boolean;
    bonusConfigurations: string[];
  };
  history: MyPayslipHistoryEntry[];
}

@Injectable({ providedIn: 'root' })
export class AttendanceService {
  private http = inject(HttpClient);
  private apiUrl = environment.apiUrl;

  private withDeviceTimeHeaders() {
    const headers = new HttpHeaders({
      'x-client-epoch-ms': Date.now().toString(),
      'x-client-timezone-offset-minutes': new Date().getTimezoneOffset().toString(),
      'x-client-timezone': Intl.DateTimeFormat().resolvedOptions().timeZone || 'unknown'
    });

    return { headers };
  }

  clockIn(staffId: string): Observable<Attendance> {
    return this.http.post<Attendance>(`${this.apiUrl}/attendance/clock-in`, { staffId, action: 'clock-in' }, this.withDeviceTimeHeaders()).pipe(
      catchError(handleServiceError('AttendanceService.clockIn'))
    );
  }

  clockOut(staffId: string): Observable<Attendance> {
    return this.http.post<Attendance>(`${this.apiUrl}/attendance/clock-out`, { staffId, action: 'clock-out' }, this.withDeviceTimeHeaders()).pipe(
      catchError(handleServiceError('AttendanceService.clockOut'))
    );
  }

  getTodayAttendance(): Observable<Attendance[]> {
    return this.http.get<Attendance[]>(`${this.apiUrl}/attendance/today`, this.withDeviceTimeHeaders()).pipe(
      catchError(handleServiceError('AttendanceService.getTodayAttendance'))
    );
  }

  getAttendanceReport(startDate: string, endDate: string, staffId?: string): Observable<Attendance[]> {
    let url = `${this.apiUrl}/attendance/report?startDate=${startDate}&endDate=${endDate}`;
    if (staffId) url += `&staffId=${staffId}`;
    return this.http.get<Attendance[]>(url, this.withDeviceTimeHeaders()).pipe(
      catchError(handleServiceError('AttendanceService.getAttendanceReport'))
    );
  }

  createLeaveRequest(request: CreateLeaveRequest): Observable<LeaveRequest> {
    return this.http.post<LeaveRequest>(`${this.apiUrl}/attendance/leave`, request, this.withDeviceTimeHeaders()).pipe(
      catchError(handleServiceError('AttendanceService.createLeaveRequest'))
    );
  }

  getLeaveRequests(status?: string): Observable<LeaveRequest[]> {
    const params = status ? `?status=${status}` : '';
    return this.http.get<LeaveRequest[]>(`${this.apiUrl}/attendance/leave${params}`, this.withDeviceTimeHeaders()).pipe(
      catchError(handleServiceError('AttendanceService.getLeaveRequests'))
    );
  }

  updateLeaveStatus(id: string, status: string): Observable<{ message: string }> {
    return this.http.put<{ message: string }>(`${this.apiUrl}/attendance/leave/${id}/status`, { status }, this.withDeviceTimeHeaders()).pipe(
      catchError(handleServiceError('AttendanceService.updateLeaveStatus'))
    );
  }

  createMyLeaveRequest(request: CreateMyLeaveRequest): Observable<LeaveRequest> {
    return this.http.post<LeaveRequest>(`${this.apiUrl}/attendance/my/leave`, request, this.withDeviceTimeHeaders()).pipe(
      catchError(handleServiceError('AttendanceService.createMyLeaveRequest'))
    );
  }

  getMyLeaveRequests(status?: string): Observable<LeaveRequest[]> {
    const params = status ? `?status=${status}` : '';
    return this.http.get<LeaveRequest[]>(`${this.apiUrl}/attendance/my/leave${params}`, this.withDeviceTimeHeaders()).pipe(
      catchError(handleServiceError('AttendanceService.getMyLeaveRequests'))
    );
  }

  getMyMonthlyLeaveBalance(month?: string): Observable<MyMonthlyLeaveBalanceResponse> {
    const query = month ? `?month=${encodeURIComponent(month)}` : '';
    return this.http.get<MyMonthlyLeaveBalanceResponse>(`${this.apiUrl}/attendance/my/leave-balance${query}`, this.withDeviceTimeHeaders()).pipe(
      catchError(handleServiceError('AttendanceService.getMyMonthlyLeaveBalance'))
    );
  }

  getMyTodayAttendance(): Observable<MyAttendanceResponse> {
    return this.http.get<MyAttendanceResponse>(`${this.apiUrl}/attendance/my/today`, this.withDeviceTimeHeaders()).pipe(
      catchError(handleServiceError('AttendanceService.getMyTodayAttendance'))
    );
  }

  startMyShift(): Observable<MyShiftActionResponse> {
    return this.http.post<MyShiftActionResponse>(`${this.apiUrl}/attendance/my/shift-start`, {}, this.withDeviceTimeHeaders()).pipe(
      catchError(handleServiceError('AttendanceService.startMyShift'))
    );
  }

  endMyShift(): Observable<MyShiftActionResponse> {
    return this.http.post<MyShiftActionResponse>(`${this.apiUrl}/attendance/my/shift-end`, {}, this.withDeviceTimeHeaders()).pipe(
      catchError(handleServiceError('AttendanceService.endMyShift'))
    );
  }

  getMyPayslip(month?: string): Observable<MyPayslipResponse> {
    const query = month ? `?month=${encodeURIComponent(month)}` : '';
    return this.http.get<MyPayslipResponse>(`${this.apiUrl}/attendance/my/payslip${query}`, this.withDeviceTimeHeaders()).pipe(
      catchError(handleServiceError('AttendanceService.getMyPayslip'))
    );
  }
}
