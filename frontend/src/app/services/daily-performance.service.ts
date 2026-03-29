import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export interface PerformanceShift {
  id?: string;
  shiftName: string;
  inTime: string;
  outTime: string;
  workingHours?: number;
  totalOrdersPrepared: number;
  goodOrdersCount: number;
  badOrdersCount: number;
  refundAmountRecovery: number;
  notes?: string;
}

export interface DailyPerformanceEntry {
  id?: string;
  outletId: string;
  staffId: string;
  staffName?: string;
  date: string; // Format: YYYY-MM-DD
  inTime: string; // Format: HH:mm (legacy)
  outTime: string; // Format: HH:mm (legacy)
  totalOrdersPrepared: number; // legacy
  goodOrdersCount: number; // legacy
  badOrdersCount: number; // legacy
  refundAmountRecovery: number; // legacy
  workingHours?: number; // legacy
  notes?: string; // legacy
  leaveHours?: number; // Leave hours for the day
  shifts?: PerformanceShift[]; // Multiple shifts per day
  createdAt?: string;
  updatedAt?: string;
}

export interface UpsertDailyPerformanceRequest {
  staffId: string;
  date: string;
  inTime: string;
  outTime: string;
  totalOrdersPrepared: number;
  goodOrdersCount: number;
  badOrdersCount: number;
  refundAmountRecovery: number;
  notes?: string;
  leaveHours?: number;
  shifts?: PerformanceShift[];
}

export interface BulkDailyPerformanceRequest {
  date: string;
  entries: {
    staffId: string;
    inTime: string;
    outTime: string;
    totalOrdersPrepared: number;
    goodOrdersCount: number;
    badOrdersCount: number;
    refundAmountRecovery: number;
    notes?: string;
    leaveHours?: number;
    shifts?: PerformanceShift[];
  }[];
}

@Injectable({
  providedIn: 'root',
})
export class DailyPerformanceService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/dailyperformance`;

  private getHeaders(): HttpHeaders {
    const token = localStorage.getItem('authToken');
    return new HttpHeaders({
      'Content-Type': 'application/json',
      Authorization: `Bearer ${token}`,
    });
  }

  getDailyPerformanceByDate(date: string): Observable<DailyPerformanceEntry[]> {
    return this.http.get<DailyPerformanceEntry[]>(
      `${this.apiUrl}/date/${date}`,
      { headers: this.getHeaders() },
    );
  }

  getDailyPerformanceByStaff(
    staffId: string,
    startDate?: string,
    endDate?: string,
  ): Observable<DailyPerformanceEntry[]> {
    let url = `${this.apiUrl}/staff/${staffId}`;
    const params: string[] = [];
    if (startDate) params.push(`startDate=${startDate}`);
    if (endDate) params.push(`endDate=${endDate}`);
    if (params.length > 0) url += `?${params.join('&')}`;

    return this.http.get<DailyPerformanceEntry[]>(url, {
      headers: this.getHeaders(),
    });
  }

  getDailyPerformanceByDateRange(
    startDate: string,
    endDate: string,
  ): Observable<DailyPerformanceEntry[]> {
    return this.http.get<DailyPerformanceEntry[]>(
      `${this.apiUrl}/range?startDate=${startDate}&endDate=${endDate}`,
      { headers: this.getHeaders() },
    );
  }

  upsertDailyPerformance(
    request: UpsertDailyPerformanceRequest,
  ): Observable<DailyPerformanceEntry> {
    return this.http.post<DailyPerformanceEntry>(this.apiUrl, request, {
      headers: this.getHeaders(),
    });
  }

  bulkUpsertDailyPerformance(
    request: BulkDailyPerformanceRequest,
  ): Observable<DailyPerformanceEntry[]> {
    return this.http.post<DailyPerformanceEntry[]>(
      `${this.apiUrl}/bulk`,
      request,
      { headers: this.getHeaders() },
    );
  }

  deleteDailyPerformance(id: string): Observable<any> {
    return this.http.delete(`${this.apiUrl}/${id}`, {
      headers: this.getHeaders(),
    });
  }

  // Shift management methods
  getPerformanceShifts(entryId: string): Observable<PerformanceShift[]> {
    return this.http.get<PerformanceShift[]>(
      `${this.apiUrl}/${entryId}/shifts`,
      { headers: this.getHeaders() },
    );
  }

  addPerformanceShift(
    entryId: string,
    shift: PerformanceShift,
  ): Observable<PerformanceShift> {
    return this.http.post<PerformanceShift>(
      `${this.apiUrl}/${entryId}/shifts`,
      shift,
      { headers: this.getHeaders() },
    );
  }

  updatePerformanceShift(
    entryId: string,
    shiftId: string,
    shift: PerformanceShift,
  ): Observable<PerformanceShift> {
    return this.http.put<PerformanceShift>(
      `${this.apiUrl}/${entryId}/shifts/${shiftId}`,
      shift,
      { headers: this.getHeaders() },
    );
  }

  deletePerformanceShift(entryId: string, shiftId: string): Observable<any> {
    return this.http.delete(`${this.apiUrl}/${entryId}/shifts/${shiftId}`, {
      headers: this.getHeaders(),
    });
  }
}
