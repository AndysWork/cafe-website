import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

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
  private apiUrl = `${environment.apiUrl}/sales`;

  // Get all sales
  getAllSales(): Observable<Sales[]> {
    return this.http.get<Sales[]>(this.apiUrl);
  }

  // Get sales by date range
  getSalesByDateRange(startDate: string, endDate: string): Observable<Sales[]> {
    return this.http.get<Sales[]>(`${this.apiUrl}/range?startDate=${startDate}&endDate=${endDate}`);
  }

  // Get sales summary by date
  getSalesSummary(date: string): Observable<SalesSummary> {
    return this.http.get<SalesSummary>(`${this.apiUrl}/summary?date=${date}`);
  }

  // Create new sales
  createSales(sales: CreateSalesRequest): Observable<Sales> {
    return this.http.post<Sales>(this.apiUrl, sales);
  }

  // Update sales
  updateSales(id: string, sales: CreateSalesRequest): Observable<Sales> {
    return this.http.put<Sales>(`${this.apiUrl}/${id}`, sales);
  }

  // Delete sales
  deleteSales(id: string): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`${this.apiUrl}/${id}`);
  }

  // Upload sales Excel
  uploadSalesExcel(file: File): Observable<any> {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post(`${this.apiUrl}/upload`, formData);
  }
}
