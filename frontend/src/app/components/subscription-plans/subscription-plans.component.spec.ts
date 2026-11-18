import { ComponentFixture, fakeAsync, TestBed, tick } from '@angular/core/testing';
import { ActivatedRoute, Params, Router, provideRouter } from '@angular/router';
import { BehaviorSubject, of } from 'rxjs';
import { SubscriptionPlansComponent } from './subscription-plans.component';
import { CustomerSubscription, SubscriptionPlan, SubscriptionService } from '../../services/subscription.service';
import { MenuItem, MenuService } from '../../services/menu.service';
import { AuthStore } from '../../store/auth.store';
import { AuthService } from '../../services/auth.service';
import { UIStore } from '../../store/ui.store';
import { OutletService } from '../../services/outlet.service';

describe('Subscription plans presentation', () => {
  let fixture: ComponentFixture<SubscriptionPlansComponent>;
  let component: SubscriptionPlansComponent;
  let service: jasmine.SpyObj<SubscriptionService>;
  let ui: jasmine.SpyObj<UIStore>;
  let queryParams: BehaviorSubject<Params>;
  let loggedIn: boolean;
  const plans: SubscriptionPlan[] = [
    {
      id: 'breakfast', name: 'Cafe Breakfast', description: 'Fresh breakfast delivered.',
      category: 'breakfast', price: 1800, durationDays: 30, isActive: true,
      benefits: ['Freshly prepared', 'Free delivery'], badgeText: 'Morning Favourite',
      imageUrl: '/breakfast.jpg',
      includedItems: [{ menuItemId: 'coffee', menuItemName: 'Coffee', dailyQuantity: 1 }]
    },
    {
      id: 'lunch', name: 'Lunch Box', category: 'lunch', price: 2600,
      durationDays: 14, benefits: ['Rotating menu'], isActive: true
    }
  ];
  const coffee: MenuItem = {
    id: 'coffee', name: 'Cafe Coffee', description: 'Fresh coffee', categoryId: 'drinks',
    category: 'Beverages', onlinePrice: 80, webPrice: 80, isAvailable: true,
    imageThumbnailUrl: '/coffee-thumb.jpg', imageUrl: '/coffee.jpg'
  };

  beforeEach(async () => {
    loggedIn = true;
    queryParams = new BehaviorSubject<Params>({});
    service = jasmine.createSpyObj<SubscriptionService>('SubscriptionService', [
      'getPlans', 'getMySubscription', 'subscribe', 'createCustomCombo', 'pauseSubscription', 'resumeSubscription'
    ]);
    service.getPlans.and.returnValue(of(plans));
    service.getMySubscription.and.returnValue(of({ active: null, history: [] }));
    ui = jasmine.createSpyObj<UIStore>('UIStore', ['error', 'warning', 'success']);
    await TestBed.configureTestingModule({
      imports: [SubscriptionPlansComponent],
      providers: [
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { queryParams } },
        { provide: SubscriptionService, useValue: service },
        { provide: MenuService, useValue: { getMenuItems: () => of([coffee]) } },
        { provide: AuthStore, useValue: {
          isLoggedIn: () => loggedIn,
          user: () => ({ username: 'customer', firstName: 'Cafe', lastName: 'Customer', phoneNumber: '1234567890' })
        } },
        { provide: AuthService, useValue: {} },
        { provide: UIStore, useValue: ui },
        { provide: OutletService, useValue: { getSelectedOutletId: () => 'cafe' } }
      ]
    }).compileComponents();
    fixture = TestBed.createComponent(SubscriptionPlansComponent);
    component = fixture.componentInstance;
  });

  async function render(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('renders API plan prices, durations, contents and benefits without reference-image values', async () => {
    await render();
    const root: HTMLElement = fixture.nativeElement;
    expect(service.getPlans).toHaveBeenCalledWith('cafe');
    expect(root.querySelector('h1')?.textContent).toContain('Cafe Meal Subscriptions');
    expect(root.querySelectorAll('.plan-card').length).toBe(2);
    expect(root.querySelector('.plan-title')?.textContent).toBe('Cafe Breakfast');
    expect(root.querySelector('.original-price')?.textContent).toBe('₹1,800');
    expect(root.querySelector('.amount')?.textContent).toBe('1,620');
    expect(root.querySelector('.period')?.textContent).toContain('30 Days');
    expect(root.querySelector('.daily-rate-pill')?.textContent).toContain('₹54/day');
    expect(root.querySelector('.items-list')?.textContent).toContain('Coffee');
    expect(root.querySelector('.benefits-list')?.textContent).toContain('Freshly prepared');
    expect(root.querySelector('.plan-artwork img')?.getAttribute('src')).toBe('/breakfast.jpg');
    expect(root.querySelectorAll('.plan-artwork span')[0].textContent).toBe('🍱');
  });

  for (const [price, displayedTotal] of [[693, '672'], [710, '689']] as const) {
    it(`shows the weekly base price ${price} crossed out and the rounded discounted total ${displayedTotal}`, async () => {
      const weeklyPlan = { ...plans[0], price, durationDays: 7, discountPercent: 10 };
      service.getPlans.and.returnValue(of([weeklyPlan]));
      await render();
      const root: HTMLElement = fixture.nativeElement;
      expect(root.querySelector('del.original-price')?.textContent).toBe(`₹${price}`);
      expect(root.querySelector('.amount')?.textContent).toBe(displayedTotal);
      root.querySelector<HTMLButtonElement>('.btn-plan-subscribe')!.click();
      await render();
      expect(component.getCuratedCalculatedPrice().total).toBe(component.sellingPriceFor(weeklyPlan));
      expect(weeklyPlan.price).toBe(price);
      expect(service.subscribe).not.toHaveBeenCalled();
    });
  }

  it('does not cross out a price when no discount applies', async () => {
    service.getPlans.and.returnValue(of([{ ...plans[0], price: 700, durationDays: 10 }]));
    await render();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelector('.original-price')).toBeNull();
    expect(root.querySelector('.amount')?.textContent).toBe('700');
    expect(root.querySelector('.daily-rate-pill')?.textContent).toContain('₹70/day');
  });

  it('uses plan, included-item, and matching menu imagery in priority order', async () => {
    await render();
    const plan = plans[0];
    expect(component.getPlanImageUrl(plan)).toBe('/breakfast.jpg');
    expect(component.getPlanImageUrl({ ...plan, imageUrl: undefined })).toBe('/coffee-thumb.jpg');
    expect(component.getPlanImageUrl({
      ...plan, imageUrl: undefined,
      includedItems: [{ menuItemId: 'coffee', menuItemName: 'Coffee', dailyQuantity: 1, imageUrl: '/meal.jpg' }]
    })).toBe('/meal.jpg');
    expect(component.getPlanImageUrl(plans[1])).toBeUndefined();
  });

  it('preserves curated category filtering and its empty state', async () => {
    await render();
    const root: HTMLElement = fixture.nativeElement;
    root.querySelectorAll<HTMLButtonElement>('.cat-chip')[2].click();
    fixture.detectChanges();
    expect(root.querySelectorAll('.plan-card').length).toBe(1);
    expect(root.querySelector('.plan-title')?.textContent).toBe('Lunch Box');
    root.querySelectorAll<HTMLButtonElement>('.cat-chip')[4].click();
    fixture.detectChanges();
    expect(root.querySelector('.empty-state')?.textContent).toContain('No meal plans found');
  });

  it('opens the builder from the curated introduction and retains selection and pricing', async () => {
    await render();
    const root: HTMLElement = fixture.nativeElement;
    root.querySelector<HTMLButtonElement>('.btn-build-combo')!.click();
    await render();
    expect(component.activeTab).toBe('custom');
    expect(root.querySelectorAll('.nav-tab-btn')[1].getAttribute('aria-pressed')).toBe('true');
    expect(root.querySelectorAll('.combo-builder-intro').length).toBe(1);
    root.querySelector<HTMLButtonElement>('.btn-add-picker')!.click();
    await render();
    expect(component.getItemQuantity('coffee')).toBe(1);
    expect(component.comboPricingSummary).toEqual({ gross: 2400, discount: 240, finalPayable: 2160 });
    component.setTab('curated');
    component.setTab('custom');
    expect(component.getItemQuantity('coffee')).toBe(1);
    component.setMobileComboStep('review');
    expect(component.mobileComboStep).toBe('review');
    component.setMobileComboStep('menu');
    expect(component.mobileComboStep).toBe('menu');
  });

  it('retains the selected plan, customer details and existing checkout calculation', async () => {
    await render();
    const root: HTMLElement = fixture.nativeElement;
    root.querySelector<HTMLButtonElement>('.btn-plan-subscribe')!.click();
    await render();
    expect(component.selectedPlanForCheckout).toBe(plans[0]);
    expect(component.checkoutName).toBe('Cafe Customer');
    expect(component.checkoutPhone).toBe('1234567890');
    expect(component.getCuratedCalculatedPrice()).toEqual({ base: 1800, discount: 180, total: 1620, discountPct: 10 });
    expect(root.querySelector('.plan-modal-overlay')).not.toBeNull();
    expect(service.subscribe).not.toHaveBeenCalled();
    component.closePlanCheckout();
    fixture.detectChanges();
    expect(root.querySelector('.plan-modal-overlay')).toBeNull();
  });

  it('requires authentication before opening plan checkout', async () => {
    loggedIn = false;
    const navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    await render();
    component.openPlanCheckout(plans[0]);
    expect(component.selectedPlanForCheckout).toBeNull();
    expect(ui.warning).toHaveBeenCalled();
    expect(navigate).toHaveBeenCalledWith(['/login'], { queryParams: { returnUrl: '/subscriptions' } });
    expect(service.subscribe).not.toHaveBeenCalled();
  });

  it('preserves subscription management navigation and query-linked builder entry', async () => {
    queryParams.next({ tab: 'custom' });
    await render();
    expect(component.activeTab).toBe('custom');
    const root: HTMLElement = fixture.nativeElement;
    root.querySelectorAll<HTMLButtonElement>('.nav-tab-btn')[2].click();
    await render();
    expect(component.activeTab).toBe('my-subscriptions');
    expect(root.querySelector('.empty-subscription-card')).not.toBeNull();
    expect(root.querySelectorAll('.nav-tab-btn')[2].getAttribute('aria-pressed')).toBe('true');
    expect(service.getMySubscription).toHaveBeenCalledTimes(2);
  });

  const activeSubscription = (overrides: Partial<CustomerSubscription> = {}): CustomerSubscription => ({
    id: 'sub', userId: 'customer', planName: 'Lunch Box', amountPaid: 2600,
    startDate: '2026-11-01T10:00:00', endDate: '2026-12-01T10:00:00',
    status: 'active', durationDays: 30, pausesRemaining: 2, pauseLimit: 2, maxPauseDays: 7, ...overrides
  });

  it('submits the selected pause duration and displays remaining allowance and the IST resume time', async () => {
    queryParams.next({ tab: 'my' });
    const sub = activeSubscription();
    service.getMySubscription.and.returnValue(of({ active: sub, history: [] }));
    service.pauseSubscription.and.returnValue(of({ message: 'Paused for 3 days', subscription: {
      ...sub, status: 'paused', pausesRemaining: 1, pauseEndsAtUtc: new Date(Date.now() + 3 * 86400000).toISOString()
    } }));
    spyOn(window, 'confirm').and.returnValue(true);
    await render();
    const root: HTMLElement = fixture.nativeElement;
    const select = root.querySelector<HTMLSelectElement>('.pause-duration select')!;
    select.value = select.options[2].value;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    root.querySelector<HTMLButtonElement>('.btn-sub-pause')!.click();
    fixture.detectChanges();
    expect(service.pauseSubscription).toHaveBeenCalledOnceWith('sub', 3);
    expect(root.querySelector('.subscription-pause-policy')?.textContent).toContain('1 of 2 pauses remaining for this subscription');
    expect(root.querySelector('.subscription-pause-policy')?.textContent).toContain('IST');
    expect(root.querySelector('.subscription-pause-policy')?.textContent).toContain('Deliveries resume automatically');
    expect(root.querySelector('.btn-sub-resume')).not.toBeNull();
    fixture.destroy();
  });

  it('disables pausing when the subscription allowance has been used and rejects invalid durations', async () => {
    queryParams.next({ tab: 'my' });
    service.getMySubscription.and.returnValue(of({ active: activeSubscription({ pausesRemaining: 0 }), history: [] }));
    await render();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelector<HTMLButtonElement>('.btn-sub-pause')!.disabled).toBeTrue();
    expect(root.querySelector<HTMLSelectElement>('.pause-duration select')!.disabled).toBeTrue();
    component.pauseActiveSubscription();
    expect(service.pauseSubscription).not.toHaveBeenCalled();
    expect(ui.error).toHaveBeenCalledWith('You have used all pauses for this subscription.');
    component.activeSubscription = activeSubscription();
    component.pauseDays = 8;
    component.pauseActiveSubscription();
    expect(service.pauseSubscription).not.toHaveBeenCalled();
    expect(ui.error).toHaveBeenCalledWith('Choose a pause duration between 1 and 7 days for this subscription.');
  });

  it('resumes early and displays the returned extended end date', async () => {
    queryParams.next({ tab: 'my' });
    const sub = activeSubscription({ status: 'paused', pausesRemaining: 1 });
    service.getMySubscription.and.returnValue(of({ active: sub, history: [] }));
    service.resumeSubscription.and.returnValue(of({ message: 'Extended by 2 days', subscription: {
      ...sub, status: 'active', endDate: '2026-12-03T10:00:00', totalDaysPaused: 2
    } }));
    await render();
    fixture.nativeElement.querySelector('.btn-sub-resume').click();
    fixture.detectChanges();
    expect(service.resumeSubscription).toHaveBeenCalledOnceWith('sub');
    expect(component.activeSubscription?.endDate).toBe('2026-12-03T10:00:00');
    expect(component.activeSubscription?.pausesRemaining).toBe(1);
    expect(fixture.nativeElement.querySelector('.btn-sub-pause')).not.toBeNull();
  });

  it('reloads the subscription at its automatic-resume deadline', fakeAsync(() => {
    const paused = activeSubscription({
      status: 'paused', pauseEndsAtUtc: new Date(Date.now() + 2000).toISOString()
    });
    service.getMySubscription.and.returnValues(
      of({ active: paused, history: [] }),
      of({ active: activeSubscription({ totalDaysPaused: 1 }), history: [] })
    );
    fixture.detectChanges();
    expect(component.activeSubscription?.status).toBe('paused');
    tick(3001);
    expect(service.getMySubscription).toHaveBeenCalledTimes(2);
    expect(component.activeSubscription?.status).toBe('active');
    fixture.destroy();
  }));

  for (const [days, percent, maxPauseDays, limit] of [[7, 3, 3, 1], [14, 5, 5, 1], [30, 10, 7, 2]]) {
    it(`applies ${percent}% discount and ${maxPauseDays}-day pause options for the ${days}-day subscription`, async () => {
      service.getMySubscription.and.returnValue(of({ active: activeSubscription({
        durationDays: days, maxPauseDays, pauseLimit: limit, pausesRemaining: limit
      }), history: [] }));
      queryParams.next({ tab: 'my' });
      await render();
      const options = fixture.nativeElement.querySelectorAll('.pause-duration option') as NodeListOf<HTMLOptionElement>;
      expect(options.length).toBe(maxPauseDays);
      expect(component.pauseDays).toBe(maxPauseDays);
      component.pauseDays = maxPauseDays + 1;
      component.pauseActiveSubscription();
      expect(service.pauseSubscription).not.toHaveBeenCalled();
      component.openPlanCheckout(plans[0]);
      component.checkoutDurationDays = days;
      expect(component.getCuratedCalculatedPrice().discountPct).toBe(percent);
      expect(component.getCuratedCalculatedPrice().total).toBeCloseTo(60 * days * (1 - percent / 100), 2);
      component.comboDurationDays = days;
      component.comboItems.set('coffee', { menuItem: coffee, quantity: 1 });
      expect(component.comboPricingSummary.finalPayable).toBeCloseTo(80 * days * (1 - percent / 100), 2);
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('.plan-modal-overlay .duration-pill-selector').textContent).toContain('3% OFF');
      expect(fixture.nativeElement.querySelector('.plan-modal-overlay .duration-pill-selector').textContent).toContain('5% OFF');
      expect(fixture.nativeElement.querySelector('.plan-modal-overlay .duration-pill-selector').textContent).toContain('10% OFF');
    });
  }
});
