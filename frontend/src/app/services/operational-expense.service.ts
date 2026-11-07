import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { environment } from '../../environments/environment';

export interface OperationalCostInput {
  key: string;
  label: string;
  expectedValue: number;
  actualValue: number;
}

export const DEFAULT_OPERATIONAL_COST_TEMPLATE: ReadonlyArray<Omit<OperationalCostInput, 'actualValue'>> = [
  { key: 'staffSalaries', label: 'Staff Salaries', expectedValue: 70000 },
  { key: 'rent', label: 'Rent', expectedValue: 32000 },
  { key: 'electricity', label: 'Electricity', expectedValue: 9000 },
  { key: 'lpgCookingGas', label: 'Gas', expectedValue: 9000 },
  { key: 'water', label: 'Water', expectedValue: 1000 },
  { key: 'internetCctv', label: 'Internet + CCTV', expectedValue: 2000 },
  { key: 'cleaningSupplies', label: 'Cleaning supplies', expectedValue: 3000 },
  { key: 'pestControl', label: 'Pest control', expectedValue: 1000 },
  { key: 'repairsReserve', label: 'Maintenance Reserve', expectedValue: 3000 },
  { key: 'localMarketing', label: 'Promotions', expectedValue: 2000 },
  { key: 'staffFoodTeaMisc', label: 'Staff food', expectedValue: 2000 },
  { key: 'tissueGlovesDisposables', label: 'Tissue, gloves, disposables', expectedValue: 1000 },
  { key: 'emi', label: 'EMI', expectedValue: 22000 },
  { key: 'contingencyBucket', label: 'Contingency bucket', expectedValue: 2000 },
  { key: 'petrolDeliveryBoy', label: 'Petrol', expectedValue: 1000 }
];

export const AUTO_CALCULATED_COST_INPUT_EXPENSE_TYPES: Readonly<Record<string, readonly string[]>> = {
  lpgCookingGas: ['gas', 'lpg', 'lpg cooking gas', 'lpg/cookinggas'],
  water: ['water'],
  staffFoodTeaMisc: ['staff food', 'stafffood', 'staff food tea miscellaneous', 'staff food tea misc'],
  petrolDeliveryBoy: ['petrol', 'petrol for delivery boy', 'delivery petrol']
};

export function normalizeExpenseType(value: string): string {
  return value.toLowerCase().replace(/[^a-z0-9]/g, '');
}

export interface OperationalExpense {
  id: string;
  month: number;
  year: number;
  costInputs: OperationalCostInput[];
  rent?: number;
  cookSalary?: number;
  helperSalary?: number;
  electricity?: number;
  machineMaintenance?: number;
  misc?: number;
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
  costInputs: OperationalCostInput[];
  transactionType: string;
  notes?: string;
}

export interface UpdateOperationalExpenseRequest {
  costInputs: OperationalCostInput[];
  transactionType: string;
  notes?: string;
}

@Injectable({
  providedIn: 'root'
})
export class OperationalExpenseService {
  private apiUrl = environment.apiUrl;

  constructor(private http: HttpClient) {}

  getAllOperationalExpenses(): Observable<OperationalExpense[]> {
    return this.http
      .get<OperationalExpense[]>(`${this.apiUrl}/operational-expenses`)
      .pipe(map(expenses => expenses.map(expense => this.normalizeOperationalExpense(expense))));
  }

  getOperationalExpensesByYear(year: number): Observable<OperationalExpense[]> {
    return this.http
      .get<OperationalExpense[]>(`${this.apiUrl}/operational-expenses/year/${year}`)
      .pipe(map(expenses => expenses.map(expense => this.normalizeOperationalExpense(expense))));
  }

  getOperationalExpenseByMonthYear(year: number, month: number): Observable<OperationalExpense> {
    return this.http
      .get<OperationalExpense>(`${this.apiUrl}/operational-expenses/${year}/${month}`)
      .pipe(map(expense => this.normalizeOperationalExpense(expense)));
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

  createDefaultCostInputs(): OperationalCostInput[] {
    return DEFAULT_OPERATIONAL_COST_TEMPLATE.map(item => ({
      key: item.key,
      label: item.label,
      expectedValue: item.expectedValue,
      actualValue: 0
    }));
  }

  private normalizeOperationalExpense(expense: OperationalExpense): OperationalExpense {
    const costInputs = this.normalizeCostInputs(expense);
    const calculatedTotal = costInputs.reduce((sum, item) => sum + (Number(item.actualValue) || 0), 0);

    return {
      ...expense,
      costInputs,
      transactionType: expense.transactionType || 'Cash',
      totalOperationalCost: calculatedTotal
    };
  }

  private normalizeCostInputs(expense: OperationalExpense): OperationalCostInput[] {
    const existingByKey = new Map(
      (expense.costInputs || []).map(item => [item.key, item])
    );

    const hasCostInputs = existingByKey.size > 0;
    const legacyValuesByKey: Record<string, number> = {
      staffSalaries: (Number(expense.cookSalary) || 0) + (Number(expense.helperSalary) || 0),
      rent: Number(expense.rent) || 0,
      electricity: Number(expense.electricity) || 0,
      repairsReserve: Number(expense.machineMaintenance) || 0,
      staffFoodTeaMisc: Number(expense.misc) || 0
    };

    return DEFAULT_OPERATIONAL_COST_TEMPLATE.map(template => {
      const existing = existingByKey.get(template.key);
      const fallbackActual = hasCostInputs ? 0 : (legacyValuesByKey[template.key] || 0);

      return {
        key: template.key,
        label: existing?.label || template.label,
        expectedValue: Number(existing?.expectedValue ?? template.expectedValue) || 0,
        actualValue: Number(existing?.actualValue ?? fallbackActual) || 0
      };
    });
  }
}
