import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
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
  total: number;
  status: 'pending' | 'confirmed' | 'preparing' | 'ready' | 'delivered' | 'cancelled';
  paymentStatus: 'pending' | 'paid' | 'refunded';
  paymentMethod: 'cod' | 'razorpay';
  razorpayOrderId?: string;
  razorpayPaymentId?: string;
  deliveryAddress?: string;
  phoneNumber?: string;
  notes?: string;
  createdAt: string;
  updatedAt: string;
  completedAt?: string;
}

export interface CreateOrderRequest {
  items: {
    menuItemId: string;
    quantity: number;
  }[];
  deliveryAddress?: string;
  phoneNumber?: string;
  notes?: string;
  paymentMethod?: 'cod' | 'razorpay';
  razorpayPaymentId?: string;
  razorpayOrderId?: string;
  razorpaySignature?: string;
}

export interface UpdateOrderStatusRequest {
  status: string;
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
  getAllOrders(): Observable<Order[]> {
    return this.http.get<Order[]>(`${this.apiUrl}/orders`).pipe(
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
  updateOrderStatus(orderId: string, status: string): Observable<{ message: string; status: string }> {
    return this.http.put<{ message: string; status: string }>(
      `${this.apiUrl}/orders/${orderId}/status`,
      { status }
    ).pipe(
      catchError(handleServiceError('OrderService.updateOrderStatus'))
    );
  }

  // Cancel order
  cancelOrder(orderId: string): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`${this.apiUrl}/orders/${orderId}`).pipe(
      catchError(handleServiceError('OrderService.cancelOrder'))
    );
  }

  // Helper: Get status display text
  getStatusDisplayText(status: string): string {
    const statusMap: { [key: string]: string } = {
      'pending': 'Pending',
      'confirmed': 'Confirmed',
      'preparing': 'Preparing',
      'ready': 'Ready for Pickup',
      'delivered': 'Delivered',
      'cancelled': 'Cancelled'
    };
    return statusMap[status] || status;
  }

  // Helper: Get status color class
  getStatusColorClass(status: string): string {
    const colorMap: { [key: string]: string } = {
      'pending': 'text-warning',
      'confirmed': 'text-info',
      'preparing': 'text-primary',
      'ready': 'text-success',
      'delivered': 'text-success',
      'cancelled': 'text-danger'
    };
    return colorMap[status] || 'text-secondary';
  }

  // Helper: Check if order can be cancelled
  canCancelOrder(status: string): boolean {
    return status === 'pending' || status === 'confirmed';
  }
}
