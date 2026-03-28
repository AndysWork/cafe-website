import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-empty-state',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="empty-state-container">
      <div class="empty-icon" aria-hidden="true">{{ icon }}</div>
      <h3 class="empty-title">{{ title }}</h3>
      <p class="empty-message" *ngIf="message">{{ message }}</p>
      <ng-content></ng-content>
    </div>
  `,
  styles: [`
    .empty-state-container {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      padding: 48px 24px;
      text-align: center;
    }
    .empty-icon { font-size: 56px; margin-bottom: 16px; }
    .empty-title { font-size: 18px; font-weight: 600; color: #374151; margin: 0 0 8px; }
    .empty-message { font-size: 14px; color: #6b7280; margin: 0 0 16px; }
  `]
})
export class EmptyStateComponent {
  @Input() icon = '📭';
  @Input() title = 'Nothing here yet';
  @Input() message = '';
}
