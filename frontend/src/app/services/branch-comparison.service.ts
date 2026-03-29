import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { handleServiceError } from '../utils/error-handler';

export interface BranchMetrics {
  outletId: string;
  outletName: string;
  totalSales: number;
  totalOrders: number;
  totalExpenses: number;
  netProfit: number;
  averageOrderValue: number;
}

export interface BranchComparison {
  period: string;
  branches: BranchMetrics[];
}

@Injectable({ providedIn: 'root' })
export class BranchComparisonService {
  private http = inject(HttpClient);
  private apiUrl = environment.apiUrl;

  compareBranches(startDate: string, endDate: string, outletIds?: string[]): Observable<BranchComparison> {
    let url = `${this.apiUrl}/manage/branch-comparison?startDate=${startDate}&endDate=${endDate}`;
    if (outletIds && outletIds.length > 0) {
      url += `&outletIds=${outletIds.join(',')}`;
    }
    return this.http.get<BranchComparison>(url).pipe(
      catchError(handleServiceError('BranchComparisonService.compareBranches'))
    );
  }
}
