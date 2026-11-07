import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { UIStore } from '../../store/ui.store';
import { Expense, ExpenseService } from '../../services/expense.service';
import {
  OperationalExpenseService,
  OperationalExpense,
  CreateOperationalExpenseRequest,
  UpdateOperationalExpenseRequest,
  OperationalCostInput,
  AUTO_CALCULATED_COST_INPUT_EXPENSE_TYPES,
  normalizeExpenseType
} from '../../services/operational-expense.service';
import { OutletService } from '../../services/outlet.service';
import { Subscription } from 'rxjs';
import { filter } from 'rxjs/operators';
import { formatIstDate } from '../../utils/date-utils';

@Component({
  selector: 'app-operational-expenses',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './operational-expenses.component.html',
  styleUrl: './operational-expenses.component.scss'
})
export class OperationalExpensesComponent implements OnInit, OnDestroy {
  private outletService = inject(OutletService);
  private uiStore = inject(UIStore);
  private outletSubscription?: Subscription;

  expenses: OperationalExpense[] = [];
  loading = false;
  showModal = false;
  isEditMode = false;
  editingId: string | null = null;
  autoCalculatingExpenseValues = false;

  // Grouping by year
  groupedExpenses: { [year: number]: OperationalExpense[] } = {};
  expandedYears: Set<number> = new Set();

  formData: CreateOperationalExpenseRequest = {
    month: new Date().getMonth() + 1,
    year: new Date().getFullYear(),
    costInputs: [],
    transactionType: 'Cash',
    notes: ''
  };
  operationalPaymentMethods = ['Cash', 'Online', 'Card', 'UPI'];

  months = [
    { value: 1, name: 'January' },
    { value: 2, name: 'February' },
    { value: 3, name: 'March' },
    { value: 4, name: 'April' },
    { value: 5, name: 'May' },
    { value: 6, name: 'June' },
    { value: 7, name: 'July' },
    { value: 8, name: 'August' },
    { value: 9, name: 'September' },
    { value: 10, name: 'October' },
    { value: 11, name: 'November' },
    { value: 12, name: 'December' }
  ];

  constructor(
    private operationalExpenseService: OperationalExpenseService,
    private expenseService: ExpenseService
  ) {}

  ngOnInit() {
    // Subscribe to outlet changes
    this.outletSubscription = this.outletService.selectedOutlet$
      .pipe(filter(outlet => outlet !== null))
      .subscribe(() => {
        this.loadExpenses();
      });

    // Load immediately if outlet is already selected
    if (this.outletService.getSelectedOutlet()) {
      this.loadExpenses();
    }
  }

  ngOnDestroy() {
    this.outletSubscription?.unsubscribe();
  }

  loadExpenses() {
    this.loading = true;
    this.operationalExpenseService.getAllOperationalExpenses().subscribe({
      next: (data) => {
        this.expenses = data;
        this.groupExpensesByYear();
        this.loading = false;
      },
      error: (error) => {
        console.error('Error loading operational expenses:', error);
        this.uiStore.error('Failed to load operational expenses');
        this.loading = false;
      }
    });
  }

  groupExpensesByYear() {
    this.groupedExpenses = {};
    this.expenses.forEach(expense => {
      if (!this.groupedExpenses[expense.year]) {
        this.groupedExpenses[expense.year] = [];
      }
      this.groupedExpenses[expense.year].push(expense);
    });

    // Automatically expand current year
    const currentYear = new Date().getFullYear();
    this.expandedYears.add(currentYear);
  }

  getYears(): number[] {
    return Object.keys(this.groupedExpenses)
      .map(y => parseInt(y))
      .sort((a, b) => b - a);
  }

  toggleYear(year: number) {
    if (this.expandedYears.has(year)) {
      this.expandedYears.delete(year);
    } else {
      this.expandedYears.add(year);
    }
  }

  isYearExpanded(year: number): boolean {
    return this.expandedYears.has(year);
  }

  getYearTotal(year: number): number {
    if (!this.groupedExpenses[year]) return 0;
    return this.groupedExpenses[year].reduce((sum, e) => sum + e.totalOperationalCost, 0);
  }

  openAddModal() {
    this.isEditMode = false;
    this.editingId = null;
    this.resetForm();
    this.showModal = true;
    this.autoFillInputsFromExpenses();
  }

  openEditModal(expense: OperationalExpense) {
    this.isEditMode = true;
    this.editingId = expense.id;
    this.formData = {
      month: expense.month,
      year: expense.year,
      costInputs: expense.costInputs.map(item => ({
        key: item.key,
        label: item.label,
        expectedValue: Number(item.expectedValue) || 0,
        actualValue: Number(item.actualValue) || 0
      })),
      transactionType: expense.transactionType || 'Cash',
      notes: expense.notes || ''
    };
    this.showModal = true;
    this.autoFillInputsFromExpenses();
  }

  closeModal() {
    this.showModal = false;
    this.editingId = null;
    this.resetForm();
  }

  resetForm() {
    const currentDate = new Date();
    this.formData = {
      month: currentDate.getMonth() + 1,
      year: currentDate.getFullYear(),
      costInputs: this.operationalExpenseService.createDefaultCostInputs(),
      transactionType: 'Cash',
      notes: ''
    };
  }

  autoFillInputsFromExpenses() {
    if (!this.formData.month || !this.formData.year) {
      this.uiStore.warning('Please select month and year first');
      return;
    }

    const { startDate, endDate } = this.getMonthDateRange(this.formData.year, this.formData.month);
    this.autoCalculatingExpenseValues = true;

    this.expenseService.getExpensesByDateRange(startDate, endDate).subscribe({
      next: (expenses) => {
        for (let i = 0; i < this.formData.costInputs.length; i++) {
          const input = this.formData.costInputs[i];
          const autoCalculatedValue = this.getAutoCalculatedValueForInput(input.key, expenses);
          if (autoCalculatedValue !== null) {
            this.formData.costInputs[i].actualValue = autoCalculatedValue;
          }
        }
        this.autoCalculatingExpenseValues = false;
      },
      error: (error) => {
        console.error('Error auto-calculating operational values from expenses:', error);
        this.uiStore.error('Failed to auto-calculate values from expenses');
        this.autoCalculatingExpenseValues = false;
      }
    });
  }

  saveExpense() {
    if (!this.formData.month || !this.formData.year) {
      this.uiStore.warning('Please select month and year');
      return;
    }

    this.loading = true;

    if (this.isEditMode && this.editingId) {
      const updateRequest: UpdateOperationalExpenseRequest = {
        costInputs: this.normalizeCostInputs(this.formData.costInputs),
        transactionType: this.formData.transactionType || 'Cash',
        notes: this.formData.notes
      };

      this.operationalExpenseService.updateOperationalExpense(this.editingId, updateRequest).subscribe({
        next: () => {
          this.loadExpenses();
          this.closeModal();
          this.loading = false;
        },
        error: (error) => {
          console.error('Error updating operational expense:', error);
          this.uiStore.error('Failed to update operational expense');
          this.loading = false;
        }
      });
    } else {
      const createRequest: CreateOperationalExpenseRequest = {
        month: Number(this.formData.month),
        year: Number(this.formData.year),
        costInputs: this.normalizeCostInputs(this.formData.costInputs),
        transactionType: this.formData.transactionType || 'Cash',
        notes: this.formData.notes
      };
      this.operationalExpenseService.createOperationalExpense(createRequest).subscribe({
        next: () => {
          this.loadExpenses();
          this.closeModal();
          this.loading = false;
        },
        error: (error) => {
          console.error('Error creating operational expense:', error);
          const errorMsg = error.error?.error || 'Failed to create operational expense';
          this.uiStore.error(errorMsg);
          this.loading = false;
        }
      });
    }
  }

  deleteExpense(id: string) {
    if (!confirm('Are you sure you want to delete this operational expense record?')) return;

    this.operationalExpenseService.deleteOperationalExpense(id).subscribe({
      next: () => {
        this.loadExpenses();
      },
      error: (error) => {
        console.error('Error deleting operational expense:', error);
        this.uiStore.error('Failed to delete operational expense');
      }
    });
  }

  getMonthName(month: number): string {
    const monthObj = this.months.find(m => m.value === month);
    return monthObj ? monthObj.name : '';
  }

  formatCurrency(amount: number): string {
    return `₹${amount.toFixed(2)}`;
  }

  formatDate(dateStr: string): string {
    return formatIstDate(dateStr, { day: '2-digit', month: 'short', year: 'numeric' });
  }

  getTotalOperationalCost(): number {
    return this.formData.costInputs.reduce((sum, item) => sum + (Number(item.actualValue) || 0), 0);
  }

  getTotalExpectedCost(): number {
    return this.formData.costInputs.reduce((sum, item) => sum + (Number(item.expectedValue) || 0), 0);
  }

  private normalizeCostInputs(costInputs: OperationalCostInput[]): OperationalCostInput[] {
    return costInputs.map(item => ({
      key: item.key,
      label: item.label,
      expectedValue: Number(item.expectedValue) || 0,
      actualValue: Number(item.actualValue) || 0
    }));
  }

  private getAutoCalculatedValueForInput(costInputKey: string, expenses: Expense[]): number | null {
    const aliases = AUTO_CALCULATED_COST_INPUT_EXPENSE_TYPES[costInputKey];
    if (!aliases || aliases.length === 0) {
      return null;
    }

    const aliasSet = new Set(aliases.map(alias => normalizeExpenseType(alias)));
    return expenses.reduce((sum, expense) => {
      const normalizedType = normalizeExpenseType(expense.expenseType || '');
      if (!aliasSet.has(normalizedType)) {
        return sum;
      }
      return sum + (Number(expense.amount) || 0);
    }, 0);
  }

  private getMonthDateRange(year: number, month: number): { startDate: string; endDate: string } {
    const monthString = String(month).padStart(2, '0');
    const lastDay = new Date(year, month, 0).getDate();
    const endDayString = String(lastDay).padStart(2, '0');

    return {
      startDate: `${year}-${monthString}-01`,
      endDate: `${year}-${monthString}-${endDayString}`
    };
  }

  trackByCostInputKey(index: number, item: OperationalCostInput): string { return item.key; }

  trackByObjId(index: number, item: any): string { return item.id; }

  trackByIndex(index: number): number { return index; }
}
