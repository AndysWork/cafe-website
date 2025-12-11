import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-loyalty',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './loyalty.component.html',
  styleUrls: ['./loyalty.component.scss']
})
export class LoyaltyComponent {
  currentPoints = 350;
  tier = 'Gold';
  nextTier = 'Platinum';
  pointsToNextTier = 150;

  rewardsHistory = [
    { date: 'Dec 10, 2025', action: 'Order #ORD001', points: '+50', type: 'earned' },
    { date: 'Dec 9, 2025', action: 'Order #ORD002', points: '+20', type: 'earned' },
    { date: 'Dec 8, 2025', action: 'Redeemed: Free Coffee', points: '-100', type: 'redeemed' },
    { date: 'Dec 5, 2025', action: 'Order #ORD003', points: '+80', type: 'earned' }
  ];

  availableRewards = [
    { name: 'Free Coffee', points: 100, icon: '☕' },
    { name: 'Free Burger', points: 200, icon: '🍔' },
    { name: '10% Off Next Order', points: 150, icon: '🎁' },
    { name: 'Free Dessert', points: 120, icon: '🍰' }
  ];

  redeemReward(reward: any) {
    if (this.currentPoints >= reward.points) {
      alert(`Successfully redeemed: ${reward.name}`);
    } else {
      alert(`You need ${reward.points - this.currentPoints} more points to redeem this reward.`);
    }
  }
}
