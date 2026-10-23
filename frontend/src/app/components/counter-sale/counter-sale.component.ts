import { CommonModule } from '@angular/common';
import { Component, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MenuItem, MenuService } from '../../services/menu.service';
import {
  CounterSaleInvoiceResponse,
  CreateCounterSaleRequest,
  OrderService
} from '../../services/order.service';

interface CounterCartItem {
  menuItemId: string;
  name: string;
  quantity: number;
  unitPrice: number;
  lineTotal: number;
}

@Component({
  selector: 'app-counter-sale',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './counter-sale.component.html',
  styleUrls: ['./counter-sale.component.scss']
})
export class CounterSaleComponent implements OnInit {
  private menuService = inject(MenuService);
  private orderService = inject(OrderService);

  searchTerm = '';
  customerName = '';
  phoneNumber = '';
  notes = '';
  paymentMethod: 'cash_at_counter' | 'upi-qr' = 'cash_at_counter';
  upiReference = '';
  autoPrintInvoice = true;

  menuItems: MenuItem[] = [];
  cartItems: CounterCartItem[] = [];
  latestInvoice: CounterSaleInvoiceResponse | null = null;

  isLoadingMenu = false;
  isGenerating = false;
  isPrinting = false;
  successMessage = '';
  errorMessage = '';

  ngOnInit(): void {
    this.loadMenuItems();
  }

  get filteredMenuItems(): MenuItem[] {
    const term = this.searchTerm.trim().toLowerCase();
    if (!term) return this.menuItems;

    return this.menuItems.filter(item =>
      item.name.toLowerCase().includes(term)
      || (item.categoryName ?? '').toLowerCase().includes(term)
    );
  }

  get cartTotal(): number {
    return this.cartItems.reduce((sum, item) => sum + item.lineTotal, 0);
  }

  addToCart(item: MenuItem): void {
    const existing = this.cartItems.find(ci => ci.menuItemId === item.id);
    const unitPrice = this.resolveCounterPrice(item);

    if (existing) {
      existing.quantity += 1;
      existing.lineTotal = existing.quantity * existing.unitPrice;
    } else {
      this.cartItems.push({
        menuItemId: item.id,
        name: item.name,
        quantity: 1,
        unitPrice,
        lineTotal: unitPrice
      });
    }
  }

  updateQuantity(item: CounterCartItem, quantity: number): void {
    const safeQty = Math.max(1, Math.floor(quantity || 1));
    item.quantity = safeQty;
    item.lineTotal = item.quantity * item.unitPrice;
  }

  removeItem(item: CounterCartItem): void {
    this.cartItems = this.cartItems.filter(ci => ci.menuItemId !== item.menuItemId);
  }

  async generateInvoice(): Promise<void> {
    this.successMessage = '';
    this.errorMessage = '';

    if (this.cartItems.length === 0) {
      this.errorMessage = 'Add at least one item to create a counter sale invoice.';
      return;
    }

    if (this.paymentMethod === 'upi-qr' && !this.upiReference.trim()) {
      this.errorMessage = 'UPI reference is required for UPI payment.';
      return;
    }

    const payload: CreateCounterSaleRequest = {
      items: this.cartItems.map(item => ({
        menuItemId: item.menuItemId,
        quantity: item.quantity
      })),
      customerName: this.customerName.trim() || undefined,
      phoneNumber: this.phoneNumber.trim() || undefined,
      notes: this.notes.trim() || undefined,
      paymentMethod: this.paymentMethod,
      upiReference: this.paymentMethod === 'upi-qr' ? this.upiReference.trim() : undefined,
      printInvoice: this.autoPrintInvoice
    };

    this.isGenerating = true;
    this.latestInvoice = null;

    try {
      const result = await this.orderService.createCounterSaleInvoice(payload).toPromise();
      if (!result) {
        this.errorMessage = 'Invoice creation failed.';
        return;
      }

      this.latestInvoice = result;
      this.successMessage = result.printQueued
        ? `Invoice ${result.invoiceNumber} created and print queued.`
        : `Invoice ${result.invoiceNumber} created successfully.`;

      this.cartItems = [];
      this.notes = '';
      this.upiReference = '';
    } catch (error: any) {
      this.errorMessage = error?.error?.error || error?.message || 'Failed to create counter sale invoice.';
    } finally {
      this.isGenerating = false;
    }
  }

  async printInvoice(): Promise<void> {
    if (!this.latestInvoice?.orderId) return;

    this.successMessage = '';
    this.errorMessage = '';
    this.isPrinting = true;

    try {
      await this.orderService.printCounterSaleInvoice(this.latestInvoice.orderId).toPromise();
      this.successMessage = `Invoice ${this.latestInvoice.invoiceNumber} print dispatched.`;
    } catch (error: any) {
      this.errorMessage = error?.error?.error || error?.message || 'Failed to dispatch invoice print.';
    } finally {
      this.isPrinting = false;
    }
  }

  async downloadReceiptPdf(): Promise<void> {
    if (!this.latestInvoice?.orderId) return;

    this.successMessage = '';
    this.errorMessage = '';

    try {
      const blob = await this.orderService.downloadReceiptPdf(this.latestInvoice.orderId).toPromise();
      if (!blob) {
        this.errorMessage = 'Unable to download receipt PDF.';
        return;
      }

      const url = window.URL.createObjectURL(blob);
      const anchor = document.createElement('a');
      anchor.href = url;
      anchor.download = `invoice-${this.latestInvoice.invoiceNumber}.pdf`;
      anchor.click();
      window.URL.revokeObjectURL(url);
    } catch (error: any) {
      this.errorMessage = error?.error?.error || error?.message || 'Failed to download receipt PDF.';
    }
  }

  private async loadMenuItems(): Promise<void> {
    this.isLoadingMenu = true;
    this.errorMessage = '';

    try {
      const items = await this.menuService.getMenuItems().toPromise();
      this.menuItems = (items || [])
        .filter(item => item.isAddOnOnly !== true)
        .filter(item => item.isAvailable !== false)
        .sort((a, b) => a.name.localeCompare(b.name));
    } catch (error: any) {
      this.errorMessage = error?.error?.error || error?.message || 'Failed to load menu items.';
    } finally {
      this.isLoadingMenu = false;
    }
  }

  resolveCounterPrice(item: MenuItem): number {
    if ((item.shopSellingPrice ?? 0) > 0) return item.shopSellingPrice as number;
    if ((item.webPrice ?? 0) > 0) return item.webPrice as number;
    return item.onlinePrice;
  }
}
