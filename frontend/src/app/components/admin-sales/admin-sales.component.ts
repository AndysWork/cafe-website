import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { SalesService, Sales, CreateSalesRequest, SalesItem } from '../../services/sales.service';
import { SalesItemTypeService, SalesItemType } from '../../services/sales-item-type.service';

@Component({
  selector: 'app-admin-sales',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './admin-sales.component.html',
  styleUrls: ['./admin-sales.component.scss']
})
export class AdminSalesComponent implements OnInit {
  sales: Sales[] = [];
  salesItemTypes: SalesItemType[] = [];
  loading = false;
  showModal = false;
  showUploadModal = false;
  editingId: string | null = null;

  // Form data
  formData: CreateSalesRequest = {
    date: new Date().toISOString().split('T')[0],
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
  summaryDate: string = new Date().toISOString().split('T')[0];
  summary: any = null;

  paymentMethods = ['Cash', 'Card', 'UPI', 'Online'];

  constructor(
    private salesService: SalesService,
    private salesItemTypeService: SalesItemTypeService
  ) {}

  ngOnInit() {
    this.loadSales();
    this.loadSalesItemTypes();
  }

  loadSales() {
    this.loading = true;
    this.salesService.getAllSales().subscribe({
      next: (data) => {
        this.sales = data;
        this.loading = false;
      },
      error: (err) => {
        console.error('Error loading sales:', err);
        this.loading = false;
      }
    });
  }

  loadSalesItemTypes() {
    this.salesItemTypeService.getActiveSalesItemTypes().subscribe({
      next: (items) => {
        this.salesItemTypes = items;
      },
      error: (err) => console.error('Error loading sales item types:', err)
    });
  }

  loadSummary() {
    this.salesService.getSalesSummary(this.summaryDate).subscribe({
      next: (data) => this.summary = data,
      error: (err) => console.error('Error loading summary:', err)
    });
  }

  openAddModal() {
    this.editingId = null;
    this.formData = {
      date: new Date().toISOString().split('T')[0],
      items: [],
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

  saveSales() {
    if (this.formData.items.length === 0) {
      alert('Please add at least one item');
      return;
    }

    this.loading = true;

    const request = this.editingId
      ? this.salesService.updateSales(this.editingId, this.formData)
      : this.salesService.createSales(this.formData);

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
    return new Date(date).toLocaleDateString();
  }

  formatCurrency(amount: number): string {
    return `₹${amount.toFixed(2)}`;
  }
}
