import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Subscription, interval } from 'rxjs';
import { CafeTableFloorView, CafeTableService } from '../../services/cafe-table.service';
import { DineInService, DineInSessionOrder, PendingQrDineInOrder } from '../../services/dine-in.service';
import { MenuItem, MenuService } from '../../services/menu.service';
import { OutletService } from '../../services/outlet.service';
import { getOrderingUnavailableReason } from '../../models/outlet.model';
import { WaiterTakeawayComponent } from '../waiter-takeaway/waiter-takeaway.component';

interface WaiterCartLine {
  menuItem: MenuItem;
  quantity: number;
  variantName?: string;
  addOnNames: string[];
}

@Component({
  selector: 'app-waiter-dine-in',
  standalone: true,
  imports: [CommonModule, FormsModule, WaiterTakeawayComponent],
  templateUrl: './waiter-dine-in.component.html',
  styleUrls: ['./waiter-dine-in.component.scss']
})
export class WaiterDineInComponent implements OnInit, OnDestroy {
  private readonly tablesService = inject(CafeTableService);
  private readonly dineInService = inject(DineInService);
  private readonly menuService = inject(MenuService);
  private readonly outletService = inject(OutletService);
  private readonly subscriptions = new Subscription();
  private tableFloorSubscription?: Subscription;
  private tableOrdersSubscription?: Subscription;

  activeTab: 'order' | 'takeaway' | 'qr' = 'order';
  readyTakeawayCount = 0;
  tables: CafeTableFloorView[] = [];
  menuItems: MenuItem[] = [];
  cart: WaiterCartLine[] = [];
  pendingQrOrders: PendingQrDineInOrder[] = [];
  currentSessionOrders: DineInSessionOrder[] = [];
  editingQrOrderId = '';
  editingQrItems: PendingQrDineInOrder['items'] = [];
  editingQrNotes = '';
  editingQrPreparationNotes = '';
  selectedVariants: Record<string, string> = {};
  selectedAddOns: Record<string, Set<string>> = {};
  selectedTableNumber = '';
  searchQuery = '';
  orderNotes = '';
  customerName = '';
  loadingTables = false;
  loadingMenu = false;
  loadingQrOrders = false;
  submittingOrder = false;
  reviewingOrderId = '';
  message = '';
  errorMessage = '';

  ngOnInit(): void {
    this.subscriptions.add(this.outletService.getPublicOutlets().subscribe({
      error: () => this.errorMessage = 'Could not refresh outlet ordering availability. It will be checked when you submit.'
    }));
    this.loadTables();
    this.loadMenu();
    this.loadPendingQrOrders();
    this.subscriptions.add(interval(10000).subscribe(() => {
      this.loadTables();
      this.loadPendingQrOrders();
    }));
  }

  ngOnDestroy(): void {
    this.tableFloorSubscription?.unsubscribe();
    this.tableOrdersSubscription?.unsubscribe();
    this.subscriptions.unsubscribe();
  }

  get availableTables(): CafeTableFloorView[] {
    return this.tables.filter(table =>
      table.isActive &&
      (table.status === 'free' || table.status === 'occupied')
    );
  }

  get filteredMenuItems(): MenuItem[] {
    const query = this.searchQuery.trim().toLowerCase();
    return this.menuItems.filter(item =>
      !item.isAddOnOnly &&
      item.isAvailable !== false &&
      (!query || item.name.toLowerCase().includes(query) || (item.categoryName || item.category || '').toLowerCase().includes(query))
    );
  }

  getActiveAddOns(menuItem: MenuItem): NonNullable<MenuItem['addOns']> {
    return (menuItem.addOns || []).filter(addOn => addOn.isActive !== false);
  }

  menuItemPrice(menuItem: MenuItem): number {
    const selectedVariantName = this.selectedVariants[menuItem.id];
    const variantPrice = menuItem.variants?.find(variant => variant.variantName === selectedVariantName)?.price;
    if (variantPrice !== undefined) return variantPrice;
    if ((menuItem.dineInPrice || 0) > 0) return menuItem.dineInPrice!;
    if ((menuItem.webPrice || 0) > 0) return menuItem.webPrice!;
    return menuItem.shopSellingPrice || 0;
  }

  toggleAddOn(menuItemId: string, addOnName: string, selected: boolean): void {
    const selections = new Set(this.selectedAddOns[menuItemId] || []);
    if (selected) selections.add(addOnName);
    else selections.delete(addOnName);
    this.selectedAddOns[menuItemId] = selections;
  }

  onAddOnChange(menuItemId: string, addOnName: string, event: Event): void {
    this.toggleAddOn(menuItemId, addOnName, (event.target as HTMLInputElement).checked);
  }

  get cartTotal(): number {
    return this.cart.reduce((total, line) => total + this.linePrice(line) * line.quantity, 0);
  }

  get cartItemCount(): number {
    return this.cart.reduce((count, line) => count + line.quantity, 0);
  }

  selectTable(tableNumber: string): void {
    this.selectedTableNumber = tableNumber;
    this.customerName = this.selectedTable?.customerName || '';
    this.message = '';
    this.errorMessage = '';
    this.loadSelectedTableOrders();
  }

  get selectedTable(): CafeTableFloorView | undefined {
    return this.tables.find(table => table.tableNumber === this.selectedTableNumber);
  }

  get editingQrSubtotal(): number {
    return this.editingQrItems.reduce((sum, item) => sum + item.total, 0);
  }

  addToCart(menuItem: MenuItem): void {
    const variantName = this.selectedVariants[menuItem.id] || undefined;
    const addOnNames = Array.from(this.selectedAddOns[menuItem.id] || []).sort();
    const existing = this.cart.find(line =>
      line.menuItem.id === menuItem.id
      && line.variantName === variantName
      && line.addOnNames.join('\u0000') === addOnNames.join('\u0000')
    );
    if (existing) {
      existing.quantity++;
    } else {
      this.cart.push({ menuItem, quantity: 1, variantName, addOnNames });
    }
  }

  changeQuantity(line: WaiterCartLine, change: number): void {
    line.quantity += change;
    if (line.quantity <= 0) {
      this.cart = this.cart.filter(item => item !== line);
    }
  }

  linePrice(line: WaiterCartLine): number {
    const variantPrice = line.variantName
      ? line.menuItem.variants?.find(variant => variant.variantName === line.variantName)?.price
      : undefined;
    const basePrice = variantPrice !== undefined
      ? variantPrice
      : (line.menuItem.dineInPrice || 0) > 0
        ? line.menuItem.dineInPrice!
        : (line.menuItem.webPrice || 0) > 0
          ? line.menuItem.webPrice!
          : line.menuItem.shopSellingPrice || 0;
    const addOnPrice = line.addOnNames.reduce((total, name) =>
      total + (line.menuItem.addOns?.find(addOn => addOn.name === name)?.price || 0), 0);
    return basePrice + addOnPrice;
  }

  submitOrder(): void {
    if (this.orderingUnavailableReason) {
      this.errorMessage = this.orderingUnavailableReason;
      return;
    }
    if (!this.selectedTableNumber || !this.cart.length || this.submittingOrder) return;

    this.submittingOrder = true;
    this.message = '';
    this.errorMessage = '';
    const outletId = this.outletService.getSelectedOutletId() || undefined;
    this.subscriptions.add(this.dineInService.placeWaiterDineInOrder({
      tableNumber: this.selectedTableNumber,
      outletId,
      customerName: this.customerName.trim() || undefined,
      items: this.cart.map(line => ({
        menuItemId: line.menuItem.id,
        quantity: line.quantity,
        selectedVariantName: line.variantName,
        selectedAddOnNames: line.addOnNames
      })),
      notes: this.orderNotes.trim() || undefined
    }).subscribe({
      next: response => {
        this.message = response?.message || 'Order sent to the kitchen.';
        this.cart = [];
        this.orderNotes = '';
        this.submittingOrder = false;
        this.loadTables();
      },
      error: error => {
        this.errorMessage = error?.error?.error || error?.message || 'Could not send this order.';
        this.submittingOrder = false;
      }
    }));
  }

  get orderingUnavailableReason(): string {
    return getOrderingUnavailableReason(this.outletService.getSelectedOutlet(), 'dine-in');
  }

  approveQrOrder(order: PendingQrDineInOrder): void {
    if (this.reviewingOrderId) return;
    this.reviewingOrderId = order.orderId;
    this.errorMessage = '';
    this.subscriptions.add(this.dineInService.approveQrDineInOrder(order.orderId).subscribe({
      next: response => {
        this.message = response.message;
        this.reviewingOrderId = '';
        this.loadPendingQrOrders();
        this.loadTables();
      },
      error: error => {
        this.errorMessage = error?.error?.error || error?.message || 'Could not approve this QR order.';
        this.reviewingOrderId = '';
        this.loadPendingQrOrders();
      }
    }));
  }

  rejectQrOrder(order: PendingQrDineInOrder): void {
    const reason = window.prompt(`Why is the order for Table ${order.tableNumber} being rejected?`);
    if (!reason?.trim() || this.reviewingOrderId) return;

    this.reviewingOrderId = order.orderId;
    this.errorMessage = '';
    this.subscriptions.add(this.dineInService.rejectQrDineInOrder(order.orderId, reason.trim()).subscribe({
      next: response => {
        this.message = response.message;
        this.reviewingOrderId = '';
        this.loadPendingQrOrders();
        this.loadTables();
      },
      error: error => {
        this.errorMessage = error?.error?.error || error?.message || 'Could not reject this QR order.';
        this.reviewingOrderId = '';
        this.loadPendingQrOrders();
      }
    }));
  }

  startEditingQrOrder(order: PendingQrDineInOrder): void {
    this.editingQrOrderId = order.orderId;
    this.editingQrItems = order.items.map(item => ({ ...item, addOns: [...item.addOns] }));
    this.editingQrNotes = order.notes || '';
    this.editingQrPreparationNotes = order.preparationNotes || '';
  }

  cancelEditingQrOrder(): void {
    this.editingQrOrderId = '';
    this.editingQrItems = [];
    this.editingQrNotes = '';
    this.editingQrPreparationNotes = '';
  }

  changeQrItemQuantity(item: PendingQrDineInOrder['items'][number], change: number): void {
    item.quantity += change;
    item.total = item.unitPrice * item.quantity;
    if (item.quantity <= 0) this.editingQrItems = this.editingQrItems.filter(line => line !== item);
  }

  saveQrOrderEdit(order: PendingQrDineInOrder): void {
    if (this.editingQrOrderId !== order.orderId || !this.editingQrItems.length || this.reviewingOrderId) return;
    this.reviewingOrderId = order.orderId;
    this.errorMessage = '';
    this.subscriptions.add(this.dineInService.editPendingQrDineInOrder(order.orderId, {
      items: this.editingQrItems.map(item => ({
        menuItemId: item.menuItemId,
        quantity: item.quantity,
        selectedVariantName: item.variant,
        selectedAddOnNames: item.addOns
      })),
      notes: this.editingQrNotes.trim() || undefined,
      preparationNotes: this.editingQrPreparationNotes.trim() || undefined
    }).subscribe({
      next: response => {
        this.message = response.message;
        this.reviewingOrderId = '';
        this.cancelEditingQrOrder();
        this.loadPendingQrOrders();
        this.loadTables();
      },
      error: error => {
        this.errorMessage = error?.error?.error || error?.message || 'Could not update this QR order.';
        this.reviewingOrderId = '';
      }
    }));
  }

  reprintKot(order: DineInSessionOrder): void {
    if (this.reviewingOrderId) return;
    this.reviewingOrderId = order.orderId;
    this.subscriptions.add(this.dineInService.reprintDineInKot(order.orderId).subscribe({
      next: response => {
        this.message = `${response.kotNumber} queued as a duplicate print.`;
        this.reviewingOrderId = '';
        this.loadSelectedTableOrders();
      },
      error: error => {
        this.errorMessage = error?.error?.error || error?.message || 'Could not queue the KOT reprint.';
        this.reviewingOrderId = '';
      }
    }));
  }

  cancelKot(order: DineInSessionOrder): void {
    const reason = window.prompt(`Reason for cancelling ${order.kotNumber || 'this KOT'}?`);
    if (!reason?.trim() || this.reviewingOrderId) return;
    this.reviewingOrderId = order.orderId;
    this.subscriptions.add(this.dineInService.cancelDineInKot(order.orderId, reason.trim()).subscribe({
      next: response => {
        this.message = response.message;
        this.reviewingOrderId = '';
        this.tableOrdersSubscription?.unsubscribe();
        this.currentSessionOrders = this.currentSessionOrders.filter(item => item.orderId !== order.orderId);
        this.loadTables();
      },
      error: error => {
        this.errorMessage = error?.error?.error || error?.message || 'Could not cancel this KOT.';
        this.reviewingOrderId = '';
      }
    }));
  }

  markItemServed(order: DineInSessionOrder, itemIndex: number): void {
    if (this.reviewingOrderId) return;
    this.reviewingOrderId = order.orderId;
    this.subscriptions.add(this.dineInService.markDineInItemServed(order.orderId, itemIndex).subscribe({
      next: response => {
        this.message = response.message;
        this.reviewingOrderId = '';
        this.loadSelectedTableOrders();
        this.loadTables();
      },
      error: error => {
        this.errorMessage = error?.error?.error || error?.message || 'Could not mark this item served.';
        this.reviewingOrderId = '';
      }
    }));
  }

  refreshQrOrders(): void {
    this.loadPendingQrOrders();
  }

  private loadTables(): void {
    this.tableFloorSubscription?.unsubscribe();
    this.loadingTables = true;
    this.tableFloorSubscription = this.tablesService.getFloor().subscribe({
      next: result => {
        const previousSessionId = this.selectedTable?.sessionId;
        this.tables = result.tables;
        if (previousSessionId !== this.selectedTable?.sessionId) {
          this.customerName = this.selectedTable?.customerName || '';
        }
        this.loadingTables = false;
        this.loadSelectedTableOrders();
      },
      error: error => {
        this.errorMessage = error?.error?.error || error?.message || 'Could not load the table floor.';
        this.loadingTables = false;
      }
    });
  }

  private loadMenu(): void {
    this.loadingMenu = true;
    this.subscriptions.add(this.menuService.getMenuItems().subscribe({
      next: items => {
        this.menuItems = items;
        this.selectedVariants = Object.fromEntries(
          items
            .filter(item => item.variants?.length)
            .map(item => [item.id, item.variants![0].variantName])
        );
        this.loadingMenu = false;
      },
      error: error => {
        this.errorMessage = error?.error?.error || error?.message || 'Could not load the menu.';
        this.loadingMenu = false;
      }
    }));
  }

  private loadPendingQrOrders(): void {
    this.loadingQrOrders = true;
    this.subscriptions.add(this.dineInService.getPendingQrDineInOrders().subscribe({
      next: result => {
        this.pendingQrOrders = result.orders || [];
        this.loadingQrOrders = false;
      },
      error: error => {
        this.errorMessage = error?.error?.error || error?.message || 'Could not load pending QR orders.';
        this.loadingQrOrders = false;
      }
    }));
  }

  private loadSelectedTableOrders(): void {
    this.tableOrdersSubscription?.unsubscribe();
    const sessionId = this.selectedTable?.sessionId;
    if (!sessionId) {
      this.currentSessionOrders = [];
      return;
    }
    this.tableOrdersSubscription = this.dineInService.getDineInSessionOrders(sessionId).subscribe({
      next: result => {
        this.currentSessionOrders = (result.orders || []).filter(order => order.status !== 'cancelled');
      },
      error: error => {
        this.errorMessage = error?.error?.error || error?.message || 'Could not load the selected table orders.';
      }
    });
  }
}
