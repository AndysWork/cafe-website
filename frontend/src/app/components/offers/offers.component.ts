import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { UIStore } from '../../store/ui.store';
import { OffersService, Offer } from '../../services/offers.service';
import { AnalyticsTrackingService } from '../../services/analytics-tracking.service';
import { getIstNow, formatIstDate, getIstDaysDifference } from '../../utils/date-utils';

@Component({
  selector: 'app-offers',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './offers.component.html',
  styleUrls: ['./offers.component.scss']
})
export class OffersComponent implements OnInit {
  offers: Offer[] = [];
  loading = true;
  error: string | null = null;
  private analyticsService = inject(AnalyticsTrackingService);
  private uiStore = inject(UIStore);

  constructor(private offersService: OffersService) {}

  ngOnInit() {
    this.analyticsService.trackFeatureUsage('Offers Page', 'Viewed offers page');
    this.loadOffers();
  }

  loadOffers() {
    this.loading = true;
    this.error = null;

    this.offersService.getActiveOffers().subscribe({
      next: (offers) => {
        this.offers = offers;
        this.loading = false;
      },
      error: (err) => {
        console.error('Error loading offers:', err);
        this.error = 'Failed to load offers';
        this.loading = false;
      }
    });
  }

  copyCode(code: string) {
    navigator.clipboard.writeText(code);
    this.analyticsService.trackFeatureUsage('Offer Code Copy', `Copied code: ${code}`);
    this.uiStore.success(`Code "${code}" copied to clipboard!`);
  }

  getDiscountDisplay(offer: Offer): string {
    switch (offer.discountType) {
      case 'percentage':
        return `${offer.discountValue}% OFF`;
      case 'flat':
        return `₹${offer.discountValue} OFF`;
      case 'bogo':
        return 'BOGO';
      default:
        return 'SPECIAL';
    }
  }

  getValidityDisplay(offer: Offer): string {
    const validTill = new Date(offer.validTill);
    const now = getIstNow();
    const daysLeft = getIstDaysDifference(validTill, now);

    if (daysLeft < 0) {
      return 'Expired';
    } else if (daysLeft === 0) {
      return 'Expires today!';
    } else if (daysLeft === 1) {
      return 'Expires tomorrow!';
    } else if (daysLeft <= 7) {
      return `${daysLeft} days left`;
    } else {
      return `Valid till ${formatIstDate(validTill)}`;
    }
  }

  trackByIndex(index: number): number { return index; }

  trackById(index: number, item: any): string { return item._id; }
}
