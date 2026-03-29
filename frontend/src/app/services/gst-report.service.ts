import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { handleServiceError } from '../utils/error-handler';

export interface GstSummary {
  period: string;
  totalSales: number;
  totalTaxableAmount: number;
  cgst: number;
  sgst: number;
  igst: number;
  totalGst: number;
  totalWithGst: number;
}

@Injectable({ providedIn: 'root' })
export class GstReportService {
  private http = inject(HttpClient);
  private apiUrl = environment.apiUrl;

  getGstSummary(startDate: string, endDate: string): Observable<GstSummary> {
    return this.http.get<GstSummary>(`${this.apiUrl}/reports/gst/summary?startDate=${startDate}&endDate=${endDate}`).pipe(
      catchError(handleServiceError('GstReportService.getGstSummary'))
    );
  }

  exportGstr1(startDate: string, endDate: string): Observable<Blob> {
    return this.http.get(`${this.apiUrl}/reports/gst/gstr1/export?startDate=${startDate}&endDate=${endDate}`, {
      responseType: 'blob'
    }).pipe(
      catchError(handleServiceError('GstReportService.exportGstr1'))
    );
  }
}
