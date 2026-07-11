import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import { RouterModule } from '@angular/router';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { AttendanceService } from '../../services/attendance.service';
import { DeliveryPartnerService } from '../../services/delivery-partner.service';
import { KitchenDisplayService } from '../../services/kitchen-display.service';

@Component({
  selector: 'app-manager-dashboard',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './manager-dashboard.component.html',
  styleUrls: ['./manager-dashboard.component.scss']
})
export class ManagerDashboardComponent implements OnInit, OnDestroy {
  private kitchenService = inject(KitchenDisplayService);
  private deliveryPartnerService = inject(DeliveryPartnerService);
  private attendanceService = inject(AttendanceService);

  loading = true;
  private refreshHandle: any = null;
  metrics = {
    kitchenActiveOrders: 0,
    kitchenReadyOrders: 0,
    deliveryPartnersOnDuty: 0,
    deliveryPartnersAvailable: 0,
    attendancePresentToday: 0,
    attendanceInShiftNow: 0
  };

  cards = [
    {
      title: 'Kitchen Display',
      description: 'Monitor and update live order preparation flow.',
      icon: '🍳',
      route: '/kitchen/display',
      cta: 'Open Kitchen'
    },
    {
      title: 'Delivery Tracking',
      description: 'Track active delivery orders and partner statuses.',
      icon: '🛵',
      route: '/manager/operations',
      cta: 'Open Delivery'
    },
    {
      title: 'Assign Parcel Task',
      description: 'Assign point-to-point parcel trips with route distance and round-trip support.',
      icon: '🧳',
      route: '/manager/operations',
      cta: 'Assign Parcel'
    },
    {
      title: 'My Attendance',
      description: 'Clock shifts and review attendance summary.',
      icon: '🕐',
      route: '/staff/attendance',
      cta: 'Open Attendance'
    },
    {
      title: 'My Payslip',
      description: 'View monthly estimate, history, and download PDF.',
      icon: '💸',
      route: '/staff/payslip',
      cta: 'Open Payslip'
    }
  ];

  ngOnInit(): void {
    this.loadMetrics();
    this.startAutoRefresh();
    document.addEventListener('visibilitychange', this.onVisibilityChange);
  }

  ngOnDestroy(): void {
    this.stopAutoRefresh();
    document.removeEventListener('visibilitychange', this.onVisibilityChange);
  }

  loadMetrics(): void {
    this.loading = true;

    forkJoin({
      kitchenOrders: this.kitchenService.getKitchenOrders().pipe(catchError(() => of([]))),
      partners: this.deliveryPartnerService.getDeliveryPartners().pipe(catchError(() => of([]))),
      attendance: this.attendanceService.getTodayAttendance().pipe(catchError(() => of([])))
    }).subscribe({
      next: ({ kitchenOrders, partners, attendance }) => {
        this.metrics.kitchenActiveOrders = (kitchenOrders || []).filter(
          o => o.status === 'pending' || o.status === 'preparing' || o.status === 'ready'
        ).length;
        this.metrics.kitchenReadyOrders = (kitchenOrders || []).filter(o => o.status === 'ready').length;

        this.metrics.deliveryPartnersOnDuty = (partners || []).filter(
          p => p.status === 'on-delivery'
        ).length;
        this.metrics.deliveryPartnersAvailable = (partners || []).filter(
          p => p.status === 'available'
        ).length;

        this.metrics.attendancePresentToday = (attendance || []).filter(a => !!a.clockIn).length;
        this.metrics.attendanceInShiftNow = (attendance || []).filter(a => !!a.clockIn && !a.clockOut).length;
        this.loading = false;
      },
      error: () => {
        this.loading = false;
      }
    });
  }

  private startAutoRefresh(): void {
    if (this.refreshHandle) {
      return;
    }

    this.refreshHandle = setInterval(() => {
      if (!document.hidden) {
        this.loadMetrics();
      }
    }, 30000);
  }

  private stopAutoRefresh(): void {
    if (this.refreshHandle) {
      clearInterval(this.refreshHandle);
      this.refreshHandle = null;
    }
  }

  private readonly onVisibilityChange = () => {
    if (!document.hidden) {
      this.loadMetrics();
      this.startAutoRefresh();
      return;
    }

    this.stopAutoRefresh();
  };
}
