import { Injectable, computed, signal } from '@angular/core';

export type NotificationType = 'success' | 'error' | 'warning' | 'info';

export interface Notification {
  id: number;
  message: string;
  type: NotificationType;
  duration?: number;
}

/**
 * Centralized UI State Store using Angular Signals.
 * Manages global loading state and notifications.
 */
@Injectable({ providedIn: 'root' })
export class UIStore {
  private static nextId = 1;

  // ── Loading state ──
  private readonly _loadingCount = signal(0);
  readonly isLoading = computed(() => this._loadingCount() > 0);

  // ── Notifications ──
  private readonly _notifications = signal<Notification[]>([]);
  readonly notifications = this._notifications.asReadonly();
  readonly hasNotifications = computed(() => this._notifications().length > 0);

  // ── Sidebar / UI toggles ──
  private readonly _sidebarCollapsed = signal(false);
  readonly sidebarCollapsed = this._sidebarCollapsed.asReadonly();

  // ── Loading actions ──

  startLoading(): void {
    this._loadingCount.update(c => c + 1);
  }

  stopLoading(): void {
    this._loadingCount.update(c => Math.max(0, c - 1));
  }

  // ── Notification actions ──

  notify(message: string, type: NotificationType = 'info', duration: number = 5000): void {
    const id = UIStore.nextId++;
    this._notifications.update(list => [...list, { id, message, type, duration }]);

    if (duration > 0) {
      setTimeout(() => this.dismissNotification(id), duration);
    }
  }

  success(message: string, duration?: number): void {
    this.notify(message, 'success', duration);
  }

  error(message: string, duration?: number): void {
    this.notify(message, 'error', duration ?? 8000);
  }

  warning(message: string, duration?: number): void {
    this.notify(message, 'warning', duration);
  }

  dismissNotification(id: number): void {
    this._notifications.update(list => list.filter(n => n.id !== id));
  }

  clearNotifications(): void {
    this._notifications.set([]);
  }

  // ── Sidebar actions ──

  toggleSidebar(): void {
    this._sidebarCollapsed.update(v => !v);
  }

  setSidebarCollapsed(collapsed: boolean): void {
    this._sidebarCollapsed.set(collapsed);
  }
}
