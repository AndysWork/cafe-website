import { DestroyRef, Injectable, NgZone, effect, inject, signal, untracked } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Subscription } from 'rxjs';
import { environment } from '../../environments/environment';
import { AuthStore } from '../store/auth.store';
import { OutletStore } from '../store/outlet.store';
import { CartStore } from '../store/cart.store';
import { AnalyticsConsentService } from './analytics-consent.service';
import { JourneyBrowserEvent, JourneyContext, JourneySessionResponse } from '../models/customer-journey.model';

interface StoredJourney {
  context: JourneyContext;
  expiresAt: string;
  lastInteraction: number;
  binding: string;
}

@Injectable({ providedIn: 'root' })
export class JourneyTrackingService {
  private readonly http = inject(HttpClient);
  private readonly consent = inject(AnalyticsConsentService);
  private readonly auth = inject(AuthStore);
  private readonly outlets = inject(OutletStore);
  private readonly cart = inject(CartStore);
  private readonly zone = inject(NgZone);
  private readonly destroyRef = inject(DestroyRef);
  private readonly base = `${environment.apiUrl}/analytics/journey`;
  private readonly storageKey = 'cafe-journey-session-v1';
  readonly context = signal<JourneyContext | null>(null);
  readonly erasureError = signal('');
  private binding = '';
  private consentKey = '';
  private starting?: Subscription;
  private sending = false;
  private generation = 0;
  private pending: JourneyBrowserEvent[] = [];
  private pendingOrderIds = new Set<string>();
  private expiry = '';
  private page = this.normalizePage(location.pathname);
  private lastInteraction = Date.now();
  private checkpoint = Date.now();
  private foreground = document.visibilityState === 'visible' && document.hasFocus();
  private revocation?: JourneyContext;
  private checkoutStarted = false;

  constructor() {
    effect(() => {
      const consent = this.consent.value();
      const user = this.auth.user();
      const roles = this.auth.roles().join(',');
      const outlet = this.outlets.selectedOutletId();
      untracked(() => {
        const key = consent ? `${consent.analytics}:${consent.linkAccount}:${consent.visitorId ?? ''}` : '';
        const binding = `${outlet ?? ''}:${consent?.linkAccount ? user?.id ?? user?.username ?? '' : !!user}:${roles}`;
        if (key !== this.consentKey || binding !== this.binding) {
          const permissionChanged = this.consentKey !== '' && key !== this.consentKey;
          if (this.generation !== 0 || this.binding !== '') this.rotate(permissionChanged, !permissionChanged);
          else if (!consent?.analytics) this.removeStoredSession();
          this.consentKey = key;
          this.binding = binding;
          if (consent?.analytics && outlet) {
            this.restore();
            this.ensureSession();
            this.event('page_view');
            this.trackCurrentCartPage();
          }
        }
      });
    });

    const input = () => {
      const now = Date.now();
      if (now - this.lastInteraction < 1000) return;
      if (now - this.lastInteraction >= 60000) this.accrue(now);
      const newVisit = now - this.lastInteraction >= 30 * 60000;
      if (newVisit) this.rotate(false);
      this.lastInteraction = now;
      this.foreground = document.visibilityState === 'visible' && document.hasFocus();
      this.ensureSession();
      if (newVisit) this.event('page_view');
    };
    const visibility = () => {
      this.accrue(Date.now());
      this.foreground = document.visibilityState === 'visible' && document.hasFocus();
      if (this.foreground) {
        input();
        this.event('heartbeat');
      } else {
        this.event('session_end');
        this.flush(true);
      }
    };
    const pagehide = () => {
      this.accrue(Date.now());
      this.event('session_end');
      this.flush(true);
    };
    this.zone.runOutsideAngular(() => {
      for (const type of ['pointerdown', 'pointermove', 'keydown', 'scroll', 'touchstart'])
        window.addEventListener(type, input, { passive: true });
      document.addEventListener('visibilitychange', visibility);
      window.addEventListener('focus', visibility);
      window.addEventListener('blur', visibility);
      window.addEventListener('pagehide', pagehide);
      const timer = setInterval(() => {
        this.consent.expireIfNeeded();
        this.accrue(Date.now());
        if (this.foreground && Date.now() - this.lastInteraction < 60000) this.event('heartbeat');
        this.flush();
        this.retryOrders();
      }, 15000);
      this.destroyRef.onDestroy(() => {
        clearInterval(timer);
        this.starting?.unsubscribe();
        for (const type of ['pointerdown', 'pointermove', 'keydown', 'scroll', 'touchstart'])
          window.removeEventListener(type, input);
        document.removeEventListener('visibilitychange', visibility);
        window.removeEventListener('focus', visibility);
        window.removeEventListener('blur', visibility);
        window.removeEventListener('pagehide', pagehide);
      });
    });
  }

  trackPage(path: string): void {
    this.accrue(Date.now());
    this.page = this.normalizePage(path);
    if (this.page !== '/checkout') this.checkoutStarted = false;
    this.event('page_view');
    this.trackCurrentCartPage();
  }

  private trackCurrentCartPage(): void {
    if (this.page === '/cart') this.trackCart('cart_view');
    if (this.page === '/checkout' && !this.checkoutStarted)
      this.trackCheckout(true, this.cart.cart().total);
  }

  trackItem(itemId: string, viewed = true): void {
    this.event('menu_item_click', { menuItemId: itemId });
    if (viewed) this.event('menu_item_view', { menuItemId: itemId });
  }

  trackCategory(categoryId: string): void {
    if (categoryId) this.event('category_view', { categoryId });
  }

  trackCart(type: 'cart_add' | 'cart_remove' | 'cart_update' | 'cart_view', itemId?: string, quantity = 0): void {
    const cart = this.cart.cart();
    this.event(type, { menuItemId: itemId, quantity, cartCount: cart.itemCount, cartValue: cart.total });
  }

  trackCheckout(started: boolean, value: number, deliveryFee?: number): void {
    if (!this.consent.analytics()) return;
    const cart = this.cart.cart();
    if (cart.itemCount > 0) {
      this.event(started && !this.checkoutStarted ? 'checkout_started' : 'checkout_updated',
        { page: '/checkout', cartCount: cart.itemCount, cartValue: value, deliveryFee });
      this.checkoutStarted = true;
    }
  }

  orderContext(): JourneyContext | undefined {
    const preference = this.consent.value();
    const user = this.auth.user();
    const binding = `${this.outlets.selectedOutletId() ?? ''}:${preference?.linkAccount ? user?.id ?? user?.username ?? '' : !!user}:${this.auth.roles().join(',')}`;
    return this.consent.analytics() && binding === this.binding && Date.parse(this.expiry) > Date.now()
      ? this.context() ?? undefined : undefined;
  }

  confirmOrder(orderId: string, original?: JourneyContext): void {
    const current = this.orderContext();
    if (!current || (original && current.sessionId !== original.sessionId)) return;
    this.pendingOrderIds.add(orderId);
    this.retryOrders();
  }

  private retryOrders(): void {
    const context = this.orderContext();
    if (!context) return;
    for (const orderId of this.pendingOrderIds) {
      this.pendingOrderIds.delete(orderId);
      const generation = this.generation;
      this.http.post<void>(`${this.base}/orders`, { context, orderId }).subscribe({
        error: (error: HttpErrorResponse) => {
          console.warn('Backend journey order confirmation failed', error.status);
          if (generation === this.generation && (error.status === 0 || error.status >= 500))
            this.pendingOrderIds.add(orderId);
        }
      });
    }
  }

  private event(type: string, fields: Partial<JourneyBrowserEvent> = {}): void {
    if (!this.consent.analytics() || !this.outlets.selectedOutletId()) return;
    this.ensureSession();
    if (this.pending.length >= 200) {
      console.warn('Journey analytics queue is full; an event could not be recorded.');
      return;
    }
    this.pending.push({ id: crypto.randomUUID(), type, occurredAt: new Date().toISOString(), page: this.page, ...fields });
  }

  private accrue(now: number): void {
    const end = Math.min(now, this.lastInteraction + 60000);
    if (this.foreground && this.consent.analytics()) {
      for (let cursor = this.checkpoint; cursor + 1000 <= end;) {
        const seconds = Math.min(30, Math.floor((end - cursor) / 1000));
        cursor += seconds * 1000;
        this.event('page_engagement', { occurredAt: new Date(cursor).toISOString(), activeSeconds: seconds });
      }
    }
    this.checkpoint = now;
  }

  private ensureSession(): void {
    if (!this.consent.analytics() || !this.outlets.selectedOutletId() || this.starting) return;
    if (this.context() && Date.parse(this.expiry) > Date.now()) return;
    this.context.set(null);
    const preference = this.consent.value();
    if (!preference?.visitorId) return;
    const generation = this.generation;
    const subscription = this.http.post<JourneySessionResponse>(`${this.base}/sessions`, {
      analyticsConsent: true, linkAccount: preference.linkAccount, consentVersion: preference.version,
      consentExpiresAt: preference.expiresAt, visitorId: preference.visitorId,
      outletId: this.outlets.selectedOutletId(), device: this.device()
    }).subscribe({
      next: response => {
        this.starting = undefined;
        if (generation !== this.generation || !this.consent.analytics()) return;
        this.context.set(response.context);
        this.expiry = response.expiresAt;
        this.persist();
        this.flush();
      },
      error: (error: HttpErrorResponse) => {
        this.starting = undefined;
        console.warn('Could not start consented journey analytics', error.status);
      }
    });
    if (!subscription.closed) this.starting = subscription;
  }

  flush(beacon = false): void {
    const context = this.orderContext();
    if (!context || this.pending.length === 0) return;
    if (this.sending && !beacon) return;
    if (beacon && navigator.sendBeacon) {
      while (this.pending.length) {
        const events = this.pending.splice(0, 50);
        const queued = navigator.sendBeacon(`${this.base}/events`,
          new Blob([JSON.stringify({ context, events })], { type: 'application/json' }));
        if (!queued) {
          this.pending.unshift(...events);
          console.warn('The browser could not queue the final journey analytics batch.');
          break;
        }
      }
      return;
    }
    const events = this.pending.splice(0, 50);
    this.sending = true;
    const generation = this.generation;
    this.http.post<void>(`${this.base}/events`, { context, events }).subscribe({
      next: () => {
        if (generation !== this.generation) return;
        this.sending = false;
        this.persist();
        this.flush();
      },
      error: (error: HttpErrorResponse) => {
        console.warn('Journey analytics delivery failed', error.status);
        if (generation !== this.generation || !this.consent.analytics()) return;
        this.sending = false;
        if (error.status === 0 || error.status >= 500) this.pending.unshift(...events);
        else if (error.status === 403) {
          this.rotate(false);
          this.ensureSession();
          this.event('page_view');
        }
      }
    });
  }

  private rotate(erase: boolean, continued = false): void {
    const context = this.context();
    if (!erase && context) {
      if (continued) this.pending.push({
        id: crypto.randomUUID(), type: 'session_continued', occurredAt: new Date().toISOString(), page: this.page
      });
      this.sendPrevious(context);
    }
    this.generation++;
    this.starting?.unsubscribe();
    this.starting = undefined;
    this.pending = [];
    this.pendingOrderIds.clear();
    this.checkoutStarted = false;
    this.context.set(null);
    this.sending = false;
    this.checkpoint = Date.now();
    this.removeStoredSession();
    if (erase && context) {
      this.revocation = context;
      this.retryErasure();
    }
  }

  private sendPrevious(context: JourneyContext): void {
    while (this.pending.length) {
      const events = this.pending.splice(0, 50);
      this.http.post<void>(`${this.base}/events`, { context, events }).subscribe({
        error: (error: HttpErrorResponse) => console.warn('Could not flush the previous journey boundary', error.status)
      });
    }
  }

  retryErasure(): void {
    if (!this.revocation) return;
    this.http.post<void>(`${this.base}/revoke`, { context: this.revocation }).subscribe({
      next: () => { this.revocation = undefined; this.erasureError.set(''); },
      error: (error: HttpErrorResponse) => {
        this.erasureError.set(error.status === 403
          ? 'The previous session has already expired or is unavailable. Its data are excluded from reports and expire automatically.'
          : 'Tracking is off for the previous session, but its history could not be erased. Retry deletion; automatic retention still applies.');
        console.warn('Journey consent withdrawal erasure failed', error.status);
      }
    });
  }

  private restore(): void {
    try {
      const raw = sessionStorage.getItem(this.storageKey);
      if (!raw) return;
      const saved: StoredJourney = JSON.parse(raw);
      if (saved.binding !== this.binding || Date.parse(saved.expiresAt) <= Date.now() ||
          Date.now() - saved.lastInteraction >= 30 * 60000 ||
          !/^[a-f0-9-]{36}$/i.test(saved.context?.sessionId ?? '') || !/^[a-f0-9]{64}$/i.test(saved.context?.token ?? ''))
        return;
      this.context.set(saved.context);
      this.expiry = saved.expiresAt;
      this.lastInteraction = Date.now();
    } catch (error) { console.warn('Could not restore a consented journey session', error); }
  }

  private persist(): void {
    const context = this.context();
    if (!context || !this.consent.analytics()) return;
    try {
      sessionStorage.setItem(this.storageKey, JSON.stringify({
        context, expiresAt: this.expiry, lastInteraction: this.lastInteraction, binding: this.binding
      } satisfies StoredJourney));
    } catch (error) { console.warn('Could not preserve the consented journey across reloads', error); }
  }

  private removeStoredSession(): void {
    try { sessionStorage.removeItem(this.storageKey); }
    catch (error) { console.warn('Could not remove the previous journey capability', error); }
  }

  private normalizePage(path: string): string {
    const first = path.split(/[?#]/)[0].split('/').filter(Boolean)[0];
    const allowed = ['home', 'menu', 'cart', 'checkout', 'orders', 'my-orders', 'profile', 'login', 'register',
      'forgot-password', 'reset-password', 'subscriptions', 'my-subscriptions', 'offers', 'loyalty',
      'contact', 'about', 'admin', 'manager', 'kitchen', 'staff', 'partner', 'phone-orders', 'privacy', 'terms',
      'dashboard', 'dine-in', 'track-order', 'reviews', 'reservations', 'finance'];
    return !first ? '/' : allowed.includes(first) ? `/${first}` : '/other';
  }

  private device(): string {
    if (window.matchMedia('(pointer: coarse)').matches)
      return Math.min(screen.width, screen.height) < 768 ? 'mobile' : 'tablet';
    return 'desktop';
  }
}
