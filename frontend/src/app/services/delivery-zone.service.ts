import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { handleServiceError } from '../utils/error-handler';

export interface DeliveryZone {
  id?: string;
  outletId?: string;
  zoneName: string;
  minDistance: number;
  maxDistance: number;
  deliveryFee: number;
  freeDeliveryAbove: number;
  estimatedMinutes: number;
  isActive: boolean;
}

export interface DeliveryFeeResult {
  zone: string;
  deliveryFee: number;
  estimatedMinutes: number;
  freeDeliveryAbove: number;
  orderAmount?: number;
  isFreeDelivery?: boolean;
}

@Injectable({ providedIn: 'root' })
export class DeliveryZoneService {
  private http = inject(HttpClient);
  private apiUrl = environment.apiUrl;

  getDeliveryZones(): Observable<DeliveryZone[]> {
    return this.http.get<DeliveryZone[]>(`${this.apiUrl}/delivery-zones`).pipe(
      catchError(handleServiceError('DeliveryZoneService.getDeliveryZones'))
    );
  }

  createDeliveryZone(zone: DeliveryZone): Observable<DeliveryZone> {
    return this.http.post<DeliveryZone>(`${this.apiUrl}/delivery-zones`, zone).pipe(
      catchError(handleServiceError('DeliveryZoneService.createDeliveryZone'))
    );
  }

  updateDeliveryZone(id: string, zone: DeliveryZone): Observable<{ message: string }> {
    return this.http.put<{ message: string }>(`${this.apiUrl}/delivery-zones/${id}`, zone).pipe(
      catchError(handleServiceError('DeliveryZoneService.updateDeliveryZone'))
    );
  }

  deleteDeliveryZone(id: string): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`${this.apiUrl}/delivery-zones/${id}`).pipe(
      catchError(handleServiceError('DeliveryZoneService.deleteDeliveryZone'))
    );
  }

  calculateDeliveryFee(distance: number, orderAmount: number): Observable<DeliveryFeeResult> {
    return this.http.get<DeliveryFeeResult>(`${this.apiUrl}/delivery-zones/calculate-fee?distance=${distance}&orderAmount=${orderAmount}`).pipe(
      catchError(handleServiceError('DeliveryZoneService.calculateDeliveryFee'))
    );
  }
}
