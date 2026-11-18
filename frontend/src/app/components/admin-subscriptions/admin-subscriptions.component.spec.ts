import { ComponentFixture, TestBed } from '@angular/core/testing';
import { BehaviorSubject, of, Subject, throwError } from 'rxjs';
import { AdminSubscriptionsComponent } from './admin-subscriptions.component';
import { CustomerSubscription, SubscriptionMenuSet, SubscriptionPlan, SubscriptionService } from '../../services/subscription.service';
import { MenuItem, MenuService } from '../../services/menu.service';
import { OutletService } from '../../services/outlet.service';
import { UIStore } from '../../store/ui.store';

describe('Admin subscription workflow', () => {
  let fixture: ComponentFixture<AdminSubscriptionsComponent>;
  let component: AdminSubscriptionsComponent;
  let api: jasmine.SpyObj<SubscriptionService>;
  let ui: jasmine.SpyObj<UIStore>;
  let outlets: BehaviorSubject<{ id: string }>;
  const coffee: MenuItem = { id: 'coffee', name: 'Coffee', description: '', categoryId: 'drinks',
    category: 'Drinks', onlinePrice: 80, webPrice: 80, makingPrice: 20, isAvailable: true };
  const plan: SubscriptionPlan = {
    id: 'plan', name: 'Cafe breakfast', description: 'A fresh start', price: 700, durationDays: 7,
    isActive: true, benefits: ['Custom benefit'], category: 'breakfast', freeDelivery: false,
    imageUrl: '/breakfast.jpg', badgeText: 'Popular', dailyItemLimit: 3,
    includedItems: [{ menuItemId: 'coffee', menuItemName: 'Coffee', dailyQuantity: 2,
      imageUrl: '/coffee.jpg', unitPrice: 80, categoryName: 'Drinks', isVeg: true, servingDays: ['Mon', 'Wed'] }]
  };

  beforeEach(async () => {
    outlets = new BehaviorSubject({ id: 'cafe' });
    api = jasmine.createSpyObj<SubscriptionService>('SubscriptionService', [
      'getAllPlans', 'getOverview', 'getPendingPayments', 'getMenuSets', 'saveMenuSet', 'deleteMenuSet',
      'createPlan', 'updatePlan', 'deletePlan', 'confirmPayment'
    ]);
    api.getAllPlans.and.returnValue(of([plan]));
    api.getOverview.and.returnValue(of({
      totalSubscriptions: 37, newSubscriptionsThisMonth: 5, previousMonthSubscriptions: 4,
      monthlyRevenue: 4125, previousMonthRevenue: 3000, planSubscribers: { plan: 12 }
    }));
    api.getPendingPayments.and.returnValue(of([]));
    api.getMenuSets.and.returnValue(of([]));
    api.createPlan.and.returnValue(of(plan));
    api.updatePlan.and.returnValue(of(plan));
    api.saveMenuSet.and.returnValue(of({ id: 'set', name: 'Breakfast', items: plan.includedItems! }));
    api.deleteMenuSet.and.returnValue(of(undefined));
    api.confirmPayment.and.returnValue(of({ message: 'Payment confirmed', subscription: {} as CustomerSubscription }));
    ui = jasmine.createSpyObj<UIStore>('UIStore', ['error', 'success', 'warning']);
    await TestBed.configureTestingModule({
      imports: [AdminSubscriptionsComponent],
      providers: [
        { provide: SubscriptionService, useValue: api },
        { provide: UIStore, useValue: ui },
        { provide: MenuService, useValue: { getMenuItems: () => of([coffee]) } },
        { provide: OutletService, useValue: {
          selectedOutlet$: outlets, getSelectedOutlet: () => outlets.value, getSelectedOutletId: () => outlets.value.id
        } }
      ]
    }).compileComponents();
    fixture = TestBed.createComponent(AdminSubscriptionsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
  });

  afterEach(() => fixture.destroy());

  it('renders live metrics, plan subscribers and filters without duplicate outlet loads', () => {
    expect(api.getAllPlans).toHaveBeenCalledTimes(1);
    expect(fixture.nativeElement.textContent).toContain('4,125');
    expect(fixture.nativeElement.querySelector('.plans-table').textContent).toContain('12');
    component.planSearch = 'missing';
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('No plans match');
    component.resetFilters();
    component.plans = [...Array(11)].map((_, i) => ({ ...plan, id: `${i}`, isDraft: i === 0, isActive: i !== 0 }));
    component.page = 2;
    expect(component.pagePlans.length).toBe(1);
    component.statusFilter = 'draft';
    component.page = 1;
    expect(component.pagePlans.map(p => p.id)).toEqual(['0']);
  });

  for (const [price, displayedTotal, dailyPrice] of [[693, '672', '96.03'], [710, '689', '98.39']] as const) {
    it(`shows the original ${price} crossed out and the discounted total rounded to ${displayedTotal}`, () => {
      component.plans = [{ ...plan, price, discountPercent: 10 }];
      fixture.detectChanges();
      const cell: HTMLElement = fixture.nativeElement.querySelector('.price-cell');
      expect(cell.querySelector('del')?.textContent).toBe(`₹${price}`);
      expect(cell.querySelector('strong')?.textContent).toBe(`₹${displayedTotal}`);
      expect(cell.querySelector('small')?.textContent).toBe(`(₹${dailyPrice}/day)`);
      component.openEditModal(component.plans[0]);
      expect(component.sellingPriceFor(component.plans[0])).toBe(component.sellingPrice);
      expect(component.plans[0].price).toBe(price);
    });
  }

  it('does not cross out the original price when no discount applies', () => {
    component.plans = [{ ...plan, durationDays: 10 }];
    fixture.detectChanges();
    const cell: HTMLElement = fixture.nativeElement.querySelector('.price-cell');
    expect(cell.querySelector('del')).toBeNull();
    expect(cell.querySelector('strong')?.textContent).toBe('₹700');
    expect(cell.querySelector('small')?.textContent).toBe('(₹70/day)');
  });

  it('does not present unavailable metrics as zero', () => {
    api.getOverview.and.returnValue(throwError(() => new Error('offline')));
    component.reload();
    fixture.detectChanges();
    expect(component.overview).toBeNull();
    expect(fixture.nativeElement.querySelector('[role=alert]').textContent).toContain('unavailable');
    expect(ui.error).toHaveBeenCalled();
  });

  it('validates each wizard step and renders the full-page menu and review', async () => {
    component.openCreateModal();
    component.nextStep();
    expect(component.editorStep).toBe('details');
    expect(component.formError).toContain('name');
    component.planForm.name = 'Breakfast';
    component.planForm.description = 'Made fresh';
    component.planForm.priceInput = 700;
    component.nextStep();
    fixture.detectChanges();
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('.menu-workspace')).not.toBeNull();
    component.nextStep();
    expect(component.editorStep).toBe('menu');
    component.toggleMenuItem(coffee);
    component.nextStep();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.review-workspace').textContent).toContain('679.00');
    expect(fixture.nativeElement.querySelector('.profitability').textContent).toContain('140.00');
  });

  it('preserves metadata and canonical price while editing per-day plans', () => {
    component.openEditModal({ ...plan, price: 1000, pricingMode: 'per_day' });
    component.savePlan();
    const payload = api.updatePlan.calls.mostRecent().args[1];
    expect(payload).toEqual(jasmine.objectContaining({
      price: 1000, pricingMode: 'per_day', category: 'breakfast', imageUrl: '/breakfast.jpg',
      badgeText: 'Popular', dailyItemLimit: 3, freeDelivery: false
    }));
    expect(payload.includedItems).toEqual(plan.includedItems);
    expect(plan.includedItems?.[0].servingDays).toEqual(['Mon', 'Wed']);
  });

  it('keeps the same canonical price when switching pricing modes and persists drafts as inactive', () => {
    component.openEditModal(plan);
    component.setPricingMode('per_day');
    expect(component.planForm.priceInput).toBe(100);
    component.savePlan(true);
    expect(api.updatePlan.calls.mostRecent().args[1]).toEqual(jasmine.objectContaining({
      price: 700, pricingMode: 'per_day', isDraft: true, isActive: false
    }));
  });

  it('supports real serving-day selection, schedule views and quantity-aware food costs', () => {
    component.openEditModal(plan);
    component.planForm.includedItems[0].servingDays = ['Mon'];
    expect(component.estimatedFoodCost).toBe(40);
    component.toggleServingDay(component.planForm.includedItems[0], 'Wed');
    component.changeQuantity(component.planForm.includedItems[0], 1);
    expect(component.estimatedFoodCost).toBe(120);
    component.editorStep = 'menu';
    component.scheduleView = 'list';
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.schedule-list').textContent).toContain('Coffee ×3');
    component.toggleServingDay(component.planForm.includedItems[0], 'Mon');
    component.toggleServingDay(component.planForm.includedItems[0], 'Wed');
    component.nextStep();
    expect(component.editorStep).toBe('menu');
    expect(component.formError).toContain('serving days');
  });

  it('withholds profitability when any item has missing costs instead of treating it as zero', () => {
    component.openEditModal(plan);
    component.menuItems = [{ ...coffee, makingPrice: undefined }];
    expect(component.estimatedFoodCost).toBeNull();
    expect(component.grossMargin).toBeNull();
    expect(component.marginPercent).toBeNull();
    component.menuItems = [{ ...coffee, makingPrice: 0 }];
    expect(component.estimatedFoodCost).toBe(0);
  });

  it('saves, applies and deletes database-backed menu sets without mutating their schedule', () => {
    component.openEditModal(plan);
    component.setName = 'Breakfast';
    component.saveMenuSet();
    expect(api.saveMenuSet).toHaveBeenCalledWith('Breakfast', plan.includedItems!);
    const set: SubscriptionMenuSet = { id: 'set', name: 'Breakfast', items: plan.includedItems! };
    spyOn(window, 'confirm').and.returnValue(true);
    component.applyMenuSet(set);
    component.toggleServingDay(component.planForm.includedItems[0], 'Tue');
    expect(set.items[0].servingDays).toEqual(['Mon', 'Wed']);
    component.deleteMenuSet(set);
    expect(api.deleteMenuSet).toHaveBeenCalledWith('set');
  });

  it('retains the editor and reports save failures', () => {
    component.openEditModal(plan);
    component.editorStep = 'review';
    api.updatePlan.and.returnValue(throwError(() => ({ error: { error: 'Price rejected' } })));
    component.savePlan();
    expect(component.editorStep).toBe('review');
    expect(component.saving).toBeFalse();
    expect(ui.error).toHaveBeenCalledWith('Price rejected');
  });

  it('cancels stale outlet reads and clears a previous outlet editor', () => {
    const stale = new Subject<SubscriptionPlan[]>();
    api.getAllPlans.and.returnValue(stale);
    component.reload();
    component.openEditModal(plan);
    api.getAllPlans.and.returnValue(of([{ ...plan, name: 'Other branch' }]));
    outlets.next({ id: 'other' });
    stale.next([plan]);
    expect(component.plans[0].name).toBe('Other branch');
    expect(component.editorStep).toBeNull();
    expect(ui.warning).toHaveBeenCalled();
  });

  it('keeps payment confirmation and refreshes the live financial metrics', () => {
    const pending = { id: 'pending', planName: 'Breakfast', amountDue: 679, paymentMethod: 'cash_at_counter' } as CustomerSubscription;
    component.pendingPayments = [pending];
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.pending-payments').textContent).toContain('Confirm Payment');
    component.paymentReferenceDraft['pending'] = 'Cash receipt';
    component.confirmSubscriptionPayment(pending);
    expect(api.confirmPayment).toHaveBeenCalledWith('pending', 'Cash receipt');
    expect(api.getOverview).toHaveBeenCalledTimes(2);
  });
});
