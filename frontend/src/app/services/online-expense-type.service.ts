import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export interface OnlineExpenseType {
  id?: string;
  expenseType: string;
  isActive: boolean;
  createdAt?: Date;
  updatedAt?: Date;
}

export interface CreateOnlineExpenseTypeRequest {
  expenseType: string;
}

@Injectable({
  providedIn: 'root'
})
export class OnlineExpenseTypeService {
  private apiUrl = `${environment.apiUrl}/onlineexpensetypes`;

  constructor(private http: HttpClient) { }

  getAllOnlineExpenseTypes(): Observable<OnlineExpenseType[]> {
    return this.http.get<OnlineExpenseType[]>(this.apiUrl);
  }

  getActiveOnlineExpenseTypes(): Observable<OnlineExpenseType[]> {
    return this.http.get<OnlineExpenseType[]>(`${this.apiUrl}/active`);
  }

  createOnlineExpenseType(request: CreateOnlineExpenseTypeRequest): Observable<OnlineExpenseType> {
    return this.http.post<OnlineExpenseType>(this.apiUrl, request);
  }

  updateOnlineExpenseType(id: string, request: CreateOnlineExpenseTypeRequest): Observable<any> {
    return this.http.put(`${this.apiUrl}/${id}`, request);
  }

  deleteOnlineExpenseType(id: string): Observable<any> {
    return this.http.delete(`${this.apiUrl}/${id}`);
  }

  initializeDefaultExpenseTypes(): Observable<any> {
    return this.http.post(`${this.apiUrl}/initialize`, {});
  }
}
