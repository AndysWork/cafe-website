import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export interface BonusCalculationDetail {
  ruleType: string;
  ruleName: string;
  isBonus: boolean;
  calculationType: string;
  metricValue: number;
  calculatedAmount: number;
  description: string;
}

export interface StaffPerformanceRecord {
  id?: string;
  outletId: string;
  staffId: string;
  staffName?: string;
  period: string; // Format: YYYY-MM
  scheduledHours: number;
  actualHours: number;
  overtimeHours?: number;
  undertimeHours?: number;
  snacksPrepared: number;
  badOrders: number;
  goodRatings: number;
  missingItemRefunds: number;
  totalBonus: number;
  totalDeductions: number;
  netBonusAmount: number;
  bonusBreakdown: BonusCalculationDetail[];
  notes?: string;
  createdAt?: string;
  updatedAt?: string;
}

export interface UpsertStaffPerformanceRequest {
  staffId: string;
  period: string;
  scheduledHours: number;
  actualHours: number;
  snacksPrepared: number;
  badOrders: number;
  goodRatings: number;
  missingItemRefunds: number;
  notes?: string;
}

@Injectable({
  providedIn: 'root'
})
export class StaffPerformanceService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/api/staffperformance`;

  private getHeaders(): HttpHeaders {
    const token = localStorage.getItem('token');
    return new HttpHeaders({
      'Content-Type': 'application/json',
      'Authorization': `Bearer ${token}`
    });
  }

  getStaffPerformanceRecords(staffId: string, period?: string): Observable<StaffPerformanceRecord[]> {
    let url = `${this.apiUrl}/staff/${staffId}`;
    if (period) {
      url += `?period=${period}`;
    }
    return this.http.get<StaffPerformanceRecord[]>(url, { headers: this.getHeaders() });
  }

  getOutletPerformanceRecords(period?: string): Observable<StaffPerformanceRecord[]> {
    let url = `${this.apiUrl}/outlet`;
    if (period) {
      url += `?period=${period}`;
    }
    return this.http.get<StaffPerformanceRecord[]>(url, { headers: this.getHeaders() });
  }

  upsertStaffPerformanceRecord(request: UpsertStaffPerformanceRequest): Observable<StaffPerformanceRecord> {
    return this.http.post<StaffPerformanceRecord>(this.apiUrl, request, { headers: this.getHeaders() });
  }

  calculateStaffBonus(staffId: string, period: string): Observable<StaffPerformanceRecord> {
    return this.http.post<StaffPerformanceRecord>(
      `${this.apiUrl}/calculate-bonus`,
      { staffId, period },
      { headers: this.getHeaders() }
    );
  }
}
