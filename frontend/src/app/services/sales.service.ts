import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { handleServiceError } from '../utils/error-handler';
import { OutletService } from './outlet.service';

export interface SalesItem {
  menuItemId?: string;
  itemName: string;
  quantity: number;
  unitPrice: number;
  totalPrice: number;
}

export interface Sales {
  id: string;
  date: string;
  items: SalesItem[];
  totalAmount: number;
  paymentMethod: string;
  notes?: string;
  recordedBy: string;
  createdAt: string;
  updatedAt: string;
}

export interface CreateSalesRequest {
  date: string;
  items: {
    menuItemId?: string;
    itemName: string;
    quantity: number;
    unitPrice: number;
  }[];
  paymentMethod: string;
  notes?: string;
}

export interface SalesSummary {
  date: string;
  totalSales: number;
  totalTransactions: number;
  paymentMethodBreakdown: { [key: string]: number };
}

@Injectable({
  providedIn: 'root'
})
export class SalesService {
  private http = inject(HttpClient);
  private outletService = inject(OutletService);
  private apiUrl = `${environment.apiUrl}/sales`;

  // Get all sales
  getAllSales(): Observable<Sales[]> {
    return this.http.get<Sales[]>(this.apiUrl).pipe(
      catchError(handleServiceError('SalesService.getAllSales'))
    );
  }

  // Get sales by date range
  getSalesByDateRange(startDate: string, endDate: string): Observable<Sales[]> {
    return this.http.get<Sales[]>(`${this.apiUrl}/range?startDate=${startDate}&endDate=${endDate}`).pipe(
      catchError(handleServiceError('SalesService.getSalesByDateRange'))
    );
  }

  // Get sales summary by date
  getSalesSummary(date: string): Observable<SalesSummary> {
    return this.http.get<SalesSummary>(`${this.apiUrl}/summary?date=${date}`).pipe(
      catchError(handleServiceError('SalesService.getSalesSummary'))
    );
  }

  // Create new sales
  createSales(sales: CreateSalesRequest): Observable<Sales> {
    return this.http.post<Sales>(this.apiUrl, sales).pipe(
      catchError(handleServiceError('SalesService.createSales'))
    );
  }

  // Update sales
  updateSales(id: string, sales: CreateSalesRequest): Observable<Sales> {
    return this.http.put<Sales>(`${this.apiUrl}/${id}`, sales).pipe(
      catchError(handleServiceError('SalesService.updateSales'))
    );
  }

  // Delete sales
  deleteSales(id: string): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`${this.apiUrl}/${id}`).pipe(
      catchError(handleServiceError('SalesService.deleteSales'))
    );
  }

  // Upload sales Excel
  uploadSalesExcel(file: File): Observable<any> {
    const formData = new FormData();
    formData.append('file', file);

    const outletId = this.outletService.getSelectedOutletId();
    let headers = new HttpHeaders();
    if (outletId) {
      headers = headers.set('X-Outlet-Id', outletId);
    }

    return this.http.post(`${this.apiUrl}/upload`, formData, { headers }).pipe(
      catchError(handleServiceError('SalesService.uploadSalesExcel'))
    );
  }
}
