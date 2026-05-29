import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { handleServiceError } from '../utils/error-handler';

export interface Expense {
  id: string;
  date: string;
  expenseType: string;
  expenseSource: string;
  amount: number;
  paymentMethod: string;
  notes?: string;
  recordedBy: string;
  createdAt: string;
  updatedAt: string;
}

export interface CreateExpenseRequest {
  date: string;
  expenseType: string;
  expenseSource?: string;
  amount: number;
  paymentMethod: string;
  notes?: string;
}

export interface ExpenseSummary {
  date: string;
  totalExpenses: number;
  expenseTypeBreakdown: { [key: string]: number };
}

export interface HierarchicalExpense {
  year: number;
  totalAmount: number;
  expenseCount: number;
  months: MonthExpense[];
}

export interface MonthExpense {
  month: number;
  monthName: string;
  totalAmount: number;
  expenseCount: number;
  weeks: WeekExpense[];
}

export interface WeekExpense {
  week: number;
  weekLabel: string;
  totalAmount: number;
  expenseCount: number;
  expenses: Expense[];
}

export interface ExpenseAnalytics {
  summary: {
    totalExpenses: number;
    expenseCount: number;
    averageExpense: number;
    dailyAverage: number;
    growthRate: number;
    dateRange: { startDate: string; endDate: string };
    source: string;
  };
  topExpenseTypes: {
    expenseType: string;
    totalAmount: number;
    count: number;
    averageAmount: number;
    percentage: number;
  }[];
  paymentMethodBreakdown: {
    paymentMethod: string;
    totalAmount: number;
    count: number;
    percentage: number;
  }[];
  sourceBreakdown: {
    source: string;
    totalAmount: number;
    count: number;
    percentage: number;
  }[];
  weeklyTrend: {
    weekStart: string;
    totalAmount: number;
    count: number;
  }[];
  monthlyComparison: {
    year: number;
    month: number;
    monthName: string;
    totalAmount: number;
    count: number;
    averageExpense: number;
  }[];
  peakExpenseDays: {
    date: string;
    totalAmount: number;
    count: number;
  }[];
  expenseTypesTrend: {
    expenseType: string;
    monthlyTrend: {
      monthName: string;
      totalAmount: number;
    }[];
  }[];
}

@Injectable({
  providedIn: 'root'
})
export class ExpenseService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/expenses`;

  // Get all expenses
  getAllExpenses(): Observable<Expense[]> {
    return this.http.get<Expense[]>(this.apiUrl).pipe(
      catchError(handleServiceError('ExpenseService.getAllExpenses'))
    );
  }

  // Get expense by ID
  getExpenseById(id: string): Observable<Expense> {
    return this.http.get<Expense>(`${this.apiUrl}/detail/${id}`).pipe(
      catchError(handleServiceError('ExpenseService.getExpenseById'))
    );
  }

  // Get expenses by date range
  getExpensesByDateRange(startDate: string, endDate: string): Observable<Expense[]> {
    return this.http.get<Expense[]>(`${this.apiUrl}/range?startDate=${startDate}&endDate=${endDate}`).pipe(
      catchError(handleServiceError('ExpenseService.getExpensesByDateRange'))
    );
  }

  // Get expense summary by date
  getExpenseSummary(date: string): Observable<ExpenseSummary> {
    return this.http.get<ExpenseSummary>(`${this.apiUrl}/summary?date=${date}`).pipe(
      catchError(handleServiceError('ExpenseService.getExpenseSummary'))
    );
  }

  // Create new expense
  createExpense(expense: CreateExpenseRequest): Observable<Expense> {
    return this.http.post<Expense>(this.apiUrl, expense).pipe(
      catchError(handleServiceError('ExpenseService.createExpense'))
    );
  }

  // Update expense
  updateExpense(id: string, expense: CreateExpenseRequest): Observable<Expense> {
    return this.http.put<Expense>(`${this.apiUrl}/${id}`, expense).pipe(
      catchError(handleServiceError('ExpenseService.updateExpense'))
    );
  }

  // Delete expense
  deleteExpense(id: string): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`${this.apiUrl}/${id}`).pipe(
      catchError(handleServiceError('ExpenseService.deleteExpense'))
    );
  }

  // Upload expenses Excel
  uploadExpensesExcel(file: File, expenseSource: string = 'Offline'): Observable<any> {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post(`${this.apiUrl}/upload?source=${expenseSource}`, formData).pipe(
      catchError(handleServiceError('ExpenseService.uploadExpensesExcel'))
    );
  }

  // Get hierarchical expenses (Year -> Month -> Week)
  getHierarchicalExpenses(source: string = 'Offline'): Observable<HierarchicalExpense[]> {
    return this.http.get<HierarchicalExpense[]>(`${this.apiUrl}/hierarchical?source=${source}`).pipe(
      catchError(handleServiceError('ExpenseService.getHierarchicalExpenses'))
    );
  }

  // Get expense analytics
  getExpenseAnalytics(startDate?: string, endDate?: string, source: string = 'All'): Observable<ExpenseAnalytics> {
    let url = `${this.apiUrl}/analytics?source=${source}`;
    if (startDate) url += `&startDate=${startDate}`;
    if (endDate) url += `&endDate=${endDate}`;
    return this.http.get<ExpenseAnalytics>(url).pipe(
      catchError(handleServiceError('ExpenseService.getExpenseAnalytics'))
    );
  }

  // Get expense type options
  getExpenseTypes(): string[] {
    return ['Inventory', 'Salary', 'Rent', 'Utilities', 'Maintenance', 'Marketing', 'Other'];
  }

  // Repair orphaned expenses — set outletId for expenses uploaded without an outlet context
  repairExpenses(payload: { startDate: string; endDate: string; targetOutletId: string; filterBySource?: string; targetExpenseSource?: string; forceAllOutlets?: boolean }): Observable<{ message: string; updatedCount: number; targetOutletId: string; outletName: string }> {
    return this.http.post<any>(`${this.apiUrl}/repair`, payload).pipe(
      catchError(handleServiceError('ExpenseService.repairExpenses'))
    );
  }

  // Diagnose: get all expenses in a date range with no outlet/source filter
  diagnoseExpenses(startDate: string, endDate: string): Observable<{ startDate: string; endDate: string; totalRecords: number; groups: { outletId: string | null; outletName: string; expenseSource: string; count: number; totalAmount: number }[] }> {
    return this.http.get<any>(`${this.apiUrl}/diagnose?startDate=${startDate}&endDate=${endDate}`).pipe(
      catchError(handleServiceError('ExpenseService.diagnoseExpenses'))
    );
  }
}
