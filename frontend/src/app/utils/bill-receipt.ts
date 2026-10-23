import { DineInBill } from '../models/dine-in.model';
import { WaiterTakeawayOrder } from '../services/takeaway.service';
import { ReceiptLine, sanitizePrinterText } from './escpos';

/** Characters per line: 32 for 58 mm paper, 48 for 80 mm paper. */
export type ReceiptWidth = 32 | 48;

interface BillItemLine {
  name: string;
  details?: string;
  quantity: number;
  amount: number;
}

const EXCLUDED_ROUND_STATUSES = new Set(['cancelled', 'rejected']);

const money = (value: number | null | undefined) => (Number(value) || 0).toFixed(2);

const clean = (value: string | null | undefined) => sanitizePrinterText(value).replace(/\n/g, ' ').trim();

function center(text: string, width: number): string {
  const value = clean(text);
  if (value.length >= width) return value.slice(0, width);
  return ' '.repeat(Math.floor((width - value.length) / 2)) + value;
}

function leftRight(left: string, right: string, width: number): string {
  const r = clean(right);
  const l = clean(left).slice(0, Math.max(0, width - r.length - 1));
  return l + ' '.repeat(Math.max(1, width - l.length - r.length)) + r;
}

function wrap(text: string, width: number): string[] {
  const words = clean(text).split(/\s+/).filter(Boolean);
  const lines: string[] = [];
  let current = '';
  for (const word of words) {
    for (let i = 0; i < word.length; i += width) {
      const part = word.slice(i, i + width);
      if (!current) current = part;
      else if (current.length + 1 + part.length <= width) current += ` ${part}`;
      else { lines.push(current); current = part; }
    }
  }
  if (current) lines.push(current);
  return lines.length ? lines : [''];
}

function formatDate(value?: string | Date): string {
  const date = value ? new Date(value) : new Date();
  if (Number.isNaN(date.getTime())) return '';
  return date.toLocaleString('en-IN', {
    timeZone: 'Asia/Kolkata', day: '2-digit', month: 'short', year: 'numeric',
    hour: '2-digit', minute: '2-digit', hour12: true
  });
}

function paymentMethodLabel(method?: string): string {
  switch ((method || '').toLowerCase()) {
    case 'upi-qr':
    case 'upi': return 'UPI';
    case 'cash_at_counter':
    case 'cash': return 'Cash';
    case 'razorpay': return 'Online';
    default: return method ? clean(method) : 'Cash';
  }
}

function itemDetails(variant?: string, addOns?: string[]): string | undefined {
  const parts = [variant ? `(${variant})` : '', ...(addOns || []).map(name => `+ ${name}`)].filter(Boolean);
  return parts.length ? parts.join(' ') : undefined;
}

function itemTable(items: BillItemLine[], width: number): ReceiptLine[] {
  const qtyWidth = 4;
  const amountWidth = 10;
  const nameWidth = width - qtyWidth - amountWidth;
  const lines: ReceiptLine[] = [
    { text: `${'Item'.padEnd(nameWidth)}${'Qty'.padStart(qtyWidth)}${'Amount'.padStart(amountWidth)}` },
    { text: '-'.repeat(width) }
  ];
  for (const item of items) {
    const nameLines = wrap(item.name, nameWidth);
    lines.push({
      text: `${nameLines[0].padEnd(nameWidth)}${String(item.quantity).padStart(qtyWidth)}${money(item.amount).padStart(amountWidth)}`
    });
    for (const extra of nameLines.slice(1)) lines.push({ text: extra });
    if (item.details) for (const detail of wrap(item.details, width - 2)) lines.push({ text: `  ${detail}` });
  }
  return lines;
}

function header(outletName: string | undefined, title: string, width: number): ReceiptLine[] {
  const lines: ReceiptLine[] = [{ text: center('MAA TARA CAFE', width), style: 'large' }];
  const outlet = clean(outletName);
  if (outlet && outlet.toUpperCase() !== 'MAA TARA CAFE') lines.push({ text: center(outlet, width) });
  lines.push({ text: center(title, width), style: 'bold' }, { text: '-'.repeat(width) });
  return lines;
}

function footer(width: number): ReceiptLine[] {
  return [
    { text: '-'.repeat(width) },
    { text: center('Thank you. Visit again!', width) },
    { text: center(`Printed ${formatDate()}`, width) }
  ];
}

/** Consolidated dine-in table bill. Cancelled and rejected rounds are left out. */
export function renderDineInBill(bill: DineInBill, width: ReceiptWidth = 32): ReceiptLine[] {
  const merged = new Map<string, BillItemLine>();
  for (const round of bill.rounds || []) {
    if (EXCLUDED_ROUND_STATUSES.has((round.status || '').toLowerCase())) continue;
    for (const item of round.items || []) {
      const details = itemDetails(item.selectedVariantName, item.selectedAddOnNames);
      const key = [item.name, details, item.unitPrice].join('|');
      const existing = merged.get(key);
      if (existing) {
        existing.quantity += item.quantity;
        existing.amount += item.totalPrice;
      } else {
        merged.set(key, { name: item.name, details, quantity: item.quantity, amount: item.totalPrice });
      }
    }
  }

  const isPaid = bill.status === 'paid' || bill.paymentStatus === 'paid';
  const lines = header(bill.outletName, isPaid ? 'PAID BILL' : 'BILL', width);
  if (bill.invoiceNumber) lines.push({ text: `Bill no : ${clean(bill.invoiceNumber)}` });
  lines.push({ text: `Table   : ${clean(bill.tableNumber)}`, style: 'bold' });
  lines.push({ text: `Date    : ${formatDate(bill.settledAt || bill.billRequestedAt || bill.createdAt)}` });
  if (bill.customerName) lines.push({ text: `Customer: ${clean(bill.customerName)}` });
  if (bill.waiterName) lines.push({ text: `Waiter  : ${clean(bill.waiterName)}` });
  lines.push({ text: '-'.repeat(width) }, ...itemTable(Array.from(merged.values()), width), { text: '-'.repeat(width) });

  lines.push({ text: leftRight('Subtotal', money(bill.subtotal), width) });
  if (bill.discountAmount > 0) {
    lines.push({ text: leftRight(bill.couponCode ? `Discount (${bill.couponCode})` : 'Discount', `-${money(bill.discountAmount)}`, width) });
  }
  if (bill.loyaltyDiscountAmount > 0) {
    lines.push({ text: leftRight('Loyalty discount', `-${money(bill.loyaltyDiscountAmount)}`, width) });
  }
  if (bill.taxAmount > 0) lines.push({ text: leftRight('Tax', money(bill.taxAmount), width) });
  lines.push({ text: leftRight('TOTAL', `Rs.${money(bill.grandTotal)}`, width), style: 'large' });

  const confirmed = (bill.paymentEntries || []).filter(entry => entry.status === 'confirmed');
  for (const entry of confirmed) {
    lines.push({ text: leftRight(`Paid (${paymentMethodLabel(entry.paymentMethod)})`, money(entry.amount), width) });
  }
  if (!confirmed.length && bill.amountPaid > 0) {
    lines.push({ text: leftRight(`Paid (${paymentMethodLabel(bill.paymentMethod)})`, money(bill.amountPaid), width) });
  }
  lines.push(bill.amountDue > 0
    ? { text: leftRight('BALANCE DUE', money(bill.amountDue), width), style: 'bold' }
    : { text: center('*** PAID IN FULL ***', width), style: 'bold' });

  return [...lines, ...footer(width)];
}

/** Takeaway (parcel) bill with token and packaging charge. */
export function renderTakeawayBill(order: WaiterTakeawayOrder, outletName?: string, width: ReceiptWidth = 32): ReceiptLine[] {
  const isPaid = order.paymentStatus === 'paid';
  const lines = header(outletName, isPaid ? 'TAKEAWAY BILL - PAID' : 'TAKEAWAY BILL', width);
  lines.push({ text: `TOKEN: ${clean(order.token)}`, style: 'large' });
  lines.push({ text: `Date    : ${formatDate(order.createdAt)}` });
  if (order.customerName) lines.push({ text: `Customer: ${clean(order.customerName)}` });
  if (order.phoneNumber) lines.push({ text: `Phone   : ${clean(order.phoneNumber)}` });
  if (order.waiterName) lines.push({ text: `Waiter  : ${clean(order.waiterName)}` });

  const items = (order.items || []).map(item => ({
    name: item.name,
    details: itemDetails(item.variant, item.addOns),
    quantity: item.quantity,
    amount: item.total
  }));
  lines.push({ text: '-'.repeat(width) }, ...itemTable(items, width), { text: '-'.repeat(width) });
  lines.push({ text: leftRight('Subtotal', money(order.subtotal), width) });
  if (order.packagingCharge > 0) lines.push({ text: leftRight('Packaging', money(order.packagingCharge), width) });
  lines.push({ text: leftRight('TOTAL', `Rs.${money(order.total)}`, width), style: 'large' });
  if (isPaid) {
    lines.push({ text: leftRight(`Paid (${paymentMethodLabel(order.paymentMethod)})`, money(order.total), width) });
    if (order.upiReference) lines.push({ text: `UPI ref : ${clean(order.upiReference)}` });
  } else {
    lines.push({ text: leftRight('AMOUNT DUE', money(order.total), width), style: 'bold' });
  }
  return [...lines, ...footer(width)];
}
