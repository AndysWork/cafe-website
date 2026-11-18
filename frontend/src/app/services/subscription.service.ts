import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { handleServiceError } from '../utils/error-handler';

export const SUBSCRIPTION_TERMS = [
  { durationDays: 7, discountPercent: 3, pauseLimit: 1, maxPauseDays: 3 },
  { durationDays: 14, discountPercent: 5, pauseLimit: 1, maxPauseDays: 5 },
  { durationDays: 30, discountPercent: 10, pauseLimit: 2, maxPauseDays: 7 }
] as const;

export interface SubscriptionItem {
  menuItemId: string;
  menuItemName: string;
  unitPrice?: number;
  dailyQuantity: number;
  categoryName?: string;
  imageUrl?: string;
  isVeg?: boolean;
  servingDays?: string[];
}

export interface SubscriptionPlan {
  id?: string;
  name: string;
  description?: string;
  category?: string;
  price: number;
  pricingMode?: 'fixed' | 'per_day';
  isDraft?: boolean;
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

export function getSubscriptionPlanSellingPrice(plan: SubscriptionPlan): number {
  const discountPercent = SUBSCRIPTION_TERMS.find(terms => terms.durationDays === plan.durationDays)?.discountPercent
    ?? plan.discountPercent ?? 0;
  const round = (value: number): number => Math.round((value + Number.EPSILON) * 100) / 100;
  return round(plan.price - round(plan.price * discountPercent / 100));
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
  status: 'pending_payment' | 'active' | 'paused' | 'cancelled' | 'expired' | 'completed';
  amountPaid: number;
  amountDue?: number;
  dailySubtotal?: number;
  discountPercent?: number;
  discountAmount?: number;
  freeDelivery?: boolean;
  paymentMethod?: 'upi-qr' | 'cash_at_counter';
  paymentStatus?: 'pending' | 'paid';
  paymentReference?: string;
  pausedAt?: string;
  totalDaysPaused?: number;
  pauseEndsAtUtc?: string;
  pausesRemaining?: number;
  pauseLimit?: number;
  maxPauseDays?: number;
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
  paymentMethod?: 'upi-qr' | 'cash_at_counter';
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
  paymentMethod?: 'upi-qr' | 'cash_at_counter';
  outletId?: string;
}

export interface MySubscriptionResponse {
  active: CustomerSubscription | null;
  history: CustomerSubscription[];
}

export interface SubscriptionOverview {
  totalSubscriptions: number;
  newSubscriptionsThisMonth: number;
  previousMonthSubscriptions: number;
  monthlyRevenue: number;
  previousMonthRevenue: number;
  planSubscribers: Record<string, number>;
}

export interface SubscriptionMenuSet {
  id?: string;
  name: string;
  items: SubscriptionItem[];
  outletId?: string;
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

  getOverview(): Observable<SubscriptionOverview> {
    return this.http.get<SubscriptionOverview>(`${this.apiUrl}/manage/subscriptions/overview`)
      .pipe(catchError(handleServiceError('SubscriptionService.getOverview')));
  }

  getMenuSets(): Observable<SubscriptionMenuSet[]> {
    return this.http.get<SubscriptionMenuSet[]>(`${this.apiUrl}/manage/subscriptions/menu-sets`)
      .pipe(catchError(handleServiceError('SubscriptionService.getMenuSets')));
  }

  saveMenuSet(name: string, items: SubscriptionItem[]): Observable<SubscriptionMenuSet> {
    return this.http.post<SubscriptionMenuSet>(`${this.apiUrl}/manage/subscriptions/menu-sets`, { name, items })
      .pipe(catchError(handleServiceError('SubscriptionService.saveMenuSet')));
  }

  deleteMenuSet(id: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/manage/subscriptions/menu-sets/${id}`)
      .pipe(catchError(handleServiceError('SubscriptionService.deleteMenuSet')));
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

  getPendingPayments(outletId?: string): Observable<CustomerSubscription[]> {
    const url = `${this.apiUrl}/manage/subscriptions/payments/pending${outletId ? `?outletId=${encodeURIComponent(outletId)}` : ''}`;
    return this.http.get<CustomerSubscription[]>(url).pipe(
      catchError(handleServiceError('SubscriptionService.getPendingPayments'))
    );
  }

  confirmPayment(id: string, paymentReference?: string): Observable<{ message: string; subscription: CustomerSubscription }> {
    return this.http.post<{ message: string; subscription: CustomerSubscription }>(
      `${this.apiUrl}/manage/subscriptions/${id}/payment/confirm`,
      { paymentReference: paymentReference?.trim() || undefined }
    ).pipe(catchError(handleServiceError('SubscriptionService.confirmPayment')));
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

  pauseSubscription(id: string, days: number): Observable<{ message: string; subscription: CustomerSubscription }> {
    return this.http.post<{ message: string; subscription: CustomerSubscription }>(`${this.apiUrl}/subscriptions/${id}/pause`, { days }).pipe(
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
