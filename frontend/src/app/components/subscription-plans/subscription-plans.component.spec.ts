import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Params, Router, provideRouter } from '@angular/router';
import { BehaviorSubject, of } from 'rxjs';
import { SubscriptionPlansComponent } from './subscription-plans.component';
import { SubscriptionPlan, SubscriptionService } from '../../services/subscription.service';
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
      'getPlans', 'getMySubscription', 'subscribe', 'createCustomCombo'
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
    expect(root.querySelector('.amount')?.textContent).toBe('1,800');
    expect(root.querySelector('.period')?.textContent).toContain('30 Days');
    expect(root.querySelector('.daily-rate-pill')?.textContent).toContain('₹60/day');
    expect(root.querySelector('.items-list')?.textContent).toContain('Coffee');
    expect(root.querySelector('.benefits-list')?.textContent).toContain('Freshly prepared');
    expect(root.querySelector('.plan-artwork img')?.getAttribute('src')).toBe('/breakfast.jpg');
    expect(root.querySelectorAll('.plan-artwork span')[0].textContent).toBe('🍱');
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
    expect(component.comboPricingSummary).toEqual({ gross: 2400, discount: 480, finalPayable: 1920 });
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
    expect(component.getCuratedCalculatedPrice()).toEqual({ base: 1800, discount: 360, total: 1440, discountPct: 20 });
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
});
