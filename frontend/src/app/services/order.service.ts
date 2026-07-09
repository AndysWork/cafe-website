import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { handleServiceError } from '../utils/error-handler';

export interface OrderItem {
  menuItemId: string;
  name: string;
  description?: string;
  categoryId?: string;
  categoryName?: string;
  quantity: number;
  price: number;
  baseUnitPrice?: number;
  selectedVariantName?: string;
  selectedVariantPrice?: number;
  selectedAddOns?: { name: string; price: number }[];
  addOnTotal?: number;
  total: number;
}

export interface Order {
  id: string;
  userId: string;
  username: string;
  userEmail?: string;
  items: OrderItem[];
  subtotal: number;
  tax: number;
  platformCharge?: number;
  total: number;
  couponCode?: string;
  discountAmount?: number;
  loyaltyPointsUsed?: number;
  loyaltyDiscountAmount?: number;
  status: 'pending' | 'confirmed' | 'preparing' | 'ready' | 'out-for-delivery' | 'delivered' | 'cancelled' | 'scheduled';
  paymentStatus: 'pending' | 'paid' | 'refunded';
  paymentMethod: 'cod' | 'razorpay' | 'upi-qr';
  razorpayOrderId?: string;
  razorpayPaymentId?: string;
  upiReference?: string;
  upiConfirmedBy?: string;
  upiConfirmedAt?: string;
  upiProofUrl?: string;
  deliveryAddress?: string;
  phoneNumber?: string;
  preparationNotes?: string;
  notes?: string;
  receiptImageUrl?: string;
  scheduledFor?: string;
  isScheduled?: boolean;
  orderType?: string;
  channel?: 'web' | 'shop' | 'partner';
  deliveryFee?: number;
  walletAmountUsed?: number;
  deliveryPartnerId?: string;
  deliveryPartnerName?: string;
  tableNumber?: string;
  loyaltyPointsAwarded?: boolean;
  loyaltyPointsAwardedValue?: number;
  createdAt: string;
  updatedAt: string;
  completedAt?: string;
}

export interface CreateOrderRequest {
  items: {
    menuItemId: string;
    quantity: number;
    selectedVariantName?: string;
    selectedAddOnNames?: string[];
  }[];
  deliveryAddress?: string;
  phoneNumber?: string;
  preparationNotes?: string;
  notes?: string;
  paymentMethod?: 'cod' | 'razorpay' | 'upi-qr';
  razorpayPaymentId?: string;
  razorpayOrderId?: string;
  razorpaySignature?: string;
  upiReference?: string;
  couponCode?: string;
  loyaltyPointsUsed?: number;
  orderType?: 'delivery' | 'pickup' | 'dine-in';
  channel?: 'web' | 'shop' | 'partner';
  scheduledFor?: string;
  deliveryFee?: number;
  walletAmountUsed?: number;
  tableNumber?: string;
  outletId?: string;
}

export interface OutletSuggestion {
  outletId: string;
  outletName: string;
  outletCode: string;
  address: string;
  city: string;
  state: string;
  rating: number;
  estimatedEtaMinutes: number;
  estimatedDistanceKm: number;
  estimatedDeliveryFee: number;
  score: number;
  reasons: string[];
}

export interface UpdateOrderStatusRequest {
  status: string;
}

export interface AdminConfirmPaymentRequest {
  paymentReference?: string;
  adminNote?: string;
}

export interface DeliveryTrackingPartnerInfo {
  id?: string;
  name?: string;
  phone?: string;
  vehicleType?: string;
  vehicleNumber?: string;
  status?: string;
  currentLatitude?: number;
  currentLongitude?: number;
  lastLocationUpdatedAt?: string;
}

export interface OrderTrackingResponse {
  orderId: string;
  status: string;
  orderType: string;
  isScheduled: boolean;
  scheduledFor?: string;
  estimatedDeliveryAt?: string;
  etaMinutes?: number;
  etaLabel: string;
  liveLocationAvailable: boolean;
  liveLocationMapUrl?: string;
  deliveryPartner?: DeliveryTrackingPartnerInfo;
  supportPhone: string;
  supportEmail: string;
}

export interface OrderIssue {
  id?: string;
  orderId: string;
  outletId?: string;
  userId: string;
  username: string;
  category: string;
  description: string;
  status: string;
  resolutionNotes?: string;
  refundProcessed?: boolean;
  resolvedAt?: string;
  createdAt: string;
  updatedAt: string;
}

export interface CreateOrderIssueRequest {
  category: 'missing-item' | 'wrong-item' | 'damaged-item' | 'delay' | 'quality' | 'other';
  description: string;
}

export interface UpdateOrderIssueStatusRequest {
  status: 'open' | 'in-progress' | 'resolved' | 'closed';
  resolutionNotes?: string;
  refundProcessed?: boolean;
}

export interface UpiReconciliationItem {
  id: string;
  userId: string;
  username: string;
  total: number;
  paymentStatus: string;
  upiReference?: string;
  upiProofUrl?: string;
  upiConfirmedBy?: string;
  upiConfirmedAt?: string;
  createdAt: string;
  updatedAt: string;
}

export interface UpiReconciliationReport {
  generatedAt: string;
  outletId?: string;
  dateRange?: {
    from?: string;
    to?: string;
  };
  summary: {
    totalUpiOrders: number;
    pending: number;
    confirmed: number;
    refunded: number;
    proofSubmitted: number;
    pendingWithoutProof: number;
    confirmedWithoutProof: number;
  };
  items: UpiReconciliationItem[];
}

@Injectable({
  providedIn: 'root'
})
export class OrderService {
  private http = inject(HttpClient);
  private apiUrl = environment.apiUrl;

  // Create new order
  createOrder(orderRequest: CreateOrderRequest): Observable<Order> {
    return this.http.post<Order>(`${this.apiUrl}/orders`, orderRequest).pipe(
      catchError(handleServiceError('OrderService.createOrder'))
    );
  }

  // Get current user's orders
  getMyOrders(): Observable<Order[]> {
    return this.http.get<Order[]>(`${this.apiUrl}/orders/my`).pipe(
      catchError(handleServiceError('OrderService.getMyOrders'))
    );
  }

  // Get all orders (admin only)
  getAllOrders(channel?: 'web' | 'shop' | 'partner'): Observable<Order[]> {
    let params = new HttpParams();
    if (channel) {
      params = params.set('channel', channel);
    }

    return this.http.get<Order[]>(`${this.apiUrl}/orders`, { params }).pipe(
      catchError(handleServiceError('OrderService.getAllOrders'))
    );
  }

  // Get order by ID
  getOrderById(orderId: string): Observable<Order> {
    return this.http.get<Order>(`${this.apiUrl}/orders/${orderId}`).pipe(
      catchError(handleServiceError('OrderService.getOrderById'))
    );
  }

  // Update order status (admin only)
  updateOrderStatus(orderId: string, status: string, channel?: 'web' | 'shop' | 'partner'): Observable<{ message: string; status: string }> {
    let params = new HttpParams();
    if (channel) {
      params = params.set('channel', channel);
    }

    return this.http.put<{ message: string; status: string }>(
      `${this.apiUrl}/orders/${orderId}/status`,
      { status },
      { params }
    ).pipe(
      catchError(handleServiceError('OrderService.updateOrderStatus'))
    );
  }

  confirmOrderPayment(orderId: string, payload: AdminConfirmPaymentRequest): Observable<{ success: boolean; message: string; paymentStatus: string }> {
    return this.http.put<{ success: boolean; message: string; paymentStatus: string }>(
      `${this.apiUrl}/orders/${orderId}/payment/confirm`,
      payload
    ).pipe(
      catchError(handleServiceError('OrderService.confirmOrderPayment'))
    );
  }

  // Cancel order
  cancelOrder(orderId: string): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`${this.apiUrl}/orders/${orderId}`).pipe(
      catchError(handleServiceError('OrderService.cancelOrder'))
    );
  }

  // Upload receipt image for an order
  uploadReceipt(orderId: string, file: File): Observable<{ receiptImageUrl: string; message: string }> {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post<{ receiptImageUrl: string; message: string }>(
      `${this.apiUrl}/orders/${orderId}/receipt`, formData
    ).pipe(
      catchError(handleServiceError('OrderService.uploadReceipt'))
    );
  }

  // Download order receipt PDF
  downloadReceiptPdf(orderId: string): Observable<Blob> {
    return this.http.get(`${this.apiUrl}/orders/${orderId}/receipt-pdf`, { responseType: 'blob' }).pipe(
      catchError(handleServiceError('OrderService.downloadReceiptPdf'))
    );
  }

  // Delete receipt image for an order
  deleteReceipt(orderId: string): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`${this.apiUrl}/orders/${orderId}/receipt`).pipe(
      catchError(handleServiceError('OrderService.deleteReceipt'))
    );
  }

  // Helper: Get status display text
  getStatusDisplayText(status: string): string {
    const statusMap: { [key: string]: string } = {
      'scheduled': 'Scheduled',
      'pending': 'Pending',
      'confirmed': 'Confirmed',
      'preparing': 'Preparing',
      'ready': 'Ready for Pickup',
      'out-for-delivery': 'Out for Delivery',
      'delivered': 'Delivered',
      'cancelled': 'Cancelled'
    };
    return statusMap[status] || status;
  }

  // Helper: Get status color class
  getStatusColorClass(status: string): string {
    const colorMap: { [key: string]: string } = {
      'scheduled': 'text-info',
      'pending': 'text-warning',
      'confirmed': 'text-info',
      'preparing': 'text-primary',
      'ready': 'text-success',
      'out-for-delivery': 'text-primary',
      'delivered': 'text-success',
      'cancelled': 'text-danger'
    };
    return colorMap[status] || 'text-secondary';
  }

  // Helper: Check if order can be cancelled
  canCancelOrder(status: string): boolean {
    return status === 'pending' || status === 'confirmed' || status === 'scheduled';
  }

  getOrderTracking(orderId: string): Observable<OrderTrackingResponse> {
    return this.http.get<OrderTrackingResponse>(`${this.apiUrl}/orders/${orderId}/tracking`).pipe(
      catchError(handleServiceError('OrderService.getOrderTracking'))
    );
  }

  cancelOrderItem(orderId: string, menuItemId: string, quantity: number): Observable<Order> {
    return this.http.post<Order>(`${this.apiUrl}/orders/${orderId}/items/${menuItemId}/cancel`, { quantity }).pipe(
      catchError(handleServiceError('OrderService.cancelOrderItem'))
    );
  }

  createOrderIssue(orderId: string, payload: CreateOrderIssueRequest): Observable<OrderIssue> {
    return this.http.post<OrderIssue>(`${this.apiUrl}/orders/${orderId}/issues`, payload).pipe(
      catchError(handleServiceError('OrderService.createOrderIssue'))
    );
  }

  getOrderIssues(orderId: string): Observable<OrderIssue[]> {
    return this.http.get<OrderIssue[]>(`${this.apiUrl}/orders/${orderId}/issues`).pipe(
      catchError(handleServiceError('OrderService.getOrderIssues'))
    );
  }

  getOutletSuggestions(orderType: 'delivery' | 'pickup' | 'dine-in', deliveryAddress?: string, subtotal?: number): Observable<OutletSuggestion[]> {
    let params = new HttpParams().set('orderType', orderType);
    if (deliveryAddress?.trim()) {
      params = params.set('deliveryAddress', deliveryAddress.trim());
    }
    if (typeof subtotal === 'number' && !Number.isNaN(subtotal)) {
      params = params.set('subtotal', String(subtotal));
    }

    return this.http.get<OutletSuggestion[]>(`${this.apiUrl}/order-outlet-suggestions`, { params }).pipe(
      catchError(handleServiceError('OrderService.getOutletSuggestions'))
    );
  }

  updateOrderIssueStatus(orderId: string, issueId: string, payload: UpdateOrderIssueStatusRequest): Observable<{ success: boolean; message: string }> {
    return this.http.put<{ success: boolean; message: string }>(`${this.apiUrl}/orders/${orderId}/issues/${issueId}`, payload).pipe(
      catchError(handleServiceError('OrderService.updateOrderIssueStatus'))
    );
  }

  getUpiReconciliationReport(from?: string, to?: string): Observable<UpiReconciliationReport> {
    let params = new HttpParams();
    if (from?.trim()) {
      params = params.set('from', from.trim());
    }
    if (to?.trim()) {
      params = params.set('to', to.trim());
    }

    return this.http.get<UpiReconciliationReport>(`${this.apiUrl}/manage/reports/upi-reconciliation`, { params }).pipe(
      catchError(handleServiceError('OrderService.getUpiReconciliationReport'))
    );
  }
}
