import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  Staff,
  StaffStatistics,
  UpdateSalaryRequest,
  UpdatePerformanceRatingRequest,
  UpdateLeaveBalancesRequest
} from '../models/staff.model';

interface ApiResponse<T> {
  success: boolean;
  data: T;
  error?: string;
}

@Injectable({
  providedIn: 'root'
})
export class StaffService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/staff`;

  /**
   * Get all staff members
   */
  getAllStaff(activeOnly: boolean = false): Observable<Staff[]> {
    const url = activeOnly ? `${this.apiUrl}?activeOnly=true` : this.apiUrl;
    return this.http.get<ApiResponse<Staff[]>>(url).pipe(
      map(response => response.data)
    );
  }

  /**
   * Get staff member by ID
   */
  getStaffById(staffId: string): Observable<Staff> {
    return this.http.get<ApiResponse<Staff>>(`${this.apiUrl}/${staffId}`).pipe(
      map(response => response.data)
    );
  }

  /**
   * Get staff by outlet
   */
  getStaffByOutlet(outletId: string): Observable<Staff[]> {
    return this.http.get<ApiResponse<Staff[]>>(`${this.apiUrl}/outlet/${outletId}`).pipe(
      map(response => response.data)
    );
  }

  /**
   * Search staff members
   */
  searchStaff(searchTerm: string): Observable<Staff[]> {
    return this.http.get<ApiResponse<Staff[]>>(`${this.apiUrl}/search?q=${encodeURIComponent(searchTerm)}`).pipe(
      map(response => response.data)
    );
  }

  /**
   * Get staff statistics
   */
  getStatistics(): Observable<StaffStatistics> {
    return this.http.get<ApiResponse<StaffStatistics>>(`${this.apiUrl}/statistics`).pipe(
      map(response => response.data)
    );
  }

  /**
   * Create new staff member
   */
  createStaff(staff: Partial<Staff>): Observable<Staff> {
    return this.http.post<ApiResponse<Staff>>(this.apiUrl, staff).pipe(
      map(response => response.data)
    );
  }

  /**
   * Update staff member
   */
  updateStaff(staffId: string, staff: Partial<Staff>): Observable<Staff> {
    return this.http.put<ApiResponse<Staff>>(`${this.apiUrl}/${staffId}`, staff).pipe(
      map(response => response.data)
    );
  }

  /**
   * Deactivate staff member
   */
  deactivateStaff(staffId: string): Observable<any> {
    return this.http.post<ApiResponse<any>>(`${this.apiUrl}/${staffId}/deactivate`, {}).pipe(
      map(response => response.data)
    );
  }

  /**
   * Activate staff member
   */
  activateStaff(staffId: string): Observable<any> {
    return this.http.post<ApiResponse<any>>(`${this.apiUrl}/${staffId}/activate`, {}).pipe(
      map(response => response.data)
    );
  }

  /**
   * Delete staff member permanently
   */
  deleteStaff(staffId: string): Observable<any> {
    return this.http.delete<ApiResponse<any>>(`${this.apiUrl}/${staffId}`).pipe(
      map(response => response.data)
    );
  }

  /**
   * Update staff salary
   */
  updateSalary(staffId: string, salary: number): Observable<any> {
    const request: UpdateSalaryRequest = { salary };
    return this.http.patch<ApiResponse<any>>(`${this.apiUrl}/${staffId}/salary`, request).pipe(
      map(response => response.data)
    );
  }

  /**
   * Update staff performance rating
   */
  updatePerformanceRating(staffId: string, rating: number): Observable<any> {
    const request: UpdatePerformanceRatingRequest = { rating };
    return this.http.patch<ApiResponse<any>>(`${this.apiUrl}/${staffId}/performance`, request).pipe(
      map(response => response.data)
    );
  }

  /**
   * Update staff leave balances
   */
  updateLeaveBalances(staffId: string, annualLeave: number, sickLeave: number, casualLeave: number): Observable<any> {
    const request: UpdateLeaveBalancesRequest = { annualLeave, sickLeave, casualLeave };
    return this.http.patch<ApiResponse<any>>(`${this.apiUrl}/${staffId}/leave-balances`, request).pipe(
      map(response => response.data)
    );
  }
}
