import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map, shareReplay } from 'rxjs/operators';
import { environment } from '../../environments/environment';

export type BonusRuleType = 'OvertimeHours' | 'UndertimeHours' | 'SnacksPreparation' | 'BadOrders' | 'GoodRatings' | 'RefundDeduction';
export type CalculationType = 'PerUnit' | 'PerHour' | 'Percentage' | 'Fixed';
export type CalculationPeriod = 'Monthly' | 'Quarterly' | 'Yearly';

export interface StaffRateOverride {
  staffId: string;
  customRate: number;
  notes?: string;
}

export interface BonusRule {
  ruleType: BonusRuleType;
  isBonus: boolean;
  calculationType: CalculationType;
  rateAmount: number;
  percentageValue?: number;
  threshold?: number;
  maxAmount?: number;
  description?: string;
  useDynamicRate?: boolean;
  rateMultiplier?: number;
  staffRateOverrides?: StaffRateOverride[];
}

export interface BonusConfiguration {
  id?: string;
  outletId: string;
  configurationName: string;
  applicablePositions: string[];
  rules: BonusRule[];
  calculationPeriod: CalculationPeriod;
  isActive: boolean;
  createdAt?: string;
  updatedAt?: string;
}

export interface BonusRuleRequest {
  ruleType: string;
  isBonus: boolean;
  calculationType: string;
  rateAmount: number;
  percentageValue?: number;
  threshold?: number;
  maxAmount?: number;
  description?: string;
  useDynamicRate?: boolean;
  rateMultiplier?: number;
  staffRateOverrides?: StaffRateOverride[];
}

export interface CreateBonusConfigurationRequest {
  configurationName: string;
  applicablePositions: string[];
  rules: BonusRuleRequest[];
  calculationPeriod: string;
  isActive: boolean;
}

export interface UpdateBonusConfigurationRequest {
  configurationName?: string;
  applicablePositions?: string[];
  rules?: BonusRuleRequest[];
  calculationPeriod?: string;
  isActive?: boolean;
}

@Injectable({
  providedIn: 'root'
})
export class BonusConfigurationService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/bonus-configurations`;

  private getHeaders(): HttpHeaders {
    const token = localStorage.getItem('token');
    return new HttpHeaders({
      'Content-Type': 'application/json',
      'Authorization': `Bearer ${token}`
    });
  }

  getBonusConfigurations(): Observable<BonusConfiguration[]> {
    return this.http.get<{ success: boolean; data: BonusConfiguration[] }>(this.apiUrl, { headers: this.getHeaders() })
      .pipe(map(response => response.data), shareReplay({ bufferSize: 1, refCount: true }));
  }

  getBonusConfigurationById(id: string): Observable<BonusConfiguration> {
    return this.http.get<{ success: boolean; data: BonusConfiguration }>(`${this.apiUrl}/${id}`, { headers: this.getHeaders() })
      .pipe(map(response => response.data));
  }

  getBonusConfigurationsForStaff(staffId: string): Observable<BonusConfiguration[]> {
    return this.http.get<{ success: boolean; data: BonusConfiguration[] }>(`${this.apiUrl}/staff/${staffId}`, { headers: this.getHeaders() })
      .pipe(map(response => response.data));
  }

  createBonusConfiguration(request: CreateBonusConfigurationRequest): Observable<BonusConfiguration> {
    return this.http.post<{ success: boolean; data: BonusConfiguration }>(this.apiUrl, request, { headers: this.getHeaders() })
      .pipe(map(response => response.data));
  }

  updateBonusConfiguration(id: string, request: UpdateBonusConfigurationRequest): Observable<BonusConfiguration> {
    return this.http.put<{ success: boolean; data: BonusConfiguration }>(`${this.apiUrl}/${id}`, request, { headers: this.getHeaders() })
      .pipe(map(response => response.data));
  }

  deleteBonusConfiguration(id: string): Observable<any> {
    return this.http.delete<{ success: boolean; message: string }>(`${this.apiUrl}/${id}`, { headers: this.getHeaders() })
      .pipe(map(response => response));
  }

  toggleActiveStatus(id: string): Observable<BonusConfiguration> {
    return this.http.patch<{ success: boolean; data: BonusConfiguration }>(`${this.apiUrl}/${id}/toggle-active`, {}, { headers: this.getHeaders() })
      .pipe(map(response => response.data));
  }
}
