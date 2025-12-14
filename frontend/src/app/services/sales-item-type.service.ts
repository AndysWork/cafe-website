import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export interface SalesItemType {
  id: string;
  itemName: string;
  defaultPrice: number;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface CreateSalesItemTypeRequest {
  itemName: string;
  defaultPrice: number;
}

@Injectable({
  providedIn: 'root'
})
export class SalesItemTypeService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/salesitemtypes`;

  getAllSalesItemTypes(): Observable<SalesItemType[]> {
    return this.http.get<SalesItemType[]>(this.apiUrl);
  }

  getActiveSalesItemTypes(): Observable<SalesItemType[]> {
    return this.http.get<SalesItemType[]>(`${this.apiUrl}/active`);
  }

  createSalesItemType(data: CreateSalesItemTypeRequest): Observable<SalesItemType> {
    return this.http.post<SalesItemType>(this.apiUrl, data);
  }

  updateSalesItemType(id: string, data: SalesItemType): Observable<SalesItemType> {
    return this.http.put<SalesItemType>(`${this.apiUrl}/${id}`, data);
  }

  deleteSalesItemType(id: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`);
  }

  initializeDefaultItems(): Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/initialize`, {});
  }
}
