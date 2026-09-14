import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { handleServiceError } from '../utils/error-handler';

export interface SubscriptionItem {
  menuItemId: string;
  menuItemName: string;
  unitPrice?: number;
  dailyQuantity: number;
  categoryName?: string;
  imageUrl?: string;
  isVeg?: boolean;
}

export interface SubscriptionPlan {
  id?: string;
  name: string;
  description?: string;
  category?: string;
  price: number;
  durationDays: number;
  benefits: string[];
  includedItems?: SubscriptionItem[];
  freeDelivery?: boolean;
  discountPercent?: number;
  dailyItemLimit?: number;
  badgeText?: string;
  imageUrl?: string;
  isActive: boolean;
  outletId?: string;
  createdAt?: string;
}

export interface CustomerSubscription {
  id?: string;
  userId: string;
  customerName?: string;
  customerPhone?: string;
  customerEmail?: string;
  subscriptionType?: 'curated_plan' | 'custom_combo';
  planId?: string;
  planName: string;
  outletId?: string;
  items?: SubscriptionItem[];
  deliveryTimeSlot?: string;
  deliveryDays?: string[];
  deliveryAddress?: string;
  specialInstructions?: string;
  startDate: string;
  endDate: string;
  durationDays?: number;
  status: 'active' | 'paused' | 'cancelled' | 'expired' | 'completed';
  amountPaid: number;
  dailySubtotal?: number;
  discountPercent?: number;
  discountAmount?: number;
  freeDelivery?: boolean;
  paymentMethod?: string;
  paymentStatus?: string;
  pausedAt?: string;
  totalDaysPaused?: number;
  createdAt?: string;
}

export interface SubscribeRequest {
  planId: string;
  deliveryTimeSlot?: string;
  deliveryDays?: string[];
  deliveryAddress?: string;
  customerPhone?: string;
  customerName?: string;
  specialInstructions?: string;
  durationDays?: number;
  paymentMethod?: string;
  outletId?: string;
}

export interface CreateCustomComboRequest {
  comboName: string;
  items: { menuItemId: string; quantity: number }[];
  deliveryTimeSlot: string;
  deliveryDays: string[];
  durationDays: number;
  deliveryAddress: string;
  customerPhone: string;
  customerName?: string;
  specialInstructions?: string;
  paymentMethod?: string;
  outletId?: string;
}

export interface MySubscriptionResponse {
  active: CustomerSubscription | null;
  history: CustomerSubscription[];
}

@Injectable({ providedIn: 'root' })
export class SubscriptionService {
  private http = inject(HttpClient);
  private apiUrl = environment.apiUrl;

  getPlans(outletId?: string): Observable<SubscriptionPlan[]> {
    const url = `${this.apiUrl}/subscriptions/plans${outletId ? `?outletId=${encodeURIComponent(outletId)}` : ''}`;
    return this.http.get<SubscriptionPlan[]>(url).pipe(
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

  subscribe(request: SubscribeRequest): Observable<{ message: string; subscription: CustomerSubscription }> {
    return this.http.post<{ message: string; subscription: CustomerSubscription }>(`${this.apiUrl}/subscriptions/subscribe`, request).pipe(
      catchError(handleServiceError('SubscriptionService.subscribe'))
    );
  }

  createCustomCombo(request: CreateCustomComboRequest): Observable<{ message: string; subscription: CustomerSubscription }> {
    return this.http.post<{ message: string; subscription: CustomerSubscription }>(`${this.apiUrl}/subscriptions/custom-combo`, request).pipe(
      catchError(handleServiceError('SubscriptionService.createCustomCombo'))
    );
  }

  getMySubscription(): Observable<MySubscriptionResponse> {
    return this.http.get<MySubscriptionResponse>(`${this.apiUrl}/subscriptions/my`).pipe(
      catchError(handleServiceError('SubscriptionService.getMySubscription'))
    );
  }

  pauseSubscription(id: string): Observable<{ message: string; subscription: CustomerSubscription }> {
    return this.http.post<{ message: string; subscription: CustomerSubscription }>(`${this.apiUrl}/subscriptions/${id}/pause`, {}).pipe(
      catchError(handleServiceError('SubscriptionService.pauseSubscription'))
    );
  }

  resumeSubscription(id: string): Observable<{ message: string; subscription: CustomerSubscription }> {
    return this.http.post<{ message: string; subscription: CustomerSubscription }>(`${this.apiUrl}/subscriptions/${id}/resume`, {}).pipe(
      catchError(handleServiceError('SubscriptionService.resumeSubscription'))
    );
  }

  cancelSubscription(id: string): Observable<{ message: string; subscription: CustomerSubscription }> {
    return this.http.post<{ message: string; subscription: CustomerSubscription }>(`${this.apiUrl}/subscriptions/${id}/cancel`, {}).pipe(
      catchError(handleServiceError('SubscriptionService.cancelSubscription'))
    );
  }
}
