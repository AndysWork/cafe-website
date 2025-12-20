import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../environments/environment';

export interface PlatformCharge {
  id?: string;
  platform: string; // "Zomato" or "Swiggy"
  month: number; // 1-12
  year: number;
  charges: number;
  chargeType?: string;
  notes?: string;
  recordedBy: string;
  createdAt: Date;
  updatedAt: Date;
}

export interface CreatePlatformChargeRequest {
  platform: string;
  month: number;
  year: number;
  charges: number;
  chargeType?: string;
  notes?: string;
}

export interface UpdatePlatformChargeRequest {
  charges?: number;
  chargeType?: string;
  notes?: string;
}

@Injectable({
  providedIn: 'root'
})
export class PlatformChargeService {
  private apiUrl = `${environment.apiUrl}/platform-charges`;

  constructor(private http: HttpClient) {}

  private getHeaders(): HttpHeaders {
    const token = localStorage.getItem('authToken');
    return new HttpHeaders({
      'Content-Type': 'application/json',
      'Authorization': token ? `Bearer ${token}` : ''
    });
  }

  getAllPlatformCharges(): Observable<PlatformCharge[]> {
    return this.http.get<any>(this.apiUrl, { headers: this.getHeaders() })
      .pipe(map(response => response.data || []));
  }

  getPlatformChargeByKey(platform: string, year: number, month: number): Observable<PlatformCharge | null> {
    return this.http.get<any>(`${this.apiUrl}/${platform}/${year}/${month}`, { headers: this.getHeaders() })
      .pipe(map(response => response.data || null));
  }

  getChargesByPlatform(platform: string): Observable<PlatformCharge[]> {
    return this.http.get<any>(`${this.apiUrl}/platform/${platform}`, { headers: this.getHeaders() })
      .pipe(map(response => response.data || []));
  }

  createPlatformCharge(request: CreatePlatformChargeRequest): Observable<any> {
    return this.http.post<any>(this.apiUrl, request, { headers: this.getHeaders() });
  }

  updatePlatformCharge(id: string, request: UpdatePlatformChargeRequest): Observable<any> {
    return this.http.put<any>(`${this.apiUrl}/${id}`, request, { headers: this.getHeaders() });
  }

  deletePlatformCharge(id: string): Observable<any> {
    return this.http.delete<any>(`${this.apiUrl}/${id}`, { headers: this.getHeaders() });
  }
}
