import { signal } from '@angular/core';
import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AnalyticsConsentService } from './analytics-consent.service';
import { JourneyTrackingService } from './journey-tracking.service';
import { AuthStore } from '../store/auth.store';
import { OutletStore } from '../store/outlet.store';
import { CartStore } from '../store/cart.store';
import { Cart } from './cart.service';
import { User } from './auth.service';
import { environment } from '../../environments/environment';
import { JourneyBrowserEvent } from '../models/customer-journey.model';

describe('Consented journey tracking', () => {
  const base = `${environment.apiUrl}/analytics/journey`;
  const context = { sessionId: '11111111-1111-4111-8111-111111111111', token: 'a'.repeat(64) };
  const storageKeys = ['cafe-analytics-consent-v1', 'cafe-journey-session-v1'];
  let http: HttpTestingController;
  let consent: AnalyticsConsentService;
  let tracker: JourneyTrackingService;
  let user = signal<User | null>(null);
  let roles = signal<string[]>([]);
  let cart = signal<Cart>({ items: [], subtotal: 0, packagingCharges: 0, total: 0, itemCount: 0 });

  beforeEach(() => {
    storageKeys.forEach(key => { localStorage.removeItem(key); sessionStorage.removeItem(key); });
    user = signal<User | null>(null);
    roles = signal<string[]>([]);
    cart = signal<Cart>({ items: [], subtotal: 0, packagingCharges: 0, total: 0, itemCount: 0 });
    spyOn(document, 'hasFocus').and.returnValue(true);
    spyOnProperty(document, 'visibilityState', 'get').and.returnValue('visible');
    spyOn(navigator, 'sendBeacon').and.returnValue(true);
    TestBed.configureTestingModule({ providers: [
      provideHttpClient(), provideHttpClientTesting(),
      { provide: AuthStore, useValue: { user, roles } },
      { provide: OutletStore, useValue: { selectedOutletId: signal('222222222222222222222222') } },
      { provide: CartStore, useValue: { cart } }
    ] });
    consent = TestBed.inject(AnalyticsConsentService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    TestBed.resetTestingModule();
    storageKeys.forEach(key => { localStorage.removeItem(key); sessionStorage.removeItem(key); });
  });

  function enable(link = false): void {
    tracker = TestBed.inject(JourneyTrackingService);
    TestBed.flushEffects();
    expect(consent.save(true, link)).toBeTrue();
    TestBed.flushEffects();
    const request = http.expectOne(`${base}/sessions`);
    expect(request.request.body.analyticsConsent).toBeTrue();
    expect(request.request.body.linkAccount).toBe(link);
    expect(request.request.body.userId).toBeUndefined();
    request.flush({ context, expiresAt: new Date(Date.now() + 86400000).toISOString() });
    http.expectOne(`${base}/events`).flush(null);
  }

  it('creates no identifiers and sends no analytics before opt-in or after decline', () => {
    tracker = TestBed.inject(JourneyTrackingService);
    TestBed.flushEffects();
    tracker.trackPage('/menu');
    tracker.trackItem('333333333333333333333333');
    expect(consent.value()).toBeNull();
    expect(sessionStorage.getItem('cafe-journey-session-v1')).toBeNull();
    consent.save(false, false);
    TestBed.flushEffects();
    expect(consent.value()?.visitorId).toBeUndefined();
    http.verify();
  });

  it('keeps account linking separate and sends no submitted account identity', () => {
    user.set({ id: 'real-user', username: 'Customer', email: 'test@example.invalid', role: 'user' });
    roles.set(['user']);
    enable();
    expect(consent.linkAccount()).toBeFalse();
    expect(tracker.orderContext()).toEqual(context);
    http.verify();
  });

  it('counts exactly 60 foreground seconds before the inactivity cutoff', fakeAsync(() => {
    enable();
    tracker.trackPage('/menu?search=not-collected');
    let active = 0;
    for (let i = 0; i < 4; i++) {
      tick(15000);
      const request = http.expectOne(`${base}/events`);
      const events: JourneyBrowserEvent[] = request.request.body.events;
      active += events.reduce((sum, e) => sum + (e.activeSeconds ?? 0), 0);
      expect(events.every(e => !e.page.includes('?'))).toBeTrue();
      request.flush(null);
    }
    expect(active).toBe(60);
    tick(15000);
    http.expectNone(`${base}/events`);
    http.verify();
    TestBed.resetTestingModule();
  }));

  it('continues measuring during frequent pointer activity without resetting the engagement clock', fakeAsync(() => {
    enable();
    for (let i = 0; i < 14; i++) {
      tick(1000);
      window.dispatchEvent(new Event('pointermove'));
    }
    tick(1000);
    const request = http.expectOne(`${base}/events`);
    const events: JourneyBrowserEvent[] = request.request.body.events;
    expect(events.reduce((sum, e) => sum + (e.activeSeconds ?? 0), 0)).toBe(15);
    request.flush(null);
    http.verify();
    TestBed.resetTestingModule();
  }));

  it('does not count engagement while the browser is unfocused', fakeAsync(() => {
    enable();
    (document.hasFocus as jasmine.Spy).and.returnValue(false);
    window.dispatchEvent(new Event('blur'));
    tick(60000);
    http.expectNone(`${base}/events`);
    expect(navigator.sendBeacon).toHaveBeenCalled();
    http.verify();
    TestBed.resetTestingModule();
  }));

  it('flushes a capability-authenticated final batch so a JWT is not needed by sendBeacon', () => {
    enable();
    tracker.trackPage('/cart');
    tracker.flush(true);
    const args = (navigator.sendBeacon as jasmine.Spy).calls.mostRecent().args;
    expect(args[0]).toBe(`${base}/events`);
    expect(args[1] instanceof Blob).toBeTrue();
    expect(args[1].size).toBeGreaterThan(0);
    http.verify();
  });

  it('stops immediately on withdrawal and requests current-session erasure', fakeAsync(() => {
    enable();
    consent.save(false, false);
    expect(tracker.orderContext()).toBeUndefined();
    TestBed.flushEffects();
    const request = http.expectOne(`${base}/revoke`);
    expect(request.request.body.context).toEqual(context);
    request.flush(null);
    tracker.trackPage('/checkout');
    tick(15000);
    http.verify();
    TestBed.resetTestingModule();
  }));

  it('drains every bounded batch on final beacon delivery', () => {
    enable();
    for (let i = 0; i < 120; i++) tracker.trackPage('/dine-in/table-1?private=excluded');
    tracker.flush(true);
    expect(navigator.sendBeacon).toHaveBeenCalledTimes(3);
    tracker.flush();
    http.verify();
  });

  it('preserves all queued events and the continuation marker when signing in', () => {
    enable(true);
    for (let i = 0; i < 120; i++) tracker.trackPage('/menu');
    user.set({ id: 'real-user', username: 'Customer', email: 'test@example.invalid', role: 'user' });
    roles.set(['user']);
    TestBed.flushEffects();
    const batches = http.match(`${base}/events`);
    expect(batches.map(batch => batch.request.body.events.length)).toEqual([50, 50, 21]);
    expect(batches.every(batch => batch.request.body.context.sessionId === context.sessionId)).toBeTrue();
    batches.forEach(batch => batch.flush(null));
    http.expectOne(`${base}/sessions`).flush({
      context: { ...context, sessionId: '44444444-4444-4444-8444-444444444444' },
      expiresAt: new Date(Date.now() + 86400000).toISOString()
    });
    http.expectOne(`${base}/events`).flush(null);
    http.verify();
  });

  it('rotates on sign-in without retroactively linking the anonymous event batch', () => {
    enable(true);
    user.set({ id: 'real-user', username: 'Customer', email: 'test@example.invalid', role: 'user' });
    roles.set(['user']);
    TestBed.flushEffects();
    const previous = http.expectOne(`${base}/events`);
    expect(previous.request.body.context).toEqual(context);
    expect(previous.request.body.events.some((e: JourneyBrowserEvent) => e.type === 'session_continued')).toBeTrue();
    previous.flush(null);
    const session = http.expectOne(`${base}/sessions`);
    expect(session.request.body.linkAccount).toBeTrue();
    session.flush({ context: { ...context, sessionId: '44444444-4444-4444-8444-444444444444' },
      expiresAt: new Date(Date.now() + 86400000).toISOString() });
    http.expectOne(`${base}/events`).flush(null);
    http.verify();
  });

  it('honors analytics withdrawal from another browser tab', () => {
    enable();
    localStorage.setItem('cafe-analytics-consent-v1', JSON.stringify({
      version: 'journey-v1', analytics: false, linkAccount: false,
      expiresAt: new Date(Date.now() + 86400000).toISOString()
    }));
    window.dispatchEvent(new StorageEvent('storage', { key: 'cafe-analytics-consent-v1' }));
    TestBed.flushEffects();
    expect(tracker.orderContext()).toBeUndefined();
    http.expectOne(`${base}/revoke`).flush(null);
    http.verify();
  });

  it('records checkout fee as unknown until quoted and captures subsequent quote changes', fakeAsync(() => {
    enable();
    cart.set({ items: [], subtotal: 100, total: 100, packagingCharges: 0, itemCount: 2 });
    tracker.trackCheckout(true, 102);
    tracker.trackCheckout(false, 137, 35);
    tick(15000);
    const request = http.expectOne(`${base}/events`);
    const events: JourneyBrowserEvent[] = request.request.body.events;
    expect(events.find(e => e.type === 'checkout_started')?.deliveryFee).toBeUndefined();
    expect(events.find(e => e.type === 'checkout_updated')?.deliveryFee).toBe(35);
    request.flush(null);
    http.verify();
    TestBed.resetTestingModule();
  }));
});
