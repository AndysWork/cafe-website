import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
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

  getAllOverheadCosts(): Observable<OverheadCost[]> {
    return this.http.get<OverheadCost[]>(this.apiUrl);
  }

  getActiveOverheadCosts(): Observable<OverheadCost[]> {
    return this.http.get<OverheadCost[]>(`${this.apiUrl}/active`);
  }

  getOverheadCostById(id: string): Observable<OverheadCost> {
    return this.http.get<OverheadCost>(`${this.apiUrl}/${id}`);
  }

  createOverheadCost(overheadCost: OverheadCost): Observable<OverheadCost> {
    return this.http.post<OverheadCost>(this.apiUrl, overheadCost);
  }

  updateOverheadCost(id: string, overheadCost: OverheadCost): Observable<any> {
    return this.http.put(`${this.apiUrl}/${id}`, overheadCost);
  }

  deleteOverheadCost(id: string): Observable<any> {
    return this.http.delete(`${this.apiUrl}/${id}`);
  }

  calculateOverheadAllocation(preparationTimeMinutes: number): Observable<OverheadAllocation> {
    return this.http.get<OverheadAllocation>(`${this.apiUrl}/calculate`, {
      params: { preparationTimeMinutes: preparationTimeMinutes.toString() }
    });
  }

  initializeDefaultOverheadCosts(): Observable<any> {
    return this.http.post(`${this.apiUrl}/initialize`, {});
  }
}
