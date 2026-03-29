import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { LoyaltyService, LoyaltyAccount, Reward, PointsTransaction, ExternalOrderClaim } from '../../services/loyalty.service';
import { AnalyticsTrackingService } from '../../services/analytics-tracking.service';
import { UIStore } from '../../store/ui.store';
import { formatIstDate } from '../../utils/date-utils';

interface DisplayTransaction {
  date: string;
  action: string;
  points: string;
  type: 'earned' | 'redeemed';
}

interface DisplayReward extends Reward {
  icon: string;
  canRedeem: boolean;
}

@Component({
  selector: 'app-loyalty',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './loyalty.component.html',
  styleUrls: ['./loyalty.component.scss']
})
export class LoyaltyComponent implements OnInit {
  private loyaltyService = inject(LoyaltyService);
  private analyticsService = inject(AnalyticsTrackingService);
  private uiStore = inject(UIStore);

  // Account data
  currentPoints = 0;
  tier = 'Bronze';
  nextTier = 'Silver';
  pointsToNextTier = 0;

  // UI state
  loading = true;
  error: string | null = null;

  // Data
  rewardsHistory: DisplayTransaction[] = [];
  availableRewards: DisplayReward[] = [];

  // External claims
  externalClaims: ExternalOrderClaim[] = [];
  showClaimForm = false;
  claimPlatform: 'zomato' | 'swiggy' = 'zomato';
  claimTotalAmount: number | null = null;
  claimFile: File | null = null;
  claimFilePreview: string | null = null;
  submittingClaim = false;
  claimSuccess: string | null = null;
  claimError: string | null = null;

  // Icon mapping for rewards
  private rewardIcons: { [key: string]: string } = {
    'coffee': '☕',
    'burger': '🍔',
    'dessert': '🍰',
    'discount': '🎁',
    'free': '🎉'
  };

  ngOnInit() {
    this.analyticsService.trackFeatureUsage('Loyalty Page', 'Viewed loyalty page');
    this.loadLoyaltyData();
  }

  async loadLoyaltyData() {
    try {
      this.loading = true;
      this.error = null;

      // Load account data, transactions, and rewards in parallel
      const [account, transactions, rewards] = await Promise.all([
        this.loyaltyService.getLoyaltyAccount().toPromise(),
        this.loyaltyService.getTransactions().toPromise(),
        this.loyaltyService.getAvailableRewards().toPromise()
      ]);

      // Update account info
      if (account) {
        this.currentPoints = account.currentPoints;
        this.tier = account.tier;
        this.nextTier = account.nextTier || '';
        this.pointsToNextTier = account.pointsToNextTier || 0;
      }

      // Transform transactions for display
      if (transactions) {
        this.rewardsHistory = transactions.map(t => this.transformTransaction(t));
      }

      // Transform rewards for display
      if (rewards) {
        this.availableRewards = rewards.map(r => this.transformReward(r));
      }

      // Load external claims
      try {
        const claims = await this.loyaltyService.getMyExternalClaims().toPromise();
        if (claims) this.externalClaims = claims;
      } catch { /* Non-critical */ }

    } catch (err: any) {
      this.error = err.error?.message || 'Failed to load loyalty data. Please try again.';
      console.error('Error loading loyalty data:', err);
    } finally {
      this.loading = false;
    }
  }

  private transformTransaction(transaction: PointsTransaction): DisplayTransaction {
    const date = formatIstDate(new Date(transaction.createdAt), {
      month: 'short',
      day: 'numeric',
      year: 'numeric'
    });

    let action = '';
    let type: 'earned' | 'redeemed' = 'earned';

    if (transaction.type === 'earned') {
      action = transaction.description || `Order #${transaction.orderId?.substring(0, 8)}`;
      type = 'earned';
    } else {
      action = transaction.description || 'Reward Redeemed';
      type = 'redeemed';
    }

    const points = transaction.points > 0 ? `+${transaction.points}` : `${transaction.points}`;

    return { date, action, points, type };
  }

  private transformReward(reward: Reward): DisplayReward {
    // Try to find a matching icon based on reward name
    let icon = '🎁'; // default icon
    const nameLower = reward.name.toLowerCase();

    for (const [key, value] of Object.entries(this.rewardIcons)) {
      if (nameLower.includes(key)) {
        icon = value;
        break;
      }
    }

    const canRedeem = this.currentPoints >= reward.pointsCost;

    return {
      ...reward,
      icon,
      canRedeem
    };
  }

  getTierColorClass(): string {
    return this.loyaltyService.getTierColorClass(this.tier);
  }

  async redeemReward(reward: DisplayReward) {
    if (!reward.canRedeem) {
      this.uiStore.warning(`You need ${reward.pointsCost - this.currentPoints} more points to redeem this reward.`);
      return;
    }

    const confirmed = confirm(`Redeem ${reward.name} for ${reward.pointsCost} points?`);
    if (!confirmed) return;

    this.analyticsService.trackFeatureUsage('Loyalty Redeem', `Redeemed: ${reward.name} (${reward.pointsCost} pts)`);

    try {
      const response = await this.loyaltyService.redeemReward(reward.id!).toPromise();

      if (response) {
        this.uiStore.success(`Successfully redeemed: ${reward.name}\n${response.message || ''}`);
        // Reload data to reflect updated points
        await this.loadLoyaltyData();
      }
    } catch (err: any) {
      const message = err.error?.message || 'Failed to redeem reward. Please try again.';
      this.uiStore.error(message);
      console.error('Error redeeming reward:', err);
    }
  }

  trackByName(index: number, item: any): string { return item.name; }

  trackByIndex(index: number): number { return index; }

  // ─── External Claims ───

  toggleClaimForm() {
    this.showClaimForm = !this.showClaimForm;
    this.claimError = null;
    this.claimSuccess = null;
  }

  onClaimFileSelected(event: Event) {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files[0]) {
      const file = input.files[0];
      if (!file.type.startsWith('image/')) {
        this.claimError = 'Please select an image file';
        return;
      }
      if (file.size > 10 * 1024 * 1024) {
        this.claimError = 'File size must be less than 10MB';
        return;
      }
      this.claimFile = file;
      this.claimError = null;
      const reader = new FileReader();
      reader.onload = (e) => this.claimFilePreview = e.target?.result as string;
      reader.readAsDataURL(file);
    }
  }

  removeClaimFile() {
    this.claimFile = null;
    this.claimFilePreview = null;
  }

  async submitExternalClaim() {
    if (!this.claimFile) {
      this.claimError = 'Please upload an invoice screenshot';
      return;
    }
    if (!this.claimTotalAmount || this.claimTotalAmount <= 0) {
      this.claimError = 'Please enter the total amount from the invoice';
      return;
    }

    this.submittingClaim = true;
    this.claimError = null;
    this.claimSuccess = null;

    const formData = new FormData();
    formData.append('file', this.claimFile, this.claimFile.name);
    formData.append('platform', this.claimPlatform);
    formData.append('totalAmount', this.claimTotalAmount.toString());

    try {
      const res = await this.loyaltyService.submitExternalClaim(formData).toPromise();
      this.claimSuccess = res?.message || 'Claim submitted successfully!';
      this.claimFile = null;
      this.claimFilePreview = null;
      this.claimTotalAmount = null;
      this.showClaimForm = false;
      // Reload claims
      const claims = await this.loyaltyService.getMyExternalClaims().toPromise();
      if (claims) this.externalClaims = claims;
    } catch (err: any) {
      this.claimError = err.error?.error || 'Failed to submit claim. Please try again.';
    } finally {
      this.submittingClaim = false;
    }
  }

  getStatusClass(status: string): string {
    switch (status) {
      case 'approved': return 'status-approved';
      case 'rejected': return 'status-rejected';
      default: return 'status-pending';
    }
  }

  getStatusIcon(status: string): string {
    switch (status) {
      case 'approved': return '✅';
      case 'rejected': return '❌';
      default: return '⏳';
    }
  }

  formatClaimDate(date: string): string {
    return formatIstDate(new Date(date), { month: 'short', day: 'numeric', year: 'numeric' });
  }

  getPlatformIcon(platform: string): string {
    return platform === 'zomato' ? '🔴' : '🟠';
  }
}
