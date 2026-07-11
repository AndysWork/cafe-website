import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { handleServiceError } from '../utils/error-handler';

export interface ManagerOrderCard {
  id?: string;
  status: string;
  total: number;
  deliveryPartnerId?: string;
  deliveryPartnerName?: string;
  deliveryAddress?: string;
  phoneNumber?: string;
  createdAt?: string;
  updatedAt?: string;
  isUrgent?: boolean;
  urgentReason?: string;
  urgentMarkedAt?: string;
}

export interface ManagerParcelTaskCard {
  id?: string;
  partnerId: string;
  partnerName?: string;
  startPoint: string;
  endPoint: string;
  distanceKm: number;
  billableDistanceKm: number;
  isRoundTrip: boolean;
  etaMinutes?: number;
  status: string;
  createdAt?: string;
  acceptedAt?: string;
  completedAt?: string;
  payoutImpact: number;
}

export interface ManagerEscalation {
  type: string;
  orderId?: string;
  taskId?: string;
  status?: string;
  waitedMinutes: number;
  createdAt?: string;
}

export interface ManagerAlert {
  type: string;
  severity: string;
  message: string;
  value: number;
}

export interface ManagerOpsBoardResponse {
  capturedAt: string;
  outletId: string;
  kitchenQueue: ManagerOrderCard[];
  deliveryQueue: ManagerOrderCard[];
  parcelTasks: ManagerParcelTaskCard[];
  escalations: ManagerEscalation[];
  alerts: ManagerAlert[];
  summary: {
    kitchenQueueCount: number;
    deliveryQueueCount: number;
    parcelAssignedCount: number;
    parcelAcceptedCount: number;
    availablePartners: number;
    partnersOnDelivery: number;
  };
}

export interface ManagerAuditEntry {
  id?: string;
  eventType: string;
  entityType: string;
  entityId: string;
  summary: string;
  metadata: Record<string, string>;
  createdByUserId?: string;
  createdAt: string;
}

export interface ManagerAuditReconciliationResponse {
  date: string;
  expectedPayout: number;
  actualPayout: number;
  variance: number;
  completedParcelTasks: number;
  auditEntries: ManagerAuditEntry[];
}

@Injectable({ providedIn: 'root' })
export class ManagerOpsService {
  private http = inject(HttpClient);
  private apiUrl = environment.apiUrl;

  getBoard(): Observable<ManagerOpsBoardResponse> {
    return this.http.get<ManagerOpsBoardResponse>(`${this.apiUrl}/manage/ops/manager/board`).pipe(
      catchError(handleServiceError('ManagerOpsService.getBoard'))
    );
  }

  getParcelTasks(status?: string): Observable<{ count: number; items: ManagerParcelTaskCard[] }> {
    let params = new HttpParams();
    if (status) {
      params = params.set('status', status);
    }
    return this.http.get<{ count: number; items: ManagerParcelTaskCard[] }>(
      `${this.apiUrl}/manage/ops/manager/parcel-tasks`,
      { params }
    ).pipe(catchError(handleServiceError('ManagerOpsService.getParcelTasks')));
  }

  reassignPartner(orderId: string, partnerId: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.apiUrl}/manage/ops/manager/reassign-partner`, { orderId, partnerId }).pipe(
      catchError(handleServiceError('ManagerOpsService.reassignPartner'))
    );
  }

  markUrgent(orderId: string, reason?: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.apiUrl}/manage/ops/manager/mark-urgent`, { orderId, reason }).pipe(
      catchError(handleServiceError('ManagerOpsService.markUrgent'))
    );
  }

  resendNotification(orderId: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.apiUrl}/manage/ops/manager/resend-notification`, { orderId }).pipe(
      catchError(handleServiceError('ManagerOpsService.resendNotification'))
    );
  }

  getAuditReconciliation(date?: string): Observable<ManagerAuditReconciliationResponse> {
    let params = new HttpParams();
    if (date) {
      params = params.set('date', date);
    }
    return this.http.get<ManagerAuditReconciliationResponse>(`${this.apiUrl}/manage/ops/manager/audit-reconciliation`, { params }).pipe(
      catchError(handleServiceError('ManagerOpsService.getAuditReconciliation'))
    );
  }
}
