import { DineInBill } from '../models/dine-in.model';
import { WaiterTakeawayOrder } from '../services/takeaway.service';
import { renderDineInBill, renderTakeawayBill } from './bill-receipt';

describe('bill-receipt', () => {
  const item = (name: string, quantity: number, unitPrice: number, extra: Record<string, unknown> = {}) =>
    ({ name, quantity, unitPrice, totalPrice: quantity * unitPrice, ...extra });

  const bill = (overrides: Partial<DineInBill> = {}): DineInBill => ({
    sessionId: 's1', outletId: 'o1', outletName: 'Salt Lake', tableNumber: 'T4',
    customerName: 'Ravi', waiterName: 'Gourab', status: 'bill_requested', paymentStatus: 'unpaid',
    amountPaid: 0, amountDue: 230, paymentEntries: [], voidRequests: [], timeline: [],
    rounds: [
      { status: 'served', items: [item('Masala Chai', 2, 30), item('Veg Grilled Sandwich Extra Cheese', 1, 120)] },
      { status: 'served', items: [item('Masala Chai', 1, 30), item('Cold Coffee', 1, 80, { selectedVariantName: 'Large' })] },
      { status: 'cancelled', items: [item('Brownie', 5, 100)] }
    ] as any,
    totalItemsCount: 5, subtotal: 290, discountAmount: 60, couponCode: 'SAVE60', loyaltyPointsUsed: 0,
    loyaltyDiscountAmount: 0, taxAmount: 0, grandTotal: 230, createdAt: '2025-01-01T10:00:00Z',
    canAddItems: false, canRequestBill: false, ...overrides
  });

  const text = (lines: { text: string }[]) => lines.map(line => line.text);

  it('fits every line within 32 columns for 58 mm paper', () => {
    const lines = text(renderDineInBill(bill()));
    expect(lines.every(line => line.length <= 32)).toBeTrue();
  });

  it('merges repeated items across rounds and leaves cancelled rounds out', () => {
    const output = text(renderDineInBill(bill())).join('\n');
    expect(output).toMatch(/Masala Chai\s+3\s+90\.00/);
    expect(output).not.toContain('Brownie');
    expect(output).toContain('(Large)');
    expect(output).toContain('Discount (SAVE60)');
    expect(output).toContain('Table   : T4');
    expect(output).toContain('Waiter  : Gourab');
  });

  it('wraps long item names onto the next line', () => {
    const lines = text(renderDineInBill(bill()));
    const index = lines.findIndex(line => line.startsWith('Veg Grilled'));
    expect(lines[index]).toContain('120.00');
    expect(lines[index + 1].trim().length).toBeGreaterThan(0);
  });

  it('shows the balance due for an unpaid bill and payments for a settled one', () => {
    expect(text(renderDineInBill(bill())).join('\n')).toMatch(/BALANCE DUE\s+230\.00/);

    const paid = text(renderDineInBill(bill({
      status: 'paid', paymentStatus: 'paid', amountPaid: 230, amountDue: 0, invoiceNumber: 'INV-9',
      paymentEntries: [{ amount: 230, paymentMethod: 'upi-qr', status: 'confirmed' } as any]
    }))).join('\n');
    expect(paid).toContain('PAID BILL');
    expect(paid).toContain('Bill no : INV-9');
    expect(paid).toMatch(/Paid \(UPI\)\s+230\.00/);
    expect(paid).toContain('*** PAID IN FULL ***');
  });

  it('supports 48 columns for 80 mm paper', () => {
    const lines = text(renderDineInBill(bill(), 48));
    expect(lines.every(line => line.length <= 48)).toBeTrue();
    expect(lines).toContain('-'.repeat(48));
  });

  it('renders a takeaway bill with token, packaging and payment state', () => {
    const order: WaiterTakeawayOrder = {
      orderId: 'o1', token: 'K-007', status: 'ready', customerName: 'Asha', phoneNumber: '9999999999',
      subtotal: 40, packagingCharge: 10, total: 50, paymentStatus: 'pending', createdAt: '2025-01-01T10:00:00Z',
      items: [{ name: 'Tea', quantity: 2, unitPrice: 20, total: 40, addOns: [] } as any]
    };
    const due = text(renderTakeawayBill(order, 'Salt Lake')).join('\n');
    expect(due).toContain('TOKEN: K-007');
    expect(due).toMatch(/Packaging\s+10\.00/);
    expect(due).toMatch(/AMOUNT DUE\s+50\.00/);

    const paid = text(renderTakeawayBill({ ...order, paymentStatus: 'paid', paymentMethod: 'cash_at_counter' })).join('\n');
    expect(paid).toContain('TAKEAWAY BILL - PAID');
    expect(paid).toMatch(/Paid \(Cash\)\s+50\.00/);
  });
});
