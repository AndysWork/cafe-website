import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { handleServiceError } from '../utils/error-handler';

export interface CustomerWallet {
  id?: string;
  userId: string;
  balance: number;
  totalCredited: number;
  totalDebited: number;
  lastUpdated?: string;
}

export interface WalletTransaction {
  id?: string;
  walletId: string;
  type: 'credit' | 'debit';
  amount: number;
  description: string;
  referenceId?: string;
  createdAt?: string;
}

export interface WalletResponse {
  id: string;
  balance: number;
  totalCredited: number;
  totalDebited: number;
  recentTransactions: WalletTransaction[];
}

export interface WalletRechargeRequest {
  amount: number;
  paymentMethod: string;
  transactionId?: string;
}

@Injectable({ providedIn: 'root' })
export class WalletService {
  private http = inject(HttpClient);
  private apiUrl = environment.apiUrl;

  getMyWallet(): Observable<WalletResponse> {
    return this.http.get<WalletResponse>(`${this.apiUrl}/wallet`).pipe(
      catchError(handleServiceError('WalletService.getMyWallet'))
    );
  }

  getTransactions(page: number = 1, pageSize: number = 20): Observable<WalletTransaction[]> {
    return this.http.get<WalletTransaction[]>(`${this.apiUrl}/wallet/transactions?page=${page}&pageSize=${pageSize}`).pipe(
      catchError(handleServiceError('WalletService.getTransactions'))
    );
  }

  rechargeWallet(request: WalletRechargeRequest): Observable<{ message: string; newBalance: number; cashbackAmount: number }> {
    return this.http.post<{ message: string; newBalance: number; cashbackAmount: number }>(`${this.apiUrl}/wallet/recharge`, request).pipe(
      catchError(handleServiceError('WalletService.rechargeWallet'))
    );
  }
}
