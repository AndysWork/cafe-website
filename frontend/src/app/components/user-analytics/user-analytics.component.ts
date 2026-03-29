import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { UIStore } from '../../store/ui.store';
import {
  UserAnalyticsService,
  AnalyticsDashboard,
  RecentSessionInfo
} from '../../services/user-analytics.service';
import { interval, Subscription } from 'rxjs';

@Component({
  selector: 'app-user-analytics',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './user-analytics.component.html',
  styleUrls: ['./user-analytics.component.scss']
})
export class UserAnalyticsComponent implements OnInit, OnDestroy {
  private uiStore = inject(UIStore);
  dashboard: AnalyticsDashboard | null = null;
  isLoading = true;
  errorMessage = '';
  activeTab = 'overview';
  selectedPeriod = '';

  periodOptions = [
    { value: '', label: 'All Time' },
    { value: 'daily', label: 'Today' },
    { value: 'weekly', label: 'This Week' },
    { value: 'monthly', label: 'This Month' },
    { value: 'yearly', label: 'This Year' }
  ];

  // For auto-refresh
  private refreshSub?: Subscription;
  lastRefreshed: Date | null = null;

  // Chart max values for bar rendering
  maxDailyUsers = 1;
  maxHourlyEvents = 1;
  maxFeatureUsage = 1;
  maxApiCalls = 1;

  constructor(private analyticsService: UserAnalyticsService) {}

  ngOnInit(): void {
    this.loadDashboard();
    // Auto-refresh every 60 seconds
    this.refreshSub = interval(60000).subscribe(() => this.loadDashboard());
  }

  ngOnDestroy(): void {
    this.refreshSub?.unsubscribe();
  }

  setPeriod(period: string): void {
    this.selectedPeriod = period;
    this.isLoading = true;
    this.loadDashboard();
  }

  loadDashboard(): void {
    this.analyticsService.getDashboard(this.selectedPeriod || undefined).subscribe({
      next: (data) => {
        this.dashboard = data;
        this.isLoading = false;
        this.lastRefreshed = new Date();
        this.calculateChartMaxes();
      },
      error: (err) => {
        this.isLoading = false;
        this.errorMessage = 'Failed to load analytics data';
        console.error('Analytics error:', err);
      }
    });
  }

  private calculateChartMaxes(): void {
    if (!this.dashboard) return;
    this.maxDailyUsers = Math.max(1, ...this.dashboard.dailyActiveUsers.map(d => d.activeUsers));
    this.maxHourlyEvents = Math.max(1, ...this.dashboard.hourlyActivity.map(h => h.eventCount));
    this.maxFeatureUsage = Math.max(1, ...this.dashboard.topFeatures.map(f => f.usageCount));
    this.maxApiCalls = Math.max(1, ...this.dashboard.apiPerformance.map(a => a.totalCalls));
  }

  setTab(tab: string): void {
    this.activeTab = tab;
  }

  refreshData(): void {
    this.isLoading = true;
    this.loadDashboard();
  }

  initializeIndexes(): void {
    this.analyticsService.initIndexes().subscribe({
      next: () => this.uiStore.success('Analytics indexes created successfully!'),
      error: () => this.uiStore.error('Failed to create indexes')
    });
  }

  // ─── Helper methods for template ───

  getBarWidth(value: number, max: number): string {
    return Math.max(2, (value / max) * 100) + '%';
  }

  getResponseTimeColor(avgMs: number): string {
    if (avgMs < 200) return '#10b981';
    if (avgMs < 500) return '#f59e0b';
    if (avgMs < 1000) return '#f97316';
    return '#ef4444';
  }

  getResponseTimeLabel(avgMs: number): string {
    if (avgMs < 200) return 'Fast';
    if (avgMs < 500) return 'Normal';
    if (avgMs < 1000) return 'Slow';
    return 'Critical';
  }

  getSessionDuration(session: RecentSessionInfo): string {
    const start = new Date(session.loginTime);
    const end = session.logoutTime ? new Date(session.logoutTime) : new Date(session.lastActiveTime);
    const diffMs = end.getTime() - start.getTime();
    const minutes = Math.floor(diffMs / 60000);
    if (minutes < 60) return `${minutes}m`;
    const hours = Math.floor(minutes / 60);
    return `${hours}h ${minutes % 60}m`;
  }

  formatTime(dateStr: string): string {
    return new Date(dateStr).toLocaleString('en-IN', {
      day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit'
    });
  }

  formatHour(hour: number): string {
    if (hour === 0) return '12 AM';
    if (hour < 12) return `${hour} AM`;
    if (hour === 12) return '12 PM';
    return `${hour - 12} PM`;
  }

  getErrorRate(stat: { totalCalls: number; errorCount: number }): string {
    if (stat.totalCalls === 0) return '0%';
    return ((stat.errorCount / stat.totalCalls) * 100).toFixed(1) + '%';
  }

  trackByIndex(index: number): number { return index; }
}
