import { Component, effect, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AnalyticsConsentService } from '../../services/analytics-consent.service';
import { JourneyTrackingService } from '../../services/journey-tracking.service';

@Component({
  selector: 'app-analytics-consent',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './analytics-consent.component.html',
  styleUrl: './analytics-consent.component.scss'
})
export class AnalyticsConsentComponent {
  readonly consent = inject(AnalyticsConsentService);
  readonly tracking = inject(JourneyTrackingService);
  readonly open = signal(false);
  analyticsEnabled = false;
  linkAccount = false;

  constructor() {
    effect(() => { if (!this.consent.value()) this.open.set(true); });
    effect(() => { if (this.tracking.erasureError()) this.open.set(true); });
  }

  show(): void {
    this.analyticsEnabled = this.consent.analytics();
    this.linkAccount = this.consent.linkAccount();
    this.open.set(true);
  }

  save(analytics = this.analyticsEnabled): void {
    if (this.consent.save(analytics, analytics && this.linkAccount)) this.open.set(false);
  }
}
