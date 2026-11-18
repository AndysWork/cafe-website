import { Component, ElementRef, OnDestroy, OnInit, ViewChild, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { distinctUntilChanged, filter } from 'rxjs/operators';
import {
  CustomerSubscription, getSubscriptionPlanSellingPrice, SUBSCRIPTION_TERMS, SubscriptionItem, SubscriptionMenuSet,
  SubscriptionOverview, SubscriptionPlan, SubscriptionService
} from '../../services/subscription.service';
import { MenuItem, MenuService } from '../../services/menu.service';
import { OutletService } from '../../services/outlet.service';
import { UIStore } from '../../store/ui.store';
import { getIstDateString } from '../../utils/date-utils';

interface PlanForm {
  name: string;
  description: string;
  durationDays: number;
  priceInput: number;
  pricingMode: 'fixed' | 'per_day';
  category: string;
  imageUrl: string;
  badgeText: string;
  dailyItemLimit?: number;
  benefits: string[];
  freeDelivery: boolean;
  includedItems: SubscriptionItem[];
  isActive: boolean;
}

@Component({
  selector: 'app-admin-subscriptions',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './admin-subscriptions.component.html',
  styleUrls: ['./admin-subscriptions.component.scss']
})
export class AdminSubscriptionsComponent implements OnInit, OnDestroy {
  private outletService = inject(OutletService);
  private uiStore = inject(UIStore);
  private menuService = inject(MenuService);
  private outletSub?: Subscription;
  private requests = new Subscription();
  private actions = new Subscription();
  @ViewChild('editorHeading') set editorHeading(element: ElementRef<HTMLElement> | undefined) {
    element?.nativeElement.focus({ preventScroll: true });
  }

  plans: SubscriptionPlan[] = [];
  overview: SubscriptionOverview | null = null;
  metricsError = '';
  pendingPayments: CustomerSubscription[] = [];
  paymentReferenceDraft: Record<string, string> = {};
  confirmingPaymentId: string | null = null;
  menuItems: MenuItem[] = [];
  menuSets: SubscriptionMenuSet[] = [];
  loading = true;
  loadingMenuItems = false;
  loadingSets = false;
  saving = false;
  savingSet = false;
  editorStep: 'details' | 'menu' | 'review' | null = null;
  currentPlan: SubscriptionPlan | null = null;
  planForm: PlanForm = this.getEmpty();
  benefitInput = '';
  formError = '';
  planSearch = '';
  statusFilter = '';
  durationFilter = '';
  page = 1;
  pageSize = 10;
  menuSearch = '';
  menuCategory = '';
  availableOnly = true;
  menuTab: 'menu' | 'sets' = 'menu';
  scheduleView: 'week' | 'list' = 'week';
  setName = '';
  readonly terms = SUBSCRIPTION_TERMS;
  readonly weekdays = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
  readonly features = ['Priority Order Handling', 'Flexible Pause', 'Exclusive Member Discounts'];

  constructor(private subscriptionService: SubscriptionService) {}

  ngOnInit(): void {
    let loadedOutletId: string | undefined;
    const selectOutlet = (id: string | undefined): void => {
      if (!id || id === loadedOutletId) return;
      loadedOutletId = id;
      if (this.editorStep) this.uiStore.warning('Outlet changed. The plan editor was closed to avoid saving to a different outlet.');
      this.saving = false;
      this.savingSet = false;
      this.confirmingPaymentId = null;
      this.paymentReferenceDraft = {};
      this.closeModal();
      this.actions.unsubscribe();
      this.actions = new Subscription();
      this.reload();
    };
    this.outletSub = this.outletService.selectedOutlet$.pipe(
      filter(outlet => outlet !== null),
      distinctUntilChanged((a, b) => a?.id === b?.id)
    ).subscribe(outlet => selectOutlet(outlet?.id || outlet?._id));
    const initial = this.outletService.getSelectedOutlet();
    if (initial) selectOutlet(initial.id || initial._id);
  }

  ngOnDestroy(): void {
    this.outletSub?.unsubscribe();
    this.requests.unsubscribe();
    this.actions.unsubscribe();
  }

  private getEmpty(): PlanForm {
    return {
      name: '', description: '', durationDays: 7, priceInput: 0, pricingMode: 'fixed',
      category: 'all-day', imageUrl: '', badgeText: '', benefits: ['Flexible Pause'],
      freeDelivery: true, includedItems: [], isActive: true
    };
  }

  reload(): void {
    this.requests.unsubscribe();
    this.requests = new Subscription();
    this.plans = [];
    this.menuItems = [];
    this.menuSets = [];
    this.page = 1;
    this.loading = true;
    this.requests.add(this.subscriptionService.getAllPlans().subscribe({
      next: plans => { this.plans = plans; this.loading = false; },
      error: () => { this.loading = false; this.uiStore.error('Failed to load plans'); }
    }));
    this.loadOverview();
    this.loadPendingPayments();
    this.loadingMenuItems = true;
    this.requests.add(this.menuService.getMenuItems().subscribe({
      next: items => {
        this.menuItems = items.filter(item => item.id && item.name).sort((a, b) => a.name.localeCompare(b.name));
        this.loadingMenuItems = false;
      },
      error: () => { this.loadingMenuItems = false; this.uiStore.error('Failed to load menu items'); }
    }));
    this.loadMenuSets();
  }

  private loadOverview(): void {
    this.overview = null;
    this.metricsError = '';
    this.requests.add(this.subscriptionService.getOverview().subscribe({
      next: overview => this.overview = overview,
      error: () => { this.metricsError = 'Subscription metrics are unavailable. Please refresh to retry.'; this.uiStore.error(this.metricsError); }
    }));
  }

  loadPendingPayments(): void {
    this.pendingPayments = [];
    this.requests.add(this.subscriptionService.getPendingPayments(this.outletService.getSelectedOutletId() || undefined).subscribe({
      next: subscriptions => this.pendingPayments = subscriptions,
      error: () => this.uiStore.error('Failed to load pending subscription payments')
    }));
  }

  private loadMenuSets(): void {
    this.loadingSets = true;
    this.requests.add(this.subscriptionService.getMenuSets().subscribe({
      next: sets => { this.menuSets = sets; this.loadingSets = false; },
      error: () => { this.loadingSets = false; this.uiStore.error('Failed to load saved menu sets'); }
    }));
  }

  confirmSubscriptionPayment(subscription: CustomerSubscription): void {
    if (!subscription.id || this.confirmingPaymentId) return;
    this.confirmingPaymentId = subscription.id;
    this.actions.add(this.subscriptionService.confirmPayment(subscription.id, this.paymentReferenceDraft[subscription.id]).subscribe({
      next: response => {
        this.confirmingPaymentId = null;
        this.uiStore.success(response.message);
        this.loadPendingPayments();
        this.loadOverview();
      },
      error: () => { this.confirmingPaymentId = null; this.uiStore.error('Failed to confirm subscription payment'); }
    }));
  }

  get activePlanCount(): number { return this.plans.filter(plan => plan.isActive).length; }
  get selectedTerms() { return this.terms.find(terms => terms.durationDays === this.planForm.durationDays); }
  get basePrice(): number {
    return this.round(this.planForm.priceInput * (this.planForm.pricingMode === 'per_day' ? this.planForm.durationDays : 1));
  }
  get discountAmount(): number { return this.round(this.basePrice * (this.selectedTerms?.discountPercent ?? 0) / 100); }
  get sellingPrice(): number { return this.round(this.basePrice - this.discountAmount); }
  durationName(days: number): string { return days === 7 ? 'Weekly' : days === 14 ? 'Biweekly' : days === 30 ? 'Monthly' : `${days} days`; }
  termsFor(plan: SubscriptionPlan) { return this.terms.find(terms => terms.durationDays === plan.durationDays); }
  sellingPriceFor(plan: SubscriptionPlan): number {
    return getSubscriptionPlanSellingPrice(plan);
  }
  private round(value: number): number { return Math.round((value + Number.EPSILON) * 100) / 100; }
  growth(current: number, previous: number): number | null { return previous > 0 ? (current - previous) / previous * 100 : null; }

  get filteredPlans(): SubscriptionPlan[] {
    const query = this.planSearch.trim().toLowerCase();
    return this.plans.filter(plan => (!query || `${plan.name} ${plan.description || ''}`.toLowerCase().includes(query))
      && (!this.statusFilter || (this.statusFilter === 'draft' ? plan.isDraft
        : this.statusFilter === 'active' ? plan.isActive : !plan.isActive && !plan.isDraft))
      && (!this.durationFilter || plan.durationDays === Number(this.durationFilter)));
  }
  get pageCount(): number { return Math.max(1, Math.ceil(this.filteredPlans.length / this.pageSize)); }
  get pagePlans(): SubscriptionPlan[] { return this.filteredPlans.slice((this.page - 1) * this.pageSize, this.page * this.pageSize); }
  get firstRow(): number { return this.filteredPlans.length ? (this.page - 1) * this.pageSize + 1 : 0; }
  get lastRow(): number { return Math.min(this.page * this.pageSize, this.filteredPlans.length); }
  resetFilters(): void { this.planSearch = ''; this.statusFilter = ''; this.durationFilter = ''; this.page = 1; }

  openCreateModal(): void {
    this.currentPlan = null;
    this.planForm = this.getEmpty();
    this.openEditor();
  }
  openEditModal(plan: SubscriptionPlan): void {
    this.currentPlan = plan;
    this.planForm = {
      name: plan.name, description: plan.description || '', durationDays: plan.durationDays,
      pricingMode: plan.pricingMode || 'fixed',
      priceInput: plan.pricingMode === 'per_day' ? plan.price / plan.durationDays : plan.price,
      category: plan.category || 'all-day', imageUrl: plan.imageUrl || '', badgeText: plan.badgeText || '',
      dailyItemLimit: plan.dailyItemLimit, benefits: [...plan.benefits], freeDelivery: plan.freeDelivery !== false,
      includedItems: this.cloneItems(plan.includedItems || []), isActive: plan.isActive
    };
    this.openEditor();
  }
  private openEditor(): void {
    this.formError = '';
    this.menuSearch = '';
    this.menuCategory = '';
    this.benefitInput = '';
    this.setName = '';
    this.editorStep = 'details';
  }
  closeModal(): void { if (!this.saving) { this.editorStep = null; this.currentPlan = null; this.formError = ''; } }
  nextStep(): void {
    if (!this.validateDetails()) return;
    if (this.editorStep === 'details') this.editorStep = 'menu';
    else if (this.validateItems()) this.editorStep = 'review';
  }
  goBack(): void { this.formError = ''; this.editorStep = this.editorStep === 'review' ? 'menu' : 'details'; }
  private validateDetails(): boolean {
    this.formError = !this.planForm.name.trim() ? 'Enter a plan name.'
      : !this.planForm.description.trim() ? 'Enter a description.'
      : !this.selectedTerms ? 'Choose a duration of 7, 14 or 30 days.'
      : !Number.isFinite(this.basePrice) || this.basePrice < 1 || this.basePrice > 100000 ? 'Plan price must be between ₹1 and ₹100,000.'
      : '';
    return !this.formError;
  }
  private validateItems(): boolean {
    this.formError = !this.planForm.includedItems.length ? 'Select at least one menu item.'
      : this.planForm.includedItems.some(item => !Number.isInteger(item.dailyQuantity) || item.dailyQuantity < 1 || item.dailyQuantity > 20
        || !item.servingDays?.length) ? 'Choose a quantity from 1 to 20 and serving days for every item.' : '';
    return !this.formError;
  }

  setPricingMode(mode: 'fixed' | 'per_day'): void {
    const price = this.basePrice;
    this.planForm.pricingMode = mode;
    this.planForm.priceInput = mode === 'per_day' ? price / this.planForm.durationDays : price;
  }

  toggleFeature(feature: string, enabled: boolean): void {
    this.planForm.benefits = enabled ? [...new Set([...this.planForm.benefits, feature])]
      : this.planForm.benefits.filter(benefit => benefit !== feature);
  }
  addBenefit(): void {
    const benefit = this.benefitInput.trim();
    if (benefit) this.toggleFeature(benefit, true);
    this.benefitInput = '';
  }
  get customBenefits(): string[] { return this.planForm.benefits.filter(benefit => !this.features.includes(benefit)); }

  get categories(): { name: string; count: number }[] {
    const counts = new Map<string, number>();
    for (const item of this.menuItems) {
      const name = this.categoryOf(item);
      counts.set(name, (counts.get(name) || 0) + 1);
    }
    return Array.from(counts, ([name, count]) => ({ name, count })).sort((a, b) => a.name.localeCompare(b.name));
  }
  categoryOf(item: MenuItem): string { return item.categoryName || item.category || 'Uncategorized'; }
  get filteredMenu(): MenuItem[] {
    const query = this.menuSearch.trim().toLowerCase();
    return this.menuItems.filter(item => (!this.availableOnly || item.isAvailable !== false)
      && (!this.menuCategory || this.categoryOf(item) === this.menuCategory)
      && (!query || `${item.name} ${this.categoryOf(item)}`.toLowerCase().includes(query)));
  }
  isSelected(item: MenuItem): boolean { return this.planForm.includedItems.some(selected => selected.menuItemId === item.id); }
  toggleMenuItem(item: MenuItem): void {
    if (this.isSelected(item)) {
      this.planForm.includedItems = this.planForm.includedItems.filter(selected => selected.menuItemId !== item.id);
    } else {
      this.planForm.includedItems.push({
        menuItemId: item.id, menuItemName: item.name, dailyQuantity: 1, unitPrice: item.onlinePrice,
        categoryName: this.categoryOf(item), imageUrl: item.imageThumbnailUrl || item.imageUrl,
        isVeg: item.dietaryType === 'veg', servingDays: ['Everyday']
      });
    }
  }
  removeItem(item: SubscriptionItem): void {
    this.planForm.includedItems = this.planForm.includedItems.filter(selected => selected !== item);
  }
  changeQuantity(item: SubscriptionItem, delta: number): void { item.dailyQuantity = Math.max(1, Math.min(20, item.dailyQuantity + delta)); }
  servesOn(item: SubscriptionItem, day: string): boolean {
    return !item.servingDays || item.servingDays.includes('Everyday') || item.servingDays.includes(day);
  }
  toggleServingDay(item: SubscriptionItem, day: string): void {
    const days = this.weekdays.filter(candidate => this.servesOn(item, candidate));
    const next = days.includes(day) ? days.filter(candidate => candidate !== day) : [...days, day];
    item.servingDays = next.length === 7 ? ['Everyday'] : this.weekdays.filter(candidate => next.includes(candidate));
  }
  servingLabel(item: SubscriptionItem): string {
    return !item.servingDays || item.servingDays.includes('Everyday') ? 'Every Day' : item.servingDays.join(', ') || 'Choose days';
  }
  mealsOn(day: string): SubscriptionItem[] { return this.planForm.includedItems.filter(item => this.servesOn(item, day)); }
  imageFor(item: SubscriptionItem): string | undefined {
    const live = this.menuItems.find(menu => menu.id === item.menuItemId);
    return live?.imageThumbnailUrl || live?.imageUrl || item.imageUrl;
  }
  planImage(plan: SubscriptionPlan): string | undefined {
    return plan.imageUrl || (plan.includedItems?.[0] ? this.imageFor(plan.includedItems[0]) : undefined);
  }
  private cloneItems(items: SubscriptionItem[]): SubscriptionItem[] {
    return items.map(item => ({ ...item, servingDays: [...(item.servingDays || ['Everyday'])] }));
  }

  get missingCostCount(): number {
    return this.planForm.includedItems.filter(item => this.recordedCost(item) === null).length;
  }
  private recordedCost(item: SubscriptionItem): number | null {
    const cost = this.menuItems.find(menu => menu.id === item.menuItemId)?.makingPrice;
    return typeof cost === 'number' && Number.isFinite(cost) && cost >= 0 ? cost : null;
  }
  get estimatedFoodCost(): number | null {
    if (!this.planForm.includedItems.length) return null;
    const start = Date.parse(`${getIstDateString()}T00:00:00Z`);
    let cost = 0;
    for (const item of this.planForm.includedItems) {
      const makingPrice = this.recordedCost(item);
      if (makingPrice === null) return null;
      for (let day = 0; day < this.planForm.durationDays; day++) {
        const weekday = this.weekdays[(new Date(start + day * 86400000).getUTCDay() + 6) % 7];
        if (this.servesOn(item, weekday)) cost += makingPrice * item.dailyQuantity;
      }
    }
    return this.round(cost);
  }
  get grossMargin(): number | null { const cost = this.estimatedFoodCost; return cost === null ? null : this.round(this.sellingPrice - cost); }
  get marginPercent(): number | null { return this.grossMargin === null || !this.sellingPrice ? null : this.grossMargin / this.sellingPrice * 100; }

  saveMenuSet(): void {
    if (this.savingSet) return;
    if (!this.setName.trim()) { this.uiStore.error('Enter a name for the saved set.'); return; }
    if (!this.validateItems()) return;
    this.savingSet = true;
    this.actions.add(this.subscriptionService.saveMenuSet(this.setName.trim(), this.cloneItems(this.planForm.includedItems)).subscribe({
      next: () => { this.savingSet = false; this.setName = ''; this.loadMenuSets(); this.uiStore.success('Menu set saved'); },
      error: () => { this.savingSet = false; this.uiStore.error('Failed to save menu set'); }
    }));
  }
  applyMenuSet(set: SubscriptionMenuSet): void {
    if (this.planForm.includedItems.length && !confirm('Replace the current menu selection with this saved set?')) return;
    this.planForm.includedItems = this.cloneItems(set.items);
    this.formError = '';
  }
  deleteMenuSet(set: SubscriptionMenuSet): void {
    if (!set.id || !confirm(`Delete saved set "${set.name}"?`)) return;
    this.actions.add(this.subscriptionService.deleteMenuSet(set.id).subscribe({
      next: () => this.loadMenuSets(),
      error: () => this.uiStore.error('Failed to delete menu set')
    }));
  }

  savePlan(draft = false): void {
    if (this.saving || !this.validateDetails() || !this.validateItems()) return;
    const { priceInput, ...form } = this.planForm;
    const payload: Partial<SubscriptionPlan> = {
      ...form, name: form.name.trim(), description: form.description.trim(), price: this.basePrice,
      isDraft: draft, isActive: !draft && form.isActive, includedItems: this.cloneItems(form.includedItems)
    };
    this.saving = true;
    const request = this.currentPlan?.id ? this.subscriptionService.updatePlan(this.currentPlan.id, payload)
      : this.subscriptionService.createPlan(payload);
    this.actions.add(request.subscribe({
      next: () => { this.saving = false; this.closeModal(); this.reload(); this.uiStore.success(draft ? 'Draft saved' : 'Plan saved'); },
      error: error => { this.saving = false; this.uiStore.error(error?.error?.error || 'Failed to save plan'); }
    }));
  }
  toggleActive(plan: SubscriptionPlan): void {
    if (!plan.id) return;
    this.actions.add(this.subscriptionService.updatePlan(plan.id, { ...plan, isActive: !plan.isActive, isDraft: false }).subscribe({
      next: () => this.reload(),
      error: () => this.uiStore.error('Failed to update plan status')
    }));
  }
  deletePlan(plan: SubscriptionPlan): void {
    if (!plan.id || !confirm(`Delete "${plan.name}"? This cannot be undone.`)) return;
    this.actions.add(this.subscriptionService.deletePlan(plan.id).subscribe({
      next: () => { this.reload(); this.uiStore.success('Plan deleted'); },
      error: error => this.uiStore.error(error?.error?.error || 'Failed to delete plan')
    }));
  }
  trackById(_: number, item: { id?: string | null }): string { return item.id || ''; }
}
