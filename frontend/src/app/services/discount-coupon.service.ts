import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export interface DiscountCoupon {
  id?: string;
  couponCode: string;
  platform: string;
  usageCount: number;
  totalDiscountAmount: number;
  averageDiscountAmount: number;
  firstUsed: string;
  lastUsed: string;
  isActive: boolean;
  maxValue?: number;
  discountPercentage?: number;
}

@Injectable({
  providedIn: 'root'
})
export class DiscountCouponService {
  private http = inject(HttpClient);
  private apiUrl = environment.apiUrl;

  getDiscountCoupons(): Observable<{ success: boolean; data: DiscountCoupon[] }> {
    return this.http.get<{ success: boolean; data: DiscountCoupon[] }>(`${this.apiUrl}/online-sales/discount-coupons`);
  }

  getActiveCoupons(): Observable<{ success: boolean; data: DiscountCoupon[] }> {
    return this.http.get<{ success: boolean; data: DiscountCoupon[] }>(`${this.apiUrl}/online-sales/discount-coupons/active`);
  }

  updateCouponStatus(couponCode: string, platform: string, isActive: boolean): Observable<{ success: boolean; message: string; data: any }> {
    return this.http.put<{ success: boolean; message: string; data: any }>(
      `${this.apiUrl}/online-sales/discount-coupons/${couponCode}/${platform}/status`,
      { isActive }
    );
  }

  updateCouponMaxValue(id: string, maxValue: number | null): Observable<{ success: boolean; message: string }> {
    return this.http.put<{ success: boolean; message: string }>(
      `${this.apiUrl}/online-sales/discount-coupons/${id}/max-value`,
      { maxValue }
    );
  }

  updateCouponDiscountPercentage(id: string, discountPercentage: number | null): Observable<{ success: boolean; message: string }> {
    return this.http.put<{ success: boolean; message: string }>(
      `${this.apiUrl}/online-sales/discount-coupons/${id}/discount-percentage`,
      { discountPercentage }
    );
  }
}
