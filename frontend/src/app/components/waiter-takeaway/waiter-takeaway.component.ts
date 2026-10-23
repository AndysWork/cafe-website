import { CommonModule } from '@angular/common';
import { Component, EventEmitter, OnDestroy, OnInit, Output, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Observable, Subscription, interval } from 'rxjs';
import { MenuItem, MenuService } from '../../services/menu.service';
import { OutletService } from '../../services/outlet.service';
import {
  TakeawayPaymentMethod,
  TakeawayPaymentTiming,
  TakeawayService,
  WaiterTakeawayOrder
} from '../../services/takeaway.service';
import { getOrderingUnavailableReason } from '../../models/outlet.model';
import { BillPrintTrigger, BillPrinterService } from '../../services/bill-printer.service';
import { renderTakeawayBill } from '../../utils/bill-receipt';
import { BillPrinterControlComponent } from '../bill-printer-control/bill-printer-control.component';

interface TakeawayCartLine {
  menuItem: MenuItem;
  quantity: number;
  variantName?: string;
  addOnNames: string[];
}

@Component({
  selector: 'app-waiter-takeaway',
  standalone: true,
  imports: [CommonModule, FormsModule, BillPrinterControlComponent],
  templateUrl: './waiter-takeaway.component.html',
  styleUrls: ['../waiter-dine-in/waiter-dine-in.component.scss', './waiter-takeaway.component.scss']
})
export class WaiterTakeawayComponent implements OnInit, OnDestroy {
  private readonly takeawayService = inject(TakeawayService);
  private readonly menuService = inject(MenuService);
  private readonly outletService = inject(OutletService);
  private readonly billPrinter = inject(BillPrinterService);
  private readonly subscriptions = new Subscription();

  @Output() readyCountChange = new EventEmitter<number>();

  readonly printTriggers: BillPrintTrigger[] = ['takeawayPaid', 'takeawayHandover'];
  printingOrderId = '';
  menuItems: MenuItem[] = [];
  cart: TakeawayCartLine[] = [];
  orders: WaiterTakeawayOrder[] = [];
  selectedVariants: Record<string, string> = {};
  selectedAddOns: Record<string, Set<string>> = {};
  searchQuery = '';
  customerName = '';
  phoneNumber = '';
  preparationNotes = '';
  paymentTiming: TakeawayPaymentTiming = 'handover';
  paymentMethod: TakeawayPaymentMethod = 'cash_at_counter';
  upiReference = '';
  collectingOrderId = '';
  collectMethod: TakeawayPaymentMethod = 'cash_at_counter';
  collectUpiReference = '';
  busyOrderId = '';
  loadingMenu = false;
  loadingOrders = false;
  submitting = false;
  message = '';
  errorMessage = '';

  ngOnInit(): void {
    this.loadMenu();
    this.loadOrders();
    this.subscriptions.add(interval(10000).subscribe(() => this.loadOrders()));
  }

  ngOnDestroy(): void {
    this.subscriptions.unsubscribe();
  }

  get orderingUnavailableReason(): string {
    return getOrderingUnavailableReason(this.outletService.getSelectedOutlet(), 'pickup');
  }

  get packagingCharge(): number {
    const charge = Number(this.outletService.getSelectedOutlet()?.settings?.packagingCharge || 0);
    return Number.isFinite(charge) && charge > 0 ? Math.min(charge, 1000) : 0;
  }

  get filteredMenuItems(): MenuItem[] {
    const query = this.searchQuery.trim().toLowerCase();
    return this.menuItems.filter(item =>
      !item.isAddOnOnly &&
      item.isAvailable !== false &&
      (!query || item.name.toLowerCase().includes(query)
        || (item.categoryName || item.category || '').toLowerCase().includes(query))
    );
  }

  get cartSubtotal(): number {
    return this.cart.reduce((total, line) => total + this.linePrice(line) * line.quantity, 0);
  }

  get cartTotal(): number {
    return this.cart.length ? this.cartSubtotal + this.packagingCharge : 0;
  }

  get cartItemCount(): number {
    return this.cart.reduce((count, line) => count + line.quantity, 0);
  }

  get readyOrders(): WaiterTakeawayOrder[] {
    return this.orders.filter(order => order.status === 'ready');
  }

  get canSubmit(): boolean {
    return !!this.cart.length
      && !!this.customerName.trim()
      && !this.submitting
      && !this.orderingUnavailableReason
      && (this.paymentTiming === 'handover' || this.paymentMethod !== 'upi-qr' || !!this.upiReference.trim());
  }

  getActiveAddOns(menuItem: MenuItem): NonNullable<MenuItem['addOns']> {
    return (menuItem.addOns || []).filter(addOn => addOn.isActive !== false);
  }

  /** Takeaway is a shop sale, so prices follow the counter (shop) price list. */
  basePrice(menuItem: MenuItem, variantName?: string): number {
    const variantPrice = variantName
      ? menuItem.variants?.find(variant => variant.variantName === variantName)?.price
      : undefined;
    if (variantPrice !== undefined) return variantPrice;
    if ((menuItem.shopSellingPrice || 0) > 0) return menuItem.shopSellingPrice!;
    if ((menuItem.webPrice || 0) > 0) return menuItem.webPrice!;
    return menuItem.onlinePrice || 0;
  }

  menuItemPrice(menuItem: MenuItem): number {
    return this.basePrice(menuItem, this.selectedVariants[menuItem.id]);
  }

  linePrice(line: TakeawayCartLine): number {
    const addOnPrice = line.addOnNames.reduce((total, name) =>
      total + (line.menuItem.addOns?.find(addOn => addOn.name === name)?.price || 0), 0);
    return this.basePrice(line.menuItem, line.variantName) + addOnPrice;
  }

  onAddOnChange(menuItemId: string, addOnName: string, event: Event): void {
    const selections = new Set(this.selectedAddOns[menuItemId] || []);
    if ((event.target as HTMLInputElement).checked) selections.add(addOnName);
    else selections.delete(addOnName);
    this.selectedAddOns[menuItemId] = selections;
  }

  addToCart(menuItem: MenuItem): void {
    const variantName = this.selectedVariants[menuItem.id] || undefined;
    const addOnNames = Array.from(this.selectedAddOns[menuItem.id] || []).sort();
    const existing = this.cart.find(line =>
      line.menuItem.id === menuItem.id
      && line.variantName === variantName
      && line.addOnNames.join('\u0000') === addOnNames.join('\u0000'));
    if (existing) existing.quantity++;
    else this.cart.push({ menuItem, quantity: 1, variantName, addOnNames });
  }

  changeQuantity(line: TakeawayCartLine, change: number): void {
    line.quantity += change;
    if (line.quantity <= 0) this.cart = this.cart.filter(item => item !== line);
  }

  submitOrder(): void {
    if (this.orderingUnavailableReason) {
      this.errorMessage = this.orderingUnavailableReason;
      return;
    }
    if (!this.canSubmit) return;

    this.submitting = true;
    this.message = '';
    this.errorMessage = '';
    const payNow = this.paymentTiming === 'upfront';
    this.subscriptions.add(this.takeawayService.createOrder({
      customerName: this.customerName.trim(),
      phoneNumber: this.phoneNumber.trim() || undefined,
      items: this.cart.map(line => ({
        menuItemId: line.menuItem.id,
        quantity: line.quantity,
        selectedVariantName: line.variantName,
        selectedAddOnNames: line.addOnNames
      })),
      preparationNotes: this.preparationNotes.trim() || undefined,
      paymentTiming: this.paymentTiming,
      paymentMethod: payNow ? this.paymentMethod : undefined,
      upiReference: payNow && this.paymentMethod === 'upi-qr' ? this.upiReference.trim() : undefined
    }).subscribe({
      next: response => {
        this.message = `${response.message}. Give token ${response.order.token} to ${response.order.customerName}.`;
        this.resetForm();
        this.submitting = false;
        this.loadOrders();
        if (response.order.paymentStatus === 'paid') this.autoPrintBill('takeawayPaid', response.order);
      },
      error: error => {
        this.errorMessage = this.errorText(error, 'Could not send this takeaway order.');
        this.submitting = false;
      }
    }));
  }

  startCollecting(order: WaiterTakeawayOrder): void {
    this.collectingOrderId = order.orderId;
    this.collectMethod = 'cash_at_counter';
    this.collectUpiReference = '';
  }

  cancelCollecting(): void {
    this.collectingOrderId = '';
    this.collectUpiReference = '';
  }

  confirmPayment(order: WaiterTakeawayOrder): void {
    if (this.busyOrderId) return;
    if (this.collectMethod === 'upi-qr' && !this.collectUpiReference.trim()) {
      this.errorMessage = 'Enter the UPI reference before confirming payment.';
      return;
    }
    this.runAction(order, this.takeawayService.recordPayment(
      order.orderId,
      this.collectMethod,
      this.collectMethod === 'upi-qr' ? this.collectUpiReference.trim() : undefined
    ), 'Could not record payment.', response => {
      this.cancelCollecting();
      this.autoPrintBill('takeawayPaid', response.order || { ...order, paymentStatus: 'paid', paymentMethod: this.collectMethod });
    });
  }

  handOver(order: WaiterTakeawayOrder): void {
    if (this.busyOrderId || !this.canHandOver(order)) return;
    this.runAction(order, this.takeawayService.handOver(order.orderId), 'Could not complete the handover.',
      response => this.autoPrintBill('takeawayHandover', response.order || order));
  }

  async printBill(order: WaiterTakeawayOrder): Promise<void> {
    if (this.printingOrderId) return;
    this.printingOrderId = order.orderId;
    this.errorMessage = '';
    try {
      await this.billPrinter.print(this.billLines(order));
      this.message = `Bill printed for token ${order.token}.`;
    } catch (error: any) {
      this.errorMessage = error?.message || 'Could not print the bill.';
    } finally {
      this.printingOrderId = '';
    }
  }

  reprintKot(order: WaiterTakeawayOrder): void {
    if (this.busyOrderId) return;
    this.runAction(order, this.takeawayService.reprintKot(order.orderId), 'Could not queue the KOT reprint.');
  }

  cancelOrder(order: WaiterTakeawayOrder): void {
    const reason = window.prompt(`Reason for cancelling takeaway ${order.token}?`);
    if (!reason?.trim() || this.busyOrderId) return;
    this.runAction(order, this.takeawayService.cancelOrder(order.orderId, reason.trim()), 'Could not cancel this takeaway.');
  }

  canHandOver(order: WaiterTakeawayOrder): boolean {
    return order.status === 'ready' && order.paymentStatus === 'paid';
  }

  canCancel(order: WaiterTakeawayOrder): boolean {
    return (order.status === 'pending' || order.status === 'confirmed') && order.paymentStatus !== 'paid';
  }

  statusLabel(order: WaiterTakeawayOrder): string {
    switch (order.status) {
      case 'pending':
      case 'confirmed': return 'Waiting for kitchen';
      case 'preparing': return 'Being packed';
      case 'ready': return 'Packed · collect from kitchen';
      default: return order.status;
    }
  }

  paymentLabel(order: WaiterTakeawayOrder): string {
    if (order.paymentStatus !== 'paid') return 'Payment due';
    return order.paymentMethod === 'upi-qr' ? 'Paid · UPI' : 'Paid · Cash';
  }

  private billLines(order: WaiterTakeawayOrder) {
    return renderTakeawayBill(order, this.outletService.getSelectedOutlet()?.outletName, this.billPrinter.settings.paperWidth);
  }

  /** Bill printing never blocks the takeaway workflow; failures only show a message. */
  private autoPrintBill(trigger: BillPrintTrigger, order: WaiterTakeawayOrder): void {
    this.billPrinter.autoPrint(trigger, () => this.billLines(order)).catch(error => {
      this.errorMessage = `Bill not printed: ${error?.message || 'printer error'}. Use Print bill to retry.`;
    });
  }

  private runAction<T extends { message: string; order?: WaiterTakeawayOrder }>(
    order: WaiterTakeawayOrder,
    request: Observable<T>,
    fallbackError: string,
    onSuccess?: (response: T) => void
  ): void {
    this.busyOrderId = order.orderId;
    this.message = '';
    this.errorMessage = '';
    this.subscriptions.add(request.subscribe({
      next: response => {
        this.message = response.message;
        this.busyOrderId = '';
        onSuccess?.(response);
        this.loadOrders();
      },
      error: error => {
        this.errorMessage = this.errorText(error, fallbackError);
        this.busyOrderId = '';
        this.loadOrders();
      }
    }));
  }

  private resetForm(): void {
    this.cart = [];
    this.customerName = '';
    this.phoneNumber = '';
    this.preparationNotes = '';
    this.paymentTiming = 'handover';
    this.paymentMethod = 'cash_at_counter';
    this.upiReference = '';
  }

  private loadMenu(): void {
    this.loadingMenu = true;
    this.subscriptions.add(this.menuService.getMenuItems().subscribe({
      next: items => {
        this.menuItems = items;
        this.selectedVariants = Object.fromEntries(
          items.filter(item => item.variants?.length).map(item => [item.id, item.variants![0].variantName]));
        this.loadingMenu = false;
      },
      error: error => {
        this.errorMessage = this.errorText(error, 'Could not load the menu.');
        this.loadingMenu = false;
      }
    }));
  }

  private loadOrders(): void {
    this.loadingOrders = true;
    this.subscriptions.add(this.takeawayService.getActiveOrders().subscribe({
      next: result => {
        this.orders = result.orders || [];
        this.loadingOrders = false;
        if (this.collectingOrderId && !this.orders.some(order => order.orderId === this.collectingOrderId)) {
          this.cancelCollecting();
        }
        this.readyCountChange.emit(this.readyOrders.length);
      },
      error: error => {
        this.errorMessage = this.errorText(error, 'Could not load takeaway orders.');
        this.loadingOrders = false;
      }
    }));
  }

  private errorText(error: any, fallback: string): string {
    return error?.error?.error || error?.message || fallback;
  }
}
