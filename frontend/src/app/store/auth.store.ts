import { Injectable, computed, signal } from '@angular/core';
import { toObservable } from '@angular/core/rxjs-interop';
import { User, UserRole } from '../services/auth.service';

/**
 * Centralized Auth State Store using Angular Signals.
 * Single source of truth for authentication state.
 *
 * Services (AuthService) delegate state mutations here.
 * Components can read state via signals (preferred) or observables (backward compat).
 */
@Injectable({ providedIn: 'root' })
export class AuthStore {
  // ── Private writable signals ──
  private readonly _user = signal<User | null>(null);
  private readonly _token = signal<string | null>(null);

  // ── Public readonly signals ──
  readonly user = this._user.asReadonly();
  readonly token = this._token.asReadonly();

  // ── Computed signals (derived state) ──
  readonly isLoggedIn = computed(() => this._user() !== null && this._token() !== null);
  readonly isAdmin = computed(() => this._user()?.role === 'admin');
  readonly isUser = computed(() => {
    const role = this._user()?.role;
    return role === 'user' || role === 'admin';
  });
  readonly userRole = computed<UserRole | 'guest'>(() => this._user()?.role ?? 'guest');
  readonly userName = computed(() => {
    const u = this._user();
    if (!u) return '';
    return u.firstName ? `${u.firstName} ${u.lastName ?? ''}`.trim() : u.username;
  });
  readonly defaultOutletId = computed(() => this._user()?.defaultOutletId ?? null);
  readonly assignedOutlets = computed(() => this._user()?.assignedOutlets ?? []);

  // ── Observable bridges (backward compatibility for existing .subscribe() code) ──
  readonly user$ = toObservable(this._user);

  // ── Constructor: hydrate from localStorage ──
  constructor() {
    this.hydrate();
  }

  // ── Actions ──

  setUser(user: User | null): void {
    this._user.set(user);
  }

  setToken(token: string | null): void {
    this._token.set(token);
  }

  /**
   * Login action — sets user + token + persists to localStorage.
   */
  login(user: User, token: string, csrfToken?: string): void {
    this._token.set(token);
    this._user.set(user);
    localStorage.setItem('authToken', token);
    localStorage.setItem('currentUser', JSON.stringify(user));
    if (csrfToken) {
      localStorage.setItem('csrfToken', csrfToken);
    }
  }

  /**
   * Logout action — clears all auth state + localStorage.
   */
  logout(): void {
    this._user.set(null);
    this._token.set(null);
    localStorage.removeItem('authToken');
    localStorage.removeItem('currentUser');
    localStorage.removeItem('csrfToken');
    localStorage.removeItem('selectedOutletId');
    localStorage.removeItem('selectedOutlet');
  }

  /**
   * Update profile data in-place (after API success).
   */
  updateProfile(data: Partial<User>): void {
    const current = this._user();
    if (current) {
      const updated = { ...current, ...data };
      this._user.set(updated);
      localStorage.setItem('currentUser', JSON.stringify(updated));
    }
  }

  // ── Private ──

  private hydrate(): void {
    const token = localStorage.getItem('authToken');
    const userJson = localStorage.getItem('currentUser');

    if (token && userJson) {
      try {
        const user: User = JSON.parse(userJson);
        if (user?.username && user?.email && user?.role && user.token === token) {
          this._token.set(token);
          this._user.set(user);
        } else {
          this.logout();
        }
      } catch {
        this.logout();
      }
    } else if (token || userJson) {
      this.logout();
    }
  }
}
