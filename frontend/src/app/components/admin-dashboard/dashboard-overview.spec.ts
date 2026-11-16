import { Sales } from '../../services/sales.service';
import { Expense } from '../../services/expense.service';
import { calculateDashboardInsights, calculateDashboardItemSales, calculateDashboardOverview, calculateRevenueBreakdown, DashboardOnlineSale } from './dashboard-overview';
import { Order } from '../../services/order.service';
import { MenuItem } from '../../services/menu.service';

describe('admin dashboard overview', () => {
  const online = (orderAt: string, payout: number, platform: string, customerName: string, itemName = 'Mojito'): DashboardOnlineSale => ({
    orderAt, payout, platform, customerName, orderedItems: [{ itemName, quantity: 2 }]
  });

  it('builds total revenue from web sales and Zomato net payout, applying only fully covered report adjustments', () => {
    const zomato = { ...online('2025-10-27T12:00:00+05:30', 300, 'Zomato', 'Z'), platformDeduction: 80 };
    const sales = [online('2025-10-26T10:00:00+05:30', 200, 'Web Sales', 'W'), zomato,
      online('2025-11-05T10:00:00+05:30', 999, 'Zomato', 'Out of range')];
    const adjustments = [
      { periodStart: '2025-10-26', periodEnd: '2025-11-01', additionalDeductions: 50, additionalAdditions: 10 },
      { periodStart: '2025-11-02', periodEnd: '2025-11-08', additionalDeductions: 70, additionalAdditions: 0 }
    ];

    const week = calculateRevenueBreakdown(sales, [], adjustments, '2025-10-26', '2025-11-01');
    expect(week.webSales).toBe(200);
    expect(week.zomato).toEqual({ orders: 1, orderPayout: 300, platformDeductions: 80, additionalDeductions: 50,
      additionalAdditions: 10, netPayout: 260, reportsApplied: 1, partialReports: 0 });
    expect(week.total).toBe(460);

    const partial = calculateRevenueBreakdown(sales, [], adjustments, '2025-10-27', '2025-11-03');
    expect(partial.zomato.additionalDeductions).toBe(0);
    expect(partial.zomato.partialReports).toBe(2);
    expect(partial.total).toBe(300);
  });

  describe('admin dashboard item sales', () => {
    const order = (overrides: Partial<Order> = {}): Order => ({
      id: 'one', userId: 'user', username: 'Customer', subtotal: 100, tax: 0, total: 100,
      status: 'delivered', paymentStatus: 'paid', paymentMethod: 'cash_at_counter',
      orderType: 'dine-in', createdAt: '2026-03-01T10:00:00+05:30', updatedAt: '',
      items: [{ menuItemId: 'tea', name: 'Milk Tea', quantity: 2, price: 50, total: 100 }],
      ...overrides
    });
    const menu: MenuItem[] = [{ id: 'tea', name: 'Milk Tea', categoryId: 'beverage', categoryName: ' Beverage ', onlinePrice: 50 }];

    it('applies item search, catalog category and source filters without including other items in totals', () => {
      const orders = [
        order({ items: [...order().items, { menuItemId: 'roll', name: 'Roll', categoryName: 'Snacks', quantity: 1, price: 80, total: 80 }] }),
        order({ id: 'pickup', orderType: 'pickup' }),
        order({ id: 'cancelled', status: 'cancelled' }),
        order({ id: 'old', createdAt: '2026-02-28T10:00:00+05:30' })
      ];
      const result = calculateDashboardItemSales(orders, menu, '2026-03-01', '2026-03-01', ' milk ', 'Beverage', 'Dine-In');
      expect(result.quantity).toBe(2);
      expect(result.revenue).toBe(100);
      expect(result.orders).toBe(1);
      expect(result.rows).toEqual([{ name: 'Milk Tea', category: 'Beverage', quantity: 2, orders: 1,
        value: 100, dineIn: 2, takeaway: 0, delivery: 0 }]);
      expect(result.categories).toEqual([{ name: 'Beverage', quantity: 2 }]);
    });

    it('counts unique orders and quantities by service mode without mutating the source orders', () => {
      const orders = [order(), order({ id: 'two', orderType: 'pickup' }),
        order({ id: 'three', orderType: 'delivery', orderSource: 'phone' })];
      const result = calculateDashboardItemSales(orders, menu, '2026-03-01', '2026-03-01', '', '', '');
      expect(result.rows[0]).toEqual(jasmine.objectContaining({ quantity: 6, orders: 3, dineIn: 2, takeaway: 2, delivery: 2 }));
      expect(result.days).toEqual([{ date: '2026-03-01', quantity: 6 }]);
      expect(orders[0].items[0].categoryName).toBeUndefined();
    });

    it('includes both days in a multi-day chart bucket and respects the IST midnight boundary', () => {
      const result = calculateDashboardItemSales([
        order({ id: 'start', createdAt: '2025-12-01T18:30:00Z', createdAtIsUtc: true }),
        order({ id: 'next', createdAt: '2025-12-02T18:30:00Z', createdAtIsUtc: true }),
        order({ id: 'before', createdAt: '2025-12-01T18:29:59Z', createdAtIsUtc: true })
      ], menu, '2025-12-02', '2026-03-01', '', '', '');
      expect(result.days[0]).toEqual({ date: '2025-12-02', quantity: 4 });
      expect(result.days.reduce((sum, day) => sum + day.quantity, 0)).toBe(result.quantity);
      expect(result.quantity).toBe(4);
    });

    it('adds parsed Zomato items as delivery rows with menu categories and price-weighted values', () => {
      const zomato: DashboardOnlineSale[] = [
        { platform: 'Zomato', orderId: 'Z1', orderAt: '2026-03-01T13:00:00+05:30', payout: 120, billSubTotal: 200,
          orderedItems: [{ itemName: 'milk-tea', quantity: 2 }, { itemName: 'Mystery Wrap', quantity: 1 }] },
        { platform: 'Web Sales', orderId: 'W1', orderAt: '2026-03-01T13:00:00+05:30', payout: 90, billSubTotal: 100,
          orderedItems: [{ itemName: 'Milk Tea', quantity: 5 }] },
        { platform: 'Zomato', orderId: 'OLD', orderAt: '2026-02-28T13:00:00+05:30', payout: 50, billSubTotal: 50,
          orderedItems: [{ itemName: 'Milk Tea', quantity: 1 }] }
      ];
      const result = calculateDashboardItemSales([order()], menu, '2026-03-01', '2026-03-01', '', '', '', zomato);
      expect(result.orders).toBe(2);
      expect(result.rows.find(row => row.name === 'Milk Tea')).toEqual(jasmine.objectContaining({
        quantity: 4, orders: 2, dineIn: 2, delivery: 2 }));
      expect(result.rows.find(row => row.name === 'Milk Tea')!.value).toBeCloseTo(233.33, 2);
      expect(result.rows.find(row => row.name === 'Mystery Wrap')).toEqual(jasmine.objectContaining({
        category: 'Uncategorized', quantity: 1, delivery: 1 }));
      const onlyZomato = calculateDashboardItemSales([order()], menu, '2026-03-01', '2026-03-01', '', '', 'Zomato', zomato);
      expect(onlyZomato.quantity).toBe(3);
      expect(onlyZomato.orders).toBe(1);
    });

    it('counts platform orders in hourly demand without double counting web sales', () => {
      const insights = calculateDashboardInsights([], [], 1, '2026-03-01', [
        { platform: 'Zomato', orderAt: '2026-03-01T13:10:00+05:30', payout: 1, orderedItems: [] },
        { platform: 'Web Sales', orderAt: '2026-03-01T13:10:00+05:30', payout: 1, orderedItems: [] }
      ]);
      expect(insights.hourlyOrders[13].count).toBe(1);
      expect(insights.peakHours).toEqual([13]);
    });
  });
  const offline = (date: string, totalAmount: number, itemName = 'Mojito'): Sales => ({
    id: date, date, totalAmount, paymentMethod: 'cash', recordedBy: '', createdAt: '', updatedAt: '',
    items: [{ itemName, quantity: 1, unitPrice: totalAmount, totalPrice: totalAmount }]
  });

  it('uses inclusive IST dates, compares the prior period, and combines each sales record once', () => {
    const result = calculateDashboardOverview([
      online('2026-02-22T18:30:00Z', 80, 'Zomato', 'Alex'),
      online('2026-03-01T10:00:00+05:30', 120, 'Web Sales', 'alex'),
      online('2026-02-22T18:29:59Z', 40, 'Swiggy', 'Alex'),
      online('2026-03-02T00:00:00+05:30', 999, 'Zomato', 'Future')
    ], [offline('2026-03-01', 100), offline('2026-02-16', 60)], 7, '2026-03-01');

    expect(result.current).toEqual({ revenue: 300, orders: 3, average: 100, customers: 1 });
    expect(result.previous).toEqual({ revenue: 100, orders: 2, average: 50, customers: 1 });
    expect(result.days[0]).toEqual({ date: '2026-02-23', revenue: 80, orders: 1 });
    expect(result.days[6]).toEqual({ date: '2026-03-01', revenue: 220, orders: 2 });
    expect(result.sources.map(source => source.count)).toEqual([1, 0, 1, 0, 1]);
    expect(result.items).toEqual([{ name: 'Mojito', quantity: 5 }]);
    expect(result.customers).toEqual([{ name: 'Alex', orders: 2, payout: 200 }]);
  });

  it('handles empty periods and excludes unnamed customers without hiding sales', () => {
    const result = calculateDashboardOverview([
      online('2024-02-29', 50, 'Other', 'Unknown')
    ], [], 7, '2024-03-01');
    expect(result.days[0].date).toBe('2024-02-24');
    expect(result.current.orders).toBe(1);
    expect(result.current.customers).toBe(0);
    expect(result.customers).toEqual([]);
    expect(result.sources[3].count).toBe(1);
    expect(result.previous).toEqual({ revenue: 0, orders: 0, average: 0, customers: 0 });
  });

  it('groups timestamped non-cancelled app orders by IST hour and filters the selected period', () => {
    const result = calculateDashboardInsights([
      { createdAt: '2026-02-22T18:30:00Z', createdAtIsUtc: true, status: 'delivered' },
      { createdAt: '2026-03-01T10:00:00Z', createdAtIsUtc: false, status: 'served' },
      { createdAt: '2026-03-01T10:20:00+05:30', createdAtIsUtc: true, status: 'confirmed' },
      { createdAt: '2026-03-01T10:00:00+05:30', status: 'cancelled' },
      { createdAt: '2026-03-01', status: 'delivered' },
      { createdAt: 'not a date', status: 'delivered' },
      { createdAt: '2026-03-01T18:30:00Z', createdAtIsUtc: true, status: 'delivered' }
    ], [], 7, '2026-03-01');

    expect(result.timedOrderCount).toBe(3);
    expect(result.hourlyOrders[0].count).toBe(1);
    expect(result.hourlyOrders[10].count).toBe(2);
    expect(result.peakHours).toEqual([10]);
    expect(calculateDashboardInsights([], [], 7, '2026-03-01').peakHours).toEqual([]);
  });

  it('summarizes recorded expenses and compares the immediately preceding period', () => {
    const expense = (date: string, expenseType: string, amount: number): Expense => ({
      id: date, date, expenseType, amount, expenseSource: 'Offline', paymentMethod: 'Cash',
      recordedBy: '', createdAt: '', updatedAt: ''
    });
    const result = calculateDashboardInsights([], [
      expense('2026-02-23', 'Inventory', 100),
      expense('2026-03-01', 'inventory', 50),
      expense('2026-03-01', 'Rent', 200),
      expense('2026-02-22', 'Rent', 140),
      expense('2026-03-02', 'Rent', 900)
    ], 7, '2026-03-01').expenses;
    expect(result.total).toBe(350);
    expect(result.previousTotal).toBe(140);
    expect(result.dailyAverage).toBe(50);
    expect(result.count).toBe(3);
    expect(result.categories).toEqual([
      { name: 'Rent', amount: 200, count: 1 },
      { name: 'Inventory', amount: 150, count: 2 }
    ]);
    expect(result.largest).toEqual({ name: 'Rent', amount: 200, date: '2026-03-01' });
  });
});
