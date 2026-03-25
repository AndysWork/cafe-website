import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { AuthService } from './auth.service';
import { environment } from '../../environments/environment';
import { Subject, bufferTime, filter } from 'rxjs';

export interface TrackEvent {
  eventType: string;
  featureName?: string;
  detail?: string;
  responseTimeMs?: number;
  httpMethod?: string;
  statusCode?: number;
  sessionId?: string;
}

@Injectable({
  providedIn: 'root'
})
export class AnalyticsTrackingService {
  private http = inject(HttpClient);
  private authService = inject(AuthService);
  private apiUrl = environment.apiUrl;

  private sessionId: string | null = null;
  private heartbeatInterval: any = null;
  private eventBuffer$ = new Subject<TrackEvent>();

  constructor() {
    // Buffer events and send in batches every 10 seconds
    this.eventBuffer$.pipe(
      bufferTime(10000),
      filter(events => events.length > 0)
    ).subscribe(events => this.flushEvents(events));

    // Listen for login/logout to manage sessions
    this.authService.currentUser$.subscribe(user => {
      if (user) {
        this.startSession();
      } else if (this.sessionId) {
        this.endSession();
      }
    });

    // Generate session ID for anonymous users too
    if (!this.sessionId) {
      this.sessionId = this.generateSessionId();
    }

    // Track page unload
    if (typeof window !== 'undefined') {
      window.addEventListener('beforeunload', () => {
        this.flushEventsSync();
        if (this.authService.isLoggedIn()) {
          this.endSessionSync();
        }
      });
    }
  }

  private generateSessionId(): string {
    return 'sess_' + Date.now() + '_' + Math.random().toString(36).substring(2, 9);
  }

  private startSession(): void {
    this.sessionId = this.generateSessionId();

    this.http.post(`${this.apiUrl}/analytics/session`, {
      sessionId: this.sessionId,
      action: 'start'
    }).subscribe({
      error: () => {} // Silent fail for tracking
    });

    // Start heartbeat (every 5 minutes)
    this.stopHeartbeat();
    this.heartbeatInterval = setInterval(() => {
      if (this.sessionId) {
        this.http.post(`${this.apiUrl}/analytics/heartbeat`, {
          sessionId: this.sessionId
        }).subscribe({ error: () => {} });
      }
    }, 300000);
  }

  private endSession(): void {
    if (this.sessionId) {
      this.http.post(`${this.apiUrl}/analytics/session`, {
        sessionId: this.sessionId,
        action: 'end'
      }).subscribe({ error: () => {} });
    }
    this.stopHeartbeat();
    this.sessionId = this.generateSessionId(); // New anonymous session ID
  }

  private endSessionSync(): void {
    if (this.sessionId && typeof navigator !== 'undefined' && navigator.sendBeacon) {
      navigator.sendBeacon(
        `${this.apiUrl}/analytics/session`,
        JSON.stringify({ sessionId: this.sessionId, action: 'end' })
      );
    }
    this.stopHeartbeat();
  }

  private stopHeartbeat(): void {
    if (this.heartbeatInterval) {
      clearInterval(this.heartbeatInterval);
      this.heartbeatInterval = null;
    }
  }

  // ─── Public tracking methods ───

  trackPageView(pageName: string): void {
    this.eventBuffer$.next({
      eventType: 'FeatureUsage',
      featureName: pageName,
      sessionId: this.sessionId || undefined
    });
  }

  trackFeatureUsage(featureName: string, detail?: string): void {
    this.eventBuffer$.next({
      eventType: 'FeatureUsage',
      featureName,
      detail,
      sessionId: this.sessionId || undefined
    });
  }

  trackLogin(): void {
    this.eventBuffer$.next({
      eventType: 'Login',
      sessionId: this.sessionId || undefined
    });
    // Flush login event immediately
    this.flushEvents([{
      eventType: 'Login',
      sessionId: this.sessionId || undefined
    }]);
  }

  trackLogout(): void {
    this.eventBuffer$.next({
      eventType: 'Logout',
      sessionId: this.sessionId || undefined
    });
  }

  trackCartView(): void {
    this.eventBuffer$.next({
      eventType: 'CartView',
      sessionId: this.sessionId || undefined
    });
  }

  trackCartAdd(itemName: string, itemId?: string): void {
    this.eventBuffer$.next({
      eventType: 'CartAdd',
      featureName: itemName,
      detail: itemId,
      sessionId: this.sessionId || undefined
    });
  }

  trackCartRemove(itemName: string, itemId?: string): void {
    this.eventBuffer$.next({
      eventType: 'CartRemove',
      featureName: itemName,
      detail: itemId,
      sessionId: this.sessionId || undefined
    });
  }

  trackApiCall(endpoint: string, method: string, statusCode: number, responseTimeMs: number): void {
    this.eventBuffer$.next({
      eventType: 'ApiCall',
      detail: endpoint,
      httpMethod: method,
      statusCode,
      responseTimeMs,
      sessionId: this.sessionId || undefined
    });
  }

  // ─── Flush logic ───

  private flushEvents(events: TrackEvent[]): void {
    if (events.length === 0) return;

    this.http.post(`${this.apiUrl}/analytics/track/batch`, {
      events: events
    }).subscribe({ error: () => {} }); // Silent fail
  }

  private flushEventsSync(): void {
    // Use sendBeacon for synchronous flush on page unload
    // Note: buffered events are lost on unload since we can't access the buffer synchronously
  }
}
