import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { handleServiceError } from '../utils/error-handler';

@Injectable({ providedIn: 'root' })
export class FavoriteService {
  private http = inject(HttpClient);
  private apiUrl = environment.apiUrl;

  getMyFavorites(): Observable<string[]> {
    return this.http.get<string[]>(`${this.apiUrl}/favorites`).pipe(
      catchError(handleServiceError('FavoriteService.getMyFavorites'))
    );
  }

  toggleFavorite(menuItemId: string): Observable<{ isFavorite: boolean; menuItemId: string }> {
    return this.http.post<{ isFavorite: boolean; menuItemId: string }>(`${this.apiUrl}/favorites/${menuItemId}`, {}).pipe(
      catchError(handleServiceError('FavoriteService.toggleFavorite'))
    );
  }
}
