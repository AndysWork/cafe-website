import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { KitchenDisplayService, KitchenStaffDashboard } from '../../services/kitchen-display.service';
import { WebPushService } from '../../services/web-push.service';
import { UIStore } from '../../store/ui.store';

@Component({
  selector: 'app-kitchen-staff-dashboard',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './kitchen-staff-dashboard.component.html',
  styleUrls: ['./kitchen-staff-dashboard.component.scss']
})
export class KitchenStaffDashboardComponent implements OnInit {
  period: 'day' | 'week' | 'month' | 'year' = 'day';
  dashboard: KitchenStaffDashboard | null = null;
  loading = true;

  constructor(
    private kitchenService: KitchenDisplayService,
    private uiStore: UIStore,
    private webPush: WebPushService
  ) {}

  ngOnInit(): void {
    this.webPush.registerKitchenWebPush('kitchen-dashboard').catch(() => {
      // Keep dashboard functional even if push setup fails.
    });
    this.loadDashboard();
  }

  setPeriod(period: 'day' | 'week' | 'month' | 'year'): void {
    this.period = period;
    this.loadDashboard();
  }

  loadDashboard(): void {
    this.loading = true;
    this.kitchenService.getKitchenStaffDashboard(this.period).subscribe({
      next: (data) => {
        this.dashboard = data;
        this.loading = false;
      },
      error: () => {
        this.loading = false;
        this.uiStore.error('Unable to load dashboard');
      }
    });
  }

  shiftIn(): void {
    this.kitchenService.shiftIn().subscribe({
      next: () => {
        this.uiStore.success('Shift started');
        this.loadDashboard();
      },
      error: () => this.uiStore.error('Failed to start shift')
    });
  }

  shiftOut(): void {
    this.kitchenService.shiftOut().subscribe({
      next: () => {
        this.uiStore.success('Shift ended');
        this.loadDashboard();
      },
      error: () => this.uiStore.error('Failed to end shift')
    });
  }

  markAttendance(): void {
    this.kitchenService.markAttendance().subscribe({
      next: () => {
        this.uiStore.success('Attendance marked');
        this.loadDashboard();
      },
      error: () => this.uiStore.error('Failed to mark attendance')
    });
  }

  get performance() {
    return this.dashboard?.kitchenPerformance ?? {
      totalOrdersPrepared: 0,
      goodOrdersPrepared: 0,
      badOrdersPrepared: 0,
      avgKitchenPreparationTimeMinutes: 0
    };
  }

  get goodOrderRate(): number {
    const perf = this.performance;
    if (!perf || perf.totalOrdersPrepared <= 0) {
      return 0;
    }

    return (perf.goodOrdersPrepared / perf.totalOrdersPrepared) * 100;
  }

  get badOrderRate(): number {
    const perf = this.performance;
    if (!perf || perf.totalOrdersPrepared <= 0) {
      return 0;
    }

    return (perf.badOrdersPrepared / perf.totalOrdersPrepared) * 100;
  }
}
