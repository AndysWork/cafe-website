import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { handleServiceError } from '../utils/error-handler';

export interface SubscriptionPlan {
  id?: string;
  name: string;
  description?: string;
  price: number;
  durationDays: number;
  benefits: string[];
  includedItems: SubscriptionItem[];
  freeDelivery?: boolean;
  discountPercent?: number;
  dailyItemLimit?: number;
  isActive: boolean;
  outletId?: string;
  createdAt?: string;
}

export interface SubscriptionItem {
  menuItemId: string;
  menuItemName: string;
  dailyQuantity: number;
}

export interface CustomerSubscription {
  id?: string;
  userId: string;
  planId: string;
  planName?: string;
  startDate: string;
  endDate: string;
  status: 'active' | 'expired' | 'cancelled';
  remainingDays?: number;
  items?: SubscriptionItem[];
}

export interface SubscribeRequest {
  planId: string;
  paymentMethod: string;
}

@Injectable({ providedIn: 'root' })
export class SubscriptionService {
  private http = inject(HttpClient);
  private apiUrl = environment.apiUrl;

  getPlans(): Observable<SubscriptionPlan[]> {
    return this.http.get<SubscriptionPlan[]>(`${this.apiUrl}/subscriptions/plans`).pipe(
      catchError(handleServiceError('SubscriptionService.getPlans'))
    );
  }

  getAllPlans(): Observable<SubscriptionPlan[]> {
    return this.http.get<SubscriptionPlan[]>(`${this.apiUrl}/manage/subscriptions/plans`).pipe(
      catchError(handleServiceError('SubscriptionService.getAllPlans'))
    );
  }

  createPlan(plan: Partial<SubscriptionPlan>): Observable<SubscriptionPlan> {
    return this.http.post<SubscriptionPlan>(`${this.apiUrl}/manage/subscriptions/plans`, plan).pipe(
      catchError(handleServiceError('SubscriptionService.createPlan'))
    );
  }

  updatePlan(id: string, plan: Partial<SubscriptionPlan>): Observable<SubscriptionPlan> {
    return this.http.put<SubscriptionPlan>(`${this.apiUrl}/manage/subscriptions/plans/${id}`, plan).pipe(
      catchError(handleServiceError('SubscriptionService.updatePlan'))
    );
  }

  deletePlan(id: string): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`${this.apiUrl}/manage/subscriptions/plans/${id}`).pipe(
      catchError(handleServiceError('SubscriptionService.deletePlan'))
    );
  }

  subscribe(request: SubscribeRequest): Observable<CustomerSubscription> {
    return this.http.post<CustomerSubscription>(`${this.apiUrl}/subscriptions/subscribe`, request).pipe(
      catchError(handleServiceError('SubscriptionService.subscribe'))
    );
  }

  getMySubscription(): Observable<CustomerSubscription> {
    return this.http.get<CustomerSubscription>(`${this.apiUrl}/subscriptions/my`).pipe(
      catchError(handleServiceError('SubscriptionService.getMySubscription'))
    );
  }
}
