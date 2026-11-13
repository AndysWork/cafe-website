import { Order } from '../../services/order.service';
import { getIstInputDate, getStoredTimestampEpoch } from '../../utils/date-utils';

export interface SalesTrendBucket {
  date: string;
  endDate: string;
  value: number;
  orders: number;
}

export interface SalesSource {
  label: string;
  color: string;
  count: number;
}

export interface SalesItem {
  name: string;
  category: string;
  quantity: number;
  orders: number;
  value: number;
}

export interface WebSalesOverview {
  orders: number;
  orderValue: number;
  grossValue: number;
  average: number;
  discounts: number;
  paidOrderValue: number;
  awaitingPaymentValue: number;
  cancelledOrders: number;
  trend: SalesTrendBucket[];
  sources: SalesSource[];
  items: SalesItem[];
}

export function orderSource(order: Order): string {
  const type = (order.orderType || '').toLowerCase();
  const source = (order.orderSource || '').toLowerCase();
  if (source === 'phone') return 'Phone';
  if (order.channel === 'partner' || source === 'partner') return 'Partner';
  if (['dine-in', 'dinein', 'dine in'].includes(type)) return 'Dine-In';
  if (['pickup', 'takeaway', 'take-away'].includes(type)) return 'Takeaway';
  if (type === 'delivery' && (!order.channel || order.channel === 'web')) return 'Website';
  return 'Other';
}

export function calculateWebSalesOverview(orders: Order[], start: string, end: string): WebSalesOverview {
  const startTime = Date.parse(`${start}T00:00:00Z`);
  const endTime = Date.parse(`${end}T00:00:00Z`);
  const days = Number.isFinite(startTime) && Number.isFinite(endTime) && endTime >= startTime
    ? Math.floor((endTime - startTime) / 86400000) + 1 : 0;
  const bucketDays = Math.max(1, Math.ceil(days / 60));
  const trend: SalesTrendBucket[] = [];
  for (let offset = 0; offset < days; offset += bucketDays) {
    trend.push({
      date: new Date(startTime + offset * 86400000).toISOString().slice(0, 10),
      endDate: new Date(startTime + Math.min(days - 1, offset + bucketDays - 1) * 86400000).toISOString().slice(0, 10),
      value: 0,
      orders: 0
    });
  }
  const sources: SalesSource[] = [
    { label: 'Dine-In', color: '#61d629', count: 0 },
    { label: 'Takeaway', color: '#35a3ed', count: 0 },
    { label: 'Website', color: '#9939ef', count: 0 },
    { label: 'Phone', color: '#ff9800', count: 0 },
    { label: 'Partner', color: '#ff3d42', count: 0 },
    { label: 'Other', color: '#8695ac', count: 0 }
  ];
  const items = new Map<string, SalesItem & { orderIds: Set<string> }>();
  let orderValue = 0;
  let grossValue = 0;
  let discounts = 0;
  let paidOrderValue = 0;
  let awaitingPaymentValue = 0;
  let cancelledOrders = 0;
  let count = 0;
  for (const order of orders) {
    const epoch = getStoredTimestampEpoch(order.createdAt, order.createdAtIsUtc);
    if (!epoch) continue;
    const date = getIstInputDate(new Date(epoch));
    if (date < start || date > end) continue;
    const index = Math.floor((Date.parse(`${date}T00:00:00Z`) - startTime) / 86400000 / bucketDays);
    const bucket = trend[index];
    if (!bucket) continue;
    if (['cancelled', 'rejected'].includes((order.status || '').toLowerCase())) {
      cancelledOrders++;
      continue;
    }
    const value = Number(order.total) || 0;
    count++;
    orderValue += value;
    grossValue += (Number(order.subtotal) || 0) + (Number(order.tax) || 0)
      + (Number(order.platformCharge) || 0) + (Number(order.deliveryFee) || 0)
      + (Number(order.packagingCharge) || 0);
    discounts += (Number(order.discountAmount) || 0) + (Number(order.loyaltyDiscountAmount) || 0);
    if (!order.dineInSessionId && order.paymentMethod !== 'dine_in_tab') {
      const paymentStatus = (order.paymentStatus || '').toLowerCase();
      if (paymentStatus === 'paid') paidOrderValue += value;
      else if (['pending', 'unpaid', 'pending_cash', 'pending_verification', 'processing'].includes(paymentStatus)) {
        awaitingPaymentValue += value;
      }
    }
    bucket.value += value;
    bucket.orders++;
    sources.find(source => source.label === orderSource(order))!.count++;
    for (const item of order.items || []) {
      const name = (item.name || '').trim();
      if (!name || item.quantity <= 0) continue;
      const category = item.categoryName?.trim() || 'Uncategorized';
      const key = `${name.toLowerCase()}\u0000${category.toLowerCase()}`;
      const row = items.get(key) || { name, category, quantity: 0, orders: 0, value: 0, orderIds: new Set<string>() };
      row.quantity += item.quantity;
      row.value += Number(item.total) || 0;
      row.orderIds.add(order.id);
      items.set(key, row);
    }
  }
  return {
    orders: count, orderValue, grossValue, average: count ? orderValue / count : 0,
    discounts, paidOrderValue, awaitingPaymentValue, cancelledOrders,
    trend, sources,
    items: Array.from(items.values())
      .map(({ orderIds, ...item }) => ({ ...item, orders: orderIds.size }))
      .sort((a, b) => b.quantity - a.quantity || a.name.localeCompare(b.name))
  };
}
