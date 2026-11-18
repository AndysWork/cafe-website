import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AnalyticsTrackingService } from './analytics-tracking.service';
import { AnalyticsConsentService } from './analytics-consent.service';
import { JourneyTrackingService } from './journey-tracking.service';
import { AuthStore } from '../store/auth.store';
import { environment } from '../../environments/environment';

describe('Legacy analytics final delivery', () => {
  const context = { sessionId: '11111111-1111-4111-8111-111111111111', token: 'a'.repeat(64) };
  let permitted = signal(true);
  let tracker: AnalyticsTrackingService;

  beforeEach(() => {
    permitted = signal(true);
    spyOn(window, 'fetch').and.resolveTo(new Response(null, { status: 204 }));
    spyOn(navigator, 'sendBeacon').and.returnValue(true);
    TestBed.configureTestingModule({ providers: [
      provideHttpClient(), provideHttpClientTesting(),
      { provide: AuthStore, useValue: { isLoggedIn: () => false } },
      { provide: AnalyticsConsentService, useValue: { analytics: permitted, linkAccount: () => false } },
      { provide: JourneyTrackingService, useValue: { context: signal(context), orderContext: () => context } }
    ] });
    tracker = TestBed.inject(AnalyticsTrackingService);
    TestBed.flushEffects();
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    TestBed.resetTestingModule();
  });

  it('sends bounded capability-authenticated batches without credentialed CORS', () => {
    for (let i = 0; i < 120; i++) tracker.trackFeatureUsage('Menu');
    window.dispatchEvent(new Event('pagehide'));
    const requests = (window.fetch as jasmine.Spy).calls.allArgs();
    expect(requests.length).toBe(3);
    const sizes = requests.map(([url, options]) => {
      expect(url).toBe(`${environment.apiUrl}/analytics/track/batch`);
      expect(options.method).toBe('POST');
      expect(options.keepalive).toBeTrue();
      expect(options.credentials).toBe('omit');
      expect(options.headers).toEqual({ 'Content-Type': 'application/json' });
      const body = JSON.parse(options.body);
      expect(body.journey).toEqual(context);
      return body.events.length;
    });
    expect(sizes).toEqual([50, 50, 20]);
    expect(navigator.sendBeacon).not.toHaveBeenCalled();
  });

  it('does not send queued final events after analytics consent is withdrawn', () => {
    tracker.trackFeatureUsage('Menu');
    permitted.set(false);
    window.dispatchEvent(new Event('pagehide'));
    expect(window.fetch).not.toHaveBeenCalled();
  });
});
