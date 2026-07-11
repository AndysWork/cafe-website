import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { handleServiceError } from '../utils/error-handler';

export interface KitchenVoiceStockRequest {
  id?: string;
  outletId: string;
  requestedByUserId: string;
  requestedByName: string;
  requestedByRole: string;
  transcriptText: string;
  requestedItems: string[];
  sttProvider?: string;
  sttConfidence?: number;
  status: 'pending' | 'approved' | 'rejected';
  reviewedByUserId?: string;
  reviewedByName?: string;
  reviewNote?: string;
  reviewedAt?: string;
  createdAt: string;
  updatedAt: string;
}

export interface KitchenVoiceStockRequestListResponse {
  items: KitchenVoiceStockRequest[];
  count: number;
  pendingCount: number;
}

export interface CreateKitchenVoiceStockRequestPayload {
  transcriptText?: string;
  requestedItems?: string[];
  sttProvider?: string;
  sttConfidence?: number;
}

@Injectable({ providedIn: 'root' })
export class KitchenVoiceStockRequestService {
  private http = inject(HttpClient);
  private apiUrl = environment.apiUrl;

  createVoiceRequest(payload: CreateKitchenVoiceStockRequestPayload): Observable<KitchenVoiceStockRequest> {
    return this.http.post<KitchenVoiceStockRequest>(`${this.apiUrl}/kitchen/stock-requests/voice`, payload).pipe(
      catchError(handleServiceError('KitchenVoiceStockRequestService.createVoiceRequest'))
    );
  }

  getAdminRequests(status?: string, outletId?: string, limit = 100): Observable<KitchenVoiceStockRequestListResponse> {
    let params = new HttpParams().set('limit', String(limit));
    if (status) {
      params = params.set('status', status);
    }
    if (outletId) {
      params = params.set('outletId', outletId);
    }

    return this.http.get<KitchenVoiceStockRequestListResponse>(`${this.apiUrl}/manage/kitchen/stock-requests/voice`, { params }).pipe(
      catchError(handleServiceError('KitchenVoiceStockRequestService.getAdminRequests'))
    );
  }

  reviewRequest(id: string, decision: 'approved' | 'rejected', note?: string): Observable<{ message: string; item: KitchenVoiceStockRequest }> {
    return this.http.put<{ message: string; item: KitchenVoiceStockRequest }>(
      `${this.apiUrl}/manage/kitchen/stock-requests/voice/${id}/decision`,
      { decision, note }
    ).pipe(
      catchError(handleServiceError('KitchenVoiceStockRequestService.reviewRequest'))
    );
  }
}
