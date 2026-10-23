import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { OutletService } from './outlet.service';

export type TakeawayPaymentTiming = 'upfront' | 'handover';
export type TakeawayPaymentMethod = 'cash_at_counter' | 'upi-qr';

export interface WaiterTakeawayOrder {
  orderId: string;
  token: string;
  status: 'pending' | 'confirmed' | 'preparing' | 'ready' | 'delivered' | 'cancelled' | string;
  customerName?: string;
  phoneNumber?: string;
  waiterUserId?: string;
  waiterName?: string;
  subtotal: number;
  packagingCharge: number;
  total: number;
  paymentStatus: string;
  paymentMethod?: string;
  paymentTiming?: TakeawayPaymentTiming;
  upiReference?: string;
  notes?: string;
  preparationNotes?: string;
  createdAt: string;
  kitchenReadyAt?: string;
  handedOverAt?: string;
  items: Array<{
    name: string;
    quantity: number;
    unitPrice: number;
    total: number;
    variant?: string;
    addOns: string[];
  }>;
}

export interface CreateWaiterTakeawayRequest {
  customerName: string;
  phoneNumber?: string;
  items: Array<{
    menuItemId: string;
    quantity: number;
    selectedVariantName?: string;
    selectedAddOnNames?: string[];
  }>;
  preparationNotes?: string;
  paymentTiming: TakeawayPaymentTiming;
  paymentMethod?: TakeawayPaymentMethod;
  upiReference?: string;
}

@Injectable({ providedIn: 'root' })
export class TakeawayService {
  private readonly http = inject(HttpClient);
  private readonly outletService = inject(OutletService);
  private readonly apiUrl = `${environment.apiUrl}/takeaway/orders`;

  getActiveOrders(): Observable<{ orders: WaiterTakeawayOrder[] }> {
    return this.http.get<{ orders: WaiterTakeawayOrder[] }>(`${this.apiUrl}${this.outletQuery()}`);
  }

  createOrder(request: CreateWaiterTakeawayRequest): Observable<{ message: string; order: WaiterTakeawayOrder }> {
    return this.http.post<{ message: string; order: WaiterTakeawayOrder }>(this.apiUrl, {
      ...request,
      outletId: this.outletService.getSelectedOutletId() || undefined
    });
  }

  recordPayment(orderId: string, paymentMethod: TakeawayPaymentMethod, upiReference?: string):
    Observable<{ message: string; order: WaiterTakeawayOrder }> {
    return this.http.post<{ message: string; order: WaiterTakeawayOrder }>(
      `${this.orderUrl(orderId)}/payment${this.outletQuery()}`, { paymentMethod, upiReference });
  }

  handOver(orderId: string): Observable<{ message: string; order: WaiterTakeawayOrder }> {
    return this.http.post<{ message: string; order: WaiterTakeawayOrder }>(
      `${this.orderUrl(orderId)}/handover${this.outletQuery()}`, {});
  }

  reprintKot(orderId: string): Observable<{ message: string; orderId: string; kotNumber: string }> {
    return this.http.post<{ message: string; orderId: string; kotNumber: string }>(
      `${this.orderUrl(orderId)}/kot/reprint${this.outletQuery()}`, {});
  }

  cancelOrder(orderId: string, reason: string): Observable<{ message: string; orderId: string }> {
    return this.http.post<{ message: string; orderId: string }>(
      `${this.orderUrl(orderId)}/cancel${this.outletQuery()}`, { reason });
  }

  private orderUrl(orderId: string): string {
    return `${this.apiUrl}/${encodeURIComponent(orderId)}`;
  }

  private outletQuery(): string {
    const outletId = this.outletService.getSelectedOutletId();
    return outletId ? `?outletId=${encodeURIComponent(outletId)}` : '';
  }
}
