import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export interface OperationalExpense {
  id: string;
  month: number;
  year: number;
  rent: number;
  cookSalary: number;
  helperSalary: number;
  electricity: number;
  machineMaintenance: number;
  misc: number;
  totalOperationalCost: number;
  notes?: string;
  recordedBy: string;
  createdAt: string;
  updatedAt: string;
  transactionType: string;
}

export interface CreateOperationalExpenseRequest {
  month: number;
  year: number;
  cookSalary: number;
  helperSalary: number;
  electricity: number;
  machineMaintenance: number;
  misc: number;
  notes?: string;
}

export interface UpdateOperationalExpenseRequest {
  cookSalary: number;
  helperSalary: number;
  electricity: number;
  machineMaintenance: number;
  misc: number;
  notes?: string;
}

@Injectable({
  providedIn: 'root'
})
export class OperationalExpenseService {
  private apiUrl = environment.apiUrl;

  constructor(private http: HttpClient) {}

  getAllOperationalExpenses(): Observable<OperationalExpense[]> {
    return this.http.get<OperationalExpense[]>(`${this.apiUrl}/operational-expenses`);
  }

  getOperationalExpensesByYear(year: number): Observable<OperationalExpense[]> {
    return this.http.get<OperationalExpense[]>(`${this.apiUrl}/operational-expenses/year/${year}`);
  }

  getOperationalExpenseByMonthYear(year: number, month: number): Observable<OperationalExpense> {
    return this.http.get<OperationalExpense>(`${this.apiUrl}/operational-expenses/${year}/${month}`);
  }

  calculateRentForMonth(year: number, month: number): Observable<{ rent: number }> {
    return this.http.get<{ rent: number }>(`${this.apiUrl}/operational-expenses/calculate-rent/${year}/${month}`);
  }

  createOperationalExpense(request: CreateOperationalExpenseRequest): Observable<OperationalExpense> {
    return this.http.post<OperationalExpense>(`${this.apiUrl}/operational-expenses`, request);
  }

  updateOperationalExpense(id: string, request: UpdateOperationalExpenseRequest): Observable<OperationalExpense> {
    return this.http.put<OperationalExpense>(`${this.apiUrl}/operational-expenses/${id}`, request);
  }

  deleteOperationalExpense(id: string): Observable<any> {
    return this.http.delete(`${this.apiUrl}/operational-expenses/${id}`);
  }
}
