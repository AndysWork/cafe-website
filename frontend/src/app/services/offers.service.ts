import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export interface Offer {
  id?: string;
  title: string;
  description: string;
  discountType: 'percentage' | 'flat' | 'bogo';
  discountValue: number;
  code: string;
  icon: string;
  minOrderAmount?: number;
  maxDiscount?: number;
  validFrom: Date;
  validTill: Date;
  isActive: boolean;
  usageLimit?: number;
  usageCount: number;
  applicableCategories?: string[];
  createdAt?: Date;
  updatedAt?: Date;
}

export interface OfferValidationRequest {
  code: string;
  orderAmount: number;
  categories?: string[];
}

export interface OfferValidationResponse {
  isValid: boolean;
  message?: string;
  offer?: Offer;
  discountAmount: number;
}

@Injectable({
  providedIn: 'root'
})
export class OffersService {
  private apiUrl = `${environment.apiUrl}/offers`;

  constructor(private http: HttpClient) {}

  private getHeaders(): HttpHeaders {
    const token = localStorage.getItem('token');
    return new HttpHeaders({
      'Content-Type': 'application/json',
      'Authorization': token ? `Bearer ${token}` : ''
    });
  }

  // Get all active offers (public)
  getActiveOffers(): Observable<Offer[]> {
    return this.http.get<Offer[]>(this.apiUrl);
  }

  // Get all offers (admin)
  getAllOffers(): Observable<Offer[]> {
    return this.http.get<Offer[]>(`${this.apiUrl}/all`, {
      headers: this.getHeaders()
    });
  }

  // Get offer by ID (admin)
  getOfferById(id: string): Observable<Offer> {
    return this.http.get<Offer>(`${this.apiUrl}/${id}`, {
      headers: this.getHeaders()
    });
  }

  // Create new offer (admin)
  createOffer(offer: Offer): Observable<Offer> {
    return this.http.post<Offer>(this.apiUrl, offer, {
      headers: this.getHeaders()
    });
  }

  // Update offer (admin)
  updateOffer(id: string, offer: Offer): Observable<Offer> {
    return this.http.put<Offer>(`${this.apiUrl}/${id}`, offer, {
      headers: this.getHeaders()
    });
  }

  // Delete offer (admin)
  deleteOffer(id: string): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`${this.apiUrl}/${id}`, {
      headers: this.getHeaders()
    });
  }

  // Validate offer code
  validateOffer(request: OfferValidationRequest): Observable<OfferValidationResponse> {
    return this.http.post<OfferValidationResponse>(`${this.apiUrl}/validate`, request, {
      headers: this.getHeaders()
    });
  }

  // Apply offer (increment usage count)
  applyOffer(id: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.apiUrl}/${id}/apply`, {}, {
      headers: this.getHeaders()
    });
  }
}
