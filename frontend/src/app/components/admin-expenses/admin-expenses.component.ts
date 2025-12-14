import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ExpenseService, Expense, CreateExpenseRequest, ExpenseSummary } from '../../services/expense.service';
import { OfflineExpenseTypeService, OfflineExpenseType } from '../../services/offline-expense-type.service';
import { OnlineExpenseTypeService, OnlineExpenseType } from '../../services/online-expense-type.service';

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
  currentExpenseSource: 'Offline' | 'Online' = 'Offline'; // Tab selection
  loading = false;
  showModal = false;
  showUploadModal = false;
  editingId: string | null = null;
  summary: ExpenseSummary | null = null;
  summaryDate: string = new Date().toISOString().split('T')[0];

  formData: CreateExpenseRequest = {
    date: new Date().toISOString().split('T')[0],
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

  constructor(
    private expenseService: ExpenseService,
    private offlineExpenseTypeService: OfflineExpenseTypeService,
    private onlineExpenseTypeService: OnlineExpenseTypeService
  ) {}

  ngOnInit() {
    this.loadExpenses();
    this.loadOfflineExpenseTypes();
    this.loadOnlineExpenseTypes();
  }

  loadExpenses() {
    this.loading = true;
    this.expenseService.getAllExpenses().subscribe({
      next: (data) => {
        this.expenses = data;
        this.loading = false;
      },
      error: (error) => {
        console.error('Error loading expenses:', error);
        this.loading = false;
      }
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

  switchExpenseSource(source: 'Offline' | 'Online') {
    this.currentExpenseSource = source;
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
      date: new Date().toISOString().split('T')[0],
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
    const date = new Date(dateStr);
    return date.toLocaleDateString('en-IN', { day: '2-digit', month: 'short', year: 'numeric' });
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
}
