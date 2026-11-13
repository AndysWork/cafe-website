import { Order } from '../../services/order.service';
import { calculateWebSalesOverview } from './web-sales-overview';

describe('web sales overview', () => {
  const order = (id: string, createdAt: string, overrides: Partial<Order> = {}): Order => ({
    id, userId: 'u', username: 'Guest', createdAt, createdAtIsUtc: true, updatedAt: createdAt,
    status: 'delivered', paymentStatus: 'paid', paymentMethod: 'cash_at_counter',
    orderType: 'dine-in', subtotal: 100, tax: 0, total: 90,
    items: [{ menuItemId: 'tea', name: 'Tea', categoryName: 'Drinks', quantity: 2, price: 50, total: 100 }],
    ...overrides
  });

  it('uses the selected IST period and excludes cancelled orders from values, sources and items', () => {
    const result = calculateWebSalesOverview([
      order('one', '2026-03-01T18:29:00Z'),
      order('two', '2026-03-01T18:30:00Z', { orderType: 'pickup', total: 50 }),
      order('cancelled', '2026-03-01T10:00:00Z', { status: 'cancelled', total: 999 }),
      order('before', '2026-02-26T18:29:00Z')
    ], '2026-02-27', '2026-03-02');

    expect(result.orders).toBe(2);
    expect(result.orderValue).toBe(140);
    expect(result.grossValue).toBe(200);
    expect(result.average).toBe(70);
    expect(result.trend.map(day => day.orders)).toEqual([0, 0, 1, 1]);
    expect(result.sources.find(source => source.label === 'Dine-In')?.count).toBe(1);
    expect(result.sources.find(source => source.label === 'Takeaway')?.count).toBe(1);
    expect(result.items[0]).toEqual({ name: 'Tea', category: 'Drinks', quantity: 4, orders: 2, value: 200 });
  });

  it('supports empty and long date ranges without unbounded chart points', () => {
    const result = calculateWebSalesOverview([], '2025-01-01', '2026-12-31');
    expect(result.orders).toBe(0);
    expect(result.average).toBe(0);
    expect(result.trend.length).toBeLessThanOrEqual(60);
    expect(result.trend[0].date).toBe('2025-01-01');
    expect(result.trend[result.trend.length - 1].endDate).toBe('2026-12-31');
    expect(calculateWebSalesOverview([], '2026-03-02', '2026-03-01').trend).toEqual([]);
  });

  it('handles legacy local timestamps and groups phone and partner sources', () => {
    const result = calculateWebSalesOverview([
      order('phone', '2026-03-02T00:15:00Z', { createdAtIsUtc: false, orderSource: 'phone' }),
      order('partner', '2026-03-02T10:00:00Z', { orderType: 'delivery', channel: 'partner' })
    ], '2026-03-02', '2026-03-02');

    expect(result.orders).toBe(2);
    expect(result.sources.find(source => source.label === 'Phone')?.count).toBe(1);
    expect(result.sources.find(source => source.label === 'Partner')?.count).toBe(1);
  });

  it('calculates gross, discounts and payment-status values without treating table or partial payments as collected', () => {
    const date = '2026-03-02T10:00:00Z';
    const result = calculateWebSalesOverview([
      order('paid', date, { total: 115, packagingCharge: 20, discountAmount: 5, loyaltyDiscountAmount: 0 }),
      order('pending', date, { total: 90, paymentStatus: 'pending' }),
      order('table', date, { total: 90, dineInSessionId: 'session' }),
      order('partial', date, { total: 90, paymentStatus: 'partially_paid' }),
      order('refunded', date, { total: 90, paymentStatus: 'refunded' }),
      order('cancelled', date, { status: 'cancelled', total: 999 })
    ], '2026-03-02', '2026-03-02');

    expect(result.grossValue).toBe(520);
    expect(result.discounts).toBe(5);
    expect(result.paidOrderValue).toBe(115);
    expect(result.awaitingPaymentValue).toBe(90);
    expect(result.cancelledOrders).toBe(1);
  });

  it('classifies partner pickup orders as partner orders', () => {
    const result = calculateWebSalesOverview([
      order('partner', '2026-03-02T10:00:00Z', { channel: 'partner', orderType: 'pickup' })
    ], '2026-03-02', '2026-03-02');
    expect(result.sources.find(source => source.label === 'Partner')?.count).toBe(1);
    expect(result.sources.find(source => source.label === 'Takeaway')?.count).toBe(0);
  });

  it('counts dine-in, takeaway and phone orders together', () => {
    const date = '2026-03-02T10:00:00Z';
    const result = calculateWebSalesOverview([
      order('dine-in', date, { orderType: 'dine-in' }),
      order('takeaway', date, { orderType: 'pickup' }),
      order('phone', date, { orderType: 'delivery', orderSource: 'phone' })
    ], '2026-03-02', '2026-03-02');
    expect(result.orders).toBe(3);
    for (const label of ['Dine-In', 'Takeaway', 'Phone']) {
      expect(result.sources.find(source => source.label === label)?.count).toBe(1);
    }
  });

  it('reports selected-period staff discounts without counting them twice in total discounts', () => {
    const date = '2026-03-02T10:00:00Z';
    const result = calculateWebSalesOverview([
      order('staff-dine-in', date, { isStaffOrder: true, staffDiscountAmount: 20, discountAmount: 20, total: 80 }),
      order('staff-takeaway', date, { orderType: 'pickup', isStaffOrder: true, staffDiscountAmount: 22, discountAmount: 22, packagingCharge: 10, total: 88 }),
      order('coupon', date, { discountAmount: 5, total: 95 }),
      order('cancelled-staff', date, { isStaffOrder: true, staffDiscountAmount: 200, discountAmount: 200, status: 'cancelled' }),
      order('previous-day', '2026-03-01T10:00:00Z', { isStaffOrder: true, staffDiscountAmount: 50, discountAmount: 50 })
    ], '2026-03-02', '2026-03-02');
    expect(result.staffOrders).toBe(2);
    expect(result.staffDiscounts).toBe(42);
    expect(result.discounts).toBe(47);
    expect(result.orderValue).toBe(263);
  });
});
