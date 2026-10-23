import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { CashierDineInComponent } from './cashier-dine-in.component';
import { CashierActiveDineInSession, DineInService } from '../../services/dine-in.service';
import { BillPrinterService } from '../../services/bill-printer.service';
import { DineInBill } from '../../models/dine-in.model';

describe('CashierDineInComponent bill printing', () => {
  let component: CashierDineInComponent;
  let dineIn: jasmine.SpyObj<DineInService>;
  let printer: jasmine.SpyObj<BillPrinterService>;
  let settings: any;

  const session = (overrides: Partial<CashierActiveDineInSession> = {}): CashierActiveDineInSession => ({
    sessionId: 's1', tableNumber: 'T1', sessionStatus: 'active', paymentStatus: 'unpaid',
    grandTotal: 200, amountPaid: 0, amountDue: 200, hasPendingPayment: false, ...overrides
  } as CashierActiveDineInSession);

  const bill = (overrides: Partial<DineInBill> = {}) => ({
    sessionId: 's1', tableNumber: 'T1', status: 'paid', paymentStatus: 'paid', amountDue: 0, amountPaid: 200,
    grandTotal: 200, subtotal: 200, discountAmount: 0, loyaltyDiscountAmount: 0, taxAmount: 0,
    rounds: [], paymentEntries: [], createdAt: '2025-01-01T10:00:00Z', ...overrides
  } as unknown as DineInBill);

  const respond = (sessions: CashierActiveDineInSession[]) =>
    dineIn.getCashierPendingDineInPayments.and.returnValue(of({ payments: [], sessions }));

  const flush = () => new Promise(resolve => setTimeout(resolve));

  beforeEach(() => {
    dineIn = jasmine.createSpyObj<DineInService>('DineInService',
      ['getCashierPendingDineInPayments', 'confirmPayment', 'recordCashierPayment', 'getStaffSessionBill']);
    dineIn.getStaffSessionBill.and.returnValue(of(bill()));
    settings = { mode: 'ble', paperWidth: 32, autoPrint: { dineInBillRequested: true, dineInSettled: true } };
    printer = jasmine.createSpyObj<BillPrinterService>('BillPrinterService', ['autoPrint', 'print'], { settings });
    printer.autoPrint.and.resolveTo(true);
    printer.print.and.resolveTo();
    spyOn(window, 'confirm').and.returnValue(true);

    TestBed.configureTestingModule({
      providers: [
        { provide: DineInService, useValue: dineIn },
        { provide: BillPrinterService, useValue: printer }
      ]
    });
    component = TestBed.runInInjectionContext(() => new CashierDineInComponent());
  });

  afterEach(() => component.ngOnDestroy());

  it('does not print bills already requested when the desk opens, only new requests', () => {
    respond([session({ sessionId: 'old', sessionStatus: 'bill_requested' })]);
    component.loadPayments();
    expect(printer.autoPrint).not.toHaveBeenCalled();

    respond([
      session({ sessionId: 'old', sessionStatus: 'bill_requested' }),
      session({ sessionId: 'new', tableNumber: 'T7', sessionStatus: 'bill_requested' })
    ]);
    component.loadPayments(true);
    component.loadPayments(true);
    expect(printer.autoPrint).toHaveBeenCalledOnceWith('dineInBillRequested', jasmine.any(Function), false);
  });

  it('does not treat a status change caused by the cashier as a customer bill request', () => {
    respond([session()]);
    component.loadPayments();
    dineIn.recordCashierPayment.and.returnValue(of({ message: 'Recorded' }));
    dineIn.getStaffSessionBill.and.returnValue(of(bill({ status: 'bill_requested', paymentStatus: 'partially_paid', amountDue: 100 })));
    respond([session({ sessionStatus: 'bill_requested', amountDue: 100 })]);
    component.paymentAmounts['s1'] = 100;

    component.recordPayment(session());

    expect(printer.autoPrint).not.toHaveBeenCalled();
  });

  it('prints the settled bill after the payment that clears the table', () => {
    respond([session()]);
    component.loadPayments();
    dineIn.recordCashierPayment.and.returnValue(of({ message: 'Recorded' }));
    respond([]);

    component.recordPayment(session());

    expect(printer.autoPrint).toHaveBeenCalledOnceWith('dineInSettled', jasmine.any(Function), true);
  });

  it('shows a print prompt when an automatic print cannot run', async () => {
    printer.autoPrint.and.resolveTo(false);
    respond([]);
    component.loadPayments();
    respond([session({ sessionStatus: 'bill_requested', tableNumber: 'T9' })]);
    component.loadPayments(true);
    await flush();

    expect(component.billPrompts.length).toBe(1);
    expect(component.billPrompts[0].tableNumber).toBe('T9');

    await component.printBill('s1');
    expect(printer.print).toHaveBeenCalledTimes(1);
    expect(component.billPrompts.length).toBe(0);
  });

  it('keeps the amount the cashier is typing when a background refresh runs', () => {
    respond([session()]);
    component.loadPayments();
    component.paymentAmounts['s1'] = 50;
    component.loadPayments(true);
    expect(component.paymentAmounts['s1']).toBe(50);

    respond([session({ amountDue: 120 })]);
    component.loadPayments(true);
    expect(component.paymentAmounts['s1']).toBe(120);
  });
});
