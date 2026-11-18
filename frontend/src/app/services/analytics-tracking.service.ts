import { DestroyRef, Injectable, effect, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { environment } from '../../environments/environment';
import { AnalyticsConsentService } from './analytics-consent.service';
import { JourneyTrackingService } from './journey-tracking.service';
import { JourneyContext } from '../models/customer-journey.model';
import { AuthStore } from '../store/auth.store';
import { postAnalyticsKeepalive } from '../utils/analytics-keepalive';

export interface TrackEvent {
  eventType: string;
  featureName?: string;
  detail?: string;
  responseTimeMs?: number;
  httpMethod?: string;
  statusCode?: number;
  sessionId?: string;
}

@Injectable({ providedIn: 'root' })
export class AnalyticsTrackingService {
  private readonly http = inject(HttpClient);
  private readonly consent = inject(AnalyticsConsentService);
  private readonly journey = inject(JourneyTrackingService);
  private readonly auth = inject(AuthStore);
  private readonly destroyRef = inject(DestroyRef);
  private readonly base = `${environment.apiUrl}/analytics`;
  private events: { context: JourneyContext; event: TrackEvent }[] = [];
  private legacySession: JourneyContext | null = null;

  constructor() {
    effect(() => {
      const context = this.journey.context();
      const permitted = this.consent.linkAccount() && this.auth.isLoggedIn();
      if (!this.consent.analytics()) this.events = [];
      if (this.legacySession?.sessionId === context?.sessionId && permitted) return;
      if (this.legacySession) this.sessionAction(this.legacySession, 'end');
      this.legacySession = permitted ? context : null;
      if (this.legacySession) this.sessionAction(this.legacySession, 'start');
    });
    const timer = setInterval(() => this.flush(), 10000);
    const heartbeat = setInterval(() => {
      if (this.consent.linkAccount() && this.legacySession)
        this.http.post(`${this.base}/heartbeat`, { journey: this.legacySession })
          .subscribe({ error: error => this.deliveryError(error) });
    }, 300000);
    const unload = () => this.flush(true);
    window.addEventListener('pagehide', unload);
    this.destroyRef.onDestroy(() => {
      clearInterval(timer);
      clearInterval(heartbeat);
      window.removeEventListener('pagehide', unload);
    });
  }

  trackPageView(pageName: string, path?: string): void {
    this.journey.trackPage(path ?? `/${pageName.toLowerCase().replaceAll(' ', '-')}`);
    this.push({ eventType: 'FeatureUsage', featureName: path?.split(/[?#]/)[0].split('/').filter(Boolean)[0] ?? pageName });
  }

  trackFeatureUsage(featureName: string, _detail?: string): void {
    this.push({ eventType: 'FeatureUsage', featureName });
  }

  trackLogin(): void {
    this.push({ eventType: 'Login' });
    this.flush();
  }

  trackLogout(): void { this.push({ eventType: 'Logout' }); }
  trackCartView(): void { this.push({ eventType: 'CartView' }); }

  trackCartAdd(itemName: string, itemId?: string, quantity = 1): void {
    this.journey.trackCart('cart_add', itemId, quantity);
    this.push({ eventType: 'CartAdd', featureName: itemName, detail: itemId });
  }

  trackCartRemove(itemName: string, itemId?: string, quantity = 1): void {
    this.journey.trackCart('cart_remove', itemId, quantity);
    this.push({ eventType: 'CartRemove', featureName: itemName, detail: itemId });
  }

  trackCartUpdate(): void { this.journey.trackCart('cart_update'); }

  trackApiCall(endpoint: string, method: string, statusCode: number, responseTimeMs: number): void {
    this.push({ eventType: 'ApiCall', detail: endpoint.split(/[/?#]/)[0],
      httpMethod: method, statusCode, responseTimeMs });
  }

  private push(event: TrackEvent): void {
    const context = this.journey.orderContext();
    if (!context) return;
    if (this.events.length >= 200) {
      console.warn('Legacy analytics queue is full; an event could not be recorded.');
      return;
    }
    this.events.push({ context, event: { ...event, sessionId: context.sessionId } });
  }

  private sessionAction(context: JourneyContext, action: 'start' | 'end'): void {
    if (!this.consent.analytics()) return;
    this.http.post(`${this.base}/session`, { journey: context, action })
      .subscribe({ error: error => this.deliveryError(error) });
  }

  private flush(keepalive = false): void {
    if (!this.consent.analytics()) { this.events = []; return; }
    const pending = this.events.splice(0);
    const groups = new Map<string, { journey: JourneyContext; events: TrackEvent[] }>();
    for (const entry of pending) {
      let group = groups.get(entry.context.sessionId);
      if (!group) {
        group = { journey: entry.context, events: [] };
        groups.set(entry.context.sessionId, group);
      }
      group.events.push(entry.event);
    }
    for (const group of groups.values()) {
      while (group.events.length) {
        const payload = { journey: group.journey, events: group.events.splice(0, 50) };
        if (keepalive) {
          void postAnalyticsKeepalive(`${this.base}/track/batch`, payload)
            .catch(error => console.warn('Final legacy analytics delivery failed', error));
        } else {
          this.http.post(`${this.base}/track/batch`, payload)
            .subscribe({ error: error => this.deliveryError(error) });
        }
      }
    }
  }

  private deliveryError(error: HttpErrorResponse): void {
    console.warn('Consented legacy analytics delivery failed', error.status);
  }
}
