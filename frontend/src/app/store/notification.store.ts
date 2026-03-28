import { Injectable, computed, signal, inject, DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { interval, Subscription, switchMap, tap, catchError, EMPTY, filter } from 'rxjs';
import { NotificationApiService, AppNotification, NotificationPreferences } from '../services/notification-api.service';
import { AuthStore } from './auth.store';

/**
 * Centralized Notification State Store using Angular Signals.
 * Polls for new notifications every 30 seconds when user is logged in.
 */
@Injectable({ providedIn: 'root' })
export class NotificationStore {
  private api = inject(NotificationApiService);
  private authStore = inject(AuthStore);
  private destroyRef = inject(DestroyRef);

  // ── Private writable signals ──
  private readonly _notifications = signal<AppNotification[]>([]);
  private readonly _unreadCount = signal<number>(0);
  private readonly _totalCount = signal<number>(0);
  private readonly _preferences = signal<NotificationPreferences>({
    orderUpdates: true,
    loyaltyPoints: true,
    offers: true,
    systemNotifications: true,
    emailNotifications: true,
    pushNotifications: true
  });
  private readonly _isLoading = signal(false);
  private readonly _isOpen = signal(false);

  // ── Public readonly signals ──
  readonly notifications = this._notifications.asReadonly();
  readonly unreadCount = this._unreadCount.asReadonly();
  readonly totalCount = this._totalCount.asReadonly();
  readonly preferences = this._preferences.asReadonly();
  readonly isLoading = this._isLoading.asReadonly();
  readonly isOpen = this._isOpen.asReadonly();

  // ── Computed ──
  readonly hasUnread = computed(() => this._unreadCount() > 0);
  readonly displayBadge = computed(() => {
    const count = this._unreadCount();
    if (count === 0) return '';
    return count > 99 ? '99+' : count.toString();
  });

  private pollSub: Subscription | null = null;
  private static readonly POLL_INTERVAL = 30_000; // 30 seconds

  constructor() {
    // Watch auth state — start/stop polling accordingly
    this.authStore.user$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(user => {
        if (user) {
          this.startPolling();
        } else {
          this.stopPolling();
          this.reset();
        }
      });
  }

  // ── Actions ──

  togglePanel(): void {
    const opening = !this._isOpen();
    this._isOpen.set(opening);
    if (opening) {
      this.loadNotifications();
    }
  }

  closePanel(): void {
    this._isOpen.set(false);
  }

  loadNotifications(page: number = 1): void {
    this._isLoading.set(true);
    this.api.getNotifications(page, 20).subscribe({
      next: (res) => {
        this._notifications.set(res.notifications);
        this._unreadCount.set(res.unreadCount);
        this._totalCount.set(res.totalCount);
        this._isLoading.set(false);
      },
      error: () => {
        this._isLoading.set(false);
      }
    });
  }

  markAsRead(notificationId: string): void {
    this.api.markAsRead(notificationId).subscribe({
      next: () => {
        this._notifications.update(list =>
          list.map(n => n.id === notificationId ? { ...n, isRead: true } : n)
        );
        this._unreadCount.update(c => Math.max(0, c - 1));
      }
    });
  }

  markAllAsRead(): void {
    this.api.markAllAsRead().subscribe({
      next: () => {
        this._notifications.update(list => list.map(n => ({ ...n, isRead: true })));
        this._unreadCount.set(0);
      }
    });
  }

  deleteNotification(notificationId: string): void {
    const wasUnread = this._notifications().find(n => n.id === notificationId && !n.isRead);
    this._notifications.update(list => list.filter(n => n.id !== notificationId));
    this._totalCount.update(c => Math.max(0, c - 1));
    if (wasUnread) this._unreadCount.update(c => Math.max(0, c - 1));

    this.api.deleteNotification(notificationId).subscribe();
  }

  deleteAllNotifications(): void {
    this._notifications.set([]);
    this._unreadCount.set(0);
    this._totalCount.set(0);
    this.api.deleteAllNotifications().subscribe();
  }

  loadPreferences(): void {
    this.api.getPreferences().subscribe({
      next: (prefs) => this._preferences.set(prefs)
    });
  }

  updatePreferences(prefs: Partial<NotificationPreferences>): void {
    const current = this._preferences();
    this._preferences.set({ ...current, ...prefs });
    this.api.updatePreferences(prefs).subscribe();
  }

  // ── Polling ──

  private startPolling(): void {
    this.stopPolling();
    // Initial fetch
    this.fetchUnreadCount();

    this.pollSub = interval(NotificationStore.POLL_INTERVAL)
      .pipe(
        filter(() => this.authStore.isLoggedIn()),
        switchMap(() => this.api.getUnreadCount().pipe(catchError(() => EMPTY))),
        tap(res => this._unreadCount.set(res.unreadCount)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();
  }

  private stopPolling(): void {
    this.pollSub?.unsubscribe();
    this.pollSub = null;
  }

  private fetchUnreadCount(): void {
    this.api.getUnreadCount().subscribe({
      next: (res) => this._unreadCount.set(res.unreadCount),
      error: () => {}
    });
  }

  private reset(): void {
    this._notifications.set([]);
    this._unreadCount.set(0);
    this._totalCount.set(0);
    this._isOpen.set(false);
  }
}
