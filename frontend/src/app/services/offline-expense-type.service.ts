import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export interface OfflineExpenseType {
  id: string;
  expenseType: string;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface CreateOfflineExpenseTypeRequest {
  expenseType: string;
}

@Injectable({
  providedIn: 'root'
})
export class OfflineExpenseTypeService {
  private apiUrl = `${environment.apiUrl}/offlineexpensetypes`;

  constructor(private http: HttpClient) {}

  getAllOfflineExpenseTypes(): Observable<OfflineExpenseType[]> {
    return this.http.get<OfflineExpenseType[]>(this.apiUrl);
  }

  getActiveOfflineExpenseTypes(): Observable<OfflineExpenseType[]> {
    return this.http.get<OfflineExpenseType[]>(`${this.apiUrl}/active`);
  }

  createOfflineExpenseType(data: CreateOfflineExpenseTypeRequest): Observable<OfflineExpenseType> {
    return this.http.post<OfflineExpenseType>(this.apiUrl, data);
  }

  updateOfflineExpenseType(id: string, data: CreateOfflineExpenseTypeRequest): Observable<any> {
    return this.http.put(`${this.apiUrl}/${id}`, data);
  }

  deleteOfflineExpenseType(id: string): Observable<any> {
    return this.http.delete(`${this.apiUrl}/${id}`);
  }

  initializeDefaultExpenseTypes(): Observable<any> {
    return this.http.post(`${this.apiUrl}/initialize`, {});
  }
}
