import { Sales } from '../../services/sales.service';
import { Expense } from '../../services/expense.service';
import { calculateDashboardInsights, calculateDashboardOverview, DashboardOnlineSale } from './dashboard-overview';

describe('admin dashboard overview', () => {
  const online = (orderAt: string, payout: number, platform: string, customerName: string, itemName = 'Mojito'): DashboardOnlineSale => ({
    orderAt, payout, platform, customerName, orderedItems: [{ itemName, quantity: 2 }]
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
