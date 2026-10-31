import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Subscription, firstValueFrom, interval } from 'rxjs';
import {
  CashierActiveDineInSession,
  CashierPendingDineInPayment,
  DineInService
} from '../../services/dine-in.service';
import { BillPrintTrigger, BillPrinterService } from '../../services/bill-printer.service';
import { DineInBill } from '../../models/dine-in.model';
import { renderDineInBill } from '../../utils/bill-receipt';
import { BillPrinterControlComponent } from '../bill-printer-control/bill-printer-control.component';
import { Order, OrderService } from '../../services/order.service';

export interface BillPrintPrompt {
  sessionId: string;
  tableNumber: string;
  reason: string;
}

const POLL_INTERVAL_MS = 15000;

@Component({
  selector: 'app-cashier-dine-in',
  standalone: true,
  imports: [CommonModule, FormsModule, BillPrinterControlComponent],
  templateUrl: './cashier-dine-in.component.html',
  styleUrls: ['./cashier-dine-in.component.scss']
})
export class CashierDineInComponent implements OnInit, OnDestroy {
  private readonly dineInService = inject(DineInService);
  private readonly billPrinter = inject(BillPrinterService);
  private readonly orderService = inject(OrderService);
  private readonly subscriptions = new Subscription();
  /** Sessions already known to be in bill_requested, so each request prints once. */
  private readonly knownBillRequests = new Set<string>();
  private billRequestsSeeded = false;
  private lastAmountDue: Record<string, number> = {};

  readonly printTriggers: BillPrintTrigger[] = ['dineInBillRequested', 'dineInSettled'];

  payments: CashierPendingDineInPayment[] = [];
  sessions: CashierActiveDineInSession[] = [];
  activeShopOrders: Order[] = [];
  paymentAmounts: Record<string, number> = {};
  paymentMethods: Record<string, 'cash_at_counter' | 'upi-qr'> = {};
  billPrompts: BillPrintPrompt[] = [];
  loading = false;
  confirmingSessionId = '';
  printingSessionId = '';
  errorMessage = '';
  successMessage = '';

  ngOnInit(): void {
    this.loadPayments();
    this.subscriptions.add(interval(POLL_INTERVAL_MS).subscribe(() => {
      if (!this.loading && !this.confirmingSessionId) this.loadPayments(true);
    }));
  }

  ngOnDestroy(): void {
    this.subscriptions.unsubscribe();
  }

  loadPayments(silent = false): void {
    this.loading = true;
    if (!silent) this.errorMessage = '';
    this.subscriptions.add(this.dineInService.getCashierPendingDineInPayments().subscribe({
      next: result => {
        this.payments = result.payments || [];
        this.sessions = result.sessions || [];
        const amounts: Record<string, number> = {};
        const methods: Record<string, 'cash_at_counter' | 'upi-qr'> = {};
        for (const session of this.sessions) {
          const unchanged = this.lastAmountDue[session.sessionId] === session.amountDue;
          amounts[session.sessionId] = unchanged && session.sessionId in this.paymentAmounts
            ? this.paymentAmounts[session.sessionId]
            : session.amountDue;
          methods[session.sessionId] = this.paymentMethods[session.sessionId] || 'cash_at_counter';
        }
        this.paymentAmounts = amounts;
        this.paymentMethods = methods;
        this.lastAmountDue = Object.fromEntries(this.sessions.map(session => [session.sessionId, session.amountDue]));
        this.detectBillRequests();
        this.loadActiveOrders();
        this.loading = false;
      },
      error: error => {
        if (!silent) this.errorMessage = error?.error?.error || error?.message || 'Could not load pending table payments.';
        this.loading = false;
      }
    }));
  }

  cancelActiveOrder(order: Order): void {
    if (!order?.id || !this.canCancelActiveOrder(order)) {
      return;
    }

    if (!window.confirm(`Cancel ${this.describeOrder(order)} #${order.id.slice(-6)}?`)) {
      return;
    }

    this.confirmingSessionId = order.id;
    this.errorMessage = '';
    this.successMessage = '';
    this.subscriptions.add(this.orderService.updateOrderStatus(order.id, 'cancelled', this.resolveOrderChannel(order)).subscribe({
      next: () => {
        this.successMessage = `${this.describeOrder(order)} #${order.id.slice(-6)} cancelled.`;
        this.confirmingSessionId = '';
        this.loadPayments(true);
        this.loadActiveOrders();
      },
      error: error => {
        this.errorMessage = error?.error?.error || error?.message || 'Could not cancel this order.';
        this.confirmingSessionId = '';
      }
    }));
  }

  canCancelActiveOrder(order: Order): boolean {
    return ['pending', 'confirmed', 'preparing', 'ready', 'out-for-delivery', 'scheduled']
      .includes((order.status || '').toLowerCase());
  }

  describeOrder(order: Order): string {
    if (order.orderSource === 'phone') return 'Phone order';
    if (order.orderType === 'dine-in') return 'Dine-in order';
    if (order.orderType === 'pickup') return 'Takeaway order';
    return 'Order';
  }

  private loadActiveOrders(): void {
    this.subscriptions.add(this.orderService.getAllOrders('shop').subscribe({
      next: orders => {
        this.activeShopOrders = (orders || [])
          .filter(order => order.orderType === 'dine-in' || order.orderType === 'pickup' || order.orderSource === 'phone')
          .filter(order => order.status !== 'delivered' && order.status !== 'cancelled')
          .sort((a, b) => new Date(b.updatedAt).getTime() - new Date(a.updatedAt).getTime());
      },
      error: error => {
        this.errorMessage = error?.error?.error || error?.message || 'Could not load active orders.';
      }
    }));
  }

  private resolveOrderChannel(order: Order): 'web' | 'shop' | 'partner' | undefined {
    if (order.channel === 'web' || order.channel === 'shop' || order.channel === 'partner') {
      return order.channel;
    }
    return undefined;
  }

  confirmPayment(payment: CashierPendingDineInPayment): void {
    if (this.confirmingSessionId) return;
    const amount = payment.pendingPayment.amount;
    const confirm = window.confirm(
      `Confirm ${payment.pendingPayment.paymentMethod} payment of ₹${amount.toFixed(2)} for Table ${payment.tableNumber}?`
    );
    if (!confirm) return;

    this.confirmingSessionId = payment.sessionId;
    this.knownBillRequests.add(payment.sessionId);
    this.errorMessage = '';
    this.successMessage = '';
    this.subscriptions.add(this.dineInService.confirmPayment(payment.sessionId).subscribe({
      next: response => {
        this.successMessage = response.message;
        this.confirmingSessionId = '';
        this.loadPayments();
        this.printIfSettled(payment.sessionId);
      },
      error: error => {
        this.errorMessage = error?.error?.error || error?.message || 'Could not confirm this payment.';
        this.confirmingSessionId = '';
      }
    }));
  }

  recordPayment(session: CashierActiveDineInSession): void {
    if (this.confirmingSessionId || session.hasPendingPayment) return;
    const amount = Number(this.paymentAmounts[session.sessionId]);
    if (!Number.isFinite(amount) || amount <= 0 || amount > session.amountDue) {
      this.errorMessage = `Enter an amount greater than ₹0 and no more than ₹${session.amountDue.toFixed(2)}.`;
      return;
    }

    const paymentMethod = this.paymentMethods[session.sessionId] || 'cash_at_counter';
    const methodLabel = paymentMethod === 'upi-qr' ? 'UPI' : 'cash';
    if (!window.confirm(
      `Record ${methodLabel} payment of ₹${amount.toFixed(2)} for Table ${session.tableNumber}?`
    )) return;

    this.confirmingSessionId = session.sessionId;
    this.knownBillRequests.add(session.sessionId);
    this.errorMessage = '';
    this.successMessage = '';
    this.subscriptions.add(this.dineInService.recordCashierPayment(session.sessionId, {
      amount,
      paymentMethod
    }).subscribe({
      next: response => {
        this.successMessage = response.message;
        this.confirmingSessionId = '';
        this.loadPayments();
        this.printIfSettled(session.sessionId);
      },
      error: error => {
        this.errorMessage = error?.error?.error || error?.message || 'Could not record this payment.';
        this.confirmingSessionId = '';
      }
    }));
  }

  /** Manual bill print (a tap), so it works in both Bluetooth and RawBT modes. */
  async printBill(sessionId: string): Promise<void> {
    if (this.printingSessionId) return;
    this.printingSessionId = sessionId;
    this.errorMessage = '';
    try {
      const bill = await firstValueFrom(this.dineInService.getStaffSessionBill(sessionId));
      await this.billPrinter.print(renderDineInBill(bill, this.billPrinter.settings.paperWidth));
      this.billPrompts = this.billPrompts.filter(prompt => prompt.sessionId !== sessionId);
      this.successMessage = `Bill printed for Table ${bill.tableNumber}.`;
    } catch (error: any) {
      this.errorMessage = error?.error?.error || error?.message || 'Could not print the bill.';
    } finally {
      this.printingSessionId = '';
    }
  }

  dismissPrompt(prompt: BillPrintPrompt): void {
    this.billPrompts = this.billPrompts.filter(item => item !== prompt);
  }

  /**
   * Finds tables that newly asked for the bill since the last refresh. The first load only
   * records existing requests so opening the desk does not reprint old bills.
   */
  private detectBillRequests(): void {
    const requested = new Map<string, string>();
    for (const entry of [...this.sessions, ...this.payments]) {
      if (entry.sessionStatus === 'bill_requested') requested.set(entry.sessionId, entry.tableNumber);
    }
    for (const sessionId of Array.from(this.knownBillRequests)) {
      if (!requested.has(sessionId) && this.confirmingSessionId !== sessionId) this.knownBillRequests.delete(sessionId);
    }

    const newRequests = Array.from(requested.entries()).filter(([sessionId]) => !this.knownBillRequests.has(sessionId));
    newRequests.forEach(([sessionId]) => this.knownBillRequests.add(sessionId));
    if (!this.billRequestsSeeded) {
      this.billRequestsSeeded = true;
      return;
    }
    for (const [sessionId, tableNumber] of newRequests) {
      this.autoPrintSession('dineInBillRequested', sessionId, tableNumber, 'asked for the bill', false);
    }
  }

  private printIfSettled(sessionId: string): void {
    if (!this.isAutoPrintEnabled('dineInSettled')) return;
    this.subscriptions.add(this.dineInService.getStaffSessionBill(sessionId).subscribe({
      next: bill => {
        if (bill.status === 'paid' || bill.paymentStatus === 'paid' || bill.amountDue <= 0) {
          this.autoPrintSession('dineInSettled', sessionId, bill.tableNumber, 'is settled', true, bill);
        }
      },
      error: () => this.addPrompt(sessionId, '', 'is settled')
    }));
  }

  private autoPrintSession(
    trigger: BillPrintTrigger,
    sessionId: string,
    tableNumber: string,
    reason: string,
    userInitiated: boolean,
    knownBill?: DineInBill
  ): void {
    if (!this.isAutoPrintEnabled(trigger)) return;
    this.billPrinter.autoPrint(trigger, async () => {
      const bill = knownBill || await this.loadBill(sessionId);
      return renderDineInBill(bill, this.billPrinter.settings.paperWidth);
    }, userInitiated).then(printed => {
      if (!printed) this.addPrompt(sessionId, tableNumber, reason);
    }).catch(() => this.addPrompt(sessionId, tableNumber, reason));
  }

  private loadBill(sessionId: string): Promise<DineInBill> {
    return firstValueFrom(this.dineInService.getStaffSessionBill(sessionId));
  }

  private isAutoPrintEnabled(trigger: BillPrintTrigger): boolean {
    const settings = this.billPrinter.settings;
    return settings.mode !== 'off' && settings.autoPrint[trigger];
  }

  /** Shown when an automatic print could not run (printer not connected, RawBT needs a tap, or an error). */
  private addPrompt(sessionId: string, tableNumber: string, reason: string): void {
    this.billPrompts = [
      ...this.billPrompts.filter(prompt => prompt.sessionId !== sessionId),
      { sessionId, tableNumber, reason }
    ];
  }
}
