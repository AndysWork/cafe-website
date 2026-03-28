import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { handleServiceError } from '../utils/error-handler';

declare var Razorpay: any;

export interface CreatePaymentOrderRequest {
  amount: number;
  receipt?: string;
}

export interface CreatePaymentOrderResponse {
  orderId: string;
  amount: number;
  currency: string;
  keyId: string;
}

export interface VerifyPaymentRequest {
  razorpayOrderId: string;
  razorpayPaymentId: string;
  razorpaySignature: string;
  orderId?: string;
}

export interface RazorpayPaymentResult {
  razorpay_order_id: string;
  razorpay_payment_id: string;
  razorpay_signature: string;
}

@Injectable({
  providedIn: 'root'
})
export class PaymentService {
  private http = inject(HttpClient);
  private apiUrl = environment.apiUrl;

  // Create Razorpay payment order on backend
  createPaymentOrder(amount: number, receipt?: string): Observable<CreatePaymentOrderResponse> {
    return this.http.post<CreatePaymentOrderResponse>(`${this.apiUrl}/payments/create-order`, {
      amount,
      receipt
    }).pipe(
      catchError(handleServiceError('PaymentService.createPaymentOrder'))
    );
  }

  // Verify payment on backend
  verifyPayment(request: VerifyPaymentRequest): Observable<{ success: boolean; message: string }> {
    return this.http.post<{ success: boolean; message: string }>(`${this.apiUrl}/payments/verify`, request).pipe(
      catchError(handleServiceError('PaymentService.verifyPayment'))
    );
  }

  // Open Razorpay checkout modal
  openRazorpayCheckout(options: {
    orderId: string;
    amount: number;
    currency: string;
    keyId: string;
    customerName: string;
    customerEmail?: string;
    customerPhone?: string;
    description?: string;
  }): Promise<RazorpayPaymentResult> {
    return new Promise((resolve, reject) => {
      const razorpayOptions = {
        key: options.keyId,
        amount: options.amount,
        currency: options.currency,
        name: 'Maa Tara Cafe',
        description: options.description || 'Order Payment',
        order_id: options.orderId,
        prefill: {
          name: options.customerName,
          email: options.customerEmail || '',
          contact: options.customerPhone || ''
        },
        theme: {
          color: '#ff6b35'
        },
        handler: (response: RazorpayPaymentResult) => {
          resolve(response);
        },
        modal: {
          ondismiss: () => {
            reject(new Error('Payment cancelled by user'));
          }
        }
      };

      const rzp = new Razorpay(razorpayOptions);
      rzp.on('payment.failed', (response: any) => {
        reject(new Error(response.error?.description || 'Payment failed'));
      });
      rzp.open();
    });
  }
}
