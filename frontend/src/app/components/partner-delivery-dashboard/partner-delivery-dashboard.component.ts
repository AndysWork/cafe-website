import { Component, OnInit, OnDestroy, HostListener, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { DeliveryPartnerService, PartnerDashboard } from '../../services/delivery-partner.service';
import { WebPushService } from '../../services/web-push.service';
import { UIStore } from '../../store/ui.store';
import { Subscription, interval } from 'rxjs';
import { getIstHour, getStoredTimestampEpoch } from '../../utils/date-utils';

@Component({
  selector: 'app-partner-delivery-dashboard',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
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
  statusOrderId: string | null = null;
  codOrderId: string | null = null;
  infoModal: 'performance' | 'incentives' | 'zones' | 'requests' | 'reviews' | null = null;
  errorMessage: string | null = null;
  private dashboardRequest = 0;
  loading = true;
  availabilityChoice = false;
  updatingAvailability = false;
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
    this.dashboardRequest++;
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
    if (this.updatingAvailability) return;
    const request = ++this.dashboardRequest;
    this.loading = !this.dashboard;
    this.partnerService.getPartnerDashboard().subscribe({
      next: data => {
        if (request !== this.dashboardRequest) return;
        const pendingRequestIds = new Set((data.pendingRequests || []).map(r => r.id).filter((id): id is string => !!id));
        const hasNewPendingRequest = this.hasHydratedPendingRequests
          && Array.from(pendingRequestIds).some(id => !this.knownPendingRequestIds.has(id));

        this.dashboard = data;
        this.availabilityChoice = data.profile?.availability?.isOnline === true;
        this.errorMessage = null;
        this.knownPendingRequestIds = pendingRequestIds;
        if (this.hasHydratedPendingRequests && hasNewPendingRequest) {
          this.playIncomingRequestAlert();
        }
        this.hasHydratedPendingRequests = true;
        this.initializeCodDrafts();
        this.tryAutoAcceptParcelTask();
        this.loading = false;
      },
      error: () => {
        if (request !== this.dashboardRequest) return;
        this.errorMessage = this.dashboard
          ? 'Could not refresh delivery data. Showing the last successful update.'
          : 'Could not load your delivery dashboard. Please try again.';
        this.uiStore.error('Failed to load partner dashboard');
        this.loading = false;
      }
    });
  }

  private initializeCodDrafts(): void {
    const amounts: Record<string, string> = {};
    const references: Record<string, string> = {};
    for (const order of this.dashboard?.activeOrders || []) {
      if (order.id) {
        amounts[order.id] = this.codAmountDraft[order.id] ?? String(order.total);
        references[order.id] = this.codReferenceDraft[order.id] ?? '';
      }
    }
    this.codAmountDraft = amounts;
    this.codReferenceDraft = references;
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
    return !!order.id && order.status === 'ready' && !!order.deliveryPartnerId
      && (!this.dashboard?.profile?.currentOrderId || this.dashboard.profile.currentOrderId === order.id);
  }

  isAssignedTimedOrder(order: { orderSource?: string; requestedDeliveryAtUtc?: string; deliveryPartnerId?: string }): boolean {
    return order.orderSource === 'phone' && !!order.requestedDeliveryAtUtc
      && !!order.deliveryPartnerId && order.deliveryPartnerId === this.dashboard?.profile?.id;
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
        this.uiStore.error(error.message || error.error?.error || 'Failed to pickup order');
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

  acceptRequest(order: { id?: string; orderSource?: string }): void {
    if (this.isPhoneManagedRequest(order)) {
      this.uiStore.warning('Phone delivery orders are assigned by admin/manager. Wait for assignment.');
      return;
    }
    if (!this.isOnline || this.updatingAvailability) {
      this.uiStore.warning('Go Online during shop hours to accept new orders.');
      return;
    }

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
    if (!this.isOnline || this.updatingAvailability) {
      this.uiStore.warning('Go Online during shop hours to accept new parcel tasks.');
      return;
    }
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

  get isOnline(): boolean {
    return this.dashboard?.profile?.availability?.isOnline === true;
  }

  get greeting(): string {
    const hour = getIstHour(Date.now());
    return hour < 12 ? 'Good Morning' : hour < 17 ? 'Good Afternoon' : 'Good Evening';
  }

  get incentiveProgress(): number {
    const incentive = this.dashboard?.incentive;
    return incentive && incentive.targetDeliveries > 0
      ? Math.max(0, Math.min(100, 100 * incentive.completedDeliveries / incentive.targetDeliveries)) : 0;
  }

  setAvailability(online: boolean): void {
    if (this.updatingAvailability || online === this.isOnline) return;
    if (!this.dashboard?.profile?.availability?.shopIsOpen) {
      this.availabilityChoice = this.isOnline;
      this.uiStore.warning(this.dashboard?.profile?.availability?.message || 'Availability is unavailable. Refresh your dashboard.');
      return;
    }
    this.updatingAvailability = true;
    ++this.dashboardRequest;
    this.partnerService.updateMyAvailability(online).subscribe({
      next: response => {
        if (this.dashboard?.profile) this.dashboard.profile.availability = response.availability;
        this.availabilityChoice = response.availability.isOnline;
        this.updatingAvailability = false;
        this.uiStore.success(response.message);
        this.loadDashboard();
      },
      error: (error: Error) => {
        this.availabilityChoice = this.isOnline;
        this.updatingAvailability = false;
        this.uiStore.error(error.message || 'Could not update availability. Please try again.');
      }
    });
  }

  @HostListener('document:keydown.escape')
  closeOverlays(): void {
    this.infoModal = null;
    this.statusOrderId = null;
    this.codOrderId = null;
  }

  displayOrderId(id?: string): string {
    return id ? (id.length > 12 ? id.slice(-6).toUpperCase() : id) : 'Unavailable';
  }

  trackOrder(index: number, order: { id?: string }): string | number {
    return order.id || index;
  }

  platformLabel(order: { orderSource?: string; channel?: string }): string {
    const source = order.orderSource?.toLowerCase();
    if (source === 'phone') return 'Phone';
    if (source === 'zomato') return 'Zomato';
    if (source === 'swiggy') return 'Swiggy';
    return order.channel === 'partner' ? 'Partner' : 'Website';
  }

  phoneHref(phone?: string): string | null {
    const number = phone?.replace(/[^\d+]/g, '');
    return number && /\d{7,}/.test(number.replace(/\+/g, '')) ? `tel:${number}` : null;
  }

  deliveryDeadline(order: PartnerDashboard['activeOrders'][number]): Date | null {
    if (order.requestedDeliveryAtUtc) {
      const timestamp = getStoredTimestampEpoch(order.requestedDeliveryAtUtc, true);
      return timestamp ? new Date(timestamp) : null;
    }
    if (order.deliveryDueAtUtc) {
      const timestamp = getStoredTimestampEpoch(order.deliveryDueAtUtc, true);
      return timestamp ? new Date(timestamp) : null;
    }
    const routeTime = getStoredTimestampEpoch(order.deliveryRouteUpdatedAt);
    return routeTime && order.deliveryEtaMinutes && order.deliveryEtaMinutes > 0
      ? new Date(routeTime + order.deliveryEtaMinutes * 60000) : null;
  }

  hourLabel(hour: number): string {
    return `${hour % 12 || 12}:00 ${hour < 12 ? 'AM' : 'PM'}`;
  }

  openCodForm(orderId?: string): void {
    if (!orderId) {
      this.uiStore.error('Invalid order');
      return;
    }
    this.statusOrderId = null;
    this.codOrderId = orderId;
  }

  formatStatus(status: string): string {
    return status.replace('-', ' ').replace(/\b\w/g, c => c.toUpperCase());
  }

  isPhoneOrder(order: { orderSource?: string }): boolean {
    return (order.orderSource || '').toLowerCase() === 'phone';
  }

  getWorkflowSteps(order: {
    status: string;
    paymentMethod?: string;
    paymentStatus?: string;
  }): Array<{ label: string; completed: boolean; active: boolean }> {
    const status = (order.status || '').toLowerCase();
    const isCod = (order.paymentMethod || '').toLowerCase() === 'cod';
    const isPaid = (order.paymentStatus || '').toLowerCase() === 'paid';

    const steps = [
      { label: 'Assigned', completed: true, active: false },
      { label: 'Picked up', completed: status === 'out-for-delivery' || status === 'delivered', active: false },
      { label: isCod ? 'COD collected' : 'Payment confirmed', completed: !isCod || isPaid, active: false },
      { label: 'Delivered', completed: status === 'delivered', active: false }
    ];

    const activeIndex = steps.findIndex(step => !step.completed);
    if (activeIndex >= 0) {
      steps[activeIndex].active = true;
    }

    return steps;
  }

  isPhoneManagedRequest(order: unknown): boolean {
    const source = typeof order === 'object' && order !== null && 'orderSource' in order
      ? String((order as { orderSource?: string }).orderSource || '')
      : '';
    return source.toLowerCase() === 'phone';
  }

  getDeliveryMapUrl(order: { deliveryRouteShortUrl?: string; deliveryRouteUrl?: string; deliveryAddress?: string; deliveryLocationUrl?: string }): string | null {
    if (order.deliveryLocationUrl) return order.deliveryLocationUrl;
    if (order.deliveryRouteShortUrl) return order.deliveryRouteShortUrl;
    if (order.deliveryRouteUrl) return order.deliveryRouteUrl;

    const address = (order.deliveryAddress || '').trim();
    if (!address) return null;

    return `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(address)}`;
  }
}
