import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { DeliveryPartnerService, PartnerDashboard, PartnerPayoutSummary } from '../../services/delivery-partner.service';
import { WebPushService } from '../../services/web-push.service';
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
  private webPush = inject(WebPushService);
  private uiStore = inject(UIStore);
  private route = inject(ActivatedRoute);
  private router = inject(Router);

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
  codAmountDraft: Record<string, string> = {};
  codReferenceDraft: Record<string, string> = {};
  confirmingCod: Record<string, boolean> = {};
  acceptingRequest: Record<string, boolean> = {};
  acceptingParcel: Record<string, boolean> = {};
  completingParcel: Record<string, boolean> = {};
  autoAcceptParcelTaskId: string | null = null;

  ngOnInit(): void {
    this.webPush.registerPartnerWebPush('partner-dashboard').catch(() => {
      // Keep dashboard functional even if push registration fails.
    });

    const action = (this.route.snapshot.queryParamMap.get('action') || '').toLowerCase();
    const taskId = this.route.snapshot.queryParamMap.get('parcelTaskId');
    if (action === 'accept' && taskId) {
      this.autoAcceptParcelTaskId = taskId;
    }

    this.loadDashboard();
  }

  loadDashboard(): void {
    this.loading = true;
    this.partnerService.getPartnerDashboard().subscribe({
      next: data => {
        this.dashboard = data;
        this.initializeCodDrafts();
        this.tryAutoAcceptParcelTask();
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

  private initializeCodDrafts(): void {
    this.codAmountDraft = {};
    this.codReferenceDraft = {};
    for (const order of this.dashboard?.activeOrders || []) {
      if (order.id) {
        this.codAmountDraft[order.id] = String(order.total || 0);
        this.codReferenceDraft[order.id] = '';
      }
    }
  }

  canConfirmCod(order: { paymentMethod?: string; paymentStatus?: string; status: string; id?: string }): boolean {
    return !!order.id && order.paymentMethod === 'cod' && order.paymentStatus === 'pending' && order.status === 'out-for-delivery';
  }

  confirmCod(order: { id?: string; total: number }): void {
    if (!order.id) {
      this.uiStore.error('Invalid order');
      return;
    }

    const amount = Number(this.codAmountDraft[order.id]);
    if (!Number.isFinite(amount) || amount < 0) {
      this.uiStore.error('Enter valid COD amount');
      return;
    }

    this.confirmingCod[order.id] = true;
    this.partnerService.confirmMyCodCollection(order.id, {
      amount,
      collectionReference: this.codReferenceDraft[order.id]?.trim() || undefined,
      notes: 'Partner self-confirmed COD payment'
    }).subscribe({
      next: () => {
        this.uiStore.success('COD payment confirmed');
        this.confirmingCod[order.id!] = false;
        this.loadDashboard();
      },
      error: (error) => {
        this.confirmingCod[order.id!] = false;
        this.uiStore.error(error.error?.error || 'Failed to confirm COD');
      }
    });
  }

  canPickup(order: { id?: string; status: string; deliveryPartnerId?: string }): boolean {
    return !!order.id && order.status === 'ready' && !!order.deliveryPartnerId;
  }

  pickupOrder(order: { id?: string }): void {
    if (!order.id) {
      this.uiStore.error('Invalid order');
      return;
    }

    this.confirmingCod[order.id] = true;
    this.partnerService.pickupAssignedOrder(order.id).subscribe({
      next: () => {
        this.uiStore.success('Order picked up and moved to out-for-delivery');
        this.confirmingCod[order.id!] = false;
        this.loadDashboard();
      },
      error: (error) => {
        this.confirmingCod[order.id!] = false;
        this.uiStore.error(error.error?.error || 'Failed to pickup order');
      }
    });
  }

  acceptRequest(order: { id?: string }): void {
    if (!order.id) {
      this.uiStore.error('Invalid order');
      return;
    }

    this.acceptingRequest[order.id] = true;
    this.partnerService.acceptDeliveryOrder(order.id).subscribe({
      next: () => {
        this.uiStore.success('Order accepted and assigned to you');
        this.acceptingRequest[order.id!] = false;
        this.loadDashboard();
      },
      error: (error) => {
        this.acceptingRequest[order.id!] = false;
        this.uiStore.error(error.error?.error || 'Failed to accept order');
        this.loadDashboard();
      }
    });
  }

  acceptParcelTask(task: { id?: string }): void {
    if (!task.id) {
      this.uiStore.error('Invalid parcel task');
      return;
    }

    this.acceptingParcel[task.id] = true;
    this.partnerService.acceptParcelTask(task.id).subscribe({
      next: () => {
        this.uiStore.success('Parcel task accepted');
        this.acceptingParcel[task.id!] = false;
        this.loadDashboard();
      },
      error: (error) => {
        this.acceptingParcel[task.id!] = false;
        this.uiStore.error(error.error?.error || 'Failed to accept parcel task');
      }
    });
  }

  completeParcelTask(task: { id?: string }): void {
    if (!task.id) {
      this.uiStore.error('Invalid parcel task');
      return;
    }

    this.completingParcel[task.id] = true;
    this.partnerService.completeParcelTask(task.id).subscribe({
      next: (res) => {
        this.uiStore.success(`Parcel task completed (${res.tripDistanceKm} km logged)`);
        this.completingParcel[task.id!] = false;
        this.loadDashboard();
      },
      error: (error) => {
        this.completingParcel[task.id!] = false;
        this.uiStore.error(error.error?.error || 'Failed to complete parcel task');
      }
    });
  }

  private tryAutoAcceptParcelTask(): void {
    if (!this.autoAcceptParcelTaskId || !this.dashboard) {
      return;
    }

    const match = this.dashboard.pendingParcelTasks.find(t => t.id === this.autoAcceptParcelTaskId);
    const taskId = this.autoAcceptParcelTaskId;
    this.autoAcceptParcelTaskId = null;

    if (!match) {
      return;
    }

    this.acceptParcelTask(match);
    this.router.navigate([], {
      queryParams: { action: null, parcelTaskId: null },
      queryParamsHandling: 'merge'
    });

    this.uiStore.success(`Opening task ${taskId} from notification`);
  }

  getStarLabel(rating: number): string {
    const full = Math.max(0, Math.min(5, Math.round(rating)));
    return `${'★'.repeat(full)}${'☆'.repeat(5 - full)}`;
  }

  formatStatus(status: string): string {
    return status.replace('-', ' ').replace(/\b\w/g, c => c.toUpperCase());
  }
}
