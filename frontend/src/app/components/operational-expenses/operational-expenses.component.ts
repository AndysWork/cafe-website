import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { OperationalExpenseService, OperationalExpense, CreateOperationalExpenseRequest, UpdateOperationalExpenseRequest } from '../../services/operational-expense.service';
import { OutletService } from '../../services/outlet.service';
import { Subscription } from 'rxjs';
import { filter } from 'rxjs/operators';
import { getIstDateString, formatIstDate, getIstNow } from '../../utils/date-utils';

@Component({
  selector: 'app-operational-expenses',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './operational-expenses.component.html',
  styleUrl: './operational-expenses.component.scss'
})
export class OperationalExpensesComponent implements OnInit, OnDestroy {
  private outletService = inject(OutletService);
  private outletSubscription?: Subscription;

  expenses: OperationalExpense[] = [];
  loading = false;
  showModal = false;
  isEditMode = false;
  editingId: string | null = null;
  calculatedRent: number = 0;
  calculatingRent = false;

  // Grouping by year
  groupedExpenses: { [year: number]: OperationalExpense[] } = {};
  expandedYears: Set<number> = new Set();

  formData: CreateOperationalExpenseRequest = {
    month: new Date().getMonth() + 1,
    year: new Date().getFullYear(),
    cookSalary: 0,
    helperSalary: 0,
    electricity: 0,
    machineMaintenance: 0,
    misc: 0,
    notes: ''
  };

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

  constructor(private operationalExpenseService: OperationalExpenseService) {}

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
        alert('Failed to load operational expenses');
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

  calculateRent() {
    if (!this.formData.month || !this.formData.year) {
      alert('Please select month and year first');
      return;
    }

    this.calculatingRent = true;
    this.operationalExpenseService.calculateRentForMonth(this.formData.year, this.formData.month).subscribe({
      next: (data) => {
        this.calculatedRent = data.rent;
        this.calculatingRent = false;
      },
      error: (error) => {
        console.error('Error calculating rent:', error);
        alert('Failed to calculate rent');
        this.calculatingRent = false;
      }
    });
  }

  openAddModal() {
    this.isEditMode = false;
    this.editingId = null;
    this.resetForm();
    this.showModal = true;
    // Calculate rent for current month
    this.calculateRent();
  }

  openEditModal(expense: OperationalExpense) {
    this.isEditMode = true;
    this.editingId = expense.id;
    this.formData = {
      month: expense.month,
      year: expense.year,
      cookSalary: expense.cookSalary,
      helperSalary: expense.helperSalary,
      electricity: expense.electricity,
      machineMaintenance: expense.machineMaintenance,
      misc: expense.misc,
      notes: expense.notes || ''
    };
    this.calculatedRent = expense.rent;
    this.showModal = true;
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
      cookSalary: 0,
      helperSalary: 0,
      electricity: 0,
      machineMaintenance: 0,
      misc: 0,
      notes: ''
    };
    this.calculatedRent = 0;
  }

  saveExpense() {
    if (!this.formData.month || !this.formData.year) {
      alert('Please select month and year');
      return;
    }

    this.loading = true;

    if (this.isEditMode && this.editingId) {
      const updateRequest: UpdateOperationalExpenseRequest = {
        cookSalary: this.formData.cookSalary,
        helperSalary: this.formData.helperSalary,
        electricity: this.formData.electricity,
        machineMaintenance: this.formData.machineMaintenance,
        misc: this.formData.misc,
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
          alert('Failed to update operational expense');
          this.loading = false;
        }
      });
    } else {
      this.operationalExpenseService.createOperationalExpense(this.formData).subscribe({
        next: () => {
          this.loadExpenses();
          this.closeModal();
          this.loading = false;
        },
        error: (error) => {
          console.error('Error creating operational expense:', error);
          const errorMsg = error.error?.error || 'Failed to create operational expense';
          alert(errorMsg);
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
        alert('Failed to delete operational expense');
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
    return this.calculatedRent + this.formData.cookSalary + this.formData.helperSalary +
           this.formData.electricity + this.formData.machineMaintenance + this.formData.misc;
  }
}
