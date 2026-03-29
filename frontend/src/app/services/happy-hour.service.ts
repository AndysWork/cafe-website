import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { handleServiceError } from '../utils/error-handler';

export interface HappyHourRule {
  id?: string;
  name: string;
  startTime: string;
  endTime: string;
  daysOfWeek: number[];
  discountType: 'percentage' | 'flat';
  discountValue: number;
  maxDiscount: number;
  applicableCategories?: string[];
  isActive: boolean;
  outletId?: string;
  createdAt?: string;
}

export interface ActiveHappyHour {
  id: string;
  name: string;
  discountType: string;
  discountValue: number;
  maxDiscount: number;
  endsAt: string;
}

@Injectable({ providedIn: 'root' })
export class HappyHourService {
  private http = inject(HttpClient);
  private apiUrl = environment.apiUrl;

  getActiveHappyHours(): Observable<ActiveHappyHour[]> {
    return this.http.get<ActiveHappyHour[]>(`${this.apiUrl}/happy-hours/active`).pipe(
      catchError(handleServiceError('HappyHourService.getActiveHappyHours'))
    );
  }

  getAllHappyHours(): Observable<HappyHourRule[]> {
    return this.http.get<HappyHourRule[]>(`${this.apiUrl}/manage/happy-hours`).pipe(
      catchError(handleServiceError('HappyHourService.getAllHappyHours'))
    );
  }

  createHappyHour(rule: Partial<HappyHourRule>): Observable<HappyHourRule> {
    return this.http.post<HappyHourRule>(`${this.apiUrl}/manage/happy-hours`, rule).pipe(
      catchError(handleServiceError('HappyHourService.createHappyHour'))
    );
  }

  updateHappyHour(id: string, rule: Partial<HappyHourRule>): Observable<HappyHourRule> {
    return this.http.put<HappyHourRule>(`${this.apiUrl}/manage/happy-hours/${id}`, rule).pipe(
      catchError(handleServiceError('HappyHourService.updateHappyHour'))
    );
  }

  deleteHappyHour(id: string): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`${this.apiUrl}/manage/happy-hours/${id}`).pipe(
      catchError(handleServiceError('HappyHourService.deleteHappyHour'))
    );
  }
}
