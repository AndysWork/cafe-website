import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ExpenseService, Expense, CreateExpenseRequest, ExpenseSummary } from '../../services/expense.service';

@Component({
  selector: 'app-admin-expenses',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './admin-expenses.component.html',
  styleUrl: './admin-expenses.component.scss'
})
export class AdminExpensesComponent implements OnInit {
  expenses: Expense[] = [];
  expenseTypes: string[] = [];
  loading = false;
  showModal = false;
  showUploadModal = false;
  editingId: string | null = null;
  summary: ExpenseSummary | null = null;
  summaryDate: string = new Date().toISOString().split('T')[0];

  formData: CreateExpenseRequest = {
    date: new Date().toISOString().split('T')[0],
    expenseType: 'Inventory',
    amount: 0,
    vendor: '',
    description: '',
    invoiceNumber: '',
    paymentMethod: 'Cash',
    notes: ''
  };

  paymentMethods = ['Cash', 'Card', 'UPI', 'Online'];
  selectedFile: File | null = null;
  uploading = false;
  uploadError = '';
  uploadResult: any = null;

  constructor(private expenseService: ExpenseService) {}

  ngOnInit() {
    this.loadExpenses();
    this.loadExpenseTypes();
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

  loadExpenseTypes() {
    this.expenseTypes = this.expenseService.getExpenseTypes();
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
    this.showModal = true;
  }

  openEditModal(expense: Expense) {
    this.editingId = expense.id;
    this.formData = {
      date: expense.date.split('T')[0],
      expenseType: expense.expenseType,
      amount: expense.amount,
      vendor: expense.vendor,
      description: expense.description,
      invoiceNumber: expense.invoiceNumber || '',
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
      expenseType: 'Inventory',
      amount: 0,
      vendor: '',
      description: '',
      invoiceNumber: '',
      paymentMethod: 'Cash',
      notes: ''
    };
  }

  saveExpense() {
    if (!this.formData.date || !this.formData.expenseType || !this.formData.vendor || this.formData.amount <= 0) {
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

    this.expenseService.uploadExpensesExcel(this.selectedFile).subscribe({
      next: (result) => {
        this.uploading = false;
        this.uploadResult = result;
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
    const csvContent = 'Date,ExpenseType,Amount,Vendor,Description,InvoiceNumber,PaymentMethod,Notes\n' +
                      '2024-01-15,Inventory,5000,ABC Suppliers,Coffee beans purchase,INV-001,Cash,Monthly stock\n' +
                      '2024-01-15,Salary,25000,Employee Name,Monthly salary,SAL-001,Online,January salary\n' +
                      '2024-01-15,Rent,15000,Landlord,Shop rent,RENT-001,Online,January rent\n' +
                      '2024-01-15,Utilities,2000,Electricity Board,Electricity bill,EB-001,Online,January bill';
    
    const blob = new Blob([csvContent], { type: 'text/csv' });
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'expense_template.csv';
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
