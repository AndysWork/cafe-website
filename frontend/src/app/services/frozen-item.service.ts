import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { FrozenItem, FrozenItemUpload } from '../models/ingredient.model';

@Injectable({
  providedIn: 'root'
})
export class FrozenItemService {
  private apiUrl = `${environment.apiUrl}/frozen-items`;

  constructor(private http: HttpClient) {}

  getAllFrozenItems(): Observable<FrozenItem[]> {
    return this.http.get<FrozenItem[]>(this.apiUrl);
  }

  getActiveFrozenItems(): Observable<FrozenItem[]> {
    return this.http.get<FrozenItem[]>(`${this.apiUrl}/active`);
  }

  getFrozenItemById(id: string): Observable<FrozenItem> {
    return this.http.get<FrozenItem>(`${this.apiUrl}/${id}`);
  }

  createFrozenItem(item: FrozenItem): Observable<FrozenItem> {
    return this.http.post<FrozenItem>(this.apiUrl, item);
  }

  updateFrozenItem(id: string, item: FrozenItem): Observable<any> {
    return this.http.put(`${this.apiUrl}/${id}`, item);
  }

  deleteFrozenItem(id: string): Observable<any> {
    return this.http.delete(`${this.apiUrl}/${id}`);
  }

  uploadExcel(file: File): Observable<{
    success: number;
    failed: number;
    total: number;
    errors: string[];
    message: string;
  }> {
    const formData = new FormData();
    formData.append('file', file);

    return this.http.post<{
      success: number;
      failed: number;
      total: number;
      errors: string[];
      message: string;
    }>(`${this.apiUrl}/upload`, formData);
  }
}
