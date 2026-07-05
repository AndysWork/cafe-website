import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DeliveryPartnerService, PartnerDashboard, PartnerPayoutSummary } from '../../services/delivery-partner.service';
import { UIStore } from '../../store/ui.store';

@Component({
  selector: 'app-partner-delivery-dashboard',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './partner-delivery-dashboard.component.html',
  styleUrls: ['./partner-delivery-dashboard.component.scss']
})
export class PartnerDeliveryDashboardComponent implements OnInit {
  private partnerService = inject(DeliveryPartnerService);
  private uiStore = inject(UIStore);

  dashboard: PartnerDashboard | null = null;
  payoutSummary: PartnerPayoutSummary | null = null;
  loading = true;
  periodType: 'day' | 'week' | 'month' | 'year' = 'day';
  shiftForm = {
    startOdometerKm: '',
    endOdometerKm: '',
    notes: ''
  };
  tripForm = {
    shiftId: '',
    tripType: 'delivery',
    orderId: '',
    startOdometerKm: '',
    endOdometerKm: '',
    startPointLabel: '',
    endPointLabel: '',
    notes: ''
  };
  submittingShiftStart = false;
  submittingShiftEnd = false;
  submittingTrip = false;

  ngOnInit(): void {
    this.loadDashboard();
  }

  loadDashboard(): void {
    this.loading = true;
    this.partnerService.getPartnerDashboard().subscribe({
      next: data => {
        this.dashboard = data;
        this.loading = false;
        this.loadPayout();
      },
      error: () => {
        this.uiStore.error('Failed to load partner dashboard');
        this.loading = false;
      }
    });
  }

  loadPayout(): void {
    this.partnerService.getMyPayoutSummary(this.periodType).subscribe({
      next: payout => {
        this.payoutSummary = payout;
      },
      error: () => {
        this.uiStore.error('Failed to load payout summary');
      }
    });
  }

  startShift(): void {
    const startOdometerKm = Number(this.shiftForm.startOdometerKm);
    if (!Number.isFinite(startOdometerKm) || startOdometerKm < 0) {
      this.uiStore.error('Enter valid start odometer');
      return;
    }

    this.submittingShiftStart = true;
    this.partnerService.startMyShift({
      startOdometerKm,
      notes: this.shiftForm.notes.trim() || undefined
    }).subscribe({
      next: shift => {
        this.uiStore.success('Shift started');
        this.dashboard = this.dashboard
          ? { ...this.dashboard, activeShift: shift }
          : this.dashboard;
        this.shiftForm.endOdometerKm = '';
        this.shiftForm.notes = '';
        this.submittingShiftStart = false;
        this.loadDashboard();
      },
      error: () => {
        this.submittingShiftStart = false;
        this.uiStore.error('Failed to start shift');
      }
    });
  }

  endShift(): void {
    const activeShiftId = this.dashboard?.activeShift?.id;
    if (!activeShiftId) {
      this.uiStore.error('No active shift found');
      return;
    }

    const endOdometerKm = Number(this.shiftForm.endOdometerKm);
    if (!Number.isFinite(endOdometerKm) || endOdometerKm < 0) {
      this.uiStore.error('Enter valid end odometer');
      return;
    }

    this.submittingShiftEnd = true;
    this.partnerService.endMyShift(activeShiftId, {
      endOdometerKm,
      notes: this.shiftForm.notes.trim() || undefined
    }).subscribe({
      next: () => {
        this.uiStore.success('Shift ended');
        this.shiftForm.startOdometerKm = '';
        this.shiftForm.endOdometerKm = '';
        this.shiftForm.notes = '';
        this.submittingShiftEnd = false;
        this.loadDashboard();
      },
      error: () => {
        this.submittingShiftEnd = false;
        this.uiStore.error('Failed to end shift');
      }
    });
  }

  logTrip(): void {
    const startOdometerKm = Number(this.tripForm.startOdometerKm);
    const endOdometerKm = Number(this.tripForm.endOdometerKm);

    if (!this.tripForm.shiftId.trim()) {
      this.uiStore.error('Shift ID is required');
      return;
    }

    if (!Number.isFinite(startOdometerKm) || !Number.isFinite(endOdometerKm) || startOdometerKm < 0 || endOdometerKm < 0) {
      this.uiStore.error('Enter valid odometer values');
      return;
    }

    this.submittingTrip = true;
    this.partnerService.createMyTrip({
      shiftId: this.tripForm.shiftId.trim(),
      tripType: this.tripForm.tripType,
      orderId: this.tripForm.orderId.trim() || undefined,
      startOdometerKm,
      endOdometerKm,
      startPointLabel: this.tripForm.startPointLabel.trim() || undefined,
      endPointLabel: this.tripForm.endPointLabel.trim() || undefined,
      notes: this.tripForm.notes.trim() || undefined
    }).subscribe({
      next: () => {
        this.uiStore.success('Trip logged');
        this.tripForm = {
          shiftId: this.dashboard?.activeShift?.id || '',
          tripType: 'delivery',
          orderId: '',
          startOdometerKm: '',
          endOdometerKm: '',
          startPointLabel: '',
          endPointLabel: '',
          notes: ''
        };
        this.submittingTrip = false;
        this.loadDashboard();
      },
      error: () => {
        this.submittingTrip = false;
        this.uiStore.error('Failed to log trip');
      }
    });
  }

  formatStatus(status: string): string {
    return status.replace('-', ' ').replace(/\b\w/g, c => c.toUpperCase());
  }
}
