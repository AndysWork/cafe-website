import { Sales } from '../../services/sales.service';
import { Expense } from '../../services/expense.service';
import { Order } from '../../services/order.service';
import { MenuItem } from '../../services/menu.service';
import { calculateWebSalesOverview, orderSource, SalesItem } from '../admin-web-sales/web-sales-overview';
import { getIstHour, getIstInputDate, getStoredTimestampEpoch } from '../../utils/date-utils';

export interface DashboardOnlineSale {
  platform: string;
  orderId?: string;
  customerName?: string;
  orderAt: Date | string;
  orderedItems: Array<{ quantity: number; itemName: string }>;
  billSubTotal?: number;
  discountAmount?: number;
  platformDeduction?: number;
  payout: number;
}

export interface DashboardZomatoAdjustment {
  periodStart: string;
  periodEnd: string;
  additionalDeductions: number;
  additionalAdditions: number;
}

export interface DashboardRevenueBreakdown {
  webSales: number;
  webOrders: number;
  zomato: {
    orders: number;
    orderPayout: number;
    platformDeductions: number;
    additionalDeductions: number;
    additionalAdditions: number;
    netPayout: number;
    reportsApplied: number;
    partialReports: number;
  };
  swiggyPayout: number;
  otherOnline: number;
  offlineSales: number;
  total: number;
}

/**
 * Total Revenue = Web Sales + Zomato net payout (+ Swiggy, other online and recorded offline sales when present).
 * Zomato report-level adjustments (ads etc.) apply only when the report period lies fully inside the range.
 */
export function calculateRevenueBreakdown(
  onlineSales: DashboardOnlineSale[],
  offlineSales: Sales[],
  adjustments: DashboardZomatoAdjustment[],
  start: string,
  end: string
): DashboardRevenueBreakdown {
  const inRange = (date: string) => date >= start && date <= end;
  const result: DashboardRevenueBreakdown = {
    webSales: 0, webOrders: 0, swiggyPayout: 0, otherOnline: 0, offlineSales: 0, total: 0,
    zomato: { orders: 0, orderPayout: 0, platformDeductions: 0, additionalDeductions: 0, additionalAdditions: 0, netPayout: 0, reportsApplied: 0, partialReports: 0 }
  };
  for (const sale of onlineSales) {
    if (!inRange(dateKey(sale.orderAt))) continue;
    const payout = Number(sale.payout) || 0;
    const platform = (sale.platform || '').trim().toLowerCase();
    if (platform === 'web sales') { result.webSales += payout; result.webOrders++; }
    else if (platform === 'zomato') {
      result.zomato.orders++;
      result.zomato.orderPayout += payout;
      result.zomato.platformDeductions += Number(sale.platformDeduction) || 0;
    } else if (platform === 'swiggy') result.swiggyPayout += payout;
    else result.otherOnline += payout;
  }
  for (const sale of offlineSales) {
    if (inRange(dateKey(sale.date))) result.offlineSales += Number(sale.totalAmount) || 0;
  }
  for (const report of adjustments) {
    if (report.periodStart >= start && report.periodEnd <= end) {
      result.zomato.additionalDeductions += Number(report.additionalDeductions) || 0;
      result.zomato.additionalAdditions += Number(report.additionalAdditions) || 0;
      result.zomato.reportsApplied++;
    } else if (report.periodStart <= end && report.periodEnd >= start) result.zomato.partialReports++;
  }
  result.zomato.netPayout = result.zomato.orderPayout - result.zomato.additionalDeductions + result.zomato.additionalAdditions;
  result.total = result.webSales + result.zomato.netPayout + result.swiggyPayout + result.otherOnline + result.offlineSales;
  return result;
}

export interface DashboardDay {
  date: string;
  revenue: number;
  orders: number;
}

export interface DashboardSummary {
  revenue: number;
  orders: number;
  average: number;
  customers: number;
}

export interface DashboardOverview {
  current: DashboardSummary;
  previous: DashboardSummary;
  days: DashboardDay[];
  sources: { label: string; count: number; color: string }[];
  items: { name: string; quantity: number }[];
  customers: { name: string; orders: number; payout: number }[];
}

export interface DashboardInsights {
  hourlyOrders: { hour: number; count: number }[];
  peakHours: number[];
  timedOrderCount: number;
  expenses: {
    total: number;
    previousTotal: number;
    count: number;
    dailyAverage: number;
    categories: { name: string; amount: number; count: number }[];
    largest: { name: string; amount: number; date: string } | null;
  };
}

function shiftDate(date: string, days: number): string {
  const epoch = Date.parse(`${date}T00:00:00+05:30`);
  return getIstInputDate(new Date(epoch + days * 86400000));
}

export interface DashboardItemRow extends SalesItem {
  dineIn: number;
  takeaway: number;
  delivery: number;
}

export interface DashboardItemSales {
  rows: DashboardItemRow[];
  days: { date: string; quantity: number }[];
  categories: { name: string; quantity: number }[];
  quantity: number;
  revenue: number;
  orders: number;
}

const normalizeItemName = (name: string): string => (name || '').toLowerCase().replace(/[^a-z0-9]/g, '');

function platformLabel(platform: string): string {
  const value = (platform || '').trim().toLowerCase();
  return value === 'zomato' ? 'Zomato' : value === 'swiggy' ? 'Swiggy' : '';
}

/** Converts parsed/recorded platform sales into order-shaped rows so item and hourly analytics include them. */
export function platformSalesAsOrders(onlineSales: DashboardOnlineSale[], menu: MenuItem[]): Map<Order, string> {
  const byName = new Map<string, MenuItem>();
  for (const item of menu) {
    const key = normalizeItemName(item.name);
    if (key && !byName.has(key)) byName.set(key, item);
  }
  const result = new Map<Order, string>();
  onlineSales.forEach((sale, index) => {
    const label = platformLabel(sale.platform);
    const epoch = new Date(sale.orderAt).getTime();
    if (!label || !Number.isFinite(epoch)) return;
    const lines = (sale.orderedItems || [])
      .filter(item => item.itemName?.trim() && item.quantity > 0)
      .map(item => ({ item, product: byName.get(normalizeItemName(item.itemName)) }));
    const weights = lines.map(line => line.item.quantity * (Number(line.product?.onlinePrice) || 0));
    const priced = weights.every(weight => weight > 0);
    const totalWeight = priced
      ? weights.reduce((sum, weight) => sum + weight, 0)
      : lines.reduce((sum, line) => sum + line.item.quantity, 0);
    const subtotal = Number(sale.billSubTotal) || 0;
    const order = {
      id: `${label}-${sale.orderId || index}`,
      status: 'delivered',
      orderType: 'delivery',
      createdAt: new Date(epoch).toISOString(),
      createdAtIsUtc: true,
      subtotal,
      total: subtotal - (Number(sale.discountAmount) || 0),
      items: lines.map((line, lineIndex) => {
        const share = totalWeight ? (priced ? weights[lineIndex] : line.item.quantity) / totalWeight : 0;
        const total = Math.round(subtotal * share * 100) / 100;
        return {
          menuItemId: line.product?.id || '',
          name: line.product?.name?.trim() || line.item.itemName.trim(),
          categoryName: line.product?.categoryName?.trim() || line.product?.category?.trim() || 'Uncategorized',
          quantity: line.item.quantity,
          price: line.item.quantity ? total / line.item.quantity : 0,
          total
        };
      })
    } as unknown as Order;
    result.set(order, label);
  });
  return result;
}

export function calculateDashboardItemSales(
  orders: Order[], menu: MenuItem[], start: string, end: string,
  search: string, category: string, source: string, onlineSales: DashboardOnlineSale[] = []
): DashboardItemSales {
  const catalog = new Map(menu.map(item => [item.id, item]));
  const query = search.trim().toLowerCase();
  const platformOrders = platformSalesAsOrders(onlineSales, menu);
  const sourceOf = (order: Order): string => platformOrders.get(order) || orderSource(order);
  const filtered = [...orders, ...platformOrders.keys()].filter(order => !source || sourceOf(order) === source).map(order => ({
    ...order,
    dashboardSource: sourceOf(order),
    items: (order.items || []).map(item => {
      const product = catalog.get(item.menuItemId);
      return { ...item, categoryName: item.categoryName?.trim() || product?.categoryName?.trim() || product?.category?.trim() || 'Uncategorized' };
    }).filter(item => (!category || item.categoryName === category)
      && (!query || item.name.toLowerCase().includes(query)))
  })).filter(order => order.items.length > 0);
  const overview = calculateWebSalesOverview(filtered, start, end);
  const days = new Map(overview.trend.map(bucket => [bucket.date, { date: bucket.date, quantity: 0 }]));
  const modes = new Map<string, { dineIn: number; takeaway: number; delivery: number }>();
  for (const order of filtered) {
    if (['cancelled', 'rejected'].includes(order.status)) continue;
    const epoch = getStoredTimestampEpoch(order.createdAt, order.createdAtIsUtc);
    if (!epoch) continue;
    const date = getIstInputDate(new Date(epoch));
    if (date < start || date > end) continue;
    const bucket = overview.trend.find(bucket => date >= bucket.date && date <= bucket.endDate);
    const day = bucket ? days.get(bucket.date) : undefined;
    for (const item of order.items) {
      if (item.quantity <= 0) continue;
      if (day) day.quantity += item.quantity;
      const key = `${item.name.trim().toLowerCase()}\u0000${item.categoryName.toLowerCase()}`;
      const mode = modes.get(key) || { dineIn: 0, takeaway: 0, delivery: 0 };
      const orderMode = order.dashboardSource;
      if (orderMode === 'Dine-In') mode.dineIn += item.quantity;
      else if (orderMode === 'Takeaway') mode.takeaway += item.quantity;
      else mode.delivery += item.quantity;
      modes.set(key, mode);
    }
  }
  const rows = overview.items.map(item => ({
    ...item, ...modes.get(`${item.name.toLowerCase()}\u0000${item.category.toLowerCase()}`)!
  }));
  const categories = new Map<string, number>();
  for (const row of rows) categories.set(row.category, (categories.get(row.category) || 0) + row.quantity);
  return {
    rows,
    days: Array.from(days.values()),
    categories: Array.from(categories, ([name, quantity]) => ({ name, quantity })).sort((a, b) => b.quantity - a.quantity),
    quantity: rows.reduce((sum, row) => sum + row.quantity, 0),
    revenue: rows.reduce((sum, row) => sum + row.value, 0),
    orders: overview.orders
  };
}

function dateKey(value: Date | string): string {
  return getIstInputDate(value);
}

export function calculateDashboardInsights(
  orders: Pick<Order, 'createdAt' | 'createdAtIsUtc' | 'status'>[],
  expenses: Expense[],
  periodDays: number,
  today: string,
  onlineSales: DashboardOnlineSale[] = []
): DashboardInsights {
  const start = shiftDate(today, 1 - periodDays);
  const previousStart = shiftDate(start, -periodDays);
  const hourlyOrders = Array.from({ length: 24 }, (_, hour) => ({ hour, count: 0 }));
  const platformOrders = onlineSales
    .filter(sale => platformLabel(sale.platform))
    .map(sale => new Date(sale.orderAt).getTime())
    .filter(epoch => Number.isFinite(epoch))
    .map(epoch => ({ createdAt: new Date(epoch).toISOString(), createdAtIsUtc: true, status: 'delivered' as const }));
  for (const order of [...orders, ...platformOrders]) {
    if (['cancelled', 'rejected'].includes((order.status || '').toLowerCase())) continue;
    if (!order.createdAt || /^\d{4}-\d{2}-\d{2}$/.test(order.createdAt.trim())) continue;
    const epoch = getStoredTimestampEpoch(order.createdAt, order.createdAtIsUtc);
    if (!epoch) continue;
    const date = getIstInputDate(new Date(epoch));
    if (date >= start && date <= today) hourlyOrders[getIstHour(epoch)].count++;
  }
  const peakCount = Math.max(...hourlyOrders.map(bucket => bucket.count));
  const categories = new Map<string, { name: string; amount: number; count: number }>();
  const result: DashboardInsights['expenses'] = {
    total: 0, previousTotal: 0, count: 0, dailyAverage: 0, categories: [], largest: null
  };
  for (const expense of expenses) {
    const date = dateKey(expense.date);
    if (date >= previousStart && date < start) {
      result.previousTotal += expense.amount;
    } else if (date >= start && date <= today) {
      const name = expense.expenseType?.trim() || 'Uncategorized';
      const key = name.toLowerCase();
      const category = categories.get(key) || { name, amount: 0, count: 0 };
      category.amount += expense.amount;
      category.count++;
      categories.set(key, category);
      result.total += expense.amount;
      result.count++;
      if (!result.largest || expense.amount > result.largest.amount) {
        result.largest = { name, amount: expense.amount, date };
      }
    }
  }
  result.dailyAverage = result.total / periodDays;
  result.categories = Array.from(categories.values()).sort((a, b) => b.amount - a.amount || a.name.localeCompare(b.name));
  return {
    hourlyOrders,
    peakHours: peakCount ? hourlyOrders.filter(bucket => bucket.count === peakCount).map(bucket => bucket.hour) : [],
    timedOrderCount: hourlyOrders.reduce((sum, bucket) => sum + bucket.count, 0),
    expenses: result
  };
}

export function calculateDashboardOverview(
  onlineSales: DashboardOnlineSale[],
  offlineSales: Sales[],
  periodDays: number,
  today: string
): DashboardOverview {
  const start = shiftDate(today, 1 - periodDays);
  const previousStart = shiftDate(start, -periodDays);
  const days = Array.from({ length: periodDays }, (_, index) => ({
    date: shiftDate(start, index), revenue: 0, orders: 0
  }));
  const daysByDate = new Map(days.map(day => [day.date, day]));
  const current: DashboardSummary = { revenue: 0, orders: 0, average: 0, customers: 0 };
  const previous: DashboardSummary = { revenue: 0, orders: 0, average: 0, customers: 0 };
  const currentNames = new Set<string>();
  const previousNames = new Set<string>();
  const itemCounts = new Map<string, { name: string; quantity: number }>();
  const customerTotals = new Map<string, { name: string; orders: number; payout: number }>();
  const sources = [
    { label: 'Zomato', count: 0, color: '#1db845' },
    { label: 'Swiggy', count: 0, color: '#ff9c00' },
    { label: 'Web sales', count: 0, color: '#258dff' },
    { label: 'Other online', count: 0, color: '#8b3cf4' },
    { label: 'Recorded offline', count: 0, color: '#00aebf' }
  ];

  const addItem = (name: string, quantity: number): void => {
    const trimmed = name?.trim();
    if (!trimmed || !Number.isFinite(quantity) || quantity <= 0) return;
    const key = trimmed.toLowerCase();
    const item = itemCounts.get(key) || { name: trimmed, quantity: 0 };
    item.quantity += quantity;
    itemCounts.set(key, item);
  };
  const recordSale = (date: string, revenue: number): 'current' | 'previous' | null => {
    if (date >= start && date <= today) {
      current.revenue += revenue;
      current.orders++;
      const day = daysByDate.get(date);
      if (day) { day.revenue += revenue; day.orders++; }
      return 'current';
    }
    if (date >= previousStart && date < start) {
      previous.revenue += revenue;
      previous.orders++;
      return 'previous';
    }
    return null;
  };

  for (const sale of onlineSales) {
    const period = recordSale(dateKey(sale.orderAt), Number(sale.payout) || 0);
    if (!period) continue;
    const name = sale.customerName?.trim();
    if (name && name.toLowerCase() !== 'unknown') {
      const key = name.toLowerCase();
      (period === 'current' ? currentNames : previousNames).add(key);
      if (period === 'current') {
        const customer = customerTotals.get(key) || { name, orders: 0, payout: 0 };
        customer.orders++;
        customer.payout += Number(sale.payout) || 0;
        customerTotals.set(key, customer);
      }
    }
    if (period === 'current') {
      const platform = (sale.platform || '').trim().toLowerCase();
      const index = platform === 'zomato' ? 0 : platform === 'swiggy' ? 1 : platform === 'web sales' ? 2 : 3;
      sources[index].count++;
      for (const item of sale.orderedItems || []) addItem(item.itemName, item.quantity);
    }
  }
  for (const sale of offlineSales) {
    if (recordSale(dateKey(sale.date), Number(sale.totalAmount) || 0) === 'current') {
      sources[4].count++;
      for (const item of sale.items || []) addItem(item.itemName, item.quantity);
    }
  }
  current.customers = currentNames.size;
  previous.customers = previousNames.size;
  current.average = current.orders ? current.revenue / current.orders : 0;
  previous.average = previous.orders ? previous.revenue / previous.orders : 0;
  return {
    current, previous, days, sources,
    items: Array.from(itemCounts.values()).sort((a, b) => b.quantity - a.quantity).slice(0, 3),
    customers: Array.from(customerTotals.values()).sort((a, b) => b.payout - a.payout).slice(0, 3)
  };
}
