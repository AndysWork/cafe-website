import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export interface OverheadCost {
  id?: string;
  costType: string;
  monthlyCost: number;
  operationalHoursPerDay: number;
  workingDaysPerMonth: number;
  costPerDay?: number;
  costPerHour?: number;
  costPerMinute?: number;
  isActive: boolean;
  description?: string;
  createdAt?: Date;
  updatedAt?: Date;
  createdBy?: string;
  lastUpdatedBy?: string;
}

export interface OverheadCostCalculation {
  costType: string;
  monthlyCost: number;
  costPerMinute: number;
  allocatedCost: number;
}

export interface OverheadAllocation {
  preparationTimeMinutes: number;
  costs: OverheadCostCalculation[];
  totalOverheadCost: number;
}

@Injectable({
  providedIn: 'root'
})
export class OverheadCostService {
  private apiUrl = `${environment.apiUrl}/overhead-costs`;

  constructor(private http: HttpClient) {}

  private getHeaders(): HttpHeaders {
    const outletId = localStorage.getItem('selectedOutletId');
    let headers = new HttpHeaders();

    if (outletId) {
      headers = headers.set('X-Outlet-Id', outletId);
    }

    return headers;
  }

  getAllOverheadCosts(): Observable<OverheadCost[]> {
    // Add timestamp to prevent caching
    const timestamp = new Date().getTime();
    return this.http.get<OverheadCost[]>(`${this.apiUrl}?_t=${timestamp}`, { headers: this.getHeaders() });
  }

  getActiveOverheadCosts(): Observable<OverheadCost[]> {
    return this.http.get<OverheadCost[]>(`${this.apiUrl}/active`, { headers: this.getHeaders() });
  }

  getOverheadCostById(id: string): Observable<OverheadCost> {
    return this.http.get<OverheadCost>(`${this.apiUrl}/${id}`, { headers: this.getHeaders() });
  }

  createOverheadCost(overheadCost: OverheadCost): Observable<OverheadCost> {
    return this.http.post<OverheadCost>(this.apiUrl, overheadCost, { headers: this.getHeaders() });
  }

  updateOverheadCost(id: string, overheadCost: OverheadCost): Observable<any> {
    return this.http.put(`${this.apiUrl}/${id}`, overheadCost, { headers: this.getHeaders() });
  }

  deleteOverheadCost(id: string): Observable<any> {
    return this.http.delete(`${this.apiUrl}/${id}`, { headers: this.getHeaders() });
  }

  calculateOverheadAllocation(preparationTimeMinutes: number): Observable<OverheadAllocation> {
    return this.http.get<OverheadAllocation>(`${this.apiUrl}/calculate`, {
      params: { preparationTimeMinutes: preparationTimeMinutes.toString() },
      headers: this.getHeaders()
    });
  }

  initializeDefaultOverheadCosts(): Observable<any> {
    return this.http.post(`${this.apiUrl}/initialize`, {}, { headers: this.getHeaders() });
  }
}
