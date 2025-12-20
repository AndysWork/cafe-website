import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ExpenseService, Expense, CreateExpenseRequest, ExpenseSummary, HierarchicalExpense, MonthExpense, WeekExpense } from '../../services/expense.service';
import { OfflineExpenseTypeService, OfflineExpenseType } from '../../services/offline-expense-type.service';
import { OnlineExpenseTypeService, OnlineExpenseType } from '../../services/online-expense-type.service';
import { OperationalExpenseService, OperationalExpense, CreateOperationalExpenseRequest, UpdateOperationalExpenseRequest } from '../../services/operational-expense.service';
import { getIstDateString, formatIstDate, convertToIst, getIstNow } from '../../utils/date-utils';

@Component({
  selector: 'app-admin-expenses',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './admin-expenses.component.html',
  styleUrl: './admin-expenses.component.scss'
})
export class AdminExpensesComponent implements OnInit {
  expenses: Expense[] = [];
  offlineExpenseTypes: OfflineExpenseType[] = [];
  onlineExpenseTypes: OnlineExpenseType[] = [];
  operationalExpenses: OperationalExpense[] = [];
  currentExpenseSource: 'Offline' | 'Online' | 'Operational' = 'Offline'; // Tab selection
  loading = false;
  showModal = false;
  showUploadModal = false;
  editingId: string | null = null;
  summary: ExpenseSummary | null = null;
  summaryDate: string = getIstDateString();

  // Grouping - matching sales management
  groupedExpenses: { [year: string]: { [month: string]: { [week: string]: Expense[] } } } = {};
  expandedYears: Set<string> = new Set();
  expandedMonths: Set<string> = new Set();
  expandedWeeks: Set<string> = new Set();

  formData: CreateExpenseRequest = {
    date: getIstDateString(),
    expenseType: '',
    expenseSource: 'Offline',
    amount: 0,
    paymentMethod: 'Cash',
    notes: ''
  };

  paymentMethods = ['Cash', 'Card', 'UPI', 'Online'];
  selectedFile: File | null = null;
  uploading = false;
  uploadError = '';
  uploadResult: any = null;

  // Operational Expenses
  showOperationalModal = false;
  isOperationalEditMode = false;
  editingOperationalId: string | null = null;
  calculatedRent: number = 0;
  calculatingRent = false;
  groupedOperationalExpenses: { [year: number]: OperationalExpense[] } = {};
  expandedOperationalYears: Set<number> = new Set();

  operationalFormData: CreateOperationalExpenseRequest = {
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

  constructor(
    private expenseService: ExpenseService,
    private offlineExpenseTypeService: OfflineExpenseTypeService,
    private onlineExpenseTypeService: OnlineExpenseTypeService,
    private operationalExpenseService: OperationalExpenseService
  ) {}

  ngOnInit() {
    this.loadExpenses();
    this.loadOfflineExpenseTypes();
    this.loadOnlineExpenseTypes();
    this.loadOperationalExpenses();
  }

  loadExpenses() {
    this.loading = true;
    this.expenseService.getAllExpenses().subscribe({
      next: (data) => {
        this.expenses = data;
        this.groupExpensesByYearMonth();
        this.loading = false;
      },
      error: (error) => {
        console.error('Error loading expenses:', error);
        this.loading = false;
      }
    });
  }

  groupExpensesByYearMonth() {
    this.groupedExpenses = {};

    // Sort expenses by date descending (newest first)
    const sortedExpenses = [...this.filteredExpenses].sort((a, b) =>
      new Date(b.date).getTime() - new Date(a.date).getTime()
    );

    sortedExpenses.forEach(expense => {
      const date = convertToIst(new Date(expense.date));
      const year = date.getFullYear().toString();
      const month = date.toLocaleString('default', { month: 'long' });
      const weekLabel = this.getWeekLabel(date);

      if (!this.groupedExpenses[year]) {
        this.groupedExpenses[year] = {};
      }
      if (!this.groupedExpenses[year][month]) {
        this.groupedExpenses[year][month] = {};
      }
      if (!this.groupedExpenses[year][month][weekLabel]) {
        this.groupedExpenses[year][month][weekLabel] = [];
      }
      this.groupedExpenses[year][month][weekLabel].push(expense);
    });

    // Auto-expand current year, month, and week
    const currentDate = getIstNow();
    const currentYear = currentDate.getFullYear().toString();
    const currentMonth = currentDate.toLocaleString('default', { month: 'long' });
    const currentWeek = this.getWeekLabel(currentDate);
    this.expandedYears.add(currentYear);
    this.expandedMonths.add(`${currentYear}-${currentMonth}`);
    this.expandedWeeks.add(`${currentYear}-${currentMonth}-${currentWeek}`);
  }

  getWeekLabel(date: Date): string {
    const day = date.getDate();
    const weekNum = Math.ceil(day / 7);
    const startDay = (weekNum - 1) * 7 + 1;
    const endDay = Math.min(weekNum * 7, new Date(date.getFullYear(), date.getMonth() + 1, 0).getDate());
    return `Week ${weekNum} (${startDay}-${endDay})`;
  }

  getYears(): string[] {
    return Object.keys(this.groupedExpenses).sort((a, b) => parseInt(b) - parseInt(a));
  }

  getMonths(year: string): string[] {
    if (!this.groupedExpenses[year]) return [];
    const monthOrder = ['January', 'February', 'March', 'April', 'May', 'June',
                        'July', 'August', 'September', 'October', 'November', 'December'];
    return Object.keys(this.groupedExpenses[year]).sort((a, b) =>
      monthOrder.indexOf(b) - monthOrder.indexOf(a)
    );
  }

  getWeeks(year: string, month: string): string[] {
    if (!this.groupedExpenses[year]?.[month]) return [];
    return Object.keys(this.groupedExpenses[year][month]).sort((a, b) => {
      const weekNumA = parseInt(a.match(/Week (\d+)/)?.[1] || '0');
      const weekNumB = parseInt(b.match(/Week (\d+)/)?.[1] || '0');
      return weekNumB - weekNumA; // Descending order
    });
  }

  loadOfflineExpenseTypes() {
    this.offlineExpenseTypeService.getActiveOfflineExpenseTypes().subscribe({
      next: (types) => {
        this.offlineExpenseTypes = types;
        console.log('Loaded offline expense types:', types);
        if (types.length === 0) {
          console.warn('No offline expense types found. You may need to initialize them.');
        }
      },
      error: (err) => {
        console.error('Error loading offline expense types:', err);
        alert('Error loading offline expense types. Please make sure you are logged in as admin.');
      }
    });
  }

  loadOnlineExpenseTypes() {
    this.onlineExpenseTypeService.getActiveOnlineExpenseTypes().subscribe({
      next: (types) => {
        this.onlineExpenseTypes = types;
        console.log('Loaded online expense types:', types);
        if (types.length === 0) {
          console.warn('No online expense types found. You may need to initialize them.');
        }
      },
      error: (err) => {
        console.error('Error loading online expense types:', err);
        alert('Error loading online expense types. Please make sure you are logged in as admin.');
      }
    });
  }

  get currentExpenseTypes(): Array<{expenseType: string}> {
    return this.currentExpenseSource === 'Offline' ? this.offlineExpenseTypes : this.onlineExpenseTypes;
  }

  get filteredExpenses() {
    return this.expenses.filter(e => (e as any).expenseSource === this.currentExpenseSource);
  }

  switchExpenseSource(source: 'Offline' | 'Online' | 'Operational') {
    this.currentExpenseSource = source;
    if (source !== 'Operational') {
      this.formData.expenseSource = source;
      this.groupExpensesByYearMonth();
    } else {
      this.groupOperationalExpensesByYear();
    }
  }

  toggleYear(year: string) {
    if (this.expandedYears.has(year)) {
      this.expandedYears.delete(year);
      // Collapse all months and weeks in this year
      this.getMonths(year).forEach(month => {
        this.expandedMonths.delete(`${year}-${month}`);
        this.getWeeks(year, month).forEach(week => {
          this.expandedWeeks.delete(`${year}-${month}-${week}`);
        });
      });
    } else {
      this.expandedYears.add(year);
    }
  }

  toggleMonth(year: string, month: string) {
    const key = `${year}-${month}`;
    if (this.expandedMonths.has(key)) {
      this.expandedMonths.delete(key);
      // Collapse all weeks in this month
      this.getWeeks(year, month).forEach(week => {
        this.expandedWeeks.delete(`${year}-${month}-${week}`);
      });
    } else {
      this.expandedMonths.add(key);
    }
  }

  toggleWeek(year: string, month: string, week: string) {
    const key = `${year}-${month}-${week}`;
    if (this.expandedWeeks.has(key)) {
      this.expandedWeeks.delete(key);
    } else {
      this.expandedWeeks.add(key);
    }
  }

  isYearExpanded(year: string): boolean {
    return this.expandedYears.has(year);
  }

  isMonthExpanded(year: string, month: string): boolean {
    return this.expandedMonths.has(`${year}-${month}`);
  }

  isWeekExpanded(year: string, month: string, week: string): boolean {
    return this.expandedWeeks.has(`${year}-${month}-${week}`);
  }

  getWeekTotal(year: string, month: string, week: string): number {
    if (!this.groupedExpenses[year]?.[month]?.[week]) return 0;
    return this.groupedExpenses[year][month][week].reduce((sum, expense) => sum + expense.amount, 0);
  }

  getMonthTotal(year: string, month: string): number {
    if (!this.groupedExpenses[year]?.[month]) return 0;
    let total = 0;
    Object.values(this.groupedExpenses[year][month]).forEach(weekExpenses => {
      weekExpenses.forEach(expense => total += expense.amount);
    });
    return total;
  }

  getYearTotal(year: string): number {
    if (!this.groupedExpenses[year]) return 0;
    let total = 0;
    Object.values(this.groupedExpenses[year]).forEach(monthWeeks => {
      Object.values(monthWeeks).forEach(weekExpenses => {
        weekExpenses.forEach(expense => total += expense.amount);
      });
    });
    return total;
  }

  initializeOfflineExpenseTypes() {
    if (confirm('Initialize default offline expense types? This will add 21 predefined expense categories like Milk, Cup, Rent, etc.')) {
      this.loading = true;
      this.offlineExpenseTypeService.initializeDefaultExpenseTypes().subscribe({
        next: (response) => {
          alert('Offline expense types initialized successfully!');
          this.loadOfflineExpenseTypes();
          this.loading = false;
        },
        error: (err) => {
          console.error('Error initializing offline expense types:', err);
          alert('Error initializing offline expense types. Please check console.');
          this.loading = false;
        }
      });
    }
  }

  initializeOnlineExpenseTypes() {
    if (confirm('Initialize default online expense types? This will add 27 predefined expense categories like Hyperpure, Blinkit, Vishal Megamart, etc.')) {
      this.loading = true;
      this.onlineExpenseTypeService.initializeDefaultExpenseTypes().subscribe({
        next: (response) => {
          alert('Online expense types initialized successfully!');
          this.loadOnlineExpenseTypes();
          this.loading = false;
        },
        error: (err) => {
          console.error('Error initializing online expense types:', err);
          alert('Error initializing online expense types. Please check console.');
          this.loading = false;
        }
      });
    }
  }

  loadSummary() {
    if (!this.summaryDate) return;

    this.expenseService.getExpenseSummary(this.summaryDate).subscribe({
      next: (data) => {
        this.summary = data;
      },
      error: (error) => {
        console.error('Error loading summary:', error);
      }
    });
  }

  openAddModal() {
    this.editingId = null;
    this.resetForm();
    this.formData.expenseSource = this.currentExpenseSource;
    this.showModal = true;
  }

  openEditModal(expense: Expense) {
    this.editingId = expense.id;
    this.formData = {
      date: expense.date.split('T')[0],
      expenseType: expense.expenseType,
      expenseSource: (expense as any).expenseSource || 'Offline',
      amount: expense.amount,
      paymentMethod: expense.paymentMethod,
      notes: expense.notes || ''
    };
    this.showModal = true;
  }

  closeModal() {
    this.showModal = false;
    this.editingId = null;
    this.resetForm();
  }

  resetForm() {
    this.formData = {
      date: getIstDateString(),
      expenseType: '',
      expenseSource: this.currentExpenseSource,
      amount: 0,
      paymentMethod: 'Cash',
      notes: ''
    };
  }

  saveExpense() {
    if (!this.formData.date || !this.formData.expenseType || this.formData.amount <= 0) {
      alert('Please fill in all required fields');
      return;
    }

    this.loading = true;

    if (this.editingId) {
      this.expenseService.updateExpense(this.editingId, this.formData).subscribe({
        next: () => {
          this.loadExpenses();
          this.closeModal();
          this.loading = false;
        },
        error: (error) => {
          console.error('Error updating expense:', error);
          alert('Failed to update expense');
          this.loading = false;
        }
      });
    } else {
      this.expenseService.createExpense(this.formData).subscribe({
        next: () => {
          this.loadExpenses();
          this.closeModal();
          this.loading = false;
        },
        error: (error) => {
          console.error('Error creating expense:', error);
          alert('Failed to create expense');
          this.loading = false;
        }
      });
    }
  }

  deleteExpense(id: string) {
    if (!confirm('Are you sure you want to delete this expense?')) return;

    this.expenseService.deleteExpense(id).subscribe({
      next: () => {
        this.loadExpenses();
      },
      error: (error) => {
        console.error('Error deleting expense:', error);
        alert('Failed to delete expense');
      }
    });
  }

  openUploadModal() {
    this.selectedFile = null;
    this.uploadError = '';
    this.uploadResult = null;
    this.showUploadModal = true;
  }

  closeUploadModal() {
    this.showUploadModal = false;
    this.selectedFile = null;
    this.uploadError = '';
    this.uploadResult = null;
  }

  onFileSelected(event: any) {
    const file = event.target.files[0];
    if (file) {
      if (file.name.endsWith('.xlsx') || file.name.endsWith('.xls')) {
        this.selectedFile = file;
        this.uploadError = '';
      } else {
        this.uploadError = 'Please select an Excel file (.xlsx or .xls)';
        this.selectedFile = null;
      }
    }
  }

  uploadFile() {
    if (!this.selectedFile) return;

    this.uploading = true;
    this.uploadError = '';
    this.uploadResult = null;

    this.expenseService.uploadExpensesExcel(this.selectedFile, this.currentExpenseSource).subscribe({
      next: (result: any) => {
        this.uploading = false;
        this.uploadResult = result;

        // Show warning if there were invalid expense types
        if (result.invalidExpenseTypes && result.invalidExpenseTypes.length > 0) {
          const invalidTypes = result.invalidExpenseTypes.join(', ');
          alert(`Upload completed with warnings!\n\n${result.processedRecords} records processed successfully.\n${result.skippedRecords} records skipped due to invalid expense types: ${invalidTypes}\n\nValid ${this.currentExpenseSource.toLowerCase()} expense types are: ${this.currentExpenseTypes.map(t => t.expenseType).join(', ')}`);
        }

        this.loadExpenses();
      },
      error: (error) => {
        console.error('Error uploading file:', error);
        this.uploading = false;
        this.uploadError = error.error?.message || 'Failed to upload file';
      }
    });
  }

  downloadTemplate() {
    // Use actual expense types from the database for better examples
    const exampleTypes = this.currentExpenseTypes.length > 0
      ? [this.currentExpenseTypes[0]?.expenseType || 'Milk',
         this.currentExpenseTypes[1]?.expenseType || 'Tea',
         this.currentExpenseTypes[2]?.expenseType || 'Rent',
         this.currentExpenseTypes[3]?.expenseType || 'Grocerry']
      : ['Milk', 'Tea', 'Rent', 'Grocerry'];

    // Add comment with all valid expense types
    const validTypesComment = this.currentExpenseTypes.length > 0
      ? `# Valid ${this.currentExpenseSource} Expense Types: ${this.currentExpenseTypes.map(t => t.expenseType).join(', ')}\n`
      : `# Valid ${this.currentExpenseSource} Expense Types: Please initialize expense types first\n`;

    const csvContent = validTypesComment +
                      '# Format: Date (YYYY-MM-DD), ExpenseType, Amount, PaymentMethod\n' +
                      'Date,ExpenseType,Amount,PaymentMethod\n' +
                      `2024-12-15,${exampleTypes[0]},500,Cash\n` +
                      `2024-12-15,${exampleTypes[1]},1200,Cash\n` +
                      `2024-12-15,${exampleTypes[2]},15000,Online\n` +
                      `2024-12-15,${exampleTypes[3]},800,Cash\n`;

    const blob = new Blob([csvContent], { type: 'text/csv' });
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `${this.currentExpenseSource.toLowerCase()}_expense_template.csv`;
    a.click();
    window.URL.revokeObjectURL(url);
  }

  formatDate(dateStr: string): string {
    return formatIstDate(dateStr, { day: '2-digit', month: 'short', year: 'numeric' });
  }

  formatCurrency(amount: number): string {
    return `₹${amount.toFixed(2)}`;
  }

  getExpenseTypeIcon(type: string): string {
    const icons: { [key: string]: string } = {
      'Inventory': '📦',
      'Salary': '👤',
      'Rent': '🏠',
      'Utilities': '⚡',
      'Maintenance': '🔧',
      'Marketing': '📢',
      'Other': '📝'
    };
    return icons[type] || '📝';
  }

  getExpenseTypeColor(type: string): string {
    const colors: { [key: string]: string } = {
      'Inventory': '#3b82f6',
      'Salary': '#10b981',
      'Rent': '#f59e0b',
      'Utilities': '#8b5cf6',
      'Maintenance': '#ef4444',
      'Marketing': '#ec4899',
      'Other': '#6b7280'
    };
    return colors[type] || '#6b7280';
  }

  // Operational Expenses Methods
  loadOperationalExpenses() {
    this.operationalExpenseService.getAllOperationalExpenses().subscribe({
      next: (data) => {
        this.operationalExpenses = data;
        this.groupOperationalExpensesByYear();
      },
      error: (error) => {
        console.error('Error loading operational expenses:', error);
      }
    });
  }

  groupOperationalExpensesByYear() {
    this.groupedOperationalExpenses = {};
    this.operationalExpenses.forEach(expense => {
      if (!this.groupedOperationalExpenses[expense.year]) {
        this.groupedOperationalExpenses[expense.year] = [];
      }
      this.groupedOperationalExpenses[expense.year].push(expense);
    });

    const currentYear = new Date().getFullYear();
    this.expandedOperationalYears.add(currentYear);
  }

  getOperationalYears(): number[] {
    return Object.keys(this.groupedOperationalExpenses)
      .map(y => parseInt(y))
      .sort((a, b) => b - a);
  }

  toggleOperationalYear(year: number) {
    if (this.expandedOperationalYears.has(year)) {
      this.expandedOperationalYears.delete(year);
    } else {
      this.expandedOperationalYears.add(year);
    }
  }

  isOperationalYearExpanded(year: number): boolean {
    return this.expandedOperationalYears.has(year);
  }

  getOperationalYearTotal(year: number): number {
    if (!this.groupedOperationalExpenses[year]) return 0;
    return this.groupedOperationalExpenses[year].reduce((sum, e) => sum + e.totalOperationalCost, 0);
  }

  calculateRent() {
    if (!this.operationalFormData.month || !this.operationalFormData.year) {
      alert('Please select month and year first');
      return;
    }

    this.calculatingRent = true;
    this.operationalExpenseService.calculateRentForMonth(this.operationalFormData.year, this.operationalFormData.month).subscribe({
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

  openOperationalAddModal() {
    this.isOperationalEditMode = false;
    this.editingOperationalId = null;
    this.resetOperationalForm();
    this.showOperationalModal = true;
    this.calculateRent();
  }

  openOperationalEditModal(expense: OperationalExpense) {
    this.isOperationalEditMode = true;
    this.editingOperationalId = expense.id;
    this.operationalFormData = {
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
    this.showOperationalModal = true;
  }

  closeOperationalModal() {
    this.showOperationalModal = false;
    this.editingOperationalId = null;
    this.resetOperationalForm();
  }

  resetOperationalForm() {
    const currentDate = new Date();
    this.operationalFormData = {
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

  saveOperationalExpense() {
    if (!this.operationalFormData.month || !this.operationalFormData.year) {
      alert('Please select month and year');
      return;
    }

    this.loading = true;

    if (this.isOperationalEditMode && this.editingOperationalId) {
      const updateRequest: UpdateOperationalExpenseRequest = {
        cookSalary: Number(this.operationalFormData.cookSalary),
        helperSalary: Number(this.operationalFormData.helperSalary),
        electricity: Number(this.operationalFormData.electricity),
        machineMaintenance: Number(this.operationalFormData.machineMaintenance),
        misc: Number(this.operationalFormData.misc),
        notes: this.operationalFormData.notes
      };

      this.operationalExpenseService.updateOperationalExpense(this.editingOperationalId, updateRequest).subscribe({
        next: () => {
          this.loadOperationalExpenses();
          this.closeOperationalModal();
          this.loading = false;
        },
        error: (error) => {
          console.error('Error updating operational expense:', error);
          alert('Failed to update operational expense');
          this.loading = false;
        }
      });
    } else {
      // Ensure month and year are numbers
      const createRequest: CreateOperationalExpenseRequest = {
        month: Number(this.operationalFormData.month),
        year: Number(this.operationalFormData.year),
        cookSalary: Number(this.operationalFormData.cookSalary),
        helperSalary: Number(this.operationalFormData.helperSalary),
        electricity: Number(this.operationalFormData.electricity),
        machineMaintenance: Number(this.operationalFormData.machineMaintenance),
        misc: Number(this.operationalFormData.misc),
        notes: this.operationalFormData.notes
      };

      this.operationalExpenseService.createOperationalExpense(createRequest).subscribe({
        next: () => {
          this.loadOperationalExpenses();
          this.closeOperationalModal();
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

  deleteOperationalExpense(id: string) {
    if (!confirm('Are you sure you want to delete this operational expense record?')) return;

    this.operationalExpenseService.deleteOperationalExpense(id).subscribe({
      next: () => {
        this.loadOperationalExpenses();
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

  getTotalOperationalCost(): number {
    return this.calculatedRent + this.operationalFormData.cookSalary + this.operationalFormData.helperSalary +
           this.operationalFormData.electricity + this.operationalFormData.machineMaintenance + this.operationalFormData.misc;
  }
}
