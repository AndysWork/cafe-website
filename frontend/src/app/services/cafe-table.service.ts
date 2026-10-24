import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map, catchError } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { handleServiceError } from '../utils/error-handler';
import { OutletService } from './outlet.service';

export type TableHousekeepingStatus = 'available' | 'cleaning' | 'out_of_service';
export type TableFloorStatus = 'free' | 'occupied' | 'bill_requested' | 'cleaning' | 'out_of_service';

export interface CafeTable {
  id: string;
  outletId: string;
  tableNumber: string;
  area?: string;
  capacity: number;
  sortOrder: number;
  isActive: boolean;
  housekeepingStatus: TableHousekeepingStatus;
  updatedAt?: string;
}

export interface CafeTableFloorView {
  id?: string;
  tableNumber: string;
  area?: string;
  capacity: number;
  sortOrder: number;
  isActive: boolean;
  housekeepingStatus: TableHousekeepingStatus;
  status: TableFloorStatus;
  sessionId?: string;
  customerName?: string;
  roundsCount: number;
  grandTotal: number;
  amountDue: number;
  occupiedSince?: string;
  minutesActive?: number;
}

export interface FloorSummary {
  total: number;
  free: number;
  occupied: number;
  billRequested: number;
  cleaning: number;
  outOfService: number;
}

export interface CancelledSessionCleanupResult {
  dryRun: boolean;
  candidateCount: number;
  closedCount: number;
  skippedCount: number;
  candidates: Array<{ sessionId: string; tableNumber: string; orderCount: number }>;
  closed: Array<{ sessionId: string; tableNumber: string; orderCount: number }>;
}

export interface SaveCafeTableRequest {
  tableNumber?: string;
  area?: string;
  capacity?: number;
  sortOrder?: number;
  isActive?: boolean;
}

export interface BulkCreateTablesRequest {
  prefix: string;
  from: number;
  to: number;
  area?: string;
  capacity: number;
}

@Injectable({ providedIn: 'root' })
export class CafeTableService {
  private http = inject(HttpClient);
  private outletService = inject(OutletService);
  private apiUrl = `${environment.apiUrl}/tables`;

  getTables(includeInactive = false): Observable<CafeTable[]> {
    const params = new HttpParams().set('includeInactive', String(includeInactive));
    return this.http.get<{ data: CafeTable[] }>(this.apiUrl, { params }).pipe(
      map(r => r?.data ?? []),
      catchError(handleServiceError('CafeTableService.getTables'))
    );
  }

  getFloor(): Observable<{ tables: CafeTableFloorView[]; summary: FloorSummary }> {
    return this.http.get<{ data: CafeTableFloorView[]; summary: FloorSummary }>(`${this.apiUrl}/floor`).pipe(
      map(r => ({ tables: r?.data ?? [], summary: r.summary })),
      catchError(handleServiceError('CafeTableService.getFloor'))
    );
  }

  cleanupCancelledSessions(dryRun = true): Observable<CancelledSessionCleanupResult> {
    const outletId = this.outletService.getSelectedOutletId();
    let params = new HttpParams().set('dryRun', String(dryRun));
    if (outletId) params = params.set('outletId', outletId);
    return this.http.post<CancelledSessionCleanupResult>(
      `${this.apiUrl}/cleanup-cancelled-sessions`, {}, { params }
    ).pipe(catchError(handleServiceError('CafeTableService.cleanupCancelledSessions')));
  }

  createTable(request: SaveCafeTableRequest): Observable<CafeTable> {
    return this.http.post<{ data: CafeTable }>(this.apiUrl, request).pipe(
      map(r => r.data),
      catchError(handleServiceError('CafeTableService.createTable'))
    );
  }

  bulkCreate(request: BulkCreateTablesRequest): Observable<{ created: string[]; skipped: string[] }> {
    return this.http.post<{ data: { created: string[]; skipped: string[] } }>(`${this.apiUrl}/bulk`, request).pipe(
      map(r => r.data),
      catchError(handleServiceError('CafeTableService.bulkCreate'))
    );
  }

  updateTable(id: string, request: SaveCafeTableRequest): Observable<CafeTable> {
    return this.http.put<{ data: CafeTable }>(`${this.apiUrl}/${id}`, request).pipe(
      map(r => r.data),
      catchError(handleServiceError('CafeTableService.updateTable'))
    );
  }

  setStatus(id: string, status: TableHousekeepingStatus): Observable<CafeTable> {
    return this.http.put<{ data: CafeTable }>(`${this.apiUrl}/${id}/status`, { status }).pipe(
      map(r => r.data),
      catchError(handleServiceError('CafeTableService.setStatus'))
    );
  }

  generateQrToken(id: string): Observable<{ tableNumber: string; outletId: string; qrToken: string }> {
    const outletId = this.outletService.getSelectedOutletId();
    const query = outletId ? `?outletId=${encodeURIComponent(outletId)}` : '';
    return this.http.post<{ success: boolean; data: { tableNumber: string; outletId: string; qrToken: string } }>(
      `${this.apiUrl}/${encodeURIComponent(id)}/qr-token${query}`, {}
    ).pipe(
      map(response => response.data),
      catchError(handleServiceError('CafeTableService.generateQrToken'))
    );
  }

  deleteTable(id: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`).pipe(
      catchError(handleServiceError('CafeTableService.deleteTable'))
    );
  }
}
