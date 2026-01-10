import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { SalesService, Sales, CreateSalesRequest, SalesItem } from '../../services/sales.service';
import { SalesItemTypeService, SalesItemType } from '../../services/sales-item-type.service';
import { OutletService } from '../../services/outlet.service';
import { Subscription } from 'rxjs';
import { filter } from 'rxjs/operators';
import { getIstDateString, getIstNow, convertToIst, formatIstDate } from '../../utils/date-utils';

@Component({
  selector: 'app-admin-sales',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './admin-sales.component.html',
  styleUrls: ['./admin-sales.component.scss']
})
export class AdminSalesComponent implements OnInit, OnDestroy {
  private outletService = inject(OutletService);
  private outletSubscription?: Subscription;

  sales: Sales[] = [];
  salesItemTypes: SalesItemType[] = [];
  loading = false;
  showModal = false;
  showUploadModal = false;
  editingId: string | null = null;

  // Pagination
  currentPage = 1;
  pageSize = 20;
  totalSales = 0;

  // Grouping
  groupedSales: { [year: string]: { [month: string]: { [week: string]: Sales[] } } } = {};
  expandedYears: Set<string> = new Set();
  expandedMonths: Set<string> = new Set();
  expandedWeeks: Set<string> = new Set();

  // Form data
  formData: CreateSalesRequest = {
    date: getIstDateString(),
    items: [],
    paymentMethod: 'Cash',
    notes: ''
  };

  // Item form
  currentItem = {
    menuItemId: '',
    itemName: '',
    quantity: 1,
    unitPrice: 0
  };

  // Upload
  selectedFile: File | null = null;
  uploadResult: any = null;
  uploadError: string | null = null;
  uploading = false;

  // Summary filter
  summaryDate: string = getIstDateString();
  summary: any = null;

  // Item Type Management
  showItemTypeModal = false;
  editingItemType: SalesItemType | null = null;
  itemTypeForm = {
    itemName: '',
    defaultPrice: 0
  };

  paymentMethods = ['Cash', 'Card', 'UPI', 'Online'];

  constructor(
    private salesService: SalesService,
    private salesItemTypeService: SalesItemTypeService
  ) {}

  ngOnInit() {
    // Subscribe to outlet changes
    this.outletSubscription = this.outletService.selectedOutlet$
      .pipe(filter(outlet => outlet !== null))
      .subscribe(() => {
        this.loadSales();
        this.loadSalesItemTypes();
      });

    // Load immediately if outlet is already selected
    if (this.outletService.getSelectedOutlet()) {
      this.loadSales();
      this.loadSalesItemTypes();
    }
  }

  ngOnDestroy() {
    this.outletSubscription?.unsubscribe();
  }

  loadSales() {
    this.loading = true;
    this.salesService.getAllSales().subscribe({
      next: (data) => {
        this.sales = data;
        this.totalSales = data.length;
        this.groupSalesByYearMonth();
        this.loading = false;
      },
      error: (err) => {
        console.error('Error loading sales:', err);
        this.loading = false;
      }
    });
  }

  groupSalesByYearMonth() {
    this.groupedSales = {};

    // Sort sales by date descending (newest first)
    const sortedSales = [...this.sales].sort((a, b) =>
      new Date(b.date).getTime() - new Date(a.date).getTime()
    );

    sortedSales.forEach(sale => {
      const date = convertToIst(new Date(sale.date));
      const year = date.getFullYear().toString();
      const month = date.toLocaleString('default', { month: 'long' });
      const weekLabel = this.getWeekLabel(date);

      if (!this.groupedSales[year]) {
        this.groupedSales[year] = {};
      }
      if (!this.groupedSales[year][month]) {
        this.groupedSales[year][month] = {};
      }
      if (!this.groupedSales[year][month][weekLabel]) {
        this.groupedSales[year][month][weekLabel] = [];
      }
      this.groupedSales[year][month][weekLabel].push(sale);
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
    return Object.keys(this.groupedSales).sort((a, b) => parseInt(b) - parseInt(a));
  }

  getMonths(year: string): string[] {
    if (!this.groupedSales[year]) return [];
    const monthOrder = ['January', 'February', 'March', 'April', 'May', 'June',
                        'July', 'August', 'September', 'October', 'November', 'December'];
    return Object.keys(this.groupedSales[year]).sort((a, b) =>
      monthOrder.indexOf(b) - monthOrder.indexOf(a)
    );
  }

  getWeeks(year: string, month: string): string[] {
    if (!this.groupedSales[year]?.[month]) return [];
    return Object.keys(this.groupedSales[year][month]).sort((a, b) => {
      const weekNumA = parseInt(a.match(/Week (\d+)/)?.[1] || '0');
      const weekNumB = parseInt(b.match(/Week (\d+)/)?.[1] || '0');
      return weekNumB - weekNumA; // Descending order
    });
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
    if (!this.groupedSales[year]?.[month]?.[week]) return 0;
    return this.groupedSales[year][month][week].reduce((sum, sale) => sum + sale.totalAmount, 0);
  }

  getMonthTotal(year: string, month: string): number {
    if (!this.groupedSales[year]?.[month]) return 0;
    let total = 0;
    Object.values(this.groupedSales[year][month]).forEach(weekSales => {
      weekSales.forEach(sale => total += sale.totalAmount);
    });
    return total;
  }

  getYearTotal(year: string): number {
    if (!this.groupedSales[year]) return 0;
    let total = 0;
    Object.values(this.groupedSales[year]).forEach(monthWeeks => {
      Object.values(monthWeeks).forEach(weekSales => {
        weekSales.forEach(sale => total += sale.totalAmount);
      });
    });
    return total;
  }

  loadSalesItemTypes() {
    this.salesItemTypeService.getActiveSalesItemTypes().subscribe({
      next: (items) => {
        this.salesItemTypes = items;
        console.log('Loaded sales item types:', items);
        if (items.length === 0) {
          console.warn('No sales item types found. You may need to initialize them.');
        }
      },
      error: (err) => {
        console.error('Error loading sales item types:', err);
        alert('Error loading sales items. Please make sure you are logged in as admin.');
      }
    });
  }

  initializeSalesItemTypes() {
    if (confirm('Initialize default sales item types? This will add predefined items like Tea-5, Tea-10, Coffee, etc.')) {
      this.loading = true;
      this.salesItemTypeService.initializeDefaultItems().subscribe({
        next: (response) => {
          alert('Sales item types initialized successfully!');
          this.loadSalesItemTypes();
          this.loading = false;
        },
        error: (err) => {
          console.error('Error initializing sales item types:', err);
          alert('Error initializing sales item types. Please check console.');
          this.loading = false;
        }
      });
    }
  }

  loadSummary() {
    this.salesService.getSalesSummary(this.summaryDate).subscribe({
      next: (data) => this.summary = data,
      error: (err) => console.error('Error loading summary:', err)
    });
  }

  openAddModal() {
    this.editingId = null;

    // Check if sales item types are loaded
    if (!this.salesItemTypes || this.salesItemTypes.length === 0) {
      alert('Loading sales items... Please try again in a moment.');
      this.loadSalesItemTypes();
      return;
    }

    // Pre-fill all sales item types
    const preFilledItems: any[] = this.salesItemTypes.map(itemType => ({
      menuItemId: itemType.id,
      itemName: itemType.itemName,
      quantity: this.isTeaVariant(itemType.itemName) ? 0 : 1,
      unitPrice: itemType.defaultPrice
    }));

    console.log('Pre-filled items:', preFilledItems); // Debug log

    this.formData = {
      date: getIstDateString(),
      items: preFilledItems,
      paymentMethod: 'Cash',
      notes: ''
    };
    this.currentItem = {
      menuItemId: '',
      itemName: '',
      quantity: 1,
      unitPrice: 0
    };
    this.showModal = true;
  }

  openEditModal(sales: Sales) {
    this.editingId = sales.id;
    this.formData = {
      date: sales.date.split('T')[0],
      items: sales.items.map(item => ({
        menuItemId: item.menuItemId,
        itemName: item.itemName,
        quantity: item.quantity,
        unitPrice: item.unitPrice
      })),
      paymentMethod: sales.paymentMethod,
      notes: sales.notes
    };
    this.showModal = true;
  }

  closeModal() {
    this.showModal = false;
    this.editingId = null;
  }

  onMenuItemChange() {
    const selected = this.salesItemTypes.find(m => m.id === this.currentItem.menuItemId);
    if (selected) {
      this.currentItem.itemName = selected.itemName;
      this.currentItem.unitPrice = selected.defaultPrice;
    }
  }

  addItem() {
    if (!this.currentItem.itemName || this.currentItem.quantity <= 0 || this.currentItem.unitPrice <= 0) {
      alert('Please fill all item details');
      return;
    }

    this.formData.items.push({ ...this.currentItem });
    this.currentItem = {
      menuItemId: '',
      itemName: '',
      quantity: 1,
      unitPrice: 0
    };
  }

  removeItem(index: number) {
    this.formData.items.splice(index, 1);
  }

  getTotalAmount(): number {
    return this.formData.items.reduce((sum, item) => sum + (item.quantity * item.unitPrice), 0);
  }

  isTeaVariant(itemName: string): boolean {
    const teaVariants = ['Tea - 5', 'Tea - 10', 'Tea - 20', 'Tea - 30'];
    return teaVariants.includes(itemName);
  }

  saveSales() {
    // Filter out items with quantity 0 or less
    const validItems = this.formData.items.filter(item => item.quantity > 0);

    if (validItems.length === 0) {
      alert('Please add at least one item with quantity greater than 0');
      return;
    }

    this.loading = true;

    // Create sales data with only valid items
    const salesDataToSave = {
      ...this.formData,
      items: validItems,
      totalAmount: validItems.reduce((sum, item) => sum + (item.quantity * item.unitPrice), 0)
    };

    const request = this.editingId
      ? this.salesService.updateSales(this.editingId, salesDataToSave)
      : this.salesService.createSales(salesDataToSave);

    request.subscribe({
      next: () => {
        this.loadSales();
        this.closeModal();
        this.loading = false;
      },
      error: (err) => {
        console.error('Error saving sales:', err);
        alert('Failed to save sales record');
        this.loading = false;
      }
    });
  }

  deleteSales(id: string) {
    if (!confirm('Are you sure you want to delete this sales record?')) return;

    this.salesService.deleteSales(id).subscribe({
      next: () => this.loadSales(),
      error: (err) => {
        console.error('Error deleting sales:', err);
        alert('Failed to delete sales record');
      }
    });
  }

  // Upload methods
  openUploadModal() {
    this.showUploadModal = true;
    this.selectedFile = null;
    this.uploadResult = null;
    this.uploadError = null;
  }

  closeUploadModal() {
    this.showUploadModal = false;
  }

  onFileSelected(event: any) {
    const file = event.target.files[0];
    if (file && this.isValidFile(file)) {
      this.selectedFile = file;
      this.uploadError = null;
    } else {
      this.uploadError = 'Please select a valid Excel file (.xlsx or .xls)';
    }
  }

  isValidFile(file: File): boolean {
    const validExtensions = ['.xlsx', '.xls'];
    return validExtensions.some(ext => file.name.toLowerCase().endsWith(ext));
  }

  uploadFile() {
    if (!this.selectedFile) {
      this.uploadError = 'Please select a file first';
      return;
    }

    this.uploading = true;
    this.uploadError = null;

    this.salesService.uploadSalesExcel(this.selectedFile).subscribe({
      next: (result) => {
        this.uploading = false;
        this.uploadResult = result;
        this.selectedFile = null;
        this.loadSales();
      },
      error: (err) => {
        this.uploading = false;
        this.uploadError = err.error?.error || 'Failed to upload file';
      }
    });
  }

  downloadTemplate() {
    const template = `Date,ItemName,Quantity,UnitPrice,TotalSale,PaymentMethod
2025-12-14,Cappuccino,2,120,240,Cash
2025-12-14,Burger,1,150,150,Cash
2025-12-14,Sandwich,3,100,300,UPI`;

    const blob = new Blob([template], { type: 'text/csv' });
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'sales_template.csv';
    a.click();
    window.URL.revokeObjectURL(url);
  }

  formatDate(date: string): string {
    return formatIstDate(date);
  }

  formatCurrency(amount: number): string {
    return `₹${amount.toFixed(2)}`;
  }

  // Item Type Management Methods
  openItemTypeModal() {
    this.showItemTypeModal = true;
  }

  closeItemTypeModal() {
    this.showItemTypeModal = false;
    this.editingItemType = null;
    this.itemTypeForm = { itemName: '', defaultPrice: 0 };
  }

  openEditItemType(itemType: SalesItemType) {
    this.editingItemType = itemType;
    this.itemTypeForm = {
      itemName: itemType.itemName,
      defaultPrice: itemType.defaultPrice
    };
  }

  saveItemType() {
    if (!this.itemTypeForm.itemName || this.itemTypeForm.defaultPrice < 0) {
      alert('Please provide item name and valid price (0 or greater)');
      return;
    }

    this.loading = true;

    if (this.editingItemType) {
      // Update existing
      const updated: SalesItemType = {
        ...this.editingItemType,
        itemName: this.itemTypeForm.itemName,
        defaultPrice: this.itemTypeForm.defaultPrice
      };

      this.salesItemTypeService.updateSalesItemType(this.editingItemType.id, updated).subscribe({
        next: () => {
          this.loadSalesItemTypes();
          this.closeEditItemType();
          this.loading = false;
        },
        error: (err) => {
          console.error('Error updating item type:', err);
          alert('Failed to update item type');
          this.loading = false;
        }
      });
    } else {
      // Create new
      this.salesItemTypeService.createSalesItemType(this.itemTypeForm).subscribe({
        next: () => {
          this.loadSalesItemTypes();
          this.closeEditItemType();
          this.loading = false;
        },
        error: (err) => {
          console.error('Error creating item type:', err);
          alert('Failed to create item type');
          this.loading = false;
        }
      });
    }
  }

  closeEditItemType() {
    this.editingItemType = null;
    this.itemTypeForm = { itemName: '', defaultPrice: 0 };
  }

  deleteItemType(itemType: SalesItemType) {
    if (!confirm(`Are you sure you want to delete "${itemType.itemName}"?`)) return;

    this.salesItemTypeService.deleteSalesItemType(itemType.id).subscribe({
      next: () => {
        this.loadSalesItemTypes();
      },
      error: (err) => {
        console.error('Error deleting item type:', err);
        alert('Failed to delete item type. It may be in use.');
      }
    });
  }
}
