import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
  InventoryService,
  Inventory,
  InventoryTransaction,
  StockAlert,
  InventoryReport,
  InventoryItem,
  ExpiringBatchItem,
  CategoryInventorySummary,
  StockInRequest,
  StockOutRequest,
  BulkUploadResult
} from '../services/inventory.service';
import { OutletService } from '../services/outlet.service';
import { UIStore } from '../store/ui.store';
import { Subscription } from 'rxjs';
import { filter } from 'rxjs/operators';

@Component({
  selector: 'app-inventory-management',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './inventory-management.component.html',
  styleUrl: './inventory-management.component.scss'
})
export class InventoryManagementComponent implements OnInit, OnDestroy {
  private outletService = inject(OutletService);
  private uiStore = inject(UIStore);
  private outletSubscription?: Subscription;

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
  showInventoryModal = false;

  // Excel Bulk Upload State
  showUploadModal = false;
  selectedUploadFile: File | null = null;
  uploading = false;
  uploadResult: BulkUploadResult | null = null;
  isDragging = false;

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
    // Subscribe to outlet changes
    this.outletSubscription = this.outletService.selectedOutlet$
      .pipe(filter(outlet => outlet !== null))
      .subscribe(() => {
        this.loadDashboard();
      });

    // Load immediately if outlet is already selected
    if (this.outletService.getSelectedOutlet()) {
      this.loadDashboard();
    }
  }

  ngOnDestroy(): void {
    this.outletSubscription?.unsubscribe();
  }

  // ===== DASHBOARD =====
  loadDashboard(): void {
    this.loading = true;
    this.activeView = 'dashboard';

    // Prefetch inventory catalog so dashboard quick actions work immediately
    this.inventoryService.getActiveInventory().subscribe({
      next: (items) => {
        this.inventoryItems = items;
      }
    });

    // Load report
    this.inventoryService.getInventoryReport().subscribe({
      next: (report) => {
        this.report = report;
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

  openStockModalForReportItem(item: any, type: 'in' | 'out'): void {
    const targetId = item.id || item.inventoryId;
    const fullItem = this.inventoryItems.find(i => i.id === targetId) || {
      id: targetId,
      ingredientName: item.name || item.itemName,
      category: item.category,
      unit: item.unit,
      currentStock: item.currentStock !== undefined ? item.currentStock : (item.remainingQuantity || 0),
      costPerUnit: item.costPerUnit || 0,
      minimumStock: item.minimumStock || 0,
      maximumStock: 0,
      reorderQuantity: 0,
      totalValue: item.value || item.batchValue || 0,
      status: (item.status || 'InStock') as any,
      isActive: true
    } as Inventory;
    this.openStockModal(fullItem, type);
  }

  openBatchModalForReportItem(item: InventoryItem): void {
    const fullItem = this.inventoryItems.find(i => i.id === item.id);
    if (fullItem) {
      this.openBatchModal(fullItem);
    } else if (item.id) {
      this.inventoryService.getInventoryById(item.id).subscribe({
        next: (loaded) => this.openBatchModal(loaded),
        error: () => this.uiStore.warning('Could not load batch details for this item')
      });
    }
  }

  openWastageForExpiringBatch(batch: any): void {
    const fullItem = this.inventoryItems.find(i => i.id === batch.inventoryId);
    if (fullItem) {
      this.selectedBatchItem = fullItem;
      const b = fullItem.batches?.find(x => x.id === batch.batchId) || {
        id: batch.batchId,
        batchNumber: batch.batchNumber,
        remainingQuantity: batch.remainingQuantity,
        costPerUnit: batch.costPerUnit,
        expiryDate: batch.expiryDate
      };
      this.openWastageModal(b);
    } else {
      this.inventoryService.getInventoryById(batch.inventoryId).subscribe({
        next: (loaded) => {
          this.selectedBatchItem = loaded;
          const b = loaded.batches?.find(x => x.id === batch.batchId) || {
            id: batch.batchId,
            batchNumber: batch.batchNumber,
            remainingQuantity: batch.remainingQuantity,
            costPerUnit: batch.costPerUnit,
            expiryDate: batch.expiryDate
          };
          this.openWastageModal(b);
        }
      });
    }
  }

  filterDashboardByCategory(category: string): void {
    this.categoryFilter = category;
    this.statusFilter = 'all';
    this.searchTerm = '';
    this.loadInventory();
  }

  filterDashboardByStatus(status: string): void {
    this.statusFilter = status;
    this.categoryFilter = 'all';
    this.searchTerm = '';
    this.loadInventory();
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
        item.ingredientName.toLowerCase().includes(this.searchTerm.toLowerCase()) ||
        (item.supplierName && item.supplierName.toLowerCase().includes(this.searchTerm.toLowerCase())) ||
        (item.storageLocation && item.storageLocation.toLowerCase().includes(this.searchTerm.toLowerCase()));

      let matchesStatus = true;
      if (this.statusFilter === 'all') {
        matchesStatus = true;
      } else if (this.statusFilter === 'Expiring') {
        const expInfo = this.getExpiryWarningInfo(item);
        matchesStatus = expInfo.level === 'warning' || expInfo.level === 'critical' || expInfo.level === 'expired' || item.status === 'Expiring';
      } else {
        matchesStatus = item.status === this.statusFilter;
      }

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
    this.showInventoryModal = true;
  }

  editItem(item: Inventory): void {
    this.selectedItem = item;
    this.inventoryForm = {
      ingredientName: item.ingredientName,
      category: item.category,
      unit: item.unit,
      minimumStock: item.minimumStock,
      maximumStock: item.maximumStock,
      reorderQuantity: item.reorderQuantity,
      storageLocation: item.storageLocation || '',
      lastPurchasePrice: this.getWeightedAverageBuyingPrice(item),
      costPerUnit: this.getWeightedAverageCostPerUnit(item),
      currentStock: item.currentStock,
      totalValue: item.totalValue
    };
    this.showInventoryModal = true;
  }

  closeInventoryModal(): void {
    this.showInventoryModal = false;
    this.selectedItem = null;
    this.inventoryForm = this.getEmptyInventoryForm();
  }

  calculateCostPerUnit(): void {
    // Buy price and cost per unit are auto-calculated from Stock In and Stock Out batches
    if (this.inventoryForm.lastPurchasePrice && this.inventoryForm.currentStock && this.inventoryForm.currentStock > 0) {
      this.inventoryForm.costPerUnit = this.inventoryForm.lastPurchasePrice / this.inventoryForm.currentStock;
      this.inventoryForm.totalValue = this.inventoryForm.lastPurchasePrice;
    }
  }

  saveInventory(): void {
    if (!this.inventoryForm.ingredientName?.trim() || !this.inventoryForm.unit?.trim()) {
      this.uiStore.warning('Please enter Item Name and Unit');
      return;
    }

    this.loading = true;

    if (this.selectedItem) {
      const updateData: Partial<Inventory> = {
        ...this.selectedItem,
        ingredientName: this.inventoryForm.ingredientName.trim(),
        category: this.inventoryForm.category || 'Other',
        unit: this.inventoryForm.unit.trim(),
        minimumStock: Number(this.inventoryForm.minimumStock || 0),
        maximumStock: Number(this.inventoryForm.maximumStock || 0),
        reorderQuantity: Number(this.inventoryForm.reorderQuantity || 0),
        storageLocation: this.inventoryForm.storageLocation?.trim() || '',
        lastUpdatedBy: 'admin'
      };

      this.inventoryService.updateInventory(this.selectedItem.id!, updateData as Inventory).subscribe({
        next: () => {
          this.showAlert('Inventory item updated successfully', 'success');
          this.closeInventoryModal();
          this.loadInventory();
        },
        error: (error) => {
          console.error('Error updating inventory:', error);
          this.showAlert('Error updating inventory', 'error');
          this.loading = false;
        }
      });
    } else {
      const createData: Partial<Inventory> = {
        ingredientName: this.inventoryForm.ingredientName.trim(),
        category: this.inventoryForm.category || 'Other',
        unit: this.inventoryForm.unit.trim(),
        minimumStock: Number(this.inventoryForm.minimumStock || 0),
        maximumStock: Number(this.inventoryForm.maximumStock || 0),
        reorderQuantity: Number(this.inventoryForm.reorderQuantity || 0),
        storageLocation: this.inventoryForm.storageLocation?.trim() || '',
        currentStock: 0,
        costPerUnit: 0,
        lastPurchasePrice: 0,
        totalValue: 0,
        batches: [],
        status: 'OutOfStock',
        isActive: true,
        createdBy: 'admin',
        lastUpdatedBy: 'admin'
      };

      this.inventoryService.createInventory(createData as Inventory).subscribe({
        next: () => {
          this.showAlert('Inventory item created successfully! Add stock anytime via Stock IN.', 'success');
          this.closeInventoryModal();
          this.loadInventory();
        },
        error: (error) => {
          console.error('Error creating inventory:', error);
          this.showAlert('Error creating inventory', 'error');
          this.loading = false;
        }
      });
    }
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

  // ===== EXCEL BULK UPLOAD =====
  openUploadModal(): void {
    this.showUploadModal = true;
    this.selectedUploadFile = null;
    this.uploadResult = null;
    this.uploading = false;
  }

  closeUploadModal(): void {
    this.showUploadModal = false;
    this.selectedUploadFile = null;
    this.uploadResult = null;
    this.uploading = false;
  }

  onFileSelected(event: any): void {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files.length > 0) {
      this.handleFile(input.files[0]);
    }
  }

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.isDragging = true;
  }

  onDragLeave(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.isDragging = false;
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.isDragging = false;
    if (event.dataTransfer && event.dataTransfer.files.length > 0) {
      this.handleFile(event.dataTransfer.files[0]);
    }
  }

  private handleFile(file: File): void {
    const lowerName = file.name.toLowerCase();
    if (!lowerName.endsWith('.xlsx') && !lowerName.endsWith('.xls')) {
      this.uiStore.warning('Please select a valid Excel file (.xlsx or .xls)');
      return;
    }
    this.selectedUploadFile = file;
    this.uploadResult = null;
  }

  removeSelectedFile(): void {
    this.selectedUploadFile = null;
    this.uploadResult = null;
    const fileInput = document.getElementById('excelFileInput') as HTMLInputElement;
    if (fileInput) fileInput.value = '';
  }

  downloadTemplate(): void {
    this.inventoryService.downloadTemplate().subscribe({
      next: (blob) => {
        const url = window.URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = 'inventory_template.xlsx';
        a.click();
        window.URL.revokeObjectURL(url);
      },
      error: (err) => {
        console.error('Error downloading template from server, using client fallback:', err);
        const headers = 'ItemName,Category,Unit,MinimumStock,MaximumStock,ReorderQuantity,StorageLocation,InitialStock,CostPerUnit,SupplierName,ExpiryDate\n';
        const sample1 = 'Amul Taaza Milk 1L,Dairy,L,10,60,20,Walk-in Chiller Rack 1,24,56.00,Amul Dairy Distributor,2026-09-20\n';
        const sample2 = 'Fresh Chicken Breast,Meats,kg,5,30,10,Meat Freezer B,12,240.00,Quality Poultry Farms,2026-09-18\n';
        const sample3 = 'English Carrots,Vegetables,kg,4,25,10,Veg Crate 3,10,42.50,Fresh Mandi Wholesale,2026-09-19\n';
        const sample4 = 'Burger Buns (Pack of 6),Bakery,packet,10,50,20,Dry Bakery Rack 2,18,45.00,Golden Crust Bakery,2026-09-20\n';
        const sample5 = 'French Fries 9mm Frozen (2.5kg),frozen,packet,4,20,8,Deep Freezer 1,6,310.00,McCain Foods India,2026-10-15\n';
        const blob = new Blob([headers + sample1 + sample2 + sample3 + sample4 + sample5], { type: 'text/csv' });
        const url = window.URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = 'inventory_template.csv';
        a.click();
        window.URL.revokeObjectURL(url);
      }
    });
  }

  uploadExcelFile(): void {
    if (!this.selectedUploadFile) {
      this.uiStore.warning('Please select an Excel file to upload');
      return;
    }

    this.uploading = true;
    this.uploadResult = null;

    this.inventoryService.uploadInventoryExcel(this.selectedUploadFile).subscribe({
      next: (res) => {
        this.uploading = false;
        this.uploadResult = res;

        if (res.success > 0) {
          this.showAlert(`✅ Successfully imported ${res.success} inventory items!`, 'success');
          this.loadInventory();
        } else {
          this.showAlert(`⚠️ Upload completed with 0 items imported. Check errors below.`, 'warning');
        }

        // Reset file input
        this.selectedUploadFile = null;
        const fileInput = document.getElementById('excelFileInput') as HTMLInputElement;
        if (fileInput) fileInput.value = '';
      },
      error: (err) => {
        this.uploading = false;
        console.error('Error uploading Excel file:', err);
        const errorMsg = err.error?.error || err.error?.message || 'Failed to process Excel upload. Please verify file format.';
        this.showAlert(errorMsg, 'error');
      }
    });
  }

  // ===== STOCK OPERATIONS =====
  commonStockOutReasons: string[] = [
    'Kitchen Recipe',
    'Wastage',
    'Spoilage / Expired',
    'Damaged',
    'Staff Meal',
    'Audit Adjustment'
  ];

  openStockModal(item: Inventory, type: 'in' | 'out' | 'adjust'): void {
    this.selectedItem = item;
    this.stockModalType = type;
    this.showStockModal = true;

    if (type === 'in') {
      this.stockInForm = {
        quantity: undefined as any,
        costPerUnit: item.costPerUnit || undefined,
        purchasePrice: undefined,
        supplierName: item.supplierName || '',
        referenceNumber: '',
        expiryDate: '',
        performedBy: 'admin'
      };
    } else if (type === 'out') {
      this.stockOutForm = {
        quantity: undefined as any,
        reason: 'Kitchen Recipe',
        batchId: '',
        performedBy: 'admin'
      };
    }
  }

  closeStockModal(): void {
    this.showStockModal = false;
    this.selectedItem = null;
    this.stockModalType = null;
  }

  getActiveBatchesSortedByExpiry(item: Inventory | null | undefined): any[] {
    if (!item || !item.batches) return [];
    return item.batches
      .filter(b => (b.remainingQuantity ?? 0) > 0)
      .slice()
      .sort((a, b) => {
        const timeA = a.expiryDate ? new Date(a.expiryDate).getTime() : Number.MAX_SAFE_INTEGER;
        const timeB = b.expiryDate ? new Date(b.expiryDate).getTime() : Number.MAX_SAFE_INTEGER;
        if (timeA !== timeB) return timeA - timeB;
        return new Date(a.receivedDate).getTime() - new Date(b.receivedDate).getTime();
      });
  }

  getSelectedStockOutBatch(): any | null {
    if (!this.selectedItem || !this.stockOutForm.batchId) return null;
    return this.selectedItem.batches?.find(b => b.id === this.stockOutForm.batchId) || null;
  }

  getStockOutAvailableQuantity(): number {
    const batch = this.getSelectedStockOutBatch();
    if (batch) return batch.remainingQuantity;
    return this.selectedItem?.currentStock || 0;
  }

  selectStockOutBatch(batchId: string): void {
    this.stockOutForm.batchId = batchId;
    const max = this.getStockOutAvailableQuantity();
    if (this.stockOutForm.quantity && this.stockOutForm.quantity > max) {
      this.stockOutForm.quantity = max;
    }
  }

  addStockInQty(amount: number): void {
    const current = Number(this.stockInForm.quantity || 0);
    this.stockInForm.quantity = Math.round((current + amount) * 100) / 100;
    this.onStockInQtyOrCostChange();
  }

  setQuickExpiry(days: number): void {
    const d = new Date();
    d.setDate(d.getDate() + days);
    const year = d.getFullYear();
    const month = String(d.getMonth() + 1).padStart(2, '0');
    const day = String(d.getDate()).padStart(2, '0');
    this.stockInForm.expiryDate = `${year}-${month}-${day}`;
  }

  onStockInCostPerUnitChange(): void {
    if (this.stockInForm.costPerUnit && this.stockInForm.quantity && this.stockInForm.quantity > 0) {
      this.stockInForm.purchasePrice = Math.round(this.stockInForm.costPerUnit * this.stockInForm.quantity * 100) / 100;
    }
  }

  onStockInPurchasePriceChange(): void {
    if (this.stockInForm.purchasePrice && this.stockInForm.quantity && this.stockInForm.quantity > 0) {
      this.stockInForm.costPerUnit = Math.round((this.stockInForm.purchasePrice / this.stockInForm.quantity) * 100) / 100;
    }
  }

  onStockInQtyOrCostChange(): void {
    if (this.stockInForm.costPerUnit && this.stockInForm.quantity && this.stockInForm.quantity > 0) {
      this.stockInForm.purchasePrice = Math.round(this.stockInForm.costPerUnit * this.stockInForm.quantity * 100) / 100;
    } else if (this.stockInForm.purchasePrice && this.stockInForm.quantity && this.stockInForm.quantity > 0) {
      this.stockInForm.costPerUnit = Math.round((this.stockInForm.purchasePrice / this.stockInForm.quantity) * 100) / 100;
    }
  }

  addStockOutQty(amount: number): void {
    const current = Number(this.stockOutForm.quantity || 0);
    const max = this.getStockOutAvailableQuantity();
    const nextVal = Math.round((current + amount) * 100) / 100;
    this.stockOutForm.quantity = max > 0 ? Math.min(nextVal, max) : nextVal;
  }

  setStockOutAll(): void {
    const max = this.getStockOutAvailableQuantity();
    if (max > 0) {
      this.stockOutForm.quantity = max;
    }
  }

  submitStockIn(): void {
    if (!this.selectedItem || !this.stockInForm.quantity || this.stockInForm.quantity <= 0) {
      this.uiStore.warning('Please enter a valid quantity');
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
    if (!this.selectedItem || !this.stockOutForm.quantity || this.stockOutForm.quantity <= 0) {
      this.uiStore.warning('Please enter a valid quantity');
      return;
    }

    const available = this.getStockOutAvailableQuantity();
    if (this.stockOutForm.quantity > available) {
      this.uiStore.warning(`Cannot remove more stock than available (${available} ${this.selectedItem.unit})`);
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

  // ===== BATCH & EXPIRY HELPERS =====
  showBatchModal = false;
  selectedBatchItem: Inventory | null = null;

  openBatchModal(item: Inventory): void {
    this.selectedBatchItem = item;
    this.showBatchModal = true;
  }

  closeBatchModal(): void {
    this.showBatchModal = false;
    this.selectedBatchItem = null;
  }

  getCategoryExpiryWarningDays(category: string | undefined): number {
    if (!category) return 7;
    const lower = category.trim().toLowerCase();
    if (lower.includes('chicken') || lower.includes('meat') || lower.includes('poultry') || lower.includes('fish') || lower.includes('seafood')) {
      return 1;
    }
    if (lower.includes('vegetable') || lower.includes('fruit') || lower.includes('produce') || lower.includes('herb')) {
      return 2;
    }
    if (lower.includes('bakery') || lower.includes('bread') || lower.includes('pastry') || lower.includes('dairy') || lower.includes('milk')) {
      return 3;
    }
    if (lower.includes('frozen')) {
      return 30; // 1 month
    }
    return 7;
  }

  getCategoryExpiryLabel(category: string | undefined): string {
    const days = this.getCategoryExpiryWarningDays(category);
    if (days >= 30) return '1 month';
    return `${days} ${days === 1 ? 'day' : 'days'}`;
  }

  getEarliestBatchExpiry(item: Inventory): Date | undefined {
    if (item.batches && item.batches.length > 0) {
      const activeBatchesWithExpiry = item.batches
        .filter(b => (b.remainingQuantity ?? 0) > 0 && b.expiryDate)
        .sort((a, b) => new Date(a.expiryDate!).getTime() - new Date(b.expiryDate!).getTime());
      if (activeBatchesWithExpiry.length > 0) {
        return activeBatchesWithExpiry[0].expiryDate;
      }
    }
    return item.expiryDate;
  }

  getDaysUntilExpiry(date: Date | string | undefined): number | null {
    if (!date) return null;
    const expiry = new Date(date).getTime();
    const today = new Date().setHours(0, 0, 0, 0);
    const diff = Math.ceil((expiry - today) / (1000 * 60 * 60 * 24));
    return diff;
  }

  getExpiryWarningInfo(item: Inventory): { level: 'expired' | 'critical' | 'warning' | 'good' | 'none'; message: string; daysLeft: number | null } {
    const earliest = this.getEarliestBatchExpiry(item);
    if (!earliest) {
      return { level: 'none', message: 'No expiry set', daysLeft: null };
    }
    const days = this.getDaysUntilExpiry(earliest);
    if (days === null) {
      return { level: 'none', message: 'No expiry set', daysLeft: null };
    }
    if (days < 0) {
      return { level: 'expired', message: `Expired (${Math.abs(days)}d ago)`, daysLeft: days };
    }
    if (days === 0) {
      return { level: 'expired', message: 'Expires today!', daysLeft: 0 };
    }

    const threshold = this.getCategoryExpiryWarningDays(item.category);
    if (days <= 1) {
      return { level: 'critical', message: `${days}d left (Threshold: ${threshold}d)`, daysLeft: days };
    }
    if (days <= threshold) {
      return { level: 'warning', message: `${days}d left (Threshold: ${threshold}d)`, daysLeft: days };
    }
    return { level: 'good', message: `${days}d left`, daysLeft: days };
  }

  getWeightedAverageCostPerUnit(item: Inventory): number {
    if (item.batches && item.batches.length > 0) {
      const activeBatches = item.batches.filter(b => (b.remainingQuantity ?? 0) > 0);
      const totalQty = activeBatches.reduce((acc, b) => acc + (b.remainingQuantity ?? 0), 0);
      const totalCost = activeBatches.reduce((acc, b) => acc + ((b.remainingQuantity ?? 0) * (b.costPerUnit || 0)), 0);
      if (totalQty > 0) {
        return totalCost / totalQty;
      }
    }
    return item.costPerUnit || 0;
  }

  getWeightedAverageBuyingPrice(item: Inventory): number {
    if (item.batches && item.batches.length > 0) {
      const activeBatches = item.batches.filter(b => (b.remainingQuantity ?? 0) > 0);
      const batchesWithPrice = activeBatches.filter(b => b.purchasePrice && b.purchasePrice > 0);
      if (batchesWithPrice.length > 0) {
        const totalQty = batchesWithPrice.reduce((acc, b) => acc + (b.remainingQuantity ?? 0), 0);
        const totalPrice = batchesWithPrice.reduce((acc, b) => {
          const unitPrice = (b.purchasePrice || 0) / (b.initialQuantity || b.remainingQuantity || 1);
          return acc + ((b.remainingQuantity ?? 0) * unitPrice);
        }, 0);
        if (totalQty > 0) {
          return totalPrice / totalQty;
        }
      }
    }
    return item.lastPurchasePrice || item.costPerUnit || 0;
  }

  getActiveBatchesCount(item: Inventory): number {
    if (!item.batches) return 0;
    return item.batches.filter(b => (b.remainingQuantity ?? 0) > 0).length;
  }

  // ===== 1-CLICK WASTAGE LOGGING =====
  selectedWastageBatch: any = null;
  wastageQuantity: number = 0;
  wastageReason: string = 'Expired';
  wastageNotes: string = '';
  showWastageConfirmModal = false;

  openWastageModal(batch: any): void {
    if (!this.selectedBatchItem || !batch) return;
    this.selectedWastageBatch = batch;
    this.wastageQuantity = batch.remainingQuantity;
    const isExpired = this.getDaysUntilExpiry(batch.expiryDate) !== null && this.getDaysUntilExpiry(batch.expiryDate)! <= 0;
    this.wastageReason = isExpired ? 'Expired' : 'Spoiled';
    this.wastageNotes = `Batch ${batch.batchNumber || batch.referenceNumber || batch.id}`;
    this.showWastageConfirmModal = true;
  }

  closeWastageModal(): void {
    this.showWastageConfirmModal = false;
    this.selectedWastageBatch = null;
    this.wastageQuantity = 0;
    this.wastageNotes = '';
  }

  confirmLogWastage(): void {
    if (!this.selectedBatchItem || !this.selectedWastageBatch) return;
    if (this.wastageQuantity <= 0 || this.wastageQuantity > this.selectedWastageBatch.remainingQuantity) {
      this.uiStore.warning('Please enter a valid wastage quantity');
      return;
    }

    this.loading = true;
    this.inventoryService.logBatchWastage(this.selectedBatchItem.id!, {
      batchId: this.selectedWastageBatch.id,
      quantity: this.wastageQuantity,
      reason: this.wastageReason,
      notes: this.wastageNotes,
      performedBy: 'admin'
    }).subscribe({
      next: (res) => {
        this.showAlert(`✅ ${res.message}`, 'success');
        this.closeWastageModal();
        this.closeBatchModal();
        this.loadInventory();
      },
      error: (err) => {
        console.error('Error logging wastage:', err);
        this.showAlert('Failed to log batch wastage', 'error');
        this.loading = false;
      }
    });
  }

  // ===== HELPERS =====
  getEmptyInventoryForm(): Partial<Inventory> {
    return {
      ingredientName: '',
      category: 'Vegetables',
      unit: 'kg',
      minimumStock: 5,
      maximumStock: 50,
      reorderQuantity: 10,
      storageLocation: '',
      currentStock: 0,
      costPerUnit: 0,
      lastPurchasePrice: 0,
      totalValue: 0,
      status: 'OutOfStock',
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

  formatCurrency(value: number | undefined | null): string {
    if (value === null || value === undefined) return '₹0.00';
    return `₹${value.toFixed(2)}`;
  }

  formatDate(date: Date | undefined): string {
    if (!date) return 'N/A';
    return new Date(date).toLocaleDateString('en-IN', { timeZone: 'Asia/Kolkata' });
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
