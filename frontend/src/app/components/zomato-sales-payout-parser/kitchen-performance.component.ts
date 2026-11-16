import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { InsightPeriod, KitchenOrderTiming } from '../../services/zomato-sales-payout-insights.service';
import {
  buildKitchenAnalysis, CohortDimension, exceedsTarget, HeatCell, KitchenAnalysis,
  KitchenTargets, markingLabel, validTiming
} from './kitchen-performance';

@Component({
  selector: 'app-kitchen-performance',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './kitchen-performance.component.html',
  styleUrl: './kitchen-performance.component.scss'
})
export class KitchenPerformanceComponent implements OnChanges {
  @Input() orders: KitchenOrderTiming[] | null = null;
  @Input() period: InsightPeriod = { startDate: null, endDate: null };
  @Input() previousOrders: KitchenOrderTiming[] | null = null;
  @Input() previousPeriod: InsightPeriod | null = null;
  @Input() loading = false;
  @Input() error = '';
  @Input() comparisonLoading = false;
  @Input() comparisonError = '';
  @Input() needsUpload = false;
  @Input() comparisonNeedsUpload = false;
  @Input() notice = '';
  @Input() comparisonEnabled = true;
  @Output() comparisonEnabledChange = new EventEmitter<boolean>();
  @Output() retry = new EventEmitter<void>();
  @Output() uploadReports = new EventEmitter<void>();
  @Output() viewSavedReports = new EventEmitter<void>();

  targets: KitchenTargets = { kpt: 20, riderWait: 5 };
  draftKpt: number | null = 20;
  draftWait: number | null = 5;
  targetError = '';
  analysis: KitchenAnalysis | null = null;
  previous: KitchenAnalysis | null = null;
  heatMetric: 'kpt' | 'wait' = 'kpt';
  cohortDimension: CohortDimension = 'items';
  cohortSearch = '';
  cohortSort: 'volume' | 'kpt' | 'wait' | 'kpt-p90' | 'wait-p90' = 'volume';
  minimumSample = 1;
  cohortPage = 1;
  exceptionSearch = '';
  exceptionKind: 'either' | 'kpt' | 'wait' | 'both' = 'either';
  exceptionMarking = 'All';
  exceptionSort: 'wait' | 'kpt' | 'newest' | 'value' = 'wait';
  exceptionPage = 1;
  readonly pageSize = 25;
  readonly hours = Array.from({ length: 24 }, (_, i) => i);
  readonly signalColors = ['#16a34a', '#f59e0b', '#2563eb', '#e23744'];
  readonly markingLabel = markingLabel;
  readonly validTiming = validTiming;

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['orders'] || changes['period'] || changes['previousOrders'] || changes['previousPeriod'] || changes['comparisonEnabled']) this.rebuild();
  }

  applyTargets(): void {
    if (this.draftKpt === null || this.draftWait === null || !Number.isFinite(this.draftKpt) ||
        !Number.isFinite(this.draftWait) || this.draftKpt < 0 || this.draftWait < 0) {
      this.targetError = 'Enter non-negative target minutes for both metrics.';
      return;
    }
    this.targetError = '';
    this.targets = { kpt: this.draftKpt, riderWait: this.draftWait };
    this.rebuild();
  }

  private rebuild(): void {
    this.analysis = this.orders === null ? null : buildKitchenAnalysis(this.orders, this.targets, this.period);
    this.previous = this.comparisonEnabled && this.previousOrders !== null && this.previousPeriod
      ? buildKitchenAnalysis(this.previousOrders, this.targets, this.previousPeriod) : null;
    this.cohortPage = 1;
    this.exceptionPage = 1;
  }

  get metricCards() {
    return this.analysis ? [
      { label: 'Kitchen preparation', stats: this.analysis.kpt, previous: this.previous?.kpt, target: this.targets.kpt },
      { label: 'Rider wait', stats: this.analysis.wait, previous: this.previous?.wait, target: this.targets.riderWait }
    ] : [];
  }

  get cohortRows() {
    const search = this.cohortSearch.trim().toLowerCase();
    return this.analysis?.cohorts[this.cohortDimension]
      .filter(c => c.orders >= this.minimumSample && c.label.toLowerCase().includes(search))
      .sort((a, b) => {
        switch (this.cohortSort) {
          case 'kpt': return (b.kpt.average ?? -1) - (a.kpt.average ?? -1);
          case 'wait': return (b.wait.average ?? -1) - (a.wait.average ?? -1);
          case 'kpt-p90': return (b.kpt.p90 ?? -1) - (a.kpt.p90 ?? -1);
          case 'wait-p90': return (b.wait.p90 ?? -1) - (a.wait.p90 ?? -1);
          default: return b.orders - a.orders;
        }
      }) ?? [];
  }

  get exceptionRows(): KitchenOrderTiming[] {
    const search = this.exceptionSearch.trim().toLowerCase();
    return (this.analysis?.orders ?? []).filter(order => {
      const kpt = validTiming(order, 'kptMinutes');
      const wait = validTiming(order, 'riderWaitMinutes');
      const kptBreach = kpt !== null && kpt > this.targets.kpt;
      const waitBreach = wait !== null && wait > this.targets.riderWait;
      return exceedsTarget(order, this.targets) &&
        (this.exceptionKind === 'either' || this.exceptionKind === 'kpt' && kptBreach ||
          this.exceptionKind === 'wait' && waitBreach || this.exceptionKind === 'both' && kptBreach && waitBreach) &&
        (this.exceptionMarking === 'All' || markingLabel(order.readyMarked) === this.exceptionMarking) &&
        [order.orderId, order.placedAt ?? '', order.itemsText, order.review, order.complaint].join(' ').toLowerCase().includes(search);
    }).sort((a, b) => {
      switch (this.exceptionSort) {
        case 'newest': return (b.placedAt ?? '').localeCompare(a.placedAt ?? '');
        case 'value': return b.orderValue - a.orderValue;
        case 'kpt': return (validTiming(b, 'kptMinutes') ?? -1) - (validTiming(a, 'kptMinutes') ?? -1);
        default: return (validTiming(b, 'riderWaitMinutes') ?? -1) - (validTiming(a, 'riderWaitMinutes') ?? -1);
      }
    });
  }

  heatColor(cell: HeatCell): string {
    const value = cell[this.heatMetric];
    if (value === null) return '#f4f3fc';
    const target = this.heatMetric === 'kpt' ? this.targets.kpt : this.targets.riderWait;
    const alpha = Math.min(0.65, 0.1 + value / Math.max(target, 1) * 0.2);
    return value > target ? `rgba(226,55,68,${alpha})` : `rgba(22,163,74,${alpha})`;
  }

  heatTitle(cell: HeatCell): string {
    const samples = this.heatMetric === 'kpt' ? cell.kptSamples : cell.waitSamples;
    const breaches = this.heatMetric === 'kpt' ? cell.kptBreaches : cell.waitBreaches;
    return `${cell.hour}:00 - ${cell.orders} orders; ${samples} valid timings; ${breaches} target breaches`;
  }

  minutes(value: number | null | undefined): string {
    return value == null ? '—' : `${value.toFixed(1)} min`;
  }

  percent(value: number | null | undefined): string {
    return value == null ? '—' : `${value.toFixed(1)}%`;
  }

  delta(value: number | null, previous: number | null | undefined, unit = ' min'): string {
    if (value === null || previous == null) return '—';
    const difference = value - previous;
    return `${difference > 0 ? '+' : ''}${difference.toFixed(1)}${unit}`;
  }

  excessWait(order: KitchenOrderTiming): number | null {
    const wait = validTiming(order, 'riderWaitMinutes');
    return wait === null ? null : Math.max(0, wait - this.targets.riderWait);
  }

  pages(count: number): number { return Math.max(1, Math.ceil(count / this.pageSize)); }

  get trendMaximum(): number {
    return Math.max(this.analysis?.kpt.maximum ?? 0, this.analysis?.wait.maximum ?? 0, 1);
  }
}
