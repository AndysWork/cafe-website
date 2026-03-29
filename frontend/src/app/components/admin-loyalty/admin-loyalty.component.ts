import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LoyaltyService, Reward, LoyaltyAccount, PointsTransaction, ExternalOrderClaim, AdminClaimsResponse } from '../../services/loyalty.service';
import { OutletService } from '../../services/outlet.service';
import { UIStore } from '../../store/ui.store';
import { Subscription } from 'rxjs';
import { filter } from 'rxjs/operators';
import { formatIstDate, formatIstDateTime } from '../../utils/date-utils';

@Component({
  selector: 'app-admin-loyalty',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './admin-loyalty.component.html',
  styleUrls: ['./admin-loyalty.component.scss']
})
export class AdminLoyaltyComponent implements OnInit, OnDestroy {
  private outletService = inject(OutletService);
  private uiStore = inject(UIStore);
  private outletSubscription?: Subscription;

  activeTab: 'rewards' | 'accounts' | 'redemptions' | 'claims' = 'rewards';

  // Rewards management
  rewards: Reward[] = [];
  showRewardModal = false;
  isEditMode = false;
  currentReward: Reward | null = null;
  rewardForm: Reward = this.getEmptyReward();

  // Loyalty accounts
  loyaltyAccounts: LoyaltyAccount[] = [];

  // Redemptions
  redemptions: PointsTransaction[] = [];

  // External Claims
  externalClaims: ExternalOrderClaim[] = [];
  claimsFilter: string = ''; // '' = all, 'pending', 'approved', 'rejected'
  claimsPendingCount = 0;
  claimsTotalCount = 0;
  claimsPage = 1;
  claimsPageSize = 20;
  reviewingClaimId: string | null = null;
  reviewNotes = '';
  reviewOverridePoints: number | null = null;
  showImageModal = false;
  modalImageUrl = '';

  loading = true;

  // Tier configuration
  tierConfig = [
    { name: 'Bronze', minPoints: 0, color: '#cd7f32', benefits: 'Basic rewards' },
    { name: 'Silver', minPoints: 500, color: '#c0c0c0', benefits: 'Better discounts' },
    { name: 'Gold', minPoints: 1500, color: '#ffd700', benefits: 'Premium perks' },
    { name: 'Platinum', minPoints: 3000, color: '#e5e4e2', benefits: 'VIP treatment' }
  ];

  constructor(private loyaltyService: LoyaltyService) {}

  ngOnInit() {
    // Subscribe to outlet changes
    this.outletSubscription = this.outletService.selectedOutlet$
      .pipe(filter(outlet => outlet !== null))
      .subscribe(() => {
        this.loadData();
      });

    // Load immediately if outlet is already selected
    if (this.outletService.getSelectedOutlet()) {
      this.loadData();
    }
  }

  ngOnDestroy() {
    this.outletSubscription?.unsubscribe();
  }

  loadData() {
    this.loading = true;

    if (this.activeTab === 'rewards') {
      this.loadRewards();
    } else if (this.activeTab === 'accounts') {
      this.loadAccounts();
    } else if (this.activeTab === 'redemptions') {
      this.loadRedemptions();
    } else if (this.activeTab === 'claims') {
      this.loadClaims();
    }
  }

  loadRewards() {
    this.loyaltyService.getAllRewards().subscribe({
      next: (rewards) => {
        this.rewards = rewards;
        this.loading = false;
      },
      error: (err) => {
        console.error('Error loading rewards:', err);
        this.uiStore.error('Failed to load rewards');
        this.loading = false;
      }
    });
  }

  loadAccounts() {
    this.loyaltyService.getAllLoyaltyAccounts().subscribe({
      next: (accounts) => {
        this.loyaltyAccounts = accounts;
        this.loading = false;
      },
      error: (err) => {
        console.error('Error loading accounts:', err);
        this.uiStore.error('Failed to load loyalty accounts');
        this.loading = false;
      }
    });
  }

  loadRedemptions() {
    this.loyaltyService.getAllRedemptions().subscribe({
      next: (redemptions) => {
        this.redemptions = redemptions;
        this.loading = false;
      },
      error: (err) => {
        console.error('Error loading redemptions:', err);
        this.uiStore.error('Failed to load redemptions');
        this.loading = false;
      }
    });
  }

  switchTab(tab: 'rewards' | 'accounts' | 'redemptions' | 'claims') {
    this.activeTab = tab;
    this.loadData();
  }

  getEmptyReward(): Reward {
    return {
      id: '',
      name: '',
      description: '',
      pointsCost: 0,
      icon: '🎁',
      isActive: true,
      canRedeem: false,
      expiresAt: undefined
    };
  }

  openCreateModal() {
    this.isEditMode = false;
    this.rewardForm = this.getEmptyReward();
    this.showRewardModal = true;
  }

  openEditModal(reward: Reward) {
    this.isEditMode = true;
    this.currentReward = reward;
    this.rewardForm = { ...reward };
    this.showRewardModal = true;
  }

  closeModal() {
    this.showRewardModal = false;
    this.currentReward = null;
    this.rewardForm = this.getEmptyReward();
  }

  saveReward() {
    if (this.isEditMode && this.currentReward?.id) {
      this.loyaltyService.updateReward(this.currentReward.id, this.rewardForm).subscribe({
        next: () => {
          this.uiStore.success('Reward updated successfully!');
          this.loadRewards();
          this.closeModal();
        },
        error: (err) => {
          console.error('Error updating reward:', err);
          this.uiStore.error('Failed to update reward');
        }
      });
    } else {
      this.loyaltyService.createReward(this.rewardForm).subscribe({
        next: () => {
          this.uiStore.success('Reward created successfully!');
          this.loadRewards();
          this.closeModal();
        },
        error: (err) => {
          console.error('Error creating reward:', err);
          this.uiStore.error('Failed to create reward');
        }
      });
    }
  }

  deleteReward(id: string) {
    if (confirm('Are you sure you want to delete this reward?')) {
      this.loyaltyService.deleteReward(id).subscribe({
        next: () => {
          this.uiStore.success('Reward deleted successfully!');
          this.loadRewards();
        },
        error: (err) => {
          console.error('Error deleting reward:', err);
          this.uiStore.error('Failed to delete reward');
        }
      });
    }
  }

  toggleRewardActive(reward: Reward) {
    if (reward.id) {
      const updated = { ...reward, isActive: !reward.isActive };
      this.loyaltyService.updateReward(reward.id, updated).subscribe({
        next: () => {
          this.loadRewards();
        },
        error: (err) => {
          console.error('Error toggling reward status:', err);
          this.uiStore.error('Failed to update reward status');
        }
      });
    }
  }

  getTierBadgeClass(tier: string): string {
    return `tier-${tier.toLowerCase()}`;
  }

  formatDate(date: Date | string): string {
    return formatIstDate(date);
  }

  formatDateTime(date: Date | string): string {
    return formatIstDateTime(date);
  }

  getAccountStats() {
    const total = this.loyaltyAccounts.length;
    const byTier: { [key: string]: number } = {
      Bronze: this.loyaltyAccounts.filter(a => a.tier === 'Bronze').length,
      Silver: this.loyaltyAccounts.filter(a => a.tier === 'Silver').length,
      Gold: this.loyaltyAccounts.filter(a => a.tier === 'Gold').length,
      Platinum: this.loyaltyAccounts.filter(a => a.tier === 'Platinum').length
    };
    const totalPoints = this.loyaltyAccounts.reduce((sum, a) => sum + a.currentPoints, 0);

    return { total, byTier, totalPoints };
  }

  getRedemptionStats() {
    const total = this.redemptions.length;
    const totalPoints = this.redemptions.reduce((sum, r) => sum + Math.abs(r.points), 0);

    return { total, totalPoints };
  }

  getAbsolutePoints(points: number): number {
    return Math.abs(points);
  }

  trackByObjId(index: number, item: any): string { return item.id; }
  trackByName(index: number, item: any): string { return item.name; }
  trackByIndex(index: number): number { return index; }

  // ─── External Claims ───

  loadClaims() {
    const status = this.claimsFilter || undefined;
    this.loyaltyService.getAdminExternalClaims(status, this.claimsPage, this.claimsPageSize).subscribe({
      next: (res: AdminClaimsResponse) => {
        this.externalClaims = res.claims;
        this.claimsTotalCount = res.totalCount;
        this.claimsPendingCount = res.pendingCount;
        this.loading = false;
      },
      error: (err) => {
        console.error('Error loading claims:', err);
        this.uiStore.error('Failed to load external claims');
        this.loading = false;
      }
    });
  }

  filterClaims(status: string) {
    this.claimsFilter = status;
    this.claimsPage = 1;
    this.loadClaims();
  }

  openImageModal(imageUrl: string) {
    this.modalImageUrl = imageUrl;
    this.showImageModal = true;
  }

  closeImageModal() {
    this.showImageModal = false;
    this.modalImageUrl = '';
  }

  startReview(claimId: string) {
    this.reviewingClaimId = claimId;
    this.reviewNotes = '';
    this.reviewOverridePoints = null;
  }

  cancelReview() {
    this.reviewingClaimId = null;
    this.reviewNotes = '';
    this.reviewOverridePoints = null;
  }

  reviewClaim(claimId: string, action: 'approve' | 'reject') {
    const claim = this.externalClaims.find(c => c.id === claimId);
    const label = action === 'approve'
      ? `Approve claim for ${claim?.username}? ${this.reviewOverridePoints ? this.reviewOverridePoints : claim?.calculatedPoints} points will be credited.`
      : `Reject claim for ${claim?.username}?`;

    if (!confirm(label)) return;

    this.loyaltyService.reviewExternalClaim(
      claimId, action, this.reviewNotes || undefined,
      action === 'approve' && this.reviewOverridePoints ? this.reviewOverridePoints : undefined
    ).subscribe({
      next: (res) => {
        this.uiStore.success(res.message);
        this.reviewingClaimId = null;
        this.reviewNotes = '';
        this.reviewOverridePoints = null;
        this.loadClaims();
      },
      error: (err) => {
        console.error('Error reviewing claim:', err);
        this.uiStore.error(err.error?.error || 'Failed to review claim');
      }
    });
  }

  getClaimStatusClass(status: string): string {
    switch (status) {
      case 'approved': return 'status-approved';
      case 'rejected': return 'status-rejected';
      default: return 'status-pending';
    }
  }

  getClaimPages(): number {
    return Math.ceil(this.claimsTotalCount / this.claimsPageSize);
  }

  goToClaimsPage(page: number) {
    if (page < 1 || page > this.getClaimPages()) return;
    this.claimsPage = page;
    this.loadClaims();
  }
}
