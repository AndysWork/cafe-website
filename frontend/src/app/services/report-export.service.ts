import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { handleServiceError } from '../utils/error-handler';

@Injectable({ providedIn: 'root' })
export class ReportExportService {
  private http = inject(HttpClient);
  private apiUrl = environment.apiUrl;

  exportSalesReport(startDate: string, endDate: string, format: 'csv' | 'excel' = 'excel'): Observable<Blob> {
    return this.http.get(`${this.apiUrl}/reports/sales/export?startDate=${startDate}&endDate=${endDate}&format=${format}`, {
      responseType: 'blob'
    }).pipe(
      catchError(handleServiceError('ReportExportService.exportSalesReport'))
    );
  }

  exportExpenseReport(startDate: string, endDate: string, format: 'csv' | 'excel' = 'excel'): Observable<Blob> {
    return this.http.get(`${this.apiUrl}/reports/expenses/export?startDate=${startDate}&endDate=${endDate}&format=${format}`, {
      responseType: 'blob'
    }).pipe(
      catchError(handleServiceError('ReportExportService.exportExpenseReport'))
    );
  }

  exportOrdersReport(startDate: string, endDate: string, format: 'csv' | 'excel' = 'excel'): Observable<Blob> {
    return this.http.get(`${this.apiUrl}/reports/orders/export?startDate=${startDate}&endDate=${endDate}&format=${format}`, {
      responseType: 'blob'
    }).pipe(
      catchError(handleServiceError('ReportExportService.exportOrdersReport'))
    );
  }

  exportProfitLossReport(startDate: string, endDate: string): Observable<Blob> {
    return this.http.get(`${this.apiUrl}/reports/profit-loss/export?startDate=${startDate}&endDate=${endDate}`, {
      responseType: 'blob'
    }).pipe(
      catchError(handleServiceError('ReportExportService.exportProfitLossReport'))
    );
  }

  downloadBlob(blob: Blob, filename: string): void {
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    a.click();
    window.URL.revokeObjectURL(url);
  }
}
