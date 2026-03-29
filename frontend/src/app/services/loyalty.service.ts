import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { handleServiceError } from '../utils/error-handler';

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

export interface ExtractedInvoiceItem {
  name: string;
  quantity: number;
  price: number;
}

export interface ExternalOrderClaim {
  id: string;
  userId: string;
  username: string;
  platform: string;
  invoiceImageUrl: string;
  extractedItems: ExtractedInvoiceItem[];
  extractedTotal: number;
  calculatedPoints: number;
  status: string;
  adminNotes?: string;
  reviewedBy?: string;
  reviewedAt?: string;
  createdAt: string;
}

export interface ExternalClaimSubmitResponse {
  message: string;
  claimId: string;
  calculatedPoints: number;
  extractedTotal: number;
}

export interface AdminClaimsResponse {
  claims: ExternalOrderClaim[];
  totalCount: number;
  pendingCount: number;
  page: number;
  pageSize: number;
}

export interface ReviewClaimResponse {
  message: string;
  claimId: string;
  status: string;
  pointsAwarded: number;
}

@Injectable({
  providedIn: 'root'
})
export class LoyaltyService {
  private http = inject(HttpClient);
  private apiUrl = environment.apiUrl;

  // Get user's loyalty account
  getLoyaltyAccount(): Observable<LoyaltyAccount> {
    return this.http.get<LoyaltyAccount>(`${this.apiUrl}/loyalty`).pipe(
      catchError(handleServiceError('LoyaltyService.getLoyaltyAccount'))
    );
  }

  // Get user's transaction history
  getTransactions(): Observable<PointsTransaction[]> {
    return this.http.get<PointsTransaction[]>(`${this.apiUrl}/loyalty/transactions`).pipe(
      catchError(handleServiceError('LoyaltyService.getTransactions'))
    );
  }

  // Get available rewards
  getAvailableRewards(): Observable<Reward[]> {
    return this.http.get<Reward[]>(`${this.apiUrl}/loyalty/rewards`).pipe(
      catchError(handleServiceError('LoyaltyService.getAvailableRewards'))
    );
  }

  // Redeem a reward
  redeemReward(rewardId: string): Observable<RedeemResponse> {
    return this.http.post<RedeemResponse>(`${this.apiUrl}/loyalty/redeem/${rewardId}`, {}).pipe(
      catchError(handleServiceError('LoyaltyService.redeemReward'))
    );
  }

  // Admin: Get all loyalty accounts
  getAllLoyaltyAccounts(): Observable<LoyaltyAccount[]> {
    return this.http.get<LoyaltyAccount[]>(`${this.apiUrl}/manage/loyalty/accounts`).pipe(
      catchError(handleServiceError('LoyaltyService.getAllLoyaltyAccounts'))
    );
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
    return this.http.post<Reward>(`${this.apiUrl}/manage/loyalty/rewards`, reward).pipe(
      catchError(handleServiceError('LoyaltyService.createReward'))
    );
  }

  // Admin: Get all rewards
  getAllRewards(): Observable<Reward[]> {
    return this.http.get<Reward[]>(`${this.apiUrl}/manage/loyalty/rewards`).pipe(
      catchError(handleServiceError('LoyaltyService.getAllRewards'))
    );
  }

  // Admin: Update reward
  updateReward(id: string, reward: Partial<Reward>): Observable<Reward> {
    return this.http.put<Reward>(`${this.apiUrl}/manage/loyalty/rewards/${id}`, reward).pipe(
      catchError(handleServiceError('LoyaltyService.updateReward'))
    );
  }

  // Admin: Delete reward
  deleteReward(id: string): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`${this.apiUrl}/manage/loyalty/rewards/${id}`).pipe(
      catchError(handleServiceError('LoyaltyService.deleteReward'))
    );
  }

  // Admin: Get all redemptions
  getAllRedemptions(): Observable<PointsTransaction[]> {
    return this.http.get<PointsTransaction[]>(`${this.apiUrl}/manage/loyalty/redemptions`).pipe(
      catchError(handleServiceError('LoyaltyService.getAllRedemptions'))
    );
  }

  // ─── External Order Claims ───

  // Submit external order claim (multipart: file + platform + totalAmount)
  submitExternalClaim(formData: FormData): Observable<ExternalClaimSubmitResponse> {
    return this.http.post<ExternalClaimSubmitResponse>(`${this.apiUrl}/loyalty/external-claim`, formData).pipe(
      catchError(handleServiceError('LoyaltyService.submitExternalClaim'))
    );
  }

  // Get my external claims
  getMyExternalClaims(): Observable<ExternalOrderClaim[]> {
    return this.http.get<ExternalOrderClaim[]>(`${this.apiUrl}/loyalty/external-claims`).pipe(
      catchError(handleServiceError('LoyaltyService.getMyExternalClaims'))
    );
  }

  // Admin: Get all external claims
  getAdminExternalClaims(status?: string, page = 1, pageSize = 20): Observable<AdminClaimsResponse> {
    let url = `${this.apiUrl}/manage/loyalty/external-claims?page=${page}&pageSize=${pageSize}`;
    if (status) url += `&status=${status}`;
    return this.http.get<AdminClaimsResponse>(url).pipe(
      catchError(handleServiceError('LoyaltyService.getAdminExternalClaims'))
    );
  }

  // Admin: Review (approve/reject) external claim
  reviewExternalClaim(claimId: string, action: 'approve' | 'reject', adminNotes?: string, overridePoints?: number): Observable<ReviewClaimResponse> {
    const body: any = { action, adminNotes };
    if (overridePoints !== undefined) body.overridePoints = overridePoints;
    return this.http.put<ReviewClaimResponse>(`${this.apiUrl}/manage/loyalty/external-claims/${claimId}/review`, body).pipe(
      catchError(handleServiceError('LoyaltyService.reviewExternalClaim'))
    );
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
