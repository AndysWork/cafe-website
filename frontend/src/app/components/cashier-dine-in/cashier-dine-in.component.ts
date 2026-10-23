import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import {
  CashierActiveDineInSession,
  CashierPendingDineInPayment,
  DineInService
} from '../../services/dine-in.service';

@Component({
  selector: 'app-cashier-dine-in',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './cashier-dine-in.component.html',
  styleUrls: ['./cashier-dine-in.component.scss']
})
export class CashierDineInComponent implements OnInit, OnDestroy {
  private readonly dineInService = inject(DineInService);
  private readonly subscriptions = new Subscription();

  payments: CashierPendingDineInPayment[] = [];
  sessions: CashierActiveDineInSession[] = [];
  paymentAmounts: Record<string, number> = {};
  paymentMethods: Record<string, 'cash_at_counter' | 'upi-qr'> = {};
  loading = false;
  confirmingSessionId = '';
  errorMessage = '';
  successMessage = '';

  ngOnInit(): void {
    this.loadPayments();
  }

  ngOnDestroy(): void {
    this.subscriptions.unsubscribe();
  }

  loadPayments(): void {
    this.loading = true;
    this.errorMessage = '';
    this.subscriptions.add(this.dineInService.getCashierPendingDineInPayments().subscribe({
      next: result => {
        this.payments = result.payments || [];
        this.sessions = result.sessions || [];
        this.paymentAmounts = Object.fromEntries(
          this.sessions.map(session => [session.sessionId, session.amountDue])
        );
        this.paymentMethods = {};
        this.sessions.forEach(session => {
          this.paymentMethods[session.sessionId] = 'cash_at_counter';
        });
        this.loading = false;
      },
      error: error => {
        this.errorMessage = error?.error?.error || error?.message || 'Could not load pending table payments.';
        this.loading = false;
      }
    }));
  }

  confirmPayment(payment: CashierPendingDineInPayment): void {
    if (this.confirmingSessionId) return;
    const amount = payment.pendingPayment.amount;
    const confirm = window.confirm(
      `Confirm ${payment.pendingPayment.paymentMethod} payment of ₹${amount.toFixed(2)} for Table ${payment.tableNumber}?`
    );
    if (!confirm) return;

    this.confirmingSessionId = payment.sessionId;
    this.errorMessage = '';
    this.successMessage = '';
    this.subscriptions.add(this.dineInService.confirmPayment(payment.sessionId).subscribe({
      next: response => {
        this.successMessage = response.message;
        this.confirmingSessionId = '';
        this.loadPayments();
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
      },
      error: error => {
        this.errorMessage = error?.error?.error || error?.message || 'Could not record this payment.';
        this.confirmingSessionId = '';
      }
    }));
  }
}
