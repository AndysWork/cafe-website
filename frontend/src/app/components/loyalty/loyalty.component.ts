import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { LoyaltyService, LoyaltyAccount, Reward, PointsTransaction } from '../../services/loyalty.service';
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
  imports: [CommonModule, RouterLink],
  templateUrl: './loyalty.component.html',
  styleUrls: ['./loyalty.component.scss']
})
export class LoyaltyComponent implements OnInit {
  private loyaltyService = inject(LoyaltyService);

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

  // Icon mapping for rewards
  private rewardIcons: { [key: string]: string } = {
    'coffee': '☕',
    'burger': '🍔',
    'dessert': '🍰',
    'discount': '🎁',
    'free': '🎉'
  };

  ngOnInit() {
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
      alert(`You need ${reward.pointsCost - this.currentPoints} more points to redeem this reward.`);
      return;
    }

    const confirmed = confirm(`Redeem ${reward.name} for ${reward.pointsCost} points?`);
    if (!confirmed) return;

    try {
      const response = await this.loyaltyService.redeemReward(reward.id!).toPromise();

      if (response) {
        alert(`Successfully redeemed: ${reward.name}\n${response.message || ''}`);
        // Reload data to reflect updated points
        await this.loadLoyaltyData();
      }
    } catch (err: any) {
      const message = err.error?.message || 'Failed to redeem reward. Please try again.';
      alert(message);
      console.error('Error redeeming reward:', err);
    }
  }
}
