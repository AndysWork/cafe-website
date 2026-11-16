import { KitchenOrderTiming } from '../../services/zomato-sales-payout-insights.service';
import { buildKitchenAnalysis, markingLabel, previousKitchenPeriod } from './kitchen-performance';

function order(overrides: Partial<KitchenOrderTiming> = {}): KitchenOrderTiming {
  return {
    orderId: 'one', placedAt: '2026-10-27T18:02:00', kptMinutes: 20, riderWaitMinutes: 5,
    kptStatus: 'valid', riderWaitStatus: 'valid', timingsValidated: true,
    items: [{ name: 'Tea', quantity: 1 }], itemsText: '1 x Tea', quantity: 1, distinctItems: 1,
    deliveryType: 'Delivery', rating: null, review: '', complaint: '', readyMarked: 'Correct',
    orderValue: 100, ...overrides
  };
}
const targets = { kpt: 20, riderWait: 5 };
const period = { startDate: '2026-10-27', endDate: '2026-10-28' };

describe('Kitchen performance analytics', () => {
  it('calculates interpolated percentiles, extremes and strictly-above-target breaches', () => {
    const result = buildKitchenAnalysis([0, 10, 20, 30].map((kptMinutes, index) => order({
      orderId: `${index}`, kptMinutes, riderWaitMinutes: [0, 2, 5, 10][index]
    })), targets, period);
    expect(result.kpt.average).toBe(15);
    expect(result.kpt.median).toBe(15);
    expect(result.kpt.p90).toBeCloseTo(27);
    expect(result.kpt.minimum).toBe(0);
    expect(result.kpt.maximum).toBe(30);
    expect(result.kpt.fastestOrder).toBe('0');
    expect(result.kpt.slowestOrder).toBe('3');
    expect(result.kpt.breaches).toBe(1);
    expect(result.kpt.breachPercent).toBe(25);
    expect(result.wait.p90).toBeCloseTo(8.5);
    expect(result.wait.excessMinutes).toBe(5);
  });

  it('keeps real zero, excludes malformed/negative/nonfinite timings and uses valid denominators', () => {
    const result = buildKitchenAnalysis([
      order({ kptMinutes: 0 }), order({ kptMinutes: 30 }),
      order({ kptMinutes: null, kptStatus: 'missing' }),
      order({ kptMinutes: null, kptStatus: 'invalid' }),
      order({ kptMinutes: -3 }), order({ kptMinutes: Infinity })
    ], targets, period);
    expect(result.kpt.valid).toBe(2);
    expect(result.kpt.missing).toBe(1);
    expect(result.kpt.invalid).toBe(3);
    expect(result.kpt.average).toBe(15);
    expect(result.kpt.breachPercent).toBe(50);
    const empty = buildKitchenAnalysis([], targets, period);
    expect(empty.kpt.average).toBeNull();
    expect(empty.kpt.breachPercent).toBeNull();
    expect(empty.kpt.fastestOrder).toBeNull();
    expect(() => buildKitchenAnalysis([], { kpt: -1, riderWait: 5 }, period)).toThrow();
    expect(() => buildKitchenAnalysis([], { kpt: 20, riderWait: NaN }, period)).toThrow();
  });

  it('assigns every valid boundary value to exactly one distribution bucket', () => {
    const result = buildKitchenAnalysis([9.99, 10, 19.99, 20, 29.99, 30].map((kptMinutes, index) =>
      order({ kptMinutes, riderWaitMinutes: [1.99, 2, 4.99, 5, 9.99, 10][index] })), targets, period);
    expect(result.distributions[0].rows.map(r => r.orders)).toEqual([1, 2, 2, 1]);
    expect(result.distributions[1].rows.map(r => r.orders)).toEqual([1, 2, 2, 1]);
  });

  it('groups order-level items and canonical quantity combinations case-insensitively', () => {
    const items = [{ name: 'Tea', quantity: 2 }, { name: 'Coffee', quantity: 1 }];
    const result = buildKitchenAnalysis([
      order({ items, quantity: 3, distinctItems: 2 }),
      order({ items: [{ name: 'coffee', quantity: 1 }, { name: 'tea', quantity: 2 }], quantity: 3, distinctItems: 2 })
    ], targets, period);
    expect(result.cohorts.items.length).toBe(2);
    expect(result.cohorts.items.map(c => c.orders)).toEqual([2, 2]);
    expect(result.cohorts.combinations.length).toBe(1);
    expect(result.cohorts.combinations[0].orders).toBe(2);
    expect(result.cohorts.quantity[0].label).toBe('3 items');
    expect(result.cohorts.distinct[0].label).toBe('2 distinct items');
    expect(result.cohorts.basket[0].label).toBe('Medium (3-5 items)');
    const duplicated = buildKitchenAnalysis([order({ items: [{ name: 'Tea', quantity: 1 }, { name: 'tea', quantity: 1 }] })], targets, period);
    expect(duplicated.cohorts.items[0].orders).toBe(1);
  });

  it('classifies paired quadrants independently and excludes incomplete pairs', () => {
    const result = buildKitchenAnalysis([
      order(), order({ riderWaitMinutes: 6 }), order({ kptMinutes: 21 }),
      order({ kptMinutes: 21, riderWaitMinutes: 6 }),
      order({ kptMinutes: null, kptStatus: 'missing' })
    ], targets, period);
    expect(result.signals.map(s => s.count)).toEqual([1, 1, 1, 1]);
    expect(result.paired).toBe(4);
    expect(result.scatter.map(p => p.signal)).toEqual([0, 1, 2, 3]);
    expect(result.scatter.every(p => p.x >= 45 && p.x <= 575 && p.y >= 30 && p.y <= 245)).toBeTrue();
  });

  it('preserves local hours, includes empty days and distinguishes zero cells from missing cells', () => {
    const result = buildKitchenAnalysis([
      order({ kptMinutes: 0, riderWaitMinutes: 0 }),
      order({ placedAt: null }),
      order({ placedAt: '2026-02-30T18:02:00' })
    ], targets, period);
    expect(result.missingDates).toBe(2);
    expect(result.daily.map(d => d.orders)).toEqual([1, 0]);
    expect(result.hourly[18].orders).toBe(1);
    expect(result.heatmap[0].cells[18].wait).toBe(0);
    expect(result.heatmap[0].cells[17].wait).toBeNull();
    expect(result.heatmap[1].cells.every(c => c.orders === 0)).toBeTrue();
    expect(result.kpt.valid).toBe(3);
  });

  it('uses rated denominators for low ratings and all bucket orders for complaints', () => {
    const result = buildKitchenAnalysis([
      order({ rating: 2, complaint: 'Food cold' }), order({ rating: 5 }), order()
    ], targets, period);
    const bucket = result.distributions[0].rows[2];
    expect(bucket.rated).toBe(2);
    expect(bucket.lowRatings).toBe(1);
    expect(bucket.lowRatingPercent).toBe(50);
    expect(bucket.averageRating).toBe(3.5);
    expect(bucket.complaints).toBe(1);
    expect(bucket.complaintPercent).toBeCloseTo(100 / 3);
    expect(result.distributions[0].rows[0].lowRatingPercent).toBeNull();
  });

  it('normalizes marking and flags recurring hours only with enough orders and distinct breach days', () => {
    expect(['Correctly', 'yes', 'Missed', 'no', 'Incorrectly', '', 'unexpected'].map(markingLabel))
      .toEqual(['Correct', 'Correct', 'Missed', 'Missed', 'Incorrect', 'Unknown', 'Unknown']);
    const orders = Array.from({ length: 5 }, (_, i) => order({
      placedAt: `2026-10-${i < 3 ? '27' : '28'}T18:02:00`, kptMinutes: 30, readyMarked: 'Missed'
    }));
    const result = buildKitchenAnalysis(orders, targets, period);
    expect(result.hourly[18].breachDays).toBe(2);
    expect(result.recommendations[0]).toContain('18:00 has target breaches on 2 of 2 active days');
    expect(result.marking.find(m => m.label === 'Missed')?.orders).toBe(5);
    expect(result.markingHours[0].orders).toBe(5);
    const isolated = buildKitchenAnalysis(orders.map(o => ({ ...o, placedAt: '2026-10-27T18:02:00' })), targets, period);
    expect(isolated.recommendations.some(r => r.includes('has target breaches on'))).toBeFalse();
  });

  it('finds the immediately preceding inclusive-length period across leap days and years', () => {
    expect(previousKitchenPeriod({ startDate: '2024-03-01', endDate: '2024-03-03' }))
      .toEqual({ startDate: '2024-02-27', endDate: '2024-02-29' });
    expect(previousKitchenPeriod({ startDate: '2026-01-01', endDate: '2026-01-01' }))
      .toEqual({ startDate: '2025-12-31', endDate: '2025-12-31' });
    expect(previousKitchenPeriod({ startDate: '2026-02-30', endDate: '2026-03-01' })).toBeNull();
    expect(previousKitchenPeriod({ startDate: '2026-03-03', endDate: '2026-03-01' })).toBeNull();
  });
});
