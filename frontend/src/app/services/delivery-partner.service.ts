import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { handleServiceError } from '../utils/error-handler';

export interface DeliveryPartner {
  id?: string;
  name: string;
  phone: string;
  vehicleType: string;
  status: 'available' | 'busy' | 'offline';
  currentOrderId?: string;
  totalDeliveries?: number;
  rating?: number;
  outletId?: string;
  createdAt?: string;
}

export interface AssignDeliveryRequest {
  orderId: string;
  deliveryPartnerId?: string;
}

@Injectable({ providedIn: 'root' })
export class DeliveryPartnerService {
  private http = inject(HttpClient);
  private apiUrl = environment.apiUrl;

  getDeliveryPartners(): Observable<DeliveryPartner[]> {
    return this.http.get<DeliveryPartner[]>(`${this.apiUrl}/manage/delivery-partners`).pipe(
      catchError(handleServiceError('DeliveryPartnerService.getDeliveryPartners'))
    );
  }

  createDeliveryPartner(partner: Partial<DeliveryPartner>): Observable<DeliveryPartner> {
    return this.http.post<DeliveryPartner>(`${this.apiUrl}/manage/delivery-partners`, partner).pipe(
      catchError(handleServiceError('DeliveryPartnerService.createDeliveryPartner'))
    );
  }

  assignDeliveryPartner(request: AssignDeliveryRequest): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.apiUrl}/manage/delivery-partners/assign`, request).pipe(
      catchError(handleServiceError('DeliveryPartnerService.assignDeliveryPartner'))
    );
  }

  completeDelivery(partnerId: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.apiUrl}/manage/delivery-partners/${partnerId}/complete`, {}).pipe(
      catchError(handleServiceError('DeliveryPartnerService.completeDelivery'))
    );
  }

  updatePartnerStatus(partnerId: string, status: string): Observable<{ message: string }> {
    return this.http.put<{ message: string }>(`${this.apiUrl}/manage/delivery-partners/${partnerId}/status`, { status }).pipe(
      catchError(handleServiceError('DeliveryPartnerService.updatePartnerStatus'))
    );
  }
}
