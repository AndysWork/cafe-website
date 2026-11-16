import type { InsightPeriod, KitchenOrderTiming } from '../../services/zomato-sales-payout-insights.service';

export type TimingMetric = 'kptMinutes' | 'riderWaitMinutes';
export type CohortDimension = 'items' | 'combinations' | 'quantity' | 'distinct' | 'basket' | 'delivery';
export interface KitchenTargets { kpt: number; riderWait: number; }
export interface TimingSummary {
  total: number;
  valid: number;
  missing: number;
  invalid: number;
  average: number | null;
  median: number | null;
  p90: number | null;
  minimum: number | null;
  maximum: number | null;
  fastestOrder: string | null;
  slowestOrder: string | null;
  breaches: number;
  breachPercent: number | null;
  excessMinutes: number;
}
export interface TimingCohort {
  label: string;
  orders: number;
  kpt: TimingSummary;
  wait: TimingSummary;
  rated: number;
  averageRating: number | null;
  lowRatings: number;
  lowRatingPercent: number | null;
  complaints: number;
  complaintPercent: number;
  activeDays: number;
  breachDays: number;
}
export interface HeatCell {
  hour: number;
  orders: number;
  kpt: number | null;
  wait: number | null;
  kptSamples: number;
  waitSamples: number;
  kptBreaches: number;
  waitBreaches: number;
}
export interface KitchenAnalysis {
  orders: KitchenOrderTiming[];
  kpt: TimingSummary;
  wait: TimingSummary;
  distributions: { label: string; metric: TimingMetric; rows: TimingCohort[] }[];
  daily: TimingCohort[];
  hourly: TimingCohort[];
  heatmap: { date: string; cells: HeatCell[] }[];
  cohorts: Record<CohortDimension, TimingCohort[]>;
  marking: TimingCohort[];
  markingHours: TimingCohort[];
  signals: { label: string; hint: string; count: number }[];
  scatter: { order: KitchenOrderTiming; x: number; y: number; signal: number }[];
  scatterMaxKpt: number;
  scatterMaxWait: number;
  paired: number;
  missingDates: number;
  unvalidated: number;
  recommendations: string[];
}

export const validTiming = (order: KitchenOrderTiming, metric: TimingMetric): number | null => {
  const status = metric === 'kptMinutes' ? order.kptStatus : order.riderWaitStatus;
  const value = order[metric];
  return status === 'valid' && value !== null && Number.isFinite(value) && value >= 0 ? value : null;
};

// Linear interpolation (R-7), including the exact observed minimum and maximum at p=0/1.
function percentile(sorted: number[], p: number): number | null {
  if (!sorted.length) return null;
  const index = (sorted.length - 1) * p;
  const lower = Math.floor(index);
  return sorted[lower] + (sorted[Math.ceil(index)] - sorted[lower]) * (index - lower);
}

function summary(orders: KitchenOrderTiming[], metric: TimingMetric, target: number): TimingSummary {
  const values = orders.flatMap(order => {
    const value = validTiming(order, metric);
    return value === null ? [] : [{ value, id: order.orderId }];
  }).sort((a, b) => a.value - b.value);
  const missing = orders.filter(o => (metric === 'kptMinutes' ? o.kptStatus : o.riderWaitStatus) === 'missing').length;
  const numeric = values.map(v => v.value);
  const breaches = numeric.filter(value => value > target).length;
  return {
    total: orders.length, valid: numeric.length, missing, invalid: orders.length - values.length - missing,
    average: numeric.length ? numeric.reduce((a, b) => a + b, 0) / numeric.length : null,
    median: percentile(numeric, 0.5), p90: percentile(numeric, 0.9),
    minimum: numeric[0] ?? null, maximum: numeric[numeric.length - 1] ?? null,
    fastestOrder: values[0]?.id ?? null, slowestOrder: values[values.length - 1]?.id ?? null,
    breaches, breachPercent: numeric.length ? breaches / numeric.length * 100 : null,
    excessMinutes: numeric.reduce((sum, value) => sum + Math.max(0, value - target), 0)
  };
}

export function exceedsTarget(order: KitchenOrderTiming, targets: KitchenTargets): boolean {
  const kpt = validTiming(order, 'kptMinutes');
  const wait = validTiming(order, 'riderWaitMinutes');
  return (kpt !== null && kpt > targets.kpt) || (wait !== null && wait > targets.riderWait);
}

function dateOf(order: KitchenOrderTiming): string | null {
  const value = order.placedAt?.slice(0, 10);
  return value && validDate(value) ? value : null;
}

function validDate(value: string): boolean {
  const timestamp = Date.parse(`${value}T00:00:00Z`);
  return /^\d{4}-\d{2}-\d{2}$/.test(value) && Number.isFinite(timestamp) &&
    new Date(timestamp).toISOString().slice(0, 10) === value;
}

function hourOf(order: KitchenOrderTiming): number | null {
  const match = order.placedAt?.match(/^\d{4}-\d{2}-\d{2}T([01]\d|2[0-3]):[0-5]\d/);
  return dateOf(order) && match ? Number(match[1]) : null;
}

function cohort(label: string, orders: KitchenOrderTiming[], targets: KitchenTargets): TimingCohort {
  const ratings = orders.flatMap(o => o.rating !== null && o.rating >= 1 && o.rating <= 5 ? [o.rating] : []);
  const complaints = orders.filter(o => o.complaint.trim()).length;
  const lowRatings = ratings.filter(r => r < 3).length;
  return {
    label, orders: orders.length,
    kpt: summary(orders, 'kptMinutes', targets.kpt), wait: summary(orders, 'riderWaitMinutes', targets.riderWait),
    rated: ratings.length, averageRating: ratings.length ? ratings.reduce((a, b) => a + b, 0) / ratings.length : null,
    lowRatings, lowRatingPercent: ratings.length ? lowRatings / ratings.length * 100 : null,
    complaints, complaintPercent: orders.length ? complaints / orders.length * 100 : 0,
    activeDays: new Set(orders.map(dateOf).filter(Boolean)).size,
    breachDays: new Set(orders.filter(o => exceedsTarget(o, targets)).map(dateOf).filter(Boolean)).size
  };
}

function groupBy(orders: KitchenOrderTiming[], labels: (order: KitchenOrderTiming) => string[], targets: KitchenTargets): TimingCohort[] {
  const groups = new Map<string, { label: string; orders: KitchenOrderTiming[] }>();
  for (const order of orders) {
    const seen = new Set<string>();
    for (const label of labels(order)) {
      const key = label.toLowerCase();
      if (seen.has(key)) continue;
      seen.add(key);
      const group = groups.get(key) ?? { label, orders: [] };
      group.orders.push(order);
      groups.set(key, group);
    }
  }
  return [...groups.values()].map(g => cohort(g.label, g.orders, targets))
    .sort((a, b) => b.orders - a.orders || a.label.localeCompare(b.label));
}

export function markingLabel(value: string): string {
  switch (value.trim().toLowerCase()) {
    case 'correct': case 'correctly': case 'yes': return 'Correct';
    case 'missed': case 'not marked': case 'no': return 'Missed';
    case 'incorrect': case 'incorrectly': return 'Incorrect';
    default: return 'Unknown';
  }
}

export function previousKitchenPeriod(period: InsightPeriod): { startDate: string; endDate: string } | null {
  if (!period.startDate || !period.endDate) return null;
  if (!validDate(period.startDate.slice(0, 10)) || !validDate(period.endDate.slice(0, 10))) return null;
  const start = Date.parse(`${period.startDate.slice(0, 10)}T00:00:00Z`);
  const end = Date.parse(`${period.endDate.slice(0, 10)}T00:00:00Z`);
  if (!Number.isFinite(start) || !Number.isFinite(end) || end < start) return null;
  const day = 86400000;
  return {
    startDate: new Date(start - (end - start + day)).toISOString().slice(0, 10),
    endDate: new Date(start - day).toISOString().slice(0, 10)
  };
}

export function buildKitchenAnalysis(orders: KitchenOrderTiming[], targets: KitchenTargets, period: InsightPeriod): KitchenAnalysis {
  if (!Number.isFinite(targets.kpt) || targets.kpt < 0 || !Number.isFinite(targets.riderWait) || targets.riderWait < 0) {
    throw new Error('Kitchen targets must be finite, non-negative minutes.');
  }
  const byDay = new Map<string, KitchenOrderTiming[]>();
  const byHour = Array.from({ length: 24 }, () => [] as KitchenOrderTiming[]);
  const byDayHour = new Map<string, KitchenOrderTiming[]>();
  for (const order of orders) {
    const date = dateOf(order);
    const hour = hourOf(order);
    if (date) {
      const dayOrders = byDay.get(date) ?? [];
      dayOrders.push(order);
      byDay.set(date, dayOrders);
      if (hour !== null) {
        byHour[hour].push(order);
        const key = `${date}-${hour}`;
        const cellOrders = byDayHour.get(key) ?? [];
        cellOrders.push(order);
        byDayHour.set(key, cellOrders);
      }
    }
  }
  const hourly = byHour.map((group, hour) => cohort(`${String(hour).padStart(2, '0')}:00`, group, targets));
  const dates = new Set(orders.map(dateOf).filter((d): d is string => d !== null));
  if (period.startDate && period.endDate && validDate(period.startDate.slice(0, 10)) && validDate(period.endDate.slice(0, 10))) {
    const end = Date.parse(`${period.endDate.slice(0, 10)}T00:00:00Z`);
    for (let day = Date.parse(`${period.startDate.slice(0, 10)}T00:00:00Z`); day <= end; day += 86400000) {
      dates.add(new Date(day).toISOString().slice(0, 10));
    }
  }
  const daily = [...dates].sort().map(date => cohort(date, byDay.get(date) ?? [], targets));
  const heatmap = daily.map(day => ({
    date: day.label,
    cells: Array.from({ length: 24 }, (_, hour) => {
      const group = cohort('', byDayHour.get(`${day.label}-${hour}`) ?? [], targets);
      return {
        hour, orders: group.orders, kpt: group.kpt.average, wait: group.wait.average,
        kptSamples: group.kpt.valid, waitSamples: group.wait.valid,
        kptBreaches: group.kpt.breaches, waitBreaches: group.wait.breaches
      };
    })
  }));
  const bucketDefinitions = [
    { metric: 'kptMinutes' as const, label: 'KPT', edges: [10, 20, 30] },
    { metric: 'riderWaitMinutes' as const, label: 'Rider wait', edges: [2, 5, 10] }
  ];
  const distributions = bucketDefinitions.map(definition => ({
    metric: definition.metric, label: definition.label,
    rows: [
      ...definition.edges.map((edge, index) => cohort(
        index === 0 ? `< ${edge} min` : `${definition.edges[index - 1]} to < ${edge} min`,
        orders.filter(o => {
          const value = validTiming(o, definition.metric);
          return value !== null && value >= (definition.edges[index - 1] ?? 0) && value < edge;
        }), targets)),
      cohort(`>= ${definition.edges[2]} min`, orders.filter(o => {
        const value = validTiming(o, definition.metric);
        return value !== null && value >= definition.edges[2];
      }), targets)
    ]
  }));
  const cohorts: Record<CohortDimension, TimingCohort[]> = {
    items: groupBy(orders, o => o.items.length ? o.items.map(i => i.name) : ['Unknown items'], targets),
    combinations: groupBy(orders, o => [o.items.length
      ? [...o.items].sort((a, b) => a.name.toLowerCase().localeCompare(b.name.toLowerCase())).map(i => `${i.quantity} x ${i.name}`).join(' + ')
      : 'Unknown items'], targets),
    quantity: groupBy(orders, o => [o.quantity ? `${o.quantity} items` : 'Unknown items'], targets),
    distinct: groupBy(orders, o => [o.distinctItems ? `${o.distinctItems} distinct items` : 'Unknown items'], targets),
    basket: groupBy(orders, o => [!o.quantity ? 'Unknown items' : o.quantity <= 2 ? 'Small (1-2 items)' : o.quantity <= 5 ? 'Medium (3-5 items)' : 'Large (6+ items)'], targets),
    delivery: groupBy(orders, o => [o.deliveryType.trim() || 'Unknown delivery type'], targets)
  };
  const marking = ['Correct', 'Missed', 'Incorrect', 'Unknown'].map(label =>
    cohort(label, orders.filter(o => markingLabel(o.readyMarked) === label), targets));
  const markingHours = groupBy(orders.filter(o => ['Missed', 'Incorrect'].includes(markingLabel(o.readyMarked))),
    o => [hourOf(o) === null ? 'Unknown hour' : `${String(hourOf(o)).padStart(2, '0')}:00`], targets);
  const pairedOrders = orders.filter(o => validTiming(o, 'kptMinutes') !== null && validTiming(o, 'riderWaitMinutes') !== null);
  const signalIndex = (o: KitchenOrderTiming) => (o.kptMinutes! > targets.kpt ? 2 : 0) + (o.riderWaitMinutes! > targets.riderWait ? 1 : 0);
  const signals = [
    { label: 'Within both targets', hint: 'Smooth-operation signal; monitor consistency.' },
    { label: 'Wait above target only', hint: 'Investigate packing, handoff, marking or rider-arrival coordination.' },
    { label: 'KPT above target only', hint: 'Investigate preparation; rider arrival may be aligned.' },
    { label: 'Both above target', hint: 'Investigate kitchen congestion and pickup coordination.' }
  ].map((signal, index) => ({ ...signal, count: pairedOrders.filter(o => signalIndex(o) === index).length }));
  const scatterMaxKpt = pairedOrders.reduce((max, o) => Math.max(max, o.kptMinutes!), Math.max(targets.kpt * 1.2, 1));
  const scatterMaxWait = pairedOrders.reduce((max, o) => Math.max(max, o.riderWaitMinutes!), Math.max(targets.riderWait * 1.2, 1));
  const scatter = pairedOrders.map(order => ({
    order, signal: signalIndex(order),
    x: 45 + order.kptMinutes! / scatterMaxKpt * 530,
    y: 245 - order.riderWaitMinutes! / scatterMaxWait * 215
  }));
  const recommendations: string[] = [];
  const repeated = hourly.filter(h => h.orders >= 5 && h.breachDays >= 2).sort((a, b) => b.kpt.breaches + b.wait.breaches - a.kpt.breaches - a.wait.breaches)[0];
  if (repeated) recommendations.push(`${repeated.label} has target breaches on ${repeated.breachDays} of ${repeated.activeDays} active days (${repeated.orders} orders). Review staffing and handoff coordination for this hour.`);
  const markingIssues = marking.filter(m => m.label === 'Missed' || m.label === 'Incorrect').reduce((n, m) => n + m.orders, 0);
  if (markingIssues) recommendations.push(`${markingIssues} orders have missed or incorrect ready marking. Review the marking-hour breakdown before attributing delays to cooking.`);
  if (signals[1].count) recommendations.push(`${signals[1].count} orders exceed the rider-wait target despite KPT being within target. Inspect the corresponding orders for handoff or arrival coordination issues.`);
  if (!recommendations.length) recommendations.push('No repeated-hour signal meets the minimum of 5 orders across 2 breach days. Inspect distributions and individual exceptions; this is not proof that all orders met targets.');
  return {
    orders, kpt: summary(orders, 'kptMinutes', targets.kpt), wait: summary(orders, 'riderWaitMinutes', targets.riderWait),
    distributions, hourly, daily, heatmap, cohorts, marking, markingHours, signals, scatter, scatterMaxKpt, scatterMaxWait,
    paired: pairedOrders.length, missingDates: orders.filter(o => hourOf(o) === null).length,
    unvalidated: orders.filter(o => !o.timingsValidated).length, recommendations
  };
}
