import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export interface LoyaltyAccount {
  id: string;
  userId: string;
  username: string;
  currentPoints: number;
  totalPointsEarned: number;
  totalPointsRedeemed: number;
  tier: string;
  nextTier?: string;
  pointsToNextTier?: number;
  createdAt: string;
  updatedAt: string;
}

export interface Reward {
  id: string;
  name: string;
  description: string;
  pointsCost: number;
  icon: string;
  isActive: boolean;
  expiresAt?: string;
  canRedeem: boolean;
}

export interface PointsTransaction {
  id: string;
  userId: string;
  points: number;
  type: string; // 'earned', 'redeemed', 'expired'
  description: string;
  orderId?: string;
  rewardId?: string;
  createdAt: string;
}

export interface RedeemResponse {
  message: string;
  account: LoyaltyAccount;
}

@Injectable({
  providedIn: 'root'
})
export class LoyaltyService {
  private http = inject(HttpClient);
  private apiUrl = environment.apiUrl;

  // Get user's loyalty account
  getLoyaltyAccount(): Observable<LoyaltyAccount> {
    return this.http.get<LoyaltyAccount>(`${this.apiUrl}/loyalty`);
  }

  // Get user's transaction history
  getTransactions(): Observable<PointsTransaction[]> {
    return this.http.get<PointsTransaction[]>(`${this.apiUrl}/loyalty/transactions`);
  }

  // Get available rewards
  getAvailableRewards(): Observable<Reward[]> {
    return this.http.get<Reward[]>(`${this.apiUrl}/loyalty/rewards`);
  }

  // Redeem a reward
  redeemReward(rewardId: string): Observable<RedeemResponse> {
    return this.http.post<RedeemResponse>(`${this.apiUrl}/loyalty/redeem/${rewardId}`, {});
  }

  // Admin: Get all loyalty accounts
  getAllLoyaltyAccounts(): Observable<LoyaltyAccount[]> {
    return this.http.get<LoyaltyAccount[]>(`${this.apiUrl}/admin/loyalty/accounts`);
  }

  // Admin: Create reward
  createReward(reward: {
    name: string;
    description: string;
    pointsCost: number;
    icon: string;
    isActive?: boolean;
    expiresAt?: string;
  }): Observable<Reward> {
    return this.http.post<Reward>(`${this.apiUrl}/admin/loyalty/rewards`, reward);
  }

  // Admin: Get all rewards
  getAllRewards(): Observable<Reward[]> {
    return this.http.get<Reward[]>(`${this.apiUrl}/admin/loyalty/rewards`);
  }

  // Admin: Update reward
  updateReward(id: string, reward: Partial<Reward>): Observable<Reward> {
    return this.http.put<Reward>(`${this.apiUrl}/admin/loyalty/rewards/${id}`, reward);
  }

  // Admin: Delete reward
  deleteReward(id: string): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`${this.apiUrl}/admin/loyalty/rewards/${id}`);
  }

  // Admin: Get all redemptions
  getAllRedemptions(): Observable<PointsTransaction[]> {
    return this.http.get<PointsTransaction[]>(`${this.apiUrl}/admin/loyalty/redemptions`);
  }

  // Helper: Get tier color class
  getTierColorClass(tier: string): string {
    switch (tier.toLowerCase()) {
      case 'platinum': return 'tier-platinum';
      case 'gold': return 'tier-gold';
      case 'silver': return 'tier-silver';
      case 'bronze': return 'tier-bronze';
      default: return 'tier-bronze';
    }
  }

  // Helper: Format transaction type
  getTransactionTypeLabel(type: string): string {
    switch (type.toLowerCase()) {
      case 'earned': return 'Earned';
      case 'redeemed': return 'Redeemed';
      case 'expired': return 'Expired';
      default: return type;
    }
  }

  // Helper: Get transaction type class
  getTransactionTypeClass(type: string): string {
    switch (type.toLowerCase()) {
      case 'earned': return 'type-earned';
      case 'redeemed': return 'type-redeemed';
      case 'expired': return 'type-expired';
      default: return '';
    }
  }
}
