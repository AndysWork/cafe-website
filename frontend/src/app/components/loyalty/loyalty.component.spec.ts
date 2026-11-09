import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, Subject, throwError } from 'rxjs';
import { LoyaltyComponent } from './loyalty.component';
import { LoyaltyAccount, LoyaltyService, RedeemResponse, Reward } from '../../services/loyalty.service';
import { AnalyticsTrackingService } from '../../services/analytics-tracking.service';
import { UIStore } from '../../store/ui.store';

describe('Loyalty rewards presentation', () => {
  let fixture: ComponentFixture<LoyaltyComponent>;
  let component: LoyaltyComponent;
  let service: jasmine.SpyObj<LoyaltyService>;
  let ui: jasmine.SpyObj<UIStore>;
  const account: LoyaltyAccount = {
    id: 'account', userId: 'customer', username: 'customer', currentPoints: 86,
    totalPointsEarned: 86, totalPointsRedeemed: 0, tier: 'Bronze', nextTier: 'Silver',
    pointsToNextTier: 314, referralCode: 'CAFE', totalReferrals: 0,
    loyaltyCardNumber: 'MTC-123', tierMultiplier: 1, tierBenefits: ['Earn points'],
    expiringPoints: 0, birthdayBonusAvailable: false, birthdayBonusPoints: 50,
    createdAt: '', updatedAt: ''
  };
  const rewards: Reward[] = [
    { id: 'coffee', name: 'Free Coffee', description: 'A coffee of your choice.', pointsCost: 100, icon: 'coffee', isActive: true, canRedeem: false },
    { id: 'discount', name: '10% Off Next Order', description: 'Save on your next order.', pointsCost: 50, icon: 'discount', isActive: true, canRedeem: true }
  ];

  beforeEach(async () => {
    service = jasmine.createSpyObj<LoyaltyService>('LoyaltyService', [
      'getLoyaltyAccount', 'getTransactions', 'getAvailableRewards', 'getMyExternalClaims',
      'getTierIcon', 'getTierColorClass', 'redeemReward'
    ]);
    ui = jasmine.createSpyObj<UIStore>('UIStore', ['success', 'warning', 'error']);
    service.getLoyaltyAccount.and.returnValue(of(account));
    service.getTransactions.and.returnValue(of([
      { id: 'old', userId: 'customer', points: 100, type: 'earned', description: 'Old order', createdAt: '2026-10-25T10:00:00Z' },
      { id: 'new', userId: 'customer', points: -100, type: 'redeemed', description: 'Redeemed: Free Coffee', createdAt: '2026-10-27T10:00:00Z' }
    ]));
    service.getAvailableRewards.and.returnValue(of(rewards));
    service.getMyExternalClaims.and.returnValue(of([]));
    service.getTierIcon.and.returnValue('Medal');
    service.getTierColorClass.and.returnValue('bronze');
    service.redeemReward.and.returnValue(of({ message: 'Reward ready', account }));
    await TestBed.configureTestingModule({
      imports: [LoyaltyComponent],
      providers: [
        provideRouter([]),
        { provide: LoyaltyService, useValue: service },
        { provide: UIStore, useValue: ui },
        { provide: AnalyticsTrackingService, useValue: { trackFeatureUsage: jasmine.createSpy('trackFeatureUsage') } }
      ]
    }).compileComponents();
    fixture = TestBed.createComponent(LoyaltyComponent);
    component = fixture.componentInstance;
  });

  async function render(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('renders actual points, tier, target and reward costs rather than reference-image values', async () => {
    await render();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelector('h1')?.textContent).toContain('MTC Rewards');
    expect(root.querySelector('.points-number')?.textContent).toContain('86');
    expect(root.querySelector('.progress-text')?.textContent).toContain('Silver');
    expect(root.querySelector('.progress-count')?.textContent).toContain('86 / 400');
    expect(root.querySelector('[role="progressbar"]')?.getAttribute('aria-valuenow')).toBe('21.5');
    expect(root.querySelectorAll('.reward-card').length).toBe(2);
    expect(root.querySelectorAll('.reward-cost')[1].textContent).toContain('50 pts');
    expect(root.querySelectorAll<HTMLButtonElement>('.btn-redeem')[0].disabled).toBeTrue();
    expect(root.querySelectorAll<HTMLButtonElement>('.btn-redeem')[1].disabled).toBeFalse();
    expect(root.querySelector('.discount-art')).not.toBeNull();
    expect(root.querySelector('.rewards-back')?.getAttribute('href')).toBe('/profile');
  });

  it('shows the latest history entry first and expands all history on demand', async () => {
    await render();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelectorAll('.history-item').length).toBe(1);
    expect(root.querySelector('.history-action')?.textContent).toBe('Redeemed: Free Coffee');
    expect(root.querySelector('.history-points')?.textContent).toBe('-100 pts');
    root.querySelector<HTMLButtonElement>('.history-toggle')!.click();
    fixture.detectChanges();
    expect(root.querySelectorAll('.history-item').length).toBe(2);
    expect(root.querySelector('.history-toggle')?.getAttribute('aria-expanded')).toBe('true');
    root.querySelector<HTMLButtonElement>('.history-toggle')!.click();
    fixture.detectChanges();
    expect(root.querySelectorAll('.history-item').length).toBe(1);
  });

  it('preserves all five sections and generates the QR after the card canvas appears', async () => {
    await render();
    const qr = spyOn(component, 'generateQrCode');
    const buttons: NodeListOf<HTMLButtonElement> = fixture.nativeElement.querySelectorAll('.tab-btn');
    expect(buttons.length).toBe(5);
    buttons[1].click();
    fixture.detectChanges();
    expect(component.activeTab).toBe('card');
    expect(fixture.nativeElement.querySelector('canvas')).not.toBeNull();
    expect(qr).toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('Earn points');
    for (const [index, tab] of [[2, 'referral'], [3, 'transfer'], [4, 'claims']] as const) {
      buttons[index].click();
      fixture.detectChanges();
      expect(component.activeTab).toBe(tab);
      expect(buttons[index].getAttribute('aria-pressed')).toBe('true');
    }
  });

  it('handles zero-point progress and maximum tier without invalid percentages', async () => {
    service.getLoyaltyAccount.and.returnValue(of({ ...account, currentPoints: 0, pointsToNextTier: 0, nextTier: undefined }));
    await render();
    expect(component.tierProgressPercent).toBe(0);
    expect(fixture.nativeElement.querySelector('[role="progressbar"]')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Maximum tier achieved!');
  });

  it('shows empty reward and history states without invented entries', async () => {
    service.getAvailableRewards.and.returnValue(of([]));
    service.getTransactions.and.returnValue(of([]));
    await render();
    expect(fixture.nativeElement.querySelectorAll('.reward-card').length).toBe(0);
    expect(fixture.nativeElement.textContent).toContain('No Rewards Available');
    expect(fixture.nativeElement.querySelector('.history-empty')).not.toBeNull();
  });

  it('shows a load failure with a retry instead of a success-shaped empty account', async () => {
    spyOn(console, 'error');
    service.getLoyaltyAccount.and.returnValue(throwError(() => new Error('offline')));
    await render();
    expect(fixture.nativeElement.querySelector('.error-card')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.loyalty-content')).toBeNull();
    service.getLoyaltyAccount.and.returnValue(of(account));
    await component.loadLoyaltyData();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.loyalty-content')).not.toBeNull();
  });

  it('retains confirmation and balance checks before redemption', async () => {
    await render();
    const confirm = spyOn(window, 'confirm').and.returnValue(false);
    await component.redeemReward(component.availableRewards[0]);
    expect(ui.warning).toHaveBeenCalled();
    expect(confirm).not.toHaveBeenCalled();
    await component.redeemReward(component.availableRewards[1]);
    expect(confirm).toHaveBeenCalled();
    expect(service.redeemReward).not.toHaveBeenCalled();
    confirm.and.returnValue(true);
    await component.redeemReward(component.availableRewards[1]);
    expect(service.redeemReward).toHaveBeenCalledOnceWith('discount');
    expect(ui.success).toHaveBeenCalled();
    expect(service.getLoyaltyAccount.calls.count()).toBe(2);
    expect(component.redeemingRewardId).toBeNull();
  });

  it('blocks duplicate redemptions while pending and restores controls after failure', async () => {
    await render();
    spyOn(window, 'confirm').and.returnValue(true);
    spyOn(console, 'error');
    const response = new Subject<RedeemResponse>();
    service.redeemReward.and.returnValue(response);
    const pending = component.redeemReward(component.availableRewards[1]);
    fixture.detectChanges();
    expect(component.redeemingRewardId).toBe('discount');
    expect(fixture.nativeElement.querySelectorAll('.btn-redeem')[1].disabled).toBeTrue();
    await component.redeemReward(component.availableRewards[1]);
    expect(service.redeemReward).toHaveBeenCalledTimes(1);
    response.error(new Error('offline'));
    await pending;
    fixture.detectChanges();
    expect(ui.error).toHaveBeenCalled();
    expect(component.redeemingRewardId).toBeNull();
    expect(fixture.nativeElement.querySelectorAll('.btn-redeem')[1].disabled).toBeFalse();
  });
});
