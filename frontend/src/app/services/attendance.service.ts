import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
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
  status: 'present' | 'absent' | 'late' | 'half-day' | 'leave';
  date?: string;
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

@Injectable({ providedIn: 'root' })
export class AttendanceService {
  private http = inject(HttpClient);
  private apiUrl = environment.apiUrl;

  clockIn(staffId: string): Observable<Attendance> {
    return this.http.post<Attendance>(`${this.apiUrl}/attendance/clock-in`, { staffId, action: 'clock-in' }).pipe(
      catchError(handleServiceError('AttendanceService.clockIn'))
    );
  }

  clockOut(staffId: string): Observable<Attendance> {
    return this.http.post<Attendance>(`${this.apiUrl}/attendance/clock-out`, { staffId, action: 'clock-out' }).pipe(
      catchError(handleServiceError('AttendanceService.clockOut'))
    );
  }

  getTodayAttendance(): Observable<Attendance[]> {
    return this.http.get<Attendance[]>(`${this.apiUrl}/attendance/today`).pipe(
      catchError(handleServiceError('AttendanceService.getTodayAttendance'))
    );
  }

  getAttendanceReport(startDate: string, endDate: string, staffId?: string): Observable<Attendance[]> {
    let url = `${this.apiUrl}/attendance/report?startDate=${startDate}&endDate=${endDate}`;
    if (staffId) url += `&staffId=${staffId}`;
    return this.http.get<Attendance[]>(url).pipe(
      catchError(handleServiceError('AttendanceService.getAttendanceReport'))
    );
  }

  createLeaveRequest(request: CreateLeaveRequest): Observable<LeaveRequest> {
    return this.http.post<LeaveRequest>(`${this.apiUrl}/attendance/leave`, request).pipe(
      catchError(handleServiceError('AttendanceService.createLeaveRequest'))
    );
  }

  getLeaveRequests(status?: string): Observable<LeaveRequest[]> {
    const params = status ? `?status=${status}` : '';
    return this.http.get<LeaveRequest[]>(`${this.apiUrl}/attendance/leave${params}`).pipe(
      catchError(handleServiceError('AttendanceService.getLeaveRequests'))
    );
  }

  updateLeaveStatus(id: string, status: string): Observable<{ message: string }> {
    return this.http.put<{ message: string }>(`${this.apiUrl}/attendance/leave/${id}/status`, { status }).pipe(
      catchError(handleServiceError('AttendanceService.updateLeaveStatus'))
    );
  }
}
