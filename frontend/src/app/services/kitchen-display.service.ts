import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { handleServiceError } from '../utils/error-handler';

export interface KitchenOrder {
  id: string;
  username: string;
  items: { name: string; quantity: number; price: number; total: number; categoryName?: string }[];
  status: string;
  orderType?: string;
  tableNumber?: number;
  notes?: string;
  createdAt: string;
  updatedAt: string;
}

export interface KitchenStats {
  pendingOrders: number;
  preparingOrders: number;
  readyOrders: number;
  avgPrepTime: number;
  completedToday: number;
}

@Injectable({ providedIn: 'root' })
export class KitchenDisplayService {
  private http = inject(HttpClient);
  private apiUrl = environment.apiUrl;

  getKitchenOrders(): Observable<KitchenOrder[]> {
    return this.http.get<KitchenOrder[]>(`${this.apiUrl}/kitchen/orders`).pipe(
      catchError(handleServiceError('KitchenDisplayService.getKitchenOrders'))
    );
  }

  updateOrderStatus(orderId: string, status: string): Observable<{ message: string }> {
    return this.http.put<{ message: string }>(`${this.apiUrl}/kitchen/orders/${orderId}/status`, { status }).pipe(
      catchError(handleServiceError('KitchenDisplayService.updateOrderStatus'))
    );
  }

  getKitchenStats(): Observable<KitchenStats> {
    return this.http.get<KitchenStats>(`${this.apiUrl}/kitchen/stats`).pipe(
      catchError(handleServiceError('KitchenDisplayService.getKitchenStats'))
    );
  }

  getKot(orderId: string): Observable<{ kotText: string }> {
    return this.http.get<{ kotText: string }>(`${this.apiUrl}/kitchen/orders/${orderId}/kot`).pipe(
      catchError(handleServiceError('KitchenDisplayService.getKot'))
    );
  }
}
