import { Component, DestroyRef, effect, inject, untracked } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { Subscription } from 'rxjs';
import { CustomerJourneyService } from '../../services/customer-journey.service';
import { OutletStore } from '../../store/outlet.store';
import {
  JourneyBreakdown, JourneyDashboard, JourneyLive, JourneyMenuStat, JourneyOverview,
  JourneySessionList, JourneySessionSummary, JourneyTimeline, JourneyTimelineEvent
} from '../../models/customer-journey.model';
import { getIstInputDate } from '../../utils/date-utils';

type JourneyTab = 'overview' | 'live' | 'journey' | 'menu' | 'conversion' | 'users' | 'reports';

@Component({
  selector: 'app-customer-journey',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './customer-journey.component.html',
  styleUrl: './customer-journey.component.scss'
})
export class CustomerJourneyComponent {
  private readonly api = inject(CustomerJourneyService);
  readonly outlet = inject(OutletStore);
  private readonly destroyRef = inject(DestroyRef);
  readonly today = getIstInputDate(new Date());
  readonly earliest = this.shiftDay(this.today, -29);
  from = this.shiftDay(this.today, -6);
  to = this.today;
  appliedFrom = this.from;
  appliedTo = this.to;
  readonly tabs: { id: JourneyTab; label: string; icon: string }[] = [
    { id: 'overview', label: 'Overview', icon: '01' },
    { id: 'live', label: 'Live Visitors', icon: '02' },
    { id: 'journey', label: 'User Journey', icon: '03' },
    { id: 'menu', label: 'Menu Intelligence', icon: '04' },
    { id: 'conversion', label: 'Conversion & Abandonment', icon: '05' },
    { id: 'users', label: 'User Analytics', icon: '06' },
    { id: 'reports', label: 'Reports', icon: '07' }
  ];
  readonly segments = [
    ['customers', 'All customer sessions'], ['anonymous', 'Anonymous visitors'], ['logged-in', 'Signed-in customers'],
    ['returning', 'Returning visitors'], ['first-time', 'First-time visitors'], ['no-order', 'Visitors without orders'],
    ['cart-abandoned', 'Abandoned carts'], ['checkout-abandoned', 'Abandoned checkouts'], ['staff', 'Staff / admin'], ['all', 'All traffic']
  ];
  activeTab: JourneyTab = 'overview';
  data?: JourneyDashboard;
  live?: JourneyLive;
  sessions?: JourneySessionList;
  timeline?: JourneyTimeline;
  selectedId = '';
  loading = false;
  sessionsLoading = false;
  timelineLoading = false;
  exporting = false;
  error = '';
  liveError = '';
  sessionsError = '';
  timelineError = '';
  segment = 'customers';
  search = '';
  menuSearch = '';
  menuCategory = '';
  menuSort = 'clicks';
  interestOnly = false;
  exportReport = 'sessions';
  compareA = 'First-time visitor';
  compareB = 'Returning visitor';
  private reportRequest?: Subscription;
  private sessionRequest?: Subscription;
  private timelineRequest?: Subscription;
  private liveRequest?: Subscription;
  private exportRequest?: Subscription;

  constructor() {
    effect(() => {
      const outletId = this.outlet.selectedOutletId();
      untracked(() => {
        this.cancel();
        this.data = undefined;
        this.live = undefined;
        this.sessions = undefined;
        this.timeline = undefined;
        this.selectedId = '';
        this.error = '';
        if (outletId) this.refresh();
      });
    });
    const timer = setInterval(() => {
      if (this.outlet.selectedOutletId() && !document.hidden &&
          (this.activeTab === 'live' || this.activeTab === 'overview')) this.loadLive();
    }, 15000);
    this.destroyRef.onDestroy(() => { clearInterval(timer); this.cancel(); });
  }

  applyRange(): void {
    if (!this.from || !this.to || this.from > this.to || this.from < this.earliest || this.to > this.today) {
      this.error = 'Choose a valid date range within the last 30 calendar days.';
      return;
    }
    this.appliedFrom = this.from;
    this.appliedTo = this.to;
    this.timelineRequest?.unsubscribe();
    this.timeline = undefined;
    this.selectedId = '';
    this.refresh();
  }

  preset(days: number): void {
    this.from = this.shiftDay(this.today, -(days - 1));
    this.to = this.today;
    this.applyRange();
  }

  refresh(): void {
    if (!this.outlet.selectedOutletId()) return;
    this.reportRequest?.unsubscribe();
    this.loading = true;
    this.error = '';
    this.data = undefined;
    this.reportRequest = this.api.dashboard(this.appliedFrom, this.appliedTo).subscribe({
      next: data => { this.data = data; this.loading = false; },
      error: error => { this.error = this.message(error); this.loading = false; }
    });
    this.loadLive();
    this.loadSessions();
  }

  selectTab(tab: JourneyTab): void {
    this.activeTab = tab;
    if (tab === 'live') this.loadLive();
    if (tab === 'journey' && !this.sessions) this.loadSessions();
  }

  loadLive(): void {
    if (!this.outlet.selectedOutletId() || (this.liveRequest && !this.liveRequest.closed)) return;
    this.liveRequest = this.api.live().subscribe({
      next: live => { this.live = live; this.liveError = ''; },
      error: error => { this.liveError = this.message(error); }
    });
  }

  loadSessions(page = 1): void {
    if (!this.outlet.selectedOutletId()) return;
    this.sessionRequest?.unsubscribe();
    this.sessionsLoading = true;
    this.sessionsError = '';
    this.sessionRequest = this.api.sessions(this.appliedFrom, this.appliedTo, this.segment, this.search, page).subscribe({
      next: sessions => { this.sessions = sessions; this.sessionsLoading = false; },
      error: error => { this.sessionsError = this.message(error); this.sessionsLoading = false; this.sessions = undefined; }
    });
  }

  explore(session: JourneySessionSummary): void {
    this.activeTab = 'journey';
    this.selectedId = session.id;
    this.loadTimeline(1);
  }

  loadTimeline(page: number): void {
    if (!this.selectedId) return;
    this.timelineRequest?.unsubscribe();
    this.timelineLoading = true;
    this.timelineError = '';
    this.timeline = undefined;
    this.timelineRequest = this.api.timeline(this.selectedId, this.appliedFrom, this.appliedTo, page).subscribe({
      next: timeline => { this.timeline = timeline; this.timelineLoading = false; },
      error: error => { this.timelineError = this.message(error); this.timelineLoading = false; }
    });
  }

  showSegment(segment: string): void {
    this.segment = segment;
    this.search = '';
    this.activeTab = 'journey';
    this.loadSessions();
  }

  showAccount(id: string): void {
    this.segment = 'customers';
    this.search = id;
    this.loadSessions();
  }

  exportCsv(): void {
    this.exporting = true;
    this.exportRequest?.unsubscribe();
    this.exportRequest = this.api.export(this.appliedFrom, this.appliedTo, this.exportReport, this.segment, this.search).subscribe({
      next: blob => {
        const url = URL.createObjectURL(blob);
        const anchor = document.createElement('a');
        anchor.href = url;
        anchor.download = `cafe-journey-${this.exportReport}-${this.appliedFrom}-${this.appliedTo}.csv`;
        anchor.click();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
        this.exporting = false;
      },
      error: error => { this.error = this.message(error); this.exporting = false; }
    });
  }

  get filteredMenu(): JourneyMenuStat[] {
    const query = this.menuSearch.trim().toLowerCase();
    return (this.data?.menu ?? []).filter(m => (!query || m.name.toLowerCase().includes(query)) &&
      (!this.menuCategory || m.category === this.menuCategory) && (!this.interestOnly || m.highInterestLowConversion))
      .sort((a, b) => this.menuSort === 'quantity' ? b.quantityOrdered - a.quantityOrdered :
        this.menuSort === 'conversion' ? a.orderRate - b.orderRate : b.clicks - a.clicks);
  }
  get categories(): string[] { return [...new Set(this.data?.menu.map(m => m.category) ?? [])].sort(); }
  get liveCustomers(): JourneySessionSummary[] { return this.live?.sessions.filter(s => !s.isStaff) ?? []; }
  get liveStaff(): JourneySessionSummary[] { return this.live?.sessions.filter(s => s.isStaff) ?? []; }
  get highInterestCount(): number { return this.data?.menu.filter(m => m.highInterestLowConversion).length ?? 0; }
  get customerTypes(): JourneyBreakdown[] { return this.data?.visitorTypes.filter(t => t.name !== 'Staff / admin') ?? []; }
  get comparisonOptions(): JourneyBreakdown[] { return this.data?.userSegments ?? []; }
  get comparison(): JourneyBreakdown[] {
    return [this.compareA, this.compareB].map(name => this.comparisonOptions.find(r => r.name === name))
      .filter((r): r is JourneyBreakdown => !!r);
  }
  get donut(): string {
    const colors = ['#9b77d1', '#cab5e6', '#e7dcf3'];
    const total = this.customerTypes.reduce((sum, t) => sum + t.sessions, 0);
    if (!total) return '#f0ebf6';
    let position = 0;
    return `conic-gradient(${this.customerTypes.map((row, i) => {
      const start = position;
      position += row.sessions / total * 100;
      return `${colors[i]} ${start}% ${position}%`;
    }).join(',')})`;
  }
  get chartMax(): number { return Math.max(5, ...(this.data?.daily.map(d => d.sessions) ?? [])); }
  get chartLabels() {
    const days = this.data?.daily ?? [];
    return days.map((d, i) => ({ date: d.date, x: 24 + i / Math.max(1, days.length - 1) * 712, i }))
      .filter(d => d.i === days.length - 1 || d.i % Math.max(1, Math.ceil(days.length / 6)) === 0);
  }
  chartPoints(key: 'visitors' | 'sessions'): string {
    const days = this.data?.daily ?? [];
    return days.map((d, i) => `${24 + i / Math.max(1, days.length - 1) * 712},${192 - d[key] / this.chartMax * 162}`).join(' ');
  }
  get chartArea(): string {
    const length = this.data?.daily.length ?? 0;
    const end = length > 1 ? 736 : 24;
    return `24,192 ${this.chartPoints('visitors')} ${end},192`;
  }
  width(value: number, max: number): number { return max > 0 ? Math.min(100, value / max * 100) : 0; }
  duration(seconds: number): string {
    const value = Math.max(0, Math.round(seconds));
    return value < 60 ? `${value}s` : `${Math.floor(value / 60)}m ${value % 60}s`;
  }
  change(key: keyof JourneyOverview): string {
    const previous = this.data?.previous?.[key];
    if (previous == null || !this.data) return 'Previous period unavailable';
    if (previous === 0) return this.data.overview[key] === 0 ? 'No change from previous period' : 'New activity this period';
    const percent = (this.data.overview[key] - previous) / previous * 100;
    return `${percent >= 0 ? '+' : ''}${percent.toFixed(1)}% vs previous period`;
  }
  eventName(type: string): string {
    return type.split('_').map(word => word.charAt(0).toUpperCase() + word.slice(1)).join(' ');
  }
  eventDetail(event: JourneyTimelineEvent): string {
    if (event.type === 'page_engagement') return `${this.duration(event.activeSeconds)} actively engaged`;
    if (event.type === 'order_placed') return `${event.cancelled ? 'Cancelled / removed' : 'Backend-confirmed'} order ${event.orderId ?? ''}`;
    if (event.itemName) return `${event.itemName}${event.quantity ? ` x ${event.quantity}` : ''}`;
    if (event.category) return event.category;
    return event.type === 'heartbeat' ? 'Foreground activity signal' : event.page;
  }
  isCartEvent(type: string): boolean {
    return type.startsWith('cart_') || type.startsWith('checkout_') || type === 'order_placed';
  }
  private shiftDay(date: string, days: number): string {
    const value = new Date(`${date}T12:00:00Z`);
    value.setUTCDate(value.getUTCDate() + days);
    return value.toISOString().slice(0, 10);
  }
  private message(error: HttpErrorResponse): string {
    return typeof error.error?.error === 'string' ? error.error.error : 'Could not load journey analytics. Please retry.';
  }
  private cancel(): void {
    for (const request of [this.reportRequest, this.sessionRequest, this.timelineRequest, this.liveRequest, this.exportRequest])
      request?.unsubscribe();
    this.loading = this.sessionsLoading = this.timelineLoading = this.exporting = false;
  }
}
