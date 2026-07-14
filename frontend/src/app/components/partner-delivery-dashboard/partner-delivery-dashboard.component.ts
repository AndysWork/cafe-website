import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { DeliveryPartnerService, PartnerDashboard, PartnerPayoutSummary } from '../../services/delivery-partner.service';
import { WebPushService } from '../../services/web-push.service';
import { UIStore } from '../../store/ui.store';
import { Subscription, interval } from 'rxjs';

@Component({
  selector: 'app-partner-delivery-dashboard',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './partner-delivery-dashboard.component.html',
  styleUrls: ['./partner-delivery-dashboard.component.scss']
})
export class PartnerDeliveryDashboardComponent implements OnInit, OnDestroy {
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
  markingDelivered: Record<string, boolean> = {};
  autoAcceptParcelTaskId: string | null = null;
  private pollSub?: Subscription;
  private knownPendingRequestIds = new Set<string>();
  private hasHydratedPendingRequests = false;
  private audioContext: AudioContext | null = null;
  private audioUnlocked = false;
  private readonly unlockAudioHandler = () => this.unlockAudio();

  ngOnInit(): void {
    this.registerAudioUnlockListeners();

    this.webPush.registerPartnerWebPush('partner-dashboard').catch(() => {
      // Keep dashboard functional even if push registration fails.
    });

    const action = (this.route.snapshot.queryParamMap.get('action') || '').toLowerCase();
    const taskId = this.route.snapshot.queryParamMap.get('parcelTaskId');
    if (action === 'accept' && taskId) {
      this.autoAcceptParcelTaskId = taskId;
    }

    this.loadDashboard();
    this.pollSub = interval(15000).subscribe(() => this.loadDashboard());
  }

  ngOnDestroy(): void {
    this.pollSub?.unsubscribe();
    this.unregisterAudioUnlockListeners();
    if (this.audioContext && this.audioContext.state !== 'closed') {
      this.audioContext.close().catch(() => {
        // Ignore teardown errors.
      });
    }
  }

  private registerAudioUnlockListeners(): void {
    if (typeof window === 'undefined') {
      return;
    }

    window.addEventListener('pointerdown', this.unlockAudioHandler, { passive: true });
    window.addEventListener('keydown', this.unlockAudioHandler);
    window.addEventListener('touchstart', this.unlockAudioHandler, { passive: true });
  }

  private unregisterAudioUnlockListeners(): void {
    if (typeof window === 'undefined') {
      return;
    }

    window.removeEventListener('pointerdown', this.unlockAudioHandler);
    window.removeEventListener('keydown', this.unlockAudioHandler);
    window.removeEventListener('touchstart', this.unlockAudioHandler);
  }

  private unlockAudio(): void {
    if (this.audioUnlocked) {
      return;
    }

    const AudioContextCtor = typeof window !== 'undefined'
      ? (window.AudioContext || (window as Window & { webkitAudioContext?: typeof AudioContext }).webkitAudioContext)
      : undefined;

    if (!AudioContextCtor) {
      return;
    }

    if (!this.audioContext) {
      this.audioContext = new AudioContextCtor();
    }

    this.audioContext.resume()
      .then(() => {
        this.audioUnlocked = this.audioContext?.state === 'running';
        if (this.audioUnlocked) {
          this.unregisterAudioUnlockListeners();
        }
      })
      .catch(() => {
        // Browser may block audio until an allowed interaction.
      });
  }

  private playIncomingRequestAlert(): void {
    if (!this.audioContext || this.audioContext.state !== 'running') {
      return;
    }

    const now = this.audioContext.currentTime;
    const pattern = [0, 0.16, 0.32];

    for (const offset of pattern) {
      const oscillator = this.audioContext.createOscillator();
      const gain = this.audioContext.createGain();

      oscillator.type = 'triangle';
      oscillator.frequency.setValueAtTime(860, now + offset);

      gain.gain.setValueAtTime(0.0001, now + offset);
      gain.gain.exponentialRampToValueAtTime(0.2, now + offset + 0.02);
      gain.gain.exponentialRampToValueAtTime(0.0001, now + offset + 0.12);

      oscillator.connect(gain);
      gain.connect(this.audioContext.destination);

      oscillator.start(now + offset);
      oscillator.stop(now + offset + 0.13);
    }
  }

  loadDashboard(): void {
    this.loading = true;
    this.partnerService.getPartnerDashboard().subscribe({
      next: data => {
        const pendingRequestIds = new Set((data.pendingRequests || []).map(r => r.id).filter((id): id is string => !!id));
        const hasNewPendingRequest = this.hasHydratedPendingRequests
          && Array.from(pendingRequestIds).some(id => !this.knownPendingRequestIds.has(id));

        this.dashboard = data;
        this.knownPendingRequestIds = pendingRequestIds;
        if (this.hasHydratedPendingRequests && hasNewPendingRequest) {
          this.playIncomingRequestAlert();
        }
        this.hasHydratedPendingRequests = true;
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

  canMarkDelivered(order: { id?: string; status: string; paymentMethod?: string; paymentStatus?: string }): boolean {
    if (!order.id || order.status !== 'out-for-delivery') {
      return false;
    }

    const isCod = order.paymentMethod === 'cod';
    return !isCod || order.paymentStatus === 'paid';
  }

  markDelivered(order: { id?: string }): void {
    if (!order.id) {
      this.uiStore.error('Invalid order');
      return;
    }

    this.markingDelivered[order.id] = true;
    this.partnerService.markOrderDelivered(order.id).subscribe({
      next: () => {
        this.uiStore.success('Order marked delivered');
        this.markingDelivered[order.id!] = false;
        this.loadDashboard();
      },
      error: (error) => {
        this.markingDelivered[order.id!] = false;
        this.uiStore.error(error.error?.error || 'Failed to mark order delivered');
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

  getDeliveryMapUrl(order: { deliveryRouteShortUrl?: string; deliveryRouteUrl?: string; deliveryAddress?: string }): string | null {
    if (order.deliveryRouteShortUrl) return order.deliveryRouteShortUrl;
    if (order.deliveryRouteUrl) return order.deliveryRouteUrl;

    const address = (order.deliveryAddress || '').trim();
    if (!address) return null;

    return `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(address)}`;
  }
}
