import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export interface Expense {
  id: string;
  date: string;
  expenseType: string;
  description: string;
  amount: number;
  vendor?: string;
  paymentMethod: string;
  invoiceNumber?: string;
  notes?: string;
  recordedBy: string;
  createdAt: string;
  updatedAt: string;
}

export interface CreateExpenseRequest {
  date: string;
  expenseType: string;
  description: string;
  amount: number;
  vendor?: string;
  paymentMethod: string;
  invoiceNumber?: string;
  notes?: string;
}

export interface ExpenseSummary {
  date: string;
  totalExpenses: number;
  expenseTypeBreakdown: { [key: string]: number };
}

@Injectable({
  providedIn: 'root'
})
export class ExpenseService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/expenses`;

  // Get all expenses
  getAllExpenses(): Observable<Expense[]> {
    return this.http.get<Expense[]>(this.apiUrl);
  }

  // Get expenses by date range
  getExpensesByDateRange(startDate: string, endDate: string): Observable<Expense[]> {
    return this.http.get<Expense[]>(`${this.apiUrl}/range?startDate=${startDate}&endDate=${endDate}`);
  }

  // Get expense summary by date
  getExpenseSummary(date: string): Observable<ExpenseSummary> {
    return this.http.get<ExpenseSummary>(`${this.apiUrl}/summary?date=${date}`);
  }

  // Create new expense
  createExpense(expense: CreateExpenseRequest): Observable<Expense> {
    return this.http.post<Expense>(this.apiUrl, expense);
  }

  // Update expense
  updateExpense(id: string, expense: CreateExpenseRequest): Observable<Expense> {
    return this.http.put<Expense>(`${this.apiUrl}/${id}`, expense);
  }

  // Delete expense
  deleteExpense(id: string): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`${this.apiUrl}/${id}`);
  }

  // Upload expenses Excel
  uploadExpensesExcel(file: File): Observable<any> {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post(`${this.apiUrl}/upload`, formData);
  }

  // Get expense type options
  getExpenseTypes(): string[] {
    return ['Inventory', 'Salary', 'Rent', 'Utilities', 'Maintenance', 'Marketing', 'Other'];
  }
}
