import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
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

interface ApiResponse<T> {
  success: boolean;
  data: T;
  error?: string;
}

@Injectable({
  providedIn: 'root'
})
export class StaffPerformanceService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/staff-performance`;

  private getHeaders(): HttpHeaders {
    const token = localStorage.getItem('authToken');
    return new HttpHeaders({
      'Content-Type': 'application/json',
      'Authorization': `Bearer ${token}`
    });
  }

  private mapRecord(record: any): StaffPerformanceRecord {
    return {
      ...record,
      netBonusAmount: record.netAmount !== undefined ? record.netAmount : (record.netBonusAmount || 0),
      bonusBreakdown: (record.bonusBreakdown || []).map((detail: any) => ({
        ...detail,
        calculatedAmount: detail.amount !== undefined ? detail.amount : (detail.calculatedAmount || 0),
        calculationType: detail.calculationType || detail.ruleType || '',
        metricValue: detail.metricValue || 0,
        description: detail.description || detail.calculationDetails || ''
      }))
    };
  }

  getStaffPerformanceRecords(staffId: string, period?: string): Observable<StaffPerformanceRecord[]> {
    let url = `${this.apiUrl}?staffId=${staffId}`;
    if (period) {
      url += `&period=${period}`;
    }
    return this.http.get<ApiResponse<any[]>>(url, { headers: this.getHeaders() }).pipe(
      map(response => (response.data || []).map(record => this.mapRecord(record)))
    );
  }

  getOutletPerformanceRecords(period?: string): Observable<StaffPerformanceRecord[]> {
    let url = `${this.apiUrl}/outlet`;
    if (period) {
      url += `?period=${period}`;
    }
    return this.http.get<ApiResponse<any[]>>(url, { headers: this.getHeaders() }).pipe(
      map(response => (response.data || []).map(record => this.mapRecord(record)))
    );
  }

  upsertStaffPerformanceRecord(request: UpsertStaffPerformanceRequest): Observable<StaffPerformanceRecord> {
    return this.http.post<ApiResponse<any>>(this.apiUrl, request, { headers: this.getHeaders() }).pipe(
      map(response => this.mapRecord(response.data))
    );
  }

  calculateStaffBonus(recordId: string): Observable<StaffPerformanceRecord> {
    return this.http.post<ApiResponse<any>>(
      `${this.apiUrl}/${recordId}/calculate`,
      {},
      { headers: this.getHeaders() }
    ).pipe(
      map(response => this.mapRecord(response.data))
    );
  }
}
