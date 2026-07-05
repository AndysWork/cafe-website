import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { KitchenDisplayService, KitchenStaffDashboard } from '../../services/kitchen-display.service';
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
    private uiStore: UIStore
  ) {}

  ngOnInit(): void {
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
}
