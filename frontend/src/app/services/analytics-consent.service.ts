import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';

export interface AnalyticsConsent {
  version: 'journey-v1';
  analytics: boolean;
  linkAccount: boolean;
  expiresAt: string;
  visitorId?: string;
}

@Injectable({ providedIn: 'root' })
export class AnalyticsConsentService {
  private readonly storageKey = 'cafe-analytics-consent-v1';
  readonly error = signal('');
  private readonly preference = signal<AnalyticsConsent | null>(this.read());
  readonly value = this.preference.asReadonly();
  readonly analytics = computed(() => this.preference()?.analytics === true);
  readonly linkAccount = computed(() => this.analytics() && this.preference()?.linkAccount === true);

  constructor() {
    const changed = (event: StorageEvent) => {
      if (event.key === this.storageKey || event.key === null) this.preference.set(this.read());
    };
    window.addEventListener('storage', changed);
    inject(DestroyRef).onDestroy(() => window.removeEventListener('storage', changed));
  }

  save(analytics: boolean, linkAccount: boolean): boolean {
    const previous = this.preference();
    const consent: AnalyticsConsent = {
      version: 'journey-v1', analytics, linkAccount: analytics && linkAccount,
      expiresAt: previous && new Date(previous.expiresAt).getTime() > Date.now()
        ? previous.expiresAt : new Date(Date.now() + 30 * 86400000).toISOString(),
      ...(analytics ? { visitorId: previous?.visitorId ?? crypto.randomUUID() } : {})
    };
    try {
      localStorage.setItem(this.storageKey, JSON.stringify(consent));
      this.preference.set(consent);
      this.error.set('');
      return true;
    } catch (error) {
      this.preference.set(null);
      this.error.set('Your privacy preferences could not be saved. Analytics remain off; shopping is unaffected.');
      console.warn('Could not save analytics consent', error);
      return false;
    }
  }

  expireIfNeeded(): void {
    const value = this.preference();
    if (value && new Date(value.expiresAt).getTime() <= Date.now()) {
      this.preference.set(null);
      try { localStorage.removeItem(this.storageKey); }
      catch (error) {
        this.error.set('Expired privacy preferences could not be removed from this browser. Analytics are off.');
        console.warn('Could not remove expired analytics consent', error);
      }
    }
  }

  private read(): AnalyticsConsent | null {
    try {
      const raw = localStorage.getItem(this.storageKey);
      if (!raw) return null;
      const value: unknown = JSON.parse(raw);
      if (!this.isConsent(value) || new Date(value.expiresAt).getTime() <= Date.now()) {
        localStorage.removeItem(this.storageKey);
        return null;
      }
      return value;
    } catch (error) {
      this.error.set('Privacy preferences could not be read. Analytics are off.');
      console.warn('Could not read analytics consent', error);
      return null;
    }
  }

  private isConsent(value: unknown): value is AnalyticsConsent {
    if (!value || typeof value !== 'object') return false;
    const v = value as Partial<AnalyticsConsent>;
    return v.version === 'journey-v1' && typeof v.analytics === 'boolean' &&
      typeof v.linkAccount === 'boolean' && typeof v.expiresAt === 'string' &&
      Number.isFinite(Date.parse(v.expiresAt)) &&
      (!v.analytics || (typeof v.visitorId === 'string' && /^[a-f0-9-]{36}$/i.test(v.visitorId)));
  }
}
