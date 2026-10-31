import { Injectable, inject } from '@angular/core';
import { HttpBackend, HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { Order } from './order.service';
import { handleServiceError } from '../utils/error-handler';

export interface PhoneOrderRequest {
  outletId: string;
  customerName: string;
  phoneNumber: string;
  deliveryAddress: string;
  deliveryLocationUrl: string;
  items: { menuItemId: string; quantity: number; selectedVariantName?: string; selectedAddOnNames: string[] }[];
  paymentMethod: 'cod' | 'cash_at_counter' | 'upi-qr';
  upiReference?: string;
  preparationNotes?: string;
  expectedTotal?: number;
  manualDeliveryFee?: number;
  manualApproximateDistanceKm?: number;
}

export interface PhoneOrderQuote {
  subtotal: number;
  tax: number;
  platformCharge: number;
  deliveryFee: number;
  total: number;
  estimatedMinutes?: number;
  approximateDistanceKm?: number;
  zone?: string;
  manualOverrideApplied?: boolean;
}

export interface PhoneDeliveryOrder extends Order {
  orderSource: 'phone';
  deliveryLocationUrl: string;
  recordedByName?: string;
  trackingPath: string;
  kotNumber?: string;
}

export interface PhoneOrderTracking {
  orderId: string;
  status: string;
  kotNumber?: string;
  createdAt: string;
  updatedAt: string;
  paymentStatus: string;
  paymentMethod: string;
  total: number;
  deliveryPartnerName?: string;
  etaMinutes?: number;
  supportPhone?: string;
}

@Injectable({ providedIn: 'root' })
export class PhoneOrderService {
  private readonly http = inject(HttpClient);
  // Public tracking must not depend on a staff login, outlet selection or auth-error redirects.
  private readonly publicHttp = new HttpClient(inject(HttpBackend));
  private readonly apiUrl = environment.apiUrl;

  getOrders(outletId: string): Observable<{ orders: PhoneDeliveryOrder[] }> {
    return this.http.get<{ orders: PhoneDeliveryOrder[] }>(`${this.apiUrl}/phone-orders`, {
      headers: new HttpHeaders({ 'X-Outlet-Id': outletId }),
      params: new HttpParams().set('ngsw-bypass', 'true')
    }).pipe(catchError(handleServiceError('PhoneOrderService.getOrders')));
  }

  quote(request: PhoneOrderRequest): Observable<PhoneOrderQuote> {
    return this.http.post<PhoneOrderQuote>(`${this.apiUrl}/phone-orders/quote`, request, {
      headers: new HttpHeaders({ 'X-Outlet-Id': request.outletId })
    }).pipe(catchError(handleServiceError('PhoneOrderService.quote')));
  }

  create(request: PhoneOrderRequest, idempotencyKey: string): Observable<{ order: PhoneDeliveryOrder; message: string }> {
    return this.http.post<{ order: PhoneDeliveryOrder; message: string }>(`${this.apiUrl}/phone-orders`, request, {
      headers: new HttpHeaders({ 'X-Outlet-Id': request.outletId, 'Idempotency-Key': idempotencyKey })
    }).pipe(catchError(handleServiceError('PhoneOrderService.create')));
  }

  track(orderId: string, token: string): Observable<PhoneOrderTracking> {
    return this.publicHttp.get<PhoneOrderTracking>(
      `${this.apiUrl}/public/phone-orders/${encodeURIComponent(orderId)}/tracking`, {
        params: new HttpParams().set('token', token).set('ngsw-bypass', 'true')
      });
  }
}
