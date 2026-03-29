import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { handleServiceError } from '../utils/error-handler';

export interface ComboItem {
  menuItemId: string;
  menuItemName?: string;
  quantity: number;
  originalPrice?: number;
}

export interface ComboMeal {
  id?: string;
  name: string;
  description?: string;
  items: ComboItem[];
  originalPrice: number;
  comboPrice: number;
  savingsAmount: number;
  imageUrl?: string;
  isActive: boolean;
  outletId?: string;
  createdAt?: string;
}

export interface CreateComboRequest {
  name: string;
  description?: string;
  items: { menuItemId: string; quantity: number }[];
  comboPrice: number;
  imageUrl?: string;
}

@Injectable({ providedIn: 'root' })
export class ComboMealService {
  private http = inject(HttpClient);
  private apiUrl = environment.apiUrl;

  getActiveCombos(): Observable<ComboMeal[]> {
    return this.http.get<ComboMeal[]>(`${this.apiUrl}/combos`).pipe(
      catchError(handleServiceError('ComboMealService.getActiveCombos'))
    );
  }

  getAllCombos(): Observable<ComboMeal[]> {
    return this.http.get<ComboMeal[]>(`${this.apiUrl}/manage/combos`).pipe(
      catchError(handleServiceError('ComboMealService.getAllCombos'))
    );
  }

  createCombo(combo: CreateComboRequest): Observable<ComboMeal> {
    return this.http.post<ComboMeal>(`${this.apiUrl}/manage/combos`, combo).pipe(
      catchError(handleServiceError('ComboMealService.createCombo'))
    );
  }

  updateCombo(id: string, combo: CreateComboRequest): Observable<ComboMeal> {
    return this.http.put<ComboMeal>(`${this.apiUrl}/manage/combos/${id}`, combo).pipe(
      catchError(handleServiceError('ComboMealService.updateCombo'))
    );
  }

  deleteCombo(id: string): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`${this.apiUrl}/manage/combos/${id}`).pipe(
      catchError(handleServiceError('ComboMealService.deleteCombo'))
    );
  }
}
