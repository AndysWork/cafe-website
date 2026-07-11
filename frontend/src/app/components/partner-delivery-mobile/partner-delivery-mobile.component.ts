import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { DeliveryPartnerService, PartnerDashboard } from '../../services/delivery-partner.service';
import { PartnerDeliveryAlertService } from '../../services/partner-delivery-alert.service';
import { WebPushService } from '../../services/web-push.service';
import { UIStore } from '../../store/ui.store';

@Component({
  selector: 'app-partner-delivery-mobile',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './partner-delivery-mobile.component.html',
  styleUrls: ['./partner-delivery-mobile.component.scss']
})
export class PartnerDeliveryMobileComponent implements OnInit, OnDestroy {
  private partnerService = inject(DeliveryPartnerService);
  private alerts = inject(PartnerDeliveryAlertService);
  private webPush = inject(WebPushService);
  private uiStore = inject(UIStore);
  private route = inject(ActivatedRoute);

  dashboard: PartnerDashboard | null = null;
  loading = true;
  offline = false;
  refreshing = false;
  accepting: Record<string, boolean> = {};
  private refreshHandle?: number;
  private knownPendingIds = new Set<string>();

  async ngOnInit(): Promise<void> {
    await this.alerts.initialize();
    await this.webPush.registerPartnerWebPush('partner-mobile');

    this.handleTrayAction();
    await this.loadDashboard(true);

    this.refreshHandle = window.setInterval(() => {
      this.loadDashboard(false);
    }, 20000);

    window.addEventListener('online', this.onConnectivityChanged);
    window.addEventListener('offline', this.onConnectivityChanged);
  }

  ngOnDestroy(): void {
    if (this.refreshHandle) {
      window.clearInterval(this.refreshHandle);
    }

    window.removeEventListener('online', this.onConnectivityChanged);
    window.removeEventListener('offline', this.onConnectivityChanged);
  }

  async loadDashboard(showLoader: boolean): Promise<void> {
    if (showLoader) {
      this.loading = true;
    } else {
      this.refreshing = true;
    }

    this.partnerService.getPartnerDashboard().subscribe({
      next: async data => {
        this.dashboard = data;
        this.offline = false;
        this.cacheDashboard(data);
        await this.notifyForNewRequests(data);
        this.loading = false;
        this.refreshing = false;
      },
      error: () => {
        const cached = this.getCachedDashboard();
        if (cached) {
          this.dashboard = cached;
          this.offline = true;
          this.loading = false;
          this.refreshing = false;
          return;
        }

        this.loading = false;
        this.refreshing = false;
        this.uiStore.error('Unable to load mobile partner dashboard');
      }
    });
  }

  accept(orderId?: string): void {
    if (!orderId) {
      return;
    }

    this.accepting[orderId] = true;
    this.partnerService.acceptDeliveryOrder(orderId).subscribe({
      next: () => {
        this.uiStore.success('Order accepted');
        this.accepting[orderId] = false;
        this.loadDashboard(false);
      },
      error: error => {
        this.accepting[orderId] = false;
        this.uiStore.error(error.error?.error || 'Could not accept order');
        this.loadDashboard(false);
      }
    });
  }

  navigate(address?: string): void {
    if (!address) {
      this.uiStore.error('Address unavailable');
      return;
    }

    const mapUrl = `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(address)}`;
    window.open(mapUrl, '_blank', 'noopener');
  }

  call(phone?: string): void {
    if (!phone) {
      this.uiStore.error('Phone number unavailable');
      return;
    }

    window.location.href = `tel:${phone}`;
  }

  private cacheDashboard(data: PartnerDashboard): void {
    localStorage.setItem('partner_mobile_dashboard_cache', JSON.stringify(data));
  }

  private getCachedDashboard(): PartnerDashboard | null {
    try {
      const raw = localStorage.getItem('partner_mobile_dashboard_cache');
      return raw ? JSON.parse(raw) as PartnerDashboard : null;
    } catch {
      return null;
    }
  }

  private async notifyForNewRequests(data: PartnerDashboard): Promise<void> {
    for (const request of data.pendingRequests || []) {
      const id = request.id;
      if (!id || this.knownPendingIds.has(id)) {
        continue;
      }

      await this.alerts.notifyNewRequest({
        orderId: id,
        deliveryAddress: request.deliveryAddress,
        phoneNumber: request.phoneNumber,
        total: request.total
      });

      this.knownPendingIds.add(id);
    }

    const activeIds = new Set((data.pendingRequests || []).map(r => r.id).filter(Boolean) as string[]);
    this.knownPendingIds.forEach(id => {
      if (!activeIds.has(id)) {
        this.knownPendingIds.delete(id);
      }
    });
  }

  private handleTrayAction(): void {
    const action = this.route.snapshot.queryParamMap.get('action');
    const orderId = this.route.snapshot.queryParamMap.get('orderId') || undefined;
    const address = this.route.snapshot.queryParamMap.get('address') || undefined;
    const phone = this.route.snapshot.queryParamMap.get('phone') || undefined;

    if (action === 'accept') {
      this.accept(orderId);
      return;
    }

    if (action === 'navigate') {
      this.navigate(address);
      return;
    }

    if (action === 'call') {
      this.call(phone);
    }
  }

  private onConnectivityChanged = () => {
    this.offline = !navigator.onLine;
  };
}
