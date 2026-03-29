import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { handleServiceError } from '../utils/error-handler';

export interface DeliveryAddress {
  id: string;
  label: string;
  fullAddress: string;
  city?: string;
  pinCode?: string;
  collectorName: string;
  collectorPhone: string;
  isDefault: boolean;
  createdAt: string;
}

export interface AddAddressRequest {
  label: string;
  fullAddress: string;
  city?: string;
  pinCode?: string;
  collectorName: string;
  collectorPhone: string;
  isDefault?: boolean;
}

export interface UpdateAddressRequest {
  label?: string;
  fullAddress?: string;
  city?: string;
  pinCode?: string;
  collectorName?: string;
  collectorPhone?: string;
  isDefault?: boolean;
}

@Injectable({ providedIn: 'root' })
export class AddressService {
  private http = inject(HttpClient);
  private apiUrl = environment.apiUrl;

  getMyAddresses(): Observable<DeliveryAddress[]> {
    return this.http.get<DeliveryAddress[]>(`${this.apiUrl}/addresses`).pipe(
      catchError(handleServiceError('AddressService.getMyAddresses'))
    );
  }

  addAddress(address: AddAddressRequest): Observable<DeliveryAddress> {
    return this.http.post<DeliveryAddress>(`${this.apiUrl}/addresses`, address).pipe(
      catchError(handleServiceError('AddressService.addAddress'))
    );
  }

  updateAddress(addressId: string, data: UpdateAddressRequest): Observable<{ message: string }> {
    return this.http.put<{ message: string }>(`${this.apiUrl}/addresses/${addressId}`, data).pipe(
      catchError(handleServiceError('AddressService.updateAddress'))
    );
  }

  deleteAddress(addressId: string): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`${this.apiUrl}/addresses/${addressId}`).pipe(
      catchError(handleServiceError('AddressService.deleteAddress'))
    );
  }
}
