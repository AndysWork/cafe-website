import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { handleServiceError } from '../utils/error-handler';

export interface RecommendedItem {
  menuItemId: string;
  name: string;
  category: string;
  onlinePrice: number;
  imageUrl?: string;
  reason: string;
  score: number;
}

export interface TrendingItem {
  menuItemId: string;
  name: string;
  category: string;
  onlinePrice: number;
  imageUrl?: string;
  orderCount: number;
}

@Injectable({ providedIn: 'root' })
export class RecommendationService {
  private http = inject(HttpClient);
  private apiUrl = environment.apiUrl;

  getPersonalRecommendations(): Observable<RecommendedItem[]> {
    return this.http.get<RecommendedItem[]>(`${this.apiUrl}/recommendations`).pipe(
      catchError(handleServiceError('RecommendationService.getPersonalRecommendations'))
    );
  }

  getTrendingItems(limit: number = 10): Observable<TrendingItem[]> {
    return this.http.get<TrendingItem[]>(`${this.apiUrl}/recommendations/trending?limit=${limit}`).pipe(
      catchError(handleServiceError('RecommendationService.getTrendingItems'))
    );
  }
}
