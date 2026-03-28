import { Component, inject, HostListener, ElementRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { NotificationStore } from '../../store/notification.store';
import { AppNotification } from '../../services/notification-api.service';

@Component({
  selector: 'app-notification-center',
  standalone: true,
  imports: [CommonModule, RouterModule],
  template: `
    <!-- Bell Icon Button -->
    <button
      class="notification-bell"
      (click)="store.togglePanel()"
      [attr.aria-label]="'Notifications' + (store.hasUnread() ? ' - ' + store.unreadCount() + ' unread' : '')"
      aria-haspopup="true"
      [attr.aria-expanded]="store.isOpen()">
      <span class="bell-icon">🔔</span>
      @if (store.hasUnread()) {
        <span class="notification-badge">{{ store.displayBadge() }}</span>
      }
    </button>

    <!-- Notification Panel Dropdown -->
    @if (store.isOpen()) {
      <div class="notification-panel" role="dialog" aria-label="Notifications">
        <!-- Header -->
        <div class="panel-header">
          <h3>Notifications</h3>
          <div class="header-actions">
            @if (store.hasUnread()) {
              <button class="btn-action" (click)="store.markAllAsRead()" title="Mark all as read">
                ✓ Read All
              </button>
            }
            @if (store.notifications().length > 0) {
              <button class="btn-action btn-clear" (click)="store.deleteAllNotifications()" title="Clear all">
                🗑️ Clear
              </button>
            }
          </div>
        </div>

        <!-- Notification List -->
        <div class="notification-list">
          @if (store.isLoading()) {
            <div class="loading-state">
              <div class="spinner"></div>
              <span>Loading...</span>
            </div>
          } @else if (store.notifications().length === 0) {
            <div class="empty-state">
              <span class="empty-icon">🔕</span>
              <p>No notifications yet</p>
            </div>
          } @else {
            @for (notification of store.notifications(); track notification.id) {
              <div
                class="notification-item"
                [class.unread]="!notification.isRead"
                (click)="onNotificationClick(notification)">
                <div class="notification-icon">{{ getTypeIcon(notification.type) }}</div>
                <div class="notification-content">
                  <div class="notification-title">{{ notification.title }}</div>
                  <div class="notification-message">{{ notification.message }}</div>
                  <div class="notification-time">{{ getTimeAgo(notification.createdAt) }}</div>
                </div>
                <div class="notification-actions">
                  @if (!notification.isRead) {
                    <button class="btn-icon" (click)="markRead($event, notification.id)" title="Mark as read">
                      ✓
                    </button>
                  }
                  <button class="btn-icon btn-delete" (click)="deleteNotif($event, notification.id)" title="Delete">
                    ×
                  </button>
                </div>
              </div>
            }
          }
        </div>

        <!-- Footer -->
        @if (store.totalCount() > store.notifications().length) {
          <div class="panel-footer">
            <a routerLink="/notifications" (click)="store.closePanel()">See all notifications</a>
          </div>
        }
      </div>
    }
  `,
  styles: [`
    :host {
      position: relative;
      display: inline-flex;
      align-items: center;
    }

    .notification-bell {
      position: relative;
      background: none;
      border: none;
      cursor: pointer;
      padding: 6px 10px;
      font-size: 1.25rem;
      transition: transform 0.2s;
      line-height: 1;

      &:hover { transform: scale(1.15); }
    }

    .bell-icon { display: inline-block; }

    .notification-badge {
      position: absolute;
      top: 0;
      right: 2px;
      background: #ef4444;
      color: #fff;
      font-size: 0.65rem;
      font-weight: 700;
      min-width: 18px;
      height: 18px;
      border-radius: 9px;
      display: flex;
      align-items: center;
      justify-content: center;
      padding: 0 4px;
      line-height: 1;
      animation: badgePulse 2s ease-in-out infinite;
      box-shadow: 0 0 0 2px var(--navbar-bg, #1a1a2e);
    }

    @keyframes badgePulse {
      0%, 100% { transform: scale(1); }
      50% { transform: scale(1.15); }
    }

    .notification-panel {
      position: absolute;
      top: calc(100% + 12px);
      right: -60px;
      width: 380px;
      max-height: 500px;
      background: #fff;
      border-radius: 16px;
      box-shadow: 0 12px 40px rgba(0,0,0,0.18), 0 4px 12px rgba(0,0,0,0.08);
      z-index: 10000;
      display: flex;
      flex-direction: column;
      overflow: hidden;
      animation: panelSlideIn 0.25s ease-out;
    }

    @keyframes panelSlideIn {
      from { opacity: 0; transform: translateY(-10px); }
      to { opacity: 1; transform: translateY(0); }
    }

    .panel-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 16px 20px 12px;
      border-bottom: 1px solid #f0f0f0;

      h3 {
        margin: 0;
        font-size: 1.05rem;
        font-weight: 700;
        color: #1a1a2e;
      }
    }

    .header-actions {
      display: flex;
      gap: 8px;
    }

    .btn-action {
      background: none;
      border: none;
      font-size: 0.75rem;
      color: #667eea;
      cursor: pointer;
      padding: 4px 8px;
      border-radius: 6px;
      font-weight: 600;
      transition: all 0.2s;

      &:hover { background: #f0f4ff; }
      &.btn-clear { color: #ef4444; &:hover { background: #fef2f2; } }
    }

    .notification-list {
      overflow-y: auto;
      max-height: 380px;
      scrollbar-width: thin;
    }

    .notification-item {
      display: flex;
      align-items: flex-start;
      gap: 12px;
      padding: 14px 20px;
      cursor: pointer;
      transition: background-color 0.15s;
      border-bottom: 1px solid #f7f7f7;

      &:hover { background: #f8f9ff; }
      &.unread {
        background: #f0f4ff;
        &:hover { background: #e8edff; }
      }
      &:last-child { border-bottom: none; }
    }

    .notification-icon {
      font-size: 1.4rem;
      flex-shrink: 0;
      width: 36px;
      height: 36px;
      display: flex;
      align-items: center;
      justify-content: center;
      background: #f0f4ff;
      border-radius: 10px;
    }

    .notification-content {
      flex: 1;
      min-width: 0;
    }

    .notification-title {
      font-size: 0.85rem;
      font-weight: 600;
      color: #1a1a2e;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .notification-message {
      font-size: 0.8rem;
      color: #6b7280;
      margin-top: 2px;
      display: -webkit-box;
      -webkit-line-clamp: 2;
      -webkit-box-orient: vertical;
      overflow: hidden;
    }

    .notification-time {
      font-size: 0.7rem;
      color: #9ca3af;
      margin-top: 4px;
    }

    .notification-actions {
      display: flex;
      gap: 2px;
      flex-shrink: 0;
      opacity: 0;
      transition: opacity 0.15s;
    }

    .notification-item:hover .notification-actions { opacity: 1; }

    .btn-icon {
      background: none;
      border: none;
      width: 26px;
      height: 26px;
      border-radius: 6px;
      cursor: pointer;
      font-size: 0.85rem;
      display: flex;
      align-items: center;
      justify-content: center;
      color: #667eea;
      transition: all 0.15s;

      &:hover { background: #e8edff; }
      &.btn-delete { color: #ef4444; &:hover { background: #fef2f2; } }
    }

    .empty-state, .loading-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      padding: 40px 20px;
      color: #9ca3af;
    }

    .empty-icon { font-size: 2.5rem; margin-bottom: 8px; }
    .empty-state p { margin: 0; font-size: 0.9rem; }

    .spinner {
      width: 28px;
      height: 28px;
      border: 3px solid #e5e7eb;
      border-top: 3px solid #667eea;
      border-radius: 50%;
      animation: spin 0.8s linear infinite;
      margin-bottom: 8px;
    }

    @keyframes spin {
      to { transform: rotate(360deg); }
    }

    .panel-footer {
      padding: 12px 20px;
      text-align: center;
      border-top: 1px solid #f0f0f0;

      a {
        color: #667eea;
        text-decoration: none;
        font-size: 0.85rem;
        font-weight: 600;
        &:hover { text-decoration: underline; }
      }
    }

    /* ── Responsive ── */
    @media (max-width: 480px) {
      .notification-panel {
        position: fixed;
        top: 60px;
        left: 8px;
        right: 8px;
        width: auto;
        max-height: calc(100vh - 80px);
        border-radius: 12px;
      }
    }
  `]
})
export class NotificationCenterComponent {
  readonly store = inject(NotificationStore);
  private elementRef = inject(ElementRef);

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent) {
    if (this.store.isOpen() && !this.elementRef.nativeElement.contains(event.target)) {
      this.store.closePanel();
    }
  }

  @HostListener('document:keydown.escape')
  onEscapeKey() {
    if (this.store.isOpen()) {
      this.store.closePanel();
    }
  }

  onNotificationClick(notification: AppNotification): void {
    if (!notification.isRead) {
      this.store.markAsRead(notification.id);
    }
    if (notification.actionUrl) {
      this.store.closePanel();
      // Navigation handled via routerLink or manual navigation
    }
  }

  markRead(event: Event, id: string): void {
    event.stopPropagation();
    this.store.markAsRead(id);
  }

  deleteNotif(event: Event, id: string): void {
    event.stopPropagation();
    this.store.deleteNotification(id);
  }

  getTypeIcon(type: string): string {
    switch (type) {
      case 'order_status': return '📦';
      case 'loyalty_points': return '⭐';
      case 'offer': return '🎁';
      case 'stock_alert': return '📊';
      case 'system': return 'ℹ️';
      default: return '🔔';
    }
  }

  getTimeAgo(dateStr: string): string {
    const date = new Date(dateStr);
    const now = new Date();
    const diffMs = now.getTime() - date.getTime();
    const diffMin = Math.floor(diffMs / 60000);

    if (diffMin < 1) return 'Just now';
    if (diffMin < 60) return `${diffMin}m ago`;
    const diffHours = Math.floor(diffMin / 60);
    if (diffHours < 24) return `${diffHours}h ago`;
    const diffDays = Math.floor(diffHours / 24);
    if (diffDays < 7) return `${diffDays}d ago`;
    return date.toLocaleDateString('en-IN', { day: 'numeric', month: 'short' });
  }
}
