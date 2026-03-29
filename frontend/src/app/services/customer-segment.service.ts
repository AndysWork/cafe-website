import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { handleServiceError } from '../utils/error-handler';

export interface CustomerSegment {
  id?: string;
  userId: string;
  username?: string;
  email?: string;
  segment: 'new' | 'regular' | 'vip' | 'dormant' | 'at-risk';
  totalOrders: number;
  totalSpent: number;
  lastOrderDate?: string;
  averageOrderValue?: number;
}

export interface SegmentSummary {
  segment: string;
  count: number;
  totalRevenue: number;
  averageSpent: number;
}

@Injectable({ providedIn: 'root' })
export class CustomerSegmentService {
  private http = inject(HttpClient);
  private apiUrl = environment.apiUrl;

  getCustomerSegments(segment?: string): Observable<CustomerSegment[]> {
    const params = segment ? `?segment=${segment}` : '';
    return this.http.get<CustomerSegment[]>(`${this.apiUrl}/manage/customer-segments${params}`).pipe(
      catchError(handleServiceError('CustomerSegmentService.getCustomerSegments'))
    );
  }

  getSegmentSummary(): Observable<SegmentSummary[]> {
    return this.http.get<SegmentSummary[]>(`${this.apiUrl}/manage/customer-segments/summary`).pipe(
      catchError(handleServiceError('CustomerSegmentService.getSegmentSummary'))
    );
  }

  refreshSegments(): Observable<{ message: string; segmentsUpdated: number }> {
    return this.http.post<{ message: string; segmentsUpdated: number }>(`${this.apiUrl}/manage/customer-segments/refresh`, {}).pipe(
      catchError(handleServiceError('CustomerSegmentService.refreshSegments'))
    );
  }
}
