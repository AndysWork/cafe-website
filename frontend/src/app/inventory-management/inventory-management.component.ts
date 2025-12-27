import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
  InventoryService,
  Inventory,
  InventoryTransaction,
  StockAlert,
  InventoryReport,
  StockInRequest,
  StockOutRequest
} from '../services/inventory.service';

@Component({
  selector: 'app-inventory-management',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './inventory-management.component.html',
  styleUrl: './inventory-management.component.scss'
})
export class InventoryManagementComponent implements OnInit {
  // Data
  inventoryItems: Inventory[] = [];
  filteredItems: Inventory[] = [];
  transactions: InventoryTransaction[] = [];
  alerts: StockAlert[] = [];
  report: InventoryReport | null = null;

  // UI State
  loading = false;
  activeView: 'dashboard' | 'inventory' | 'transactions' | 'alerts' = 'dashboard';
  selectedItem: Inventory | null = null;
  showStockModal = false;
  stockModalType: 'in' | 'out' | 'adjust' | null = null;

  // Forms
  inventoryForm: Partial<Inventory> = this.getEmptyInventoryForm();
  stockInForm: StockInRequest = { quantity: 0 };
  stockOutForm: StockOutRequest = { quantity: 0 };

  // Filters
  searchTerm = '';
  statusFilter = 'all';
  categoryFilter = 'all';

  // Categories (from your ingredients)
  categories = [
    'Beverages', 'Dairy', 'Vegetables', 'Meats', 'Spices', 'Oils',
    'Grains', 'Sauces', 'Bakery', 'Packaging', 'Cleaning', 'frozen', 'Other'
  ];

  measurementUnits = ['kg', 'g', 'L', 'ml', 'pcs', 'dozen', 'packet', 'box'];

  // Alert types and styles
  alertStyles = {
    'LowStock': { color: 'orange', icon: '⚠️' },
    'OutOfStock': { color: 'red', icon: '❌' },
    'ExpiringStock': { color: 'orange', icon: '⏰' },
    'ExpiredStock': { color: 'red', icon: '⛔' },
    'Overstock': { color: 'blue', icon: 'ℹ️' }
  };

  constructor(private inventoryService: InventoryService) {}

  ngOnInit(): void {
    this.loadDashboard();
  }

  // ===== DASHBOARD =====
  loadDashboard(): void {
    this.loading = true;
    this.activeView = 'dashboard';

    // Load report
    this.inventoryService.getInventoryReport().subscribe({
      next: (report) => {
        this.report = report;
        console.log('Dashboard report loaded:', report);
      },
      error: (error) => {
        console.error('Error loading dashboard report:', error);
        this.showAlert('Failed to load dashboard', 'error');
      }
    });

    // Load alerts
    this.inventoryService.getStockAlerts().subscribe({
      next: (alerts) => {
        this.alerts = alerts;
        this.loading = false;
      },
      error: (error) => {
        console.error('Error loading alerts:', error);
        this.loading = false;
      }
    });
  }

  // ===== INVENTORY MANAGEMENT =====
  loadInventory(): void {
    this.loading = true;
    this.activeView = 'inventory';

    this.inventoryService.getActiveInventory().subscribe({
      next: (items) => {
        this.inventoryItems = items;
        this.applyFilters();
        this.loading = false;
      },
      error: (error) => {
        console.error('Error loading inventory:', error);
        this.loading = false;
      }
    });
  }

  applyFilters(): void {
    this.filteredItems = this.inventoryItems.filter(item => {
      const matchesSearch = !this.searchTerm ||
        item.ingredientName.toLowerCase().includes(this.searchTerm.toLowerCase());
      const matchesStatus = this.statusFilter === 'all' || item.status === this.statusFilter;
      const matchesCategory = this.categoryFilter === 'all' || item.category === this.categoryFilter;

      return matchesSearch && matchesStatus && matchesCategory;
    });
  }

  onSearchChange(): void {
    this.applyFilters();
  }

  onFilterChange(): void {
    this.applyFilters();
  }

  createNewItem(): void {
    this.inventoryForm = this.getEmptyInventoryForm();
    this.selectedItem = null;
  }

  editItem(item: Inventory): void {
    this.selectedItem = item;
    this.inventoryForm = { ...item };
  }

  saveInventory(): void {
    if (!this.inventoryForm.ingredientName || !this.inventoryForm.unit) {
      alert('Please fill in all required fields');
      return;
    }

    this.loading = true;

    const inventoryData = {
      ...this.inventoryForm,
      isActive: true,
      createdBy: 'admin',
      lastUpdatedBy: 'admin'
    } as Inventory;

    const request = this.selectedItem
      ? this.inventoryService.updateInventory(this.selectedItem.id!, inventoryData)
      : this.inventoryService.createInventory(inventoryData);

    request.subscribe({
      next: () => {
        this.showAlert('Inventory saved successfully', 'success');
        this.loadInventory();
        this.inventoryForm = this.getEmptyInventoryForm();
        this.selectedItem = null;
      },
      error: (error) => {
        console.error('Error saving inventory:', error);
        this.showAlert('Error saving inventory', 'error');
        this.loading = false;
      }
    });
  }

  deleteItem(item: Inventory): void {
    if (!confirm(`Are you sure you want to delete ${item.ingredientName}?`)) {
      return;
    }

    this.inventoryService.deleteInventory(item.id!).subscribe({
      next: () => {
        this.showAlert('Item deleted successfully', 'success');
        this.loadInventory();
      },
      error: (error) => {
        console.error('Error deleting item:', error);
        this.showAlert('Error deleting item', 'error');
      }
    });
  }

  // ===== STOCK OPERATIONS =====
  openStockModal(item: Inventory, type: 'in' | 'out' | 'adjust'): void {
    this.selectedItem = item;
    this.stockModalType = type;
    this.showStockModal = true;

    if (type === 'in') {
      this.stockInForm = { quantity: 0, performedBy: 'admin' };
    } else if (type === 'out') {
      this.stockOutForm = { quantity: 0, performedBy: 'admin' };
    }
  }

  closeStockModal(): void {
    this.showStockModal = false;
    this.selectedItem = null;
    this.stockModalType = null;
  }

  submitStockIn(): void {
    if (!this.selectedItem || this.stockInForm.quantity! <= 0) {
      alert('Please enter a valid quantity');
      return;
    }

    this.loading = true;
    this.inventoryService.stockIn(this.selectedItem.id!, this.stockInForm).subscribe({
      next: () => {
        this.showAlert('Stock added successfully', 'success');
        this.closeStockModal();
        this.loadInventory();
      },
      error: (error) => {
        console.error('Error adding stock:', error);
        this.showAlert('Error adding stock', 'error');
        this.loading = false;
      }
    });
  }

  submitStockOut(): void {
    if (!this.selectedItem || this.stockOutForm.quantity! <= 0) {
      alert('Please enter a valid quantity');
      return;
    }

    if (this.stockOutForm.quantity! > this.selectedItem.currentStock) {
      alert('Cannot remove more stock than available');
      return;
    }

    this.loading = true;
    this.inventoryService.stockOut(this.selectedItem.id!, this.stockOutForm).subscribe({
      next: () => {
        this.showAlert('Stock removed successfully', 'success');
        this.closeStockModal();
        this.loadInventory();
      },
      error: (error) => {
        console.error('Error removing stock:', error);
        this.showAlert('Error removing stock', 'error');
        this.loading = false;
      }
    });
  }

  // ===== TRANSACTIONS =====
  loadTransactions(item?: Inventory): void {
    this.loading = true;
    this.activeView = 'transactions';
    this.selectedItem = item || null;

    const request = item
      ? this.inventoryService.getInventoryTransactions(item.id!)
      : this.inventoryService.getRecentTransactions(50);

    request.subscribe({
      next: (transactions) => {
        this.transactions = transactions;
        this.loading = false;
      },
      error: (error) => {
        console.error('Error loading transactions:', error);
        this.loading = false;
      }
    });
  }

  // ===== ALERTS =====
  loadAlerts(): void {
    this.loading = true;
    this.activeView = 'alerts';

    this.inventoryService.getStockAlerts().subscribe({
      next: (alerts) => {
        this.alerts = alerts;
        this.loading = false;
      },
      error: (error) => {
        console.error('Error loading alerts:', error);
        this.loading = false;
      }
    });
  }

  resolveAlert(alert: StockAlert): void {
    this.inventoryService.resolveAlert(alert.id!, 'admin').subscribe({
      next: () => {
        this.showAlert('Alert resolved', 'success');
        this.loadAlerts();
      },
      error: (error) => {
        console.error('Error resolving alert:', error);
        this.showAlert('Error resolving alert', 'error');
      }
    });
  }

  // ===== HELPERS =====
  getEmptyInventoryForm(): Partial<Inventory> {
    return {
      ingredientName: '',
      category: 'Other',
      unit: 'kg',
      currentStock: 0,
      minimumStock: 10,
      maximumStock: 100,
      reorderQuantity: 20,
      costPerUnit: 0,
      totalValue: 0,
      status: 'InStock',
      isActive: true
    };
  }

  getStatusColor(status: string): string {
    const colors: any = {
      'InStock': 'green',
      'LowStock': 'orange',
      'OutOfStock': 'red',
      'Overstock': 'blue',
      'Expiring': 'purple'
    };
    return colors[status] || 'gray';
  }

  getTransactionIcon(type: string): string {
    const icons: any = {
      'StockIn': '📥',
      'StockOut': '📤',
      'Adjustment': '⚙️',
      'Transfer': '🔄',
      'Wastage': '🗑️',
      'Return': '↩️'
    };
    return icons[type] || '📦';
  }

  formatCurrency(value: number): string {
    return `₹${value.toFixed(2)}`;
  }

  formatDate(date: Date | undefined): string {
    if (!date) return 'N/A';
    return new Date(date).toLocaleDateString('en-IN');
  }

  // Alert notification
  private alertMessage = '';
  private alertType: 'success' | 'error' | 'warning' = 'success';
  private alertVisible = false;

  showAlert(message: string, type: 'success' | 'error' | 'warning', duration = 3000): void {
    this.alertMessage = message;
    this.alertType = type;
    this.alertVisible = true;

    setTimeout(() => {
      this.alertVisible = false;
    }, duration);
  }

  get alertClass(): string {
    return `alert alert-${this.alertType}`;
  }

  get showAlertNotification(): boolean {
    return this.alertVisible;
  }

  get alertNotificationMessage(): string {
    return this.alertMessage;
  }
}
