import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { handleServiceError } from '../utils/error-handler';

export interface WastageItem {
  itemName: string;
  menuItemId?: string;
  ingredientId?: string;
  quantity: number;
  unit: string;
  costPerUnit: number;
  totalCost: number;
}

export interface WastageRecord {
  id?: string;
  outletId?: string;
  date?: string;
  items: WastageItem[];
  totalValue: number;
  reason: string;
  notes?: string;
  recordedBy?: string;
  createdAt?: string;
}

export interface CreateWastageRequest {
  date: string;
  items: { itemName: string; quantity: number; unit: string; costPerUnit: number }[];
  reason: string;
  notes?: string;
}

export interface WastageSummary {
  totalWastageValue: number;
  totalRecords: number;
  byReason: { [key: string]: number };
  records: WastageRecord[];
}

@Injectable({ providedIn: 'root' })
export class WastageService {
  private http = inject(HttpClient);
  private apiUrl = environment.apiUrl;

  createWastageRecord(request: CreateWastageRequest): Observable<WastageRecord> {
    return this.http.post<WastageRecord>(`${this.apiUrl}/wastage`, request).pipe(
      catchError(handleServiceError('WastageService.createWastageRecord'))
    );
  }

  getWastageRecords(startDate: string, endDate: string): Observable<WastageRecord[]> {
    return this.http.get<WastageRecord[]>(`${this.apiUrl}/wastage?startDate=${startDate}&endDate=${endDate}`).pipe(
      catchError(handleServiceError('WastageService.getWastageRecords'))
    );
  }

  getWastageSummary(startDate: string, endDate: string): Observable<WastageSummary> {
    return this.http.get<WastageSummary>(`${this.apiUrl}/wastage/summary?startDate=${startDate}&endDate=${endDate}`).pipe(
      catchError(handleServiceError('WastageService.getWastageSummary'))
    );
  }
}
