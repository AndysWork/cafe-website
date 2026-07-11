import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { forkJoin } from 'rxjs';
import { DeliveryPartnerService, ParcelRouteQuote } from '../../services/delivery-partner.service';
import {
  ManagerAuditReconciliationResponse,
  ManagerOpsBoardResponse,
  ManagerOpsService
} from '../../services/manager-ops.service';

@Component({
  selector: 'app-manager-operations-board',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './manager-operations-board.component.html',
  styleUrls: ['./manager-operations-board.component.scss']
})
export class ManagerOperationsBoardComponent implements OnInit, OnDestroy {
  private managerOps = inject(ManagerOpsService);
  private deliveryPartners = inject(DeliveryPartnerService);

  loading = true;
  actionBusy = false;
  errorMessage = '';
  parcelActionMessage = '';
  routeQuoteLoading = false;
  routeQuoteError = '';
  routeQuoteBaseDistanceKm: number | null = null;
  activeTab: 'kitchen' | 'delivery' | 'parcel' | 'escalations' = 'kitchen';

  board: ManagerOpsBoardResponse | null = null;
  reconciliation: ManagerAuditReconciliationResponse | null = null;
  partners: Array<{ id?: string; name: string }> = [];

  selectedPartnerByOrder: Record<string, string> = {};
  urgentReasonByOrder: Record<string, string> = {};
  parcelForm = {
    partnerId: '',
    startPoint: '',
    endPoint: '',
    isRoundTrip: false,
    notes: ''
  };

  private refreshHandle: any = null;
  private parcelQuoteDebounceHandle: any = null;
  private parcelQuoteRequestVersion = 0;

  get calculatedDistanceKm(): number | null {
    if (this.routeQuoteBaseDistanceKm === null) {
      return null;
    }

    return this.parcelForm.isRoundTrip
      ? this.routeQuoteBaseDistanceKm * 2
      : this.routeQuoteBaseDistanceKm;
  }

  ngOnInit(): void {
    this.loadAll();
    this.refreshHandle = setInterval(() => {
      if (!document.hidden) {
        this.loadAll();
      }
    }, 30000);
  }

  ngOnDestroy(): void {
    if (this.refreshHandle) {
      clearInterval(this.refreshHandle);
      this.refreshHandle = null;
    }

    if (this.parcelQuoteDebounceHandle) {
      clearTimeout(this.parcelQuoteDebounceHandle);
      this.parcelQuoteDebounceHandle = null;
    }
  }

  loadAll(): void {
    this.loading = true;
    this.errorMessage = '';

    const date = new Date().toISOString().slice(0, 10);
    forkJoin({
      board: this.managerOps.getBoard(),
      reconciliation: this.managerOps.getAuditReconciliation(date),
      partners: this.deliveryPartners.getDeliveryPartners()
    }).subscribe({
      next: ({ board, reconciliation, partners }) => {
        this.board = board;
        this.reconciliation = reconciliation;
        this.partners = (partners || []).map(p => ({ id: p.id, name: p.name }));
        this.loading = false;
      },
      error: () => {
        this.errorMessage = 'Failed to load manager operations board.';
        this.loading = false;
      }
    });
  }

  canAssignParcel(): boolean {
    return !!this.parcelForm.partnerId
      && !!this.parcelForm.startPoint.trim()
      && !!this.parcelForm.endPoint.trim();
  }

  trackByOrder(_: number, item: { id?: string }): string {
    return item.id || '';
  }

  trackByParcel(_: number, item: { id?: string }): string {
    return item.id || '';
  }

  getStatusClass(status?: string): string {
    const normalized = (status || '').trim().toLowerCase();

    if (normalized.includes('out-for-delivery') || normalized.includes('out for delivery')) {
      return 'status-out-for-delivery';
    }
    if (normalized.includes('pending')) {
      return 'status-pending';
    }
    if (normalized.includes('ready')) {
      return 'status-ready';
    }
    if (normalized.includes('assigned')) {
      return 'status-assigned';
    }
    if (normalized.includes('accepted')) {
      return 'status-accepted';
    }
    if (normalized.includes('picked') || normalized.includes('in transit')) {
      return 'status-in-transit';
    }
    if (normalized.includes('delivered') || normalized.includes('completed') || normalized.includes('done')) {
      return 'status-completed';
    }
    if (normalized.includes('cancelled') || normalized.includes('canceled') || normalized.includes('failed')) {
      return 'status-cancelled';
    }

    return 'status-neutral';
  }

  setTab(tab: 'kitchen' | 'delivery' | 'parcel' | 'escalations'): void {
    this.activeTab = tab;
  }

  onReassign(orderId?: string): void {
    if (!orderId) {
      return;
    }
    const partnerId = this.selectedPartnerByOrder[orderId];
    if (!partnerId) {
      return;
    }

    this.actionBusy = true;
    this.managerOps.reassignPartner(orderId, partnerId).subscribe({
      next: () => {
        this.actionBusy = false;
        this.loadAll();
      },
      error: () => {
        this.actionBusy = false;
      }
    });
  }

  onMarkUrgent(orderId?: string): void {
    if (!orderId) {
      return;
    }

    this.actionBusy = true;
    this.managerOps.markUrgent(orderId, this.urgentReasonByOrder[orderId]).subscribe({
      next: () => {
        this.actionBusy = false;
        this.loadAll();
      },
      error: () => {
        this.actionBusy = false;
      }
    });
  }

  onResend(orderId?: string): void {
    if (!orderId) {
      return;
    }

    this.actionBusy = true;
    this.managerOps.resendNotification(orderId).subscribe({
      next: () => {
        this.actionBusy = false;
        this.loadAll();
      },
      error: () => {
        this.actionBusy = false;
      }
    });
  }

  onAssignParcelTask(): void {
    if (!this.canAssignParcel()) {
      return;
    }

    this.actionBusy = true;
    this.parcelActionMessage = '';

    this.deliveryPartners.createParcelTask({
      partnerId: this.parcelForm.partnerId,
      startPoint: this.parcelForm.startPoint.trim(),
      endPoint: this.parcelForm.endPoint.trim(),
      isRoundTrip: this.parcelForm.isRoundTrip,
      notes: this.parcelForm.notes.trim() || undefined
    }).subscribe({
      next: () => {
        this.actionBusy = false;
        this.parcelActionMessage = 'Parcel task assigned successfully.';
        this.parcelForm = {
          partnerId: '',
          startPoint: '',
          endPoint: '',
          isRoundTrip: false,
          notes: ''
        };
        this.resetParcelQuoteState();
        this.loadAll();
      },
      error: () => {
        this.actionBusy = false;
        this.parcelActionMessage = 'Failed to assign parcel task.';
      }
    });
  }

  onParcelRouteInputChange(): void {
    if (this.parcelQuoteDebounceHandle) {
      clearTimeout(this.parcelQuoteDebounceHandle);
      this.parcelQuoteDebounceHandle = null;
    }

    const startPoint = this.parcelForm.startPoint.trim();
    const endPoint = this.parcelForm.endPoint.trim();

    if (!startPoint || !endPoint) {
      this.resetParcelQuoteState();
      return;
    }

    this.routeQuoteLoading = true;
    this.routeQuoteError = '';

    this.parcelQuoteDebounceHandle = setTimeout(() => {
      this.fetchParcelRouteQuote(startPoint, endPoint);
    }, 350);
  }

  private fetchParcelRouteQuote(startPoint: string, endPoint: string): void {
    const requestVersion = ++this.parcelQuoteRequestVersion;

    this.deliveryPartners.getParcelRouteQuote({
      startPoint,
      endPoint,
      isRoundTrip: false
    }).subscribe({
      next: (quote: ParcelRouteQuote) => {
        if (requestVersion !== this.parcelQuoteRequestVersion) {
          return;
        }

        this.routeQuoteLoading = false;
        this.routeQuoteError = '';
        this.routeQuoteBaseDistanceKm = quote.distanceKm ?? null;
      },
      error: () => {
        if (requestVersion !== this.parcelQuoteRequestVersion) {
          return;
        }

        this.routeQuoteLoading = false;
        this.routeQuoteBaseDistanceKm = null;
        this.routeQuoteError = 'Unable to calculate distance right now.';
      }
    });
  }

  private resetParcelQuoteState(): void {
    this.parcelQuoteRequestVersion++;
    this.routeQuoteLoading = false;
    this.routeQuoteError = '';
    this.routeQuoteBaseDistanceKm = null;
  }
}
