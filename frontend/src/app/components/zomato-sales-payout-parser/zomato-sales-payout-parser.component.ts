import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
  SavedInsightSnapshot,
  ZomatoSalesPayoutInsights,
  ZomatoSalesPayoutInsightsService
} from '../../services/zomato-sales-payout-insights.service';
import { UIStore } from '../../store/ui.store';

export type ParserTab = 'overview' | 'orders' | 'items' | 'settlement' | 'reconciliation' | 'ratings' | 'snapshots';

interface WaterfallBar {
  label: string;
  value: number;
  kind: 'total' | 'negative' | 'positive' | 'credit';
  x: number;
  y: number;
  height: number;
  labelY: number;
}

interface KpiCard {
  key: string;
  label: string;
  value: string;
  icon: string;
  tone: string;
  note: string;
  noteTone: 'up' | 'down' | 'muted';
}

interface BreakdownRow {
  label: string;
  amount: number;
  percent: number;
  color: string;
}

interface HourBar {
  hour: number;
  label: string;
  orders: number;
  height: number;
  isPeak: boolean;
}

const WATERFALL_WIDTH = 700;
const WATERFALL_HEIGHT = 230;
const WATERFALL_TOP = 24;
const WATERFALL_BOTTOM = 200;
const TREND_WIDTH = 520;
const TREND_HEIGHT = 200;
const TREND_PAD_X = 48;
const TREND_PAD_TOP = 16;
const TREND_PAD_BOTTOM = 28;

@Component({
  selector: 'app-zomato-sales-payout-parser',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './zomato-sales-payout-parser.component.html',
  styleUrl: './zomato-sales-payout-parser.component.scss'
})
export class ZomatoSalesPayoutParserComponent implements OnInit {
  private insightsService = inject(ZomatoSalesPayoutInsightsService);
  private uiStore = inject(UIStore);

  readonly tabs: { id: ParserTab; label: string; icon: string }[] = [
    { id: 'overview', label: 'Overview', icon: 'fa-chart-pie' },
    { id: 'orders', label: 'Orders', icon: 'fa-receipt' },
    { id: 'items', label: 'Items', icon: 'fa-utensils' },
    { id: 'settlement', label: 'Settlement', icon: 'fa-building-columns' },
    { id: 'reconciliation', label: 'Reconciliation', icon: 'fa-scale-balanced' },
    { id: 'ratings', label: 'Ratings & Reviews', icon: 'fa-star' },
    { id: 'snapshots', label: 'Saved Snapshots', icon: 'fa-clock-rotate-left' }
  ];

  readonly waterfallWidth = WATERFALL_WIDTH;
  readonly waterfallHeight = WATERFALL_HEIGHT;
  readonly waterfallBaseline = WATERFALL_BOTTOM;
  readonly trendWidth = TREND_WIDTH;
  readonly trendHeight = TREND_HEIGHT;

  activeTab: ParserTab = 'overview';
  chartMode: 'waterfall' | 'breakdown' = 'waterfall';
  compareMode: 'previous' | 'none' = 'previous';
  moreSheetOpen = false;
  saveMenuOpen = false;

  orderHistoryFile: File | null = null;
  settlementFile: File | null = null;
  isParsing = false;
  isDeleting = false;
  isLoadingSnapshots = false;

  insights: ZomatoSalesPayoutInsights | null = null;
  snapshots: SavedInsightSnapshot[] = [];
  selectedSnapshotId = '';
  lastUpdatedAt: string | null = null;
  lastParsedBy: string | null = null;

  deleteStartDate = '';
  deleteEndDate = '';

  kpis: KpiCard[] = [];
  waterfallBars: WaterfallBar[] = [];
  waterfallBarWidth = 60;
  breakdownRows: BreakdownRow[] = [];
  breakdownTotal = 0;
  hourBars: HourBar[] = [];
  peakHour: HourBar | null = null;
  trendGrossPoints = '';
  trendPayoutPoints = '';
  trendDots: { x: number; grossY: number; payoutY: number; label: string }[] = [];
  trendTicks: { y: number; label: string }[] = [];
  topItems: { name: string; quantity: number; revenue: number; percent: number }[] = [];
  ratingTrendPoints = '';
  ratingTrendDots: { x: number; y: number; label: string; value: number }[] = [];
  recentReviews: { rating: number; text: string; date: string | null; items: string }[] = [];
  reconciliationChecks: { label: string; value: number; ok: boolean; icon: string }[] = [];
  allReconciled = false;

  ngOnInit(): void {
    this.loadSnapshots();
  }

  // ---------- File handling ----------

  onOrderHistoryFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.orderHistoryFile = input.files?.[0] ?? null;
  }

  onSettlementFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.settlementFile = input.files?.[0] ?? null;
  }

  get canParse(): boolean {
    return !!this.orderHistoryFile && !!this.settlementFile && !this.isParsing;
  }

  // ---------- Actions ----------

  parseReports(): void {
    if (!this.orderHistoryFile || !this.settlementFile) {
      this.uiStore.warning('Upload both the Order History CSV and the Settlement XLSX.');
      return;
    }

    this.isParsing = true;
    this.saveMenuOpen = false;
    this.insightsService.parseReports(this.orderHistoryFile, this.settlementFile).subscribe({
      next: (response) => {
        this.isParsing = false;
        this.selectedSnapshotId = response.persisted?.id ?? '';
        this.lastUpdatedAt = response.persisted?.updatedAt ?? null;
        this.lastParsedBy = null;
        this.setInsights(response.data);
        this.uiStore.success('Reports parsed and snapshot saved.');
        this.loadSnapshots(false);
      },
      error: (error) => {
        this.isParsing = false;
        this.uiStore.error(error?.message || 'Failed to parse Zomato reports.');
      }
    });
  }

  saveSnapshot(): void {
    this.saveMenuOpen = false;
    if (this.orderHistoryFile && this.settlementFile) {
      this.parseReports();
      return;
    }
    if (this.insights && this.selectedSnapshotId) {
      this.uiStore.success('This period is already saved as a snapshot.');
      return;
    }
    this.uiStore.warning('Upload and parse reports before saving a snapshot.');
  }

  deleteCurrentPeriod(): void {
    const start = this.dateOnly(this.insights?.period.startDate);
    const end = this.dateOnly(this.insights?.period.endDate);
    if (!start || !end) {
      this.uiStore.warning('Load a period before deleting it.');
      return;
    }
    if (!confirm(`Delete saved Zomato insights for ${start} to ${end}?`)) {
      return;
    }
    this.runDelete(start, end, true);
  }

  deleteSnapshot(snapshot: SavedInsightSnapshot): void {
    const start = this.dateOnly(snapshot.periodStartDate);
    const end = this.dateOnly(snapshot.periodEndDate);
    if (!start || !end || !confirm(`Delete saved Zomato insights for ${start} to ${end}?`)) {
      return;
    }
    this.runDelete(start, end, snapshot.id === this.selectedSnapshotId);
  }

  deleteRange(): void {
    if (!this.deleteStartDate || !this.deleteEndDate) {
      this.uiStore.warning('Select both start and end dates.');
      return;
    }
    if (this.deleteStartDate > this.deleteEndDate) {
      this.uiStore.warning('Start date must be before end date.');
      return;
    }
    if (!confirm(`Delete all saved Zomato insights between ${this.deleteStartDate} and ${this.deleteEndDate}?`)) {
      return;
    }
    const currentStart = this.dateOnly(this.insights?.period.startDate);
    const currentEnd = this.dateOnly(this.insights?.period.endDate);
    const clearsCurrent = !!currentStart && !!currentEnd && currentStart >= this.deleteStartDate && currentEnd <= this.deleteEndDate;
    this.runDelete(this.deleteStartDate, this.deleteEndDate, clearsCurrent);
  }

  onSnapshotSelected(id: string): void {
    const snapshot = this.snapshots.find((s) => s.id === id);
    if (snapshot) {
      this.loadSnapshot(snapshot);
    }
  }

  loadSnapshot(snapshot: SavedInsightSnapshot): void {
    this.selectedSnapshotId = snapshot.id;
    this.lastUpdatedAt = snapshot.updatedAt;
    this.lastParsedBy = snapshot.parsedBy;
    this.setInsights(snapshot.data);
    this.activeTab = 'overview';
    this.moreSheetOpen = false;
  }

  selectTab(tab: ParserTab): void {
    this.activeTab = tab;
    this.moreSheetOpen = false;
  }

  onCompareModeChanged(): void {
    this.rebuildKpis();
  }

  // ---------- Data loading ----------

  private loadSnapshots(autoSelectLatest = true): void {
    this.isLoadingSnapshots = true;
    this.insightsService.listSavedSnapshots().subscribe({
      next: (snapshots) => {
        this.isLoadingSnapshots = false;
        this.snapshots = snapshots;
        if (autoSelectLatest && !this.insights && snapshots.length > 0) {
          this.loadSnapshot(snapshots[0]);
        } else {
          this.rebuildKpis();
        }
      },
      error: () => {
        this.isLoadingSnapshots = false;
      }
    });
  }

  private runDelete(start: string, end: string, clearsCurrent: boolean): void {
    this.isDeleting = true;
    this.insightsService.deleteSavedInsights(start, end).subscribe({
      next: (deletedCount) => {
        this.isDeleting = false;
        this.uiStore.success(`Deleted ${deletedCount} saved snapshot(s).`);
        if (clearsCurrent) {
          this.insights = null;
          this.selectedSnapshotId = '';
          this.lastUpdatedAt = null;
        }
        this.loadSnapshots();
      },
      error: (error) => {
        this.isDeleting = false;
        this.uiStore.error(error?.message || 'Failed to delete saved insights.');
      }
    });
  }

  private setInsights(data: ZomatoSalesPayoutInsights): void {
    this.insights = data;
    this.rebuildKpis();
    this.buildWaterfall();
    this.buildHourly();
    this.buildTrend();
    this.buildTopItems();
    this.buildRatings();
    this.buildReconciliation();
  }

  // ---------- View-model builders ----------

  get previousSnapshot(): SavedInsightSnapshot | null {
    const start = this.dateOnly(this.insights?.period.startDate);
    if (!start) {
      return null;
    }
    return (
      this.snapshots
        .filter((s) => this.dateOnly(s.periodEndDate) < start)
        .sort((a, b) => this.dateOnly(b.periodEndDate).localeCompare(this.dateOnly(a.periodEndDate)))[0] ?? null
    );
  }

  private rebuildKpis(): void {
    const data = this.insights;
    if (!data) {
      this.kpis = [];
      return;
    }
    const prev = this.compareMode === 'previous' ? this.previousSnapshot?.data ?? null : null;
    const o = data.orderHistorySummary;
    const s = data.settlementSummary;
    const gross = o.grossSales || 0;
    const ofGross = (value: number) => (gross > 0 ? `${((value / gross) * 100).toFixed(1)}% of gross` : '—');
    const delta = (cur: number, before: number | undefined): Pick<KpiCard, 'note' | 'noteTone'> => {
      if (before === undefined || before === null || before === 0) {
        return { note: prev ? 'No prior data' : 'vs previous period', noteTone: 'muted' };
      }
      const change = ((cur - before) / Math.abs(before)) * 100;
      return { note: `${change >= 0 ? '▲' : '▼'} ${Math.abs(change).toFixed(1)}% vs prev`, noteTone: change >= 0 ? 'up' : 'down' };
    };
    const cancelPct = o.totalOrders > 0 ? (o.cancelledOrRejectedOrders / o.totalOrders) * 100 : 0;

    this.kpis = [
      { key: 'orders', label: 'Orders', value: `${o.deliveredOrders}`, icon: 'fa-bag-shopping', tone: 'red', ...delta(o.deliveredOrders, prev?.orderHistorySummary.deliveredOrders) },
      { key: 'gross', label: 'Gross Sales', value: this.formatCurrency(gross), icon: 'fa-indian-rupee-sign', tone: 'green', ...delta(gross, prev?.orderHistorySummary.grossSales) },
      { key: 'payout', label: 'Total Payout', value: this.formatCurrency(s.totalPayout), icon: 'fa-wallet', tone: 'blue', note: ofGross(s.totalPayout), noteTone: 'muted' },
      { key: 'credited', label: 'Actual Credited', value: this.formatCurrency(s.actualPayoutCredited), icon: 'fa-building-columns', tone: 'purple', note: ofGross(s.actualPayoutCredited), noteTone: 'muted' },
      { key: 'deductions', label: 'Net Deductions', value: this.formatCurrency(s.netDeductions + s.additionalDeductionsAdjusted), icon: 'fa-scissors', tone: 'orange', note: ofGross(s.netDeductions + s.additionalDeductionsAdjusted), noteTone: 'muted' },
      { key: 'aov', label: 'Avg Order Value', value: this.formatCurrency(o.averageOrderValue), icon: 'fa-chart-line', tone: 'teal', ...delta(o.averageOrderValue, prev?.orderHistorySummary.averageOrderValue) },
      { key: 'cancelled', label: 'Cancelled', value: `${o.cancelledOrRejectedOrders}`, icon: 'fa-ban', tone: 'gray', note: `${cancelPct.toFixed(1)}% of orders`, noteTone: cancelPct > 5 ? 'down' : 'muted' }
    ];
  }

  private buildWaterfall(): void {
    const data = this.insights!;
    const o = data.orderHistorySummary;
    const s = data.settlementSummary;
    const gross = o.grossSales;
    const discounts = o.totalDiscount;
    const fees = s.serviceFeeAndPmFee;
    const taxes = s.governmentCharges;
    const payout = s.totalPayout;
    const adjustments = gross - discounts - fees - taxes - payout;
    const ads = s.additionalDeductionsAdjusted - s.additionalAdditionsAdjusted;
    const credited = s.actualPayoutCredited;

    type Step = { label: string; value: number; kind: WaterfallBar['kind'] };
    const steps: Step[] = [
      { label: 'Gross', value: gross, kind: 'total' },
      { label: 'Discounts', value: -discounts, kind: 'negative' },
      { label: 'Platform Fees', value: -fees, kind: 'negative' },
      { label: 'Taxes', value: -taxes, kind: 'negative' },
      { label: 'Adjustments', value: -adjustments, kind: adjustments >= 0 ? 'negative' : 'positive' },
      { label: 'Total Payout', value: payout, kind: 'total' },
      { label: 'Ads & Other', value: -ads, kind: ads >= 0 ? 'negative' : 'positive' },
      { label: 'Actual Credited', value: credited, kind: 'credit' }
    ];

    let running = 0;
    const ranges = steps.map((step) => {
      if (step.kind === 'total' || step.kind === 'credit') {
        running = step.value;
        return { step, low: 0, high: step.value };
      }
      const before = running;
      running += step.value;
      return { step, low: Math.min(before, running), high: Math.max(before, running) };
    });

    const max = Math.max(...ranges.map((r) => r.high), 1);
    const scale = (WATERFALL_BOTTOM - WATERFALL_TOP) / max;
    const slot = WATERFALL_WIDTH / steps.length;
    this.waterfallBarWidth = Math.min(64, slot * 0.62);

    this.waterfallBars = ranges.map((r, index) => {
      const y = WATERFALL_BOTTOM - r.high * scale;
      const height = Math.max((r.high - r.low) * scale, 2);
      return {
        label: r.step.label,
        value: r.step.value,
        kind: r.step.kind,
        x: index * slot + (slot - this.waterfallBarWidth) / 2,
        y,
        height,
        labelY: Math.max(y - 6, 12)
      };
    });

    const rows: BreakdownRow[] = [
      { label: 'Discounts', amount: discounts, percent: 0, color: '#f97316' },
      { label: 'Platform Fees', amount: fees, percent: 0, color: '#e23744' },
      { label: 'Taxes & Govt Charges', amount: taxes, percent: 0, color: '#8b5cf6' },
      { label: 'Other Adjustments', amount: Math.max(adjustments, 0), percent: 0, color: '#64748b' },
      { label: 'Ads & Misc (Additional)', amount: Math.max(ads, 0), percent: 0, color: '#0ea5e9' }
    ];
    this.breakdownTotal = rows.reduce((sum, row) => sum + row.amount, 0);
    rows.forEach((row) => (row.percent = this.breakdownTotal > 0 ? (row.amount / this.breakdownTotal) * 100 : 0));
    this.breakdownRows = rows.sort((a, b) => b.amount - a.amount);
  }

  private buildHourly(): void {
    const byHour = new Map<number, number>();
    for (const row of this.insights!.hourlyOrderInsights) {
      const hour = parseInt(row.hourLabel, 10);
      if (!isNaN(hour)) {
        byHour.set(hour, (byHour.get(hour) ?? 0) + row.orders);
      }
    }
    const hours = [...byHour.keys()];
    if (hours.length === 0) {
      this.hourBars = [];
      this.peakHour = null;
      return;
    }
    const first = Math.min(...hours);
    const last = Math.max(...hours);
    const max = Math.max(...byHour.values(), 1);
    const bars: HourBar[] = [];
    for (let hour = first; hour <= last; hour++) {
      const orders = byHour.get(hour) ?? 0;
      bars.push({ hour, label: this.hourLabel(hour), orders, height: (orders / max) * 100, isPeak: false });
    }
    const peak = bars.reduce((best, bar) => (bar.orders > best.orders ? bar : best), bars[0]);
    peak.isPeak = true;
    this.hourBars = bars;
    this.peakHour = peak;
  }

  private buildTrend(): void {
    const data = this.insights!;
    const dates = new Set<string>();
    const gross = new Map<string, number>();
    const payout = new Map<string, number>();
    data.dailySales.forEach((d) => {
      const key = this.dateOnly(d.date);
      dates.add(key);
      gross.set(key, d.grossSales);
    });
    data.dailySettlement.forEach((d) => {
      const key = this.dateOnly(d.date);
      dates.add(key);
      payout.set(key, d.payout);
    });
    const sorted = [...dates].filter(Boolean).sort();
    if (sorted.length === 0) {
      this.trendGrossPoints = this.trendPayoutPoints = '';
      this.trendDots = [];
      this.trendTicks = [];
      return;
    }
    const max = Math.max(...sorted.map((d) => Math.max(gross.get(d) ?? 0, payout.get(d) ?? 0)), 1);
    const niceMax = Math.ceil(max / 1000) * 1000 || 1000;
    const plotW = TREND_WIDTH - TREND_PAD_X - 12;
    const plotH = TREND_HEIGHT - TREND_PAD_TOP - TREND_PAD_BOTTOM;
    const xAt = (i: number) => TREND_PAD_X + (sorted.length === 1 ? plotW / 2 : (i / (sorted.length - 1)) * plotW);
    const yAt = (v: number) => TREND_PAD_TOP + plotH - (v / niceMax) * plotH;

    this.trendDots = sorted.map((d, i) => ({
      x: xAt(i),
      grossY: yAt(gross.get(d) ?? 0),
      payoutY: yAt(payout.get(d) ?? 0),
      label: this.shortDate(d)
    }));
    this.trendGrossPoints = this.trendDots.map((p) => `${p.x},${p.grossY}`).join(' ');
    this.trendPayoutPoints = this.trendDots.map((p) => `${p.x},${p.payoutY}`).join(' ');
    this.trendTicks = [0, 0.25, 0.5, 0.75, 1].map((f) => ({ y: yAt(niceMax * f), label: this.compactCurrency(niceMax * f) }));
  }

  private buildTopItems(): void {
    const items = [...this.insights!.itemSalesInsights].sort((a, b) => b.quantitySold - a.quantitySold).slice(0, 5);
    const max = Math.max(...items.map((i) => i.quantitySold), 1);
    this.topItems = items.map((i) => ({
      name: i.itemName,
      quantity: i.quantitySold,
      revenue: i.estimatedGrossSales,
      percent: (i.quantitySold / max) * 100
    }));
  }

  private buildRatings(): void {
    const r = this.insights!.reviewRatingInsights;
    if (!r) {
      this.ratingTrendPoints = '';
      this.ratingTrendDots = [];
      this.recentReviews = [];
      return;
    }
    const days = [...(r.dailyRatings ?? [])].sort((a, b) => a.date.localeCompare(b.date)).slice(-7);
    const w = 260;
    const h = 90;
    const xAt = (i: number) => 16 + (days.length <= 1 ? (w - 32) / 2 : (i / (days.length - 1)) * (w - 32));
    const yAt = (v: number) => 10 + (h - 28) * (1 - (v - 1) / 4);
    this.ratingTrendDots = days.map((d, i) => ({ x: xAt(i), y: yAt(d.averageRating), label: this.weekday(d.date), value: d.averageRating }));
    this.ratingTrendPoints = this.ratingTrendDots.map((p) => `${p.x},${p.y}`).join(' ');

    const feedback = (r.orderFeedback ?? []).filter((f) => f.rating !== null);
    const withText = feedback.filter((f) => f.review?.trim() || f.complaintTag?.trim());
    const source = withText.length > 0 ? withText : feedback;
    this.recentReviews = [...source]
      .sort((a, b) => (b.orderDate ?? '').localeCompare(a.orderDate ?? ''))
      .slice(0, 4)
      .map((f) => ({
        rating: f.rating ?? 0,
        text: f.review?.trim() || (f.complaintTag ? `Complaint: ${f.complaintTag}` : 'Rated without a written review'),
        date: f.orderDate,
        items: f.items
      }));
  }

  private buildReconciliation(): void {
    const rec = this.insights!.reconciliation;
    const s = this.insights!.settlementSummary;
    this.reconciliationChecks = [
      { label: 'Orders matched', value: rec.matchedDeliveredOrders, ok: true, icon: 'fa-circle-check' },
      { label: 'Missing in settlement', value: rec.missingInSettlementCount, ok: rec.missingInSettlementCount === 0, icon: 'fa-file-circle-xmark' },
      { label: 'Missing in order history', value: rec.missingInOrderHistoryCount, ok: rec.missingInOrderHistoryCount === 0, icon: 'fa-file-circle-question' },
      { label: 'Unsettled orders', value: s.unsettledOrders, ok: s.unsettledOrders === 0, icon: 'fa-hourglass-half' }
    ];
    this.allReconciled = this.reconciliationChecks.every((c) => c.ok);
  }

  // ---------- Template helpers ----------

  get periodLabel(): string {
    const start = this.insights?.period.startDate;
    const end = this.insights?.period.endDate;
    if (!start || !end) {
      return 'No period loaded';
    }
    return `${this.longDate(start)} – ${this.longDate(end)}`;
  }

  get periodDays(): number {
    const start = this.dateOnly(this.insights?.period.startDate);
    const end = this.dateOnly(this.insights?.period.endDate);
    if (!start || !end) {
      return 0;
    }
    return Math.round((new Date(end).getTime() - new Date(start).getTime()) / 86400000) + 1;
  }

  get topItem(): { name: string; quantity: number; revenue: number; percent: number } | null {
    return this.topItems.length > 0 ? this.topItems[0] : null;
  }

  get openIssues(): number {
    return this.reconciliationChecks.filter((c) => !c.ok).reduce((sum, c) => sum + c.value, 0);
  }

  get mobileKpis(): KpiCard[] {
    const pick = ['orders', 'gross', 'payout', 'credited'];
    return this.kpis.filter((k) => pick.includes(k.key));
  }

  get sortedItems() {
    return [...(this.insights?.itemSalesInsights ?? [])].sort((a, b) => b.quantitySold - a.quantitySold);
  }

  get sortedHourly() {
    return [...(this.insights?.hourlyOrderInsights ?? [])].sort((a, b) => parseInt(a.hourLabel, 10) - parseInt(b.hourLabel, 10));
  }

  snapshotLabel(snapshot: SavedInsightSnapshot): string {
    return `${this.longDate(snapshot.periodStartDate)} – ${this.longDate(snapshot.periodEndDate)}`;
  }

  stars(rating: number): ('full' | 'half' | 'empty')[] {
    return [1, 2, 3, 4, 5].map((i) => (rating >= i ? 'full' : rating >= i - 0.5 ? 'half' : 'empty'));
  }

  ratingCount(stars: number): number {
    return this.insights?.reviewRatingInsights?.ratingDistribution.find((d) => d.stars === stars)?.count ?? 0;
  }

  ratingPercent(stars: number): number {
    return this.insights?.reviewRatingInsights?.ratingDistribution.find((d) => d.stars === stars)?.percent ?? 0;
  }

  formatCurrency(value: number | null | undefined): string {
    return new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR', maximumFractionDigits: 2 }).format(value ?? 0);
  }

  formatPercent(value: number | null | undefined): string {
    return `${(value ?? 0).toFixed(2)}%`;
  }

  compactCurrency(value: number): string {
    const abs = Math.abs(value);
    const sign = value < 0 ? '-' : '';
    if (abs >= 100000) {
      return `${sign}₹${(abs / 100000).toFixed(1)}L`;
    }
    if (abs >= 1000) {
      return `${sign}₹${(abs / 1000).toFixed(1)}K`;
    }
    return `${sign}₹${abs.toFixed(0)}`;
  }

  dateOnly(value: string | null | undefined): string {
    return value ? value.slice(0, 10) : '';
  }

  shortDate(value: string | null | undefined): string {
    const d = this.toDate(value);
    return d ? d.toLocaleDateString('en-IN', { day: '2-digit', month: 'short' }) : '';
  }

  longDate(value: string | null | undefined): string {
    const d = this.toDate(value);
    return d ? d.toLocaleDateString('en-IN', { day: '2-digit', month: 'short', year: 'numeric' }) : '';
  }

  private weekday(value: string): string {
    const d = this.toDate(value);
    return d ? d.toLocaleDateString('en-IN', { weekday: 'short' }) : '';
  }

  private toDate(value: string | null | undefined): Date | null {
    const day = this.dateOnly(value);
    if (!day) {
      return null;
    }
    const d = new Date(`${day}T00:00:00`);
    return isNaN(d.getTime()) ? null : d;
  }

  private hourLabel(hour: number): string {
    const h = hour % 24;
    const suffix = h < 12 ? 'a' : 'p';
    const display = h % 12 === 0 ? 12 : h % 12;
    return `${display}${suffix}`;
  }
}
