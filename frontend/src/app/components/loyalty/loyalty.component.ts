import { Component, OnInit, inject, ElementRef, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LoyaltyService, LoyaltyAccount, Reward, PointsTransaction, ExternalOrderClaim } from '../../services/loyalty.service';
import { AnalyticsTrackingService } from '../../services/analytics-tracking.service';
import { UIStore } from '../../store/ui.store';
import { formatIstDate } from '../../utils/date-utils';

interface DisplayTransaction {
  date: string;
  action: string;
  points: string;
  type: 'earned' | 'redeemed' | 'expired' | 'transferred' | 'received';
  expiresAt?: string;
}

interface DisplayReward extends Reward {
  icon: string;
  canRedeem: boolean;
}

@Component({
  selector: 'app-loyalty',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './loyalty.component.html',
  styleUrls: ['./loyalty.component.scss']
})
export class LoyaltyComponent implements OnInit {
  private loyaltyService = inject(LoyaltyService);
  private analyticsService = inject(AnalyticsTrackingService);
  private uiStore = inject(UIStore);

  @ViewChild('qrCanvas') qrCanvas!: ElementRef<HTMLCanvasElement>;

  // Account data
  currentPoints = 0;
  tier = 'Bronze';
  nextTier = 'Silver';
  pointsToNextTier = 0;
  tierMultiplier = 1.0;
  tierBenefits: string[] = [];
  referralCode = '';
  totalReferrals = 0;
  loyaltyCardNumber = '';
  dateOfBirth: string | null = null;
  expiringPoints = 0;
  expiringDate: string | null = null;
  birthdayBonusAvailable = false;
  birthdayBonusPoints = 0;
  hasReferral = false;

  // UI state
  loading = true;
  error: string | null = null;
  activeTab: 'rewards' | 'card' | 'referral' | 'transfer' | 'claims' = 'rewards';

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

  // Referral
  referralInput = '';
  applyingReferral = false;
  referralCopied = false;

  // Transfer
  transferUsername = '';
  transferPoints = 0;
  transferring = false;

  // Birthday
  birthdayInput = '';
  settingBirthday = false;
  claimingBirthday = false;

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
        this.tierMultiplier = account.tierMultiplier || 1.0;
        this.tierBenefits = account.tierBenefits || [];
        this.referralCode = account.referralCode || '';
        this.totalReferrals = account.totalReferrals || 0;
        this.loyaltyCardNumber = account.loyaltyCardNumber || '';
        this.dateOfBirth = account.dateOfBirth || null;
        this.expiringPoints = account.expiringPoints || 0;
        this.expiringDate = account.expiringDate || null;
        this.birthdayBonusAvailable = account.birthdayBonusAvailable || false;
        this.birthdayBonusPoints = account.birthdayBonusPoints || 0;
        this.hasReferral = !!account.dateOfBirth; // Use dateOfBirth as proxy; referredBy not in response
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

    let action = transaction.description || '';
    let type: DisplayTransaction['type'] = 'earned';

    switch (transaction.type) {
      case 'earned':
      case 'received':
        type = transaction.type as DisplayTransaction['type'];
        if (!action) action = transaction.orderId ? `Order #${transaction.orderId.substring(0, 8)}` : 'Points earned';
        break;
      case 'redeemed':
        type = 'redeemed';
        if (!action) action = 'Reward Redeemed';
        break;
      case 'expired':
        type = 'expired';
        if (!action) action = 'Points expired';
        break;
      case 'transferred':
        type = 'transferred';
        break;
      default:
        if (!action) action = transaction.type;
    }

    const points = transaction.points > 0 ? `+${transaction.points}` : `${transaction.points}`;
    const expiresAt = transaction.expiresAt
      ? formatIstDate(new Date(transaction.expiresAt), { month: 'short', day: 'numeric', year: 'numeric' })
      : undefined;

    return { date, action, points, type, expiresAt };
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

  // ─── Referral ───
  copyReferralCode() {
    if (this.referralCode) {
      navigator.clipboard.writeText(this.referralCode);
      this.referralCopied = true;
      this.uiStore.success('Referral code copied to clipboard!');
      setTimeout(() => this.referralCopied = false, 3000);
    }
  }

  async applyReferral() {
    if (!this.referralInput.trim()) {
      this.uiStore.warning('Please enter a referral code');
      return;
    }
    this.applyingReferral = true;
    try {
      const res = await this.loyaltyService.applyReferralCode(this.referralInput.trim().toUpperCase()).toPromise();
      if (res?.success) {
        this.uiStore.success(res.message);
        this.referralInput = '';
        await this.loadLoyaltyData();
      }
    } catch (err: any) {
      this.uiStore.error(err.error?.error || 'Failed to apply referral code');
    } finally {
      this.applyingReferral = false;
    }
  }

  // ─── Transfer ───
  async doTransferPoints() {
    if (!this.transferUsername.trim()) {
      this.uiStore.warning('Please enter a recipient username');
      return;
    }
    if (!this.transferPoints || this.transferPoints < 10) {
      this.uiStore.warning('Minimum transfer is 10 points');
      return;
    }
    if (this.transferPoints > this.currentPoints) {
      this.uiStore.warning('Insufficient points');
      return;
    }

    const confirmed = confirm(`Transfer ${this.transferPoints} points to ${this.transferUsername}?`);
    if (!confirmed) return;

    this.transferring = true;
    try {
      const res = await this.loyaltyService.transferPoints(this.transferUsername.trim(), this.transferPoints).toPromise();
      if (res?.success) {
        this.uiStore.success(res.message);
        this.transferUsername = '';
        this.transferPoints = 0;
        await this.loadLoyaltyData();
      }
    } catch (err: any) {
      this.uiStore.error(err.error?.error || 'Failed to transfer points');
    } finally {
      this.transferring = false;
    }
  }

  // ─── Birthday ───
  async saveBirthday() {
    if (!this.birthdayInput) {
      this.uiStore.warning('Please select your date of birth');
      return;
    }
    this.settingBirthday = true;
    try {
      const res = await this.loyaltyService.setBirthday(this.birthdayInput).toPromise();
      if (res?.success) {
        this.uiStore.success(res.message);
        await this.loadLoyaltyData();
      }
    } catch (err: any) {
      this.uiStore.error(err.error?.error || 'Failed to set birthday');
    } finally {
      this.settingBirthday = false;
    }
  }

  async claimBirthdayBonus() {
    this.claimingBirthday = true;
    try {
      const res = await this.loyaltyService.claimBirthdayBonus().toPromise();
      if (res?.success) {
        this.uiStore.success(res.message);
        await this.loadLoyaltyData();
      }
    } catch (err: any) {
      this.uiStore.error(err.error?.error || 'Birthday bonus not available');
    } finally {
      this.claimingBirthday = false;
    }
  }

  // ─── QR Code ───
  generateQrCode() {
    if (!this.qrCanvas || !this.loyaltyCardNumber) return;
    const canvas = this.qrCanvas.nativeElement;
    const ctx = canvas.getContext('2d')!;
    const data = JSON.stringify({
      card: this.loyaltyCardNumber,
      tier: this.tier,
      points: this.currentPoints
    });
    // Simple visual QR placeholder — generates a data-matrix style pattern
    this.drawQrPattern(ctx, canvas, data);
  }

  private drawQrPattern(ctx: CanvasRenderingContext2D, canvas: HTMLCanvasElement, data: string) {
    const size = 200;
    canvas.width = size;
    canvas.height = size;
    const cellSize = 8;
    const gridSize = Math.floor(size / cellSize);

    // Create deterministic pattern from data hash
    let hash = 0;
    for (let i = 0; i < data.length; i++) {
      hash = ((hash << 5) - hash + data.charCodeAt(i)) | 0;
    }

    ctx.fillStyle = '#ffffff';
    ctx.fillRect(0, 0, size, size);

    // Draw finder patterns (3 corners)
    this.drawFinderPattern(ctx, 0, 0, cellSize);
    this.drawFinderPattern(ctx, (gridSize - 7) * cellSize, 0, cellSize);
    this.drawFinderPattern(ctx, 0, (gridSize - 7) * cellSize, cellSize);

    // Fill data cells using hash-based pattern
    ctx.fillStyle = '#1A1A2E';
    let seed = Math.abs(hash);
    for (let y = 0; y < gridSize; y++) {
      for (let x = 0; x < gridSize; x++) {
        // Skip finder pattern areas
        if ((x < 8 && y < 8) || (x >= gridSize - 8 && y < 8) || (x < 8 && y >= gridSize - 8)) continue;
        seed = (seed * 1103515245 + 12345) & 0x7fffffff;
        if (seed % 3 === 0) {
          ctx.fillRect(x * cellSize, y * cellSize, cellSize, cellSize);
        }
      }
    }
  }

  private drawFinderPattern(ctx: CanvasRenderingContext2D, x: number, y: number, cell: number) {
    // Outer dark 7x7
    ctx.fillStyle = '#1A1A2E';
    ctx.fillRect(x, y, 7 * cell, 7 * cell);
    // Inner light 5x5
    ctx.fillStyle = '#ffffff';
    ctx.fillRect(x + cell, y + cell, 5 * cell, 5 * cell);
    // Center dark 3x3
    ctx.fillStyle = '#FF6B35';
    ctx.fillRect(x + 2 * cell, y + 2 * cell, 3 * cell, 3 * cell);
  }

  getTierIcon(): string {
    return this.loyaltyService.getTierIcon(this.tier);
  }

  getShareText(): string {
    return `Join Maa Tara Cafe rewards! Use my referral code: ${this.referralCode} to get 50 bonus points!`;
  }

  shareReferral() {
    if (navigator.share) {
      navigator.share({
        title: 'Maa Tara Cafe Rewards',
        text: this.getShareText(),
      });
    } else {
      this.copyReferralCode();
    }
  }
}
