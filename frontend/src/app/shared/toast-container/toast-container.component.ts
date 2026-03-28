import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { UIStore } from '../../store/ui.store';

@Component({
  selector: 'app-toast-container',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="toast-container" aria-live="polite" aria-atomic="true">
      @for (notification of uiStore.notifications(); track notification.id) {
        <div class="toast toast-{{ notification.type }}" role="alert">
          <div class="toast-body">
            <span class="toast-icon">
              @switch (notification.type) {
                @case ('success') { ✓ }
                @case ('error') { ✕ }
                @case ('warning') { ⚠ }
                @default { ℹ }
              }
            </span>
            <span class="toast-message">{{ notification.message }}</span>
            <button
              class="toast-close"
              (click)="uiStore.dismissNotification(notification.id)"
              aria-label="Dismiss notification">
              ×
            </button>
          </div>
        </div>
      }
    </div>
  `,
  styles: [`
    .toast-container {
      position: fixed;
      top: 1rem;
      right: 1rem;
      z-index: 9999;
      display: flex;
      flex-direction: column;
      gap: 0.5rem;
      max-width: 400px;
    }
    .toast {
      padding: 0.75rem 1rem;
      border-radius: 8px;
      box-shadow: 0 4px 12px rgba(0,0,0,0.15);
      animation: slideIn 0.3s ease-out;
      color: #fff;
    }
    .toast-success { background: #16a34a; }
    .toast-error { background: #dc2626; }
    .toast-warning { background: #d97706; }
    .toast-info { background: #2563eb; }
    .toast-body {
      display: flex;
      align-items: center;
      gap: 0.5rem;
    }
    .toast-icon { font-weight: bold; font-size: 1.1rem; }
    .toast-message { flex: 1; font-size: 0.9rem; }
    .toast-close {
      background: none;
      border: none;
      color: inherit;
      font-size: 1.2rem;
      cursor: pointer;
      opacity: 0.8;
      padding: 0 0.25rem;
    }
    .toast-close:hover { opacity: 1; }
    @keyframes slideIn {
      from { transform: translateX(100%); opacity: 0; }
      to { transform: translateX(0); opacity: 1; }
    }
  `]
})
export class ToastContainerComponent {
  readonly uiStore = inject(UIStore);
}
