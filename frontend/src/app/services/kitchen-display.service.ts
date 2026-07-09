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
  preparationNotes?: string;
  notes?: string;
  createdAt: string;
  updatedAt: string;
  kptMinutes?: number;
  kitchenPrepStartedAt?: string;
  kitchenReadyAt?: string;
  kitchenChecklist?: KitchenChecklistItem[];
  kitchenAssignedStaffId?: string;
  kitchenAssignedStaffName?: string;
  kitchenAssignedRole?: string;
  kitchenAssignedAt?: string;
}

export interface KitchenChecklistItem {
  id?: string;
  label: string;
  isCompleted: boolean;
}

export interface KitchenStats {
  pendingOrders: number;
  preparingOrders: number;
  readyOrders: number;
  avgPrepTime: number;
  avgKptMinutes?: number;
  completedToday: number;
}

export interface KitchenStaffDashboard {
  role: string;
  period: 'day' | 'week' | 'month' | 'year';
  ratings: {
    average: number;
    reviewsCount: number;
    start: string;
    end: string;
  };
  shift: {
    isInShift: boolean;
    clockIn?: string;
    clockOut?: string;
    hoursWorked?: number;
  };
  attendance?: any;
  payslip: {
    route: string;
    label: string;
  };
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

  updateOrderStatus(orderId: string, status: string, checklistItems?: KitchenChecklistItem[]): Observable<{ message: string }> {
    return this.http.put<{ message: string }>(`${this.apiUrl}/kitchen/orders/${orderId}/status`, { status, checklistItems }).pipe(
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

  getKitchenStaffDashboard(period: 'day' | 'week' | 'month' | 'year'): Observable<KitchenStaffDashboard> {
    return this.http.get<KitchenStaffDashboard>(`${this.apiUrl}/kitchen/staff/dashboard?period=${period}`).pipe(
      catchError(handleServiceError('KitchenDisplayService.getKitchenStaffDashboard'))
    );
  }

  shiftIn(): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.apiUrl}/kitchen/staff/shift-in`, {}).pipe(
      catchError(handleServiceError('KitchenDisplayService.shiftIn'))
    );
  }

  shiftOut(): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.apiUrl}/kitchen/staff/shift-out`, {}).pipe(
      catchError(handleServiceError('KitchenDisplayService.shiftOut'))
    );
  }

  markAttendance(): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.apiUrl}/kitchen/staff/attendance/mark`, {}).pipe(
      catchError(handleServiceError('KitchenDisplayService.markAttendance'))
    );
  }
}
