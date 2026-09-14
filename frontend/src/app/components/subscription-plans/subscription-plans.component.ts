import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule, Router, ActivatedRoute } from '@angular/router';
import { SubscriptionService, SubscriptionPlan, CustomerSubscription, SubscriptionItem } from '../../services/subscription.service';
import { MenuService, MenuItem } from '../../services/menu.service';
import { AuthStore } from '../../store/auth.store';
import { AuthService } from '../../services/auth.service';
import { UIStore } from '../../store/ui.store';
import { OutletService } from '../../services/outlet.service';

interface SelectedComboItem {
  menuItem: MenuItem;
  quantity: number;
}

@Component({
  selector: 'app-subscription-plans',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './subscription-plans.component.html',
  styleUrls: ['./subscription-plans.component.scss']
})
export class SubscriptionPlansComponent implements OnInit {
  private subscriptionService = inject(SubscriptionService);
  private menuService = inject(MenuService);
  public authStore = inject(AuthStore);
  private authService = inject(AuthService);
  private uiStore = inject(UIStore);
  private outletService = inject(OutletService);
  private router = inject(Router);
  private route = inject(ActivatedRoute);

  // Active Tab: 'curated' | 'custom' | 'my-subscriptions'
  activeTab: 'curated' | 'custom' | 'my-subscriptions' = 'curated';

  // Curated Plans
  plans: SubscriptionPlan[] = [];
  selectedPlanCategory: string = 'all';
  loadingPlans = true;

  // Curated Plan Subscribe Modal / Checkout
  selectedPlanForCheckout: SubscriptionPlan | null = null;
  checkoutDurationDays: number = 30;
  checkoutDeliverySlot: string = '12:30 PM - 2:00 PM';
  checkoutDeliveryDays: string = 'Everyday';
  checkoutAddress: string = '';
  checkoutPhone: string = '';
  checkoutName: string = '';
  checkoutNotes: string = '';
  checkoutPaymentMethod: string = 'upi-qr';
  subscribingCurated = false;

  // Custom Combo Builder
  menuItems: MenuItem[] = [];
  loadingMenuItems = false;
  menuSearchQuery: string = '';
  selectedMenuCategory: string = 'all';
  showComboCategoryDrawer = false;
  comboItems: Map<string, SelectedComboItem> = new Map();

  comboName: string = 'My Daily Cafe Combo';
  comboDeliverySlot: string = '12:30 PM - 2:00 PM';
  comboDeliverySchedule: 'everyday' | 'weekdays' | 'custom' = 'everyday';
  customDaysOfWeek: { name: string; selected: boolean }[] = [
    { name: 'Mon', selected: true },
    { name: 'Tue', selected: true },
    { name: 'Wed', selected: true },
    { name: 'Thu', selected: true },
    { name: 'Fri', selected: true },
    { name: 'Sat', selected: false },
    { name: 'Sun', selected: false }
  ];
  comboDurationDays: number = 30; // 7, 14, 30
  comboDeliveryAddress: string = '';
  comboCustomerPhone: string = '';
  comboCustomerName: string = '';
  comboSpecialNotes: string = '';
  comboPaymentMethod: string = 'upi-qr';
  submittingCustomCombo = false;
  mobileComboStep: 'menu' | 'review' = 'menu';

  // My Subscriptions
  activeSubscription: CustomerSubscription | null = null;
  subscriptionHistory: CustomerSubscription[] = [];
  loadingMySubscriptions = false;
  processingAction = false;

  // Time Slot Options
  readonly timeSlots: string[] = [
    '8:00 AM - 9:30 AM (Breakfast)',
    '12:30 PM - 2:00 PM (Lunch)',
    '4:30 PM - 6:00 PM (Evening Snacks)',
    '7:30 PM - 9:00 PM (Dinner)'
  ];

  ngOnInit() {
    this.route.queryParams.subscribe(params => {
      if (params['tab'] === 'custom') {
        this.activeTab = 'custom';
      } else if (params['tab'] === 'my') {
        this.activeTab = 'my-subscriptions';
      }
    });

    this.populateUserDetails();
    this.loadPlans();
    this.loadMenuItems();
    this.loadMySubscriptions();
  }

  populateUserDetails() {
    const user = this.authStore.user();
    if (user) {
      const fullName = user.firstName ? `${user.firstName} ${user.lastName || ''}`.trim() : user.username;
      this.checkoutName = fullName;
      this.comboCustomerName = fullName;
      this.checkoutPhone = user.phoneNumber || '';
      this.comboCustomerPhone = user.phoneNumber || '';
    }
  }

  // --- TAB SWITCHING ---
  setTab(tab: 'curated' | 'custom' | 'my-subscriptions') {
    this.activeTab = tab;
    if (tab === 'custom' && this.isMobileViewport()) {
      this.mobileComboStep = 'menu';
      this.showComboCategoryDrawer = false;
    }
    if (tab === 'my-subscriptions') {
      this.loadMySubscriptions();
    }
  }

  private isMobileViewport(): boolean {
    return typeof window !== 'undefined' && window.matchMedia('(max-width: 768px)').matches;
  }

  private scrollComboSectionIntoView(step: 'menu' | 'review'): void {
    if (typeof window === 'undefined' || typeof document === 'undefined') {
      return;
    }

    const targetId = step === 'review' ? 'combo-config-section' : 'combo-builder-start';
    const target = document.getElementById(targetId);
    if (target) {
      target.scrollIntoView({ behavior: 'smooth', block: 'start' });
      return;
    }

    window.scrollTo({ top: 120, behavior: 'smooth' });
  }

  // --- CURATED PLANS ---
  loadPlans() {
    this.loadingPlans = true;
    const outletId = this.outletService.getSelectedOutletId() || 'default';
    this.subscriptionService.getPlans(outletId).subscribe({
      next: (plans) => {
        this.plans = plans || [];
        this.loadingPlans = false;
      },
      error: (err) => {
        console.warn('Could not load plans', err);
        this.uiStore.error('Failed to load subscription plans');
        this.loadingPlans = false;
      }
    });
  }

  get filteredPlans(): SubscriptionPlan[] {
    if (this.selectedPlanCategory === 'all') return this.plans;
    return this.plans.filter(p => (p.category || 'all-day').toLowerCase() === this.selectedPlanCategory.toLowerCase());
  }

  getDailyValue(plan: SubscriptionPlan): string {
    const days = plan.durationDays > 0 ? plan.durationDays : 30;
    const daily = plan.price / days;
    return '₹' + Math.round(daily);
  }

  openPlanCheckout(plan: SubscriptionPlan) {
    if (!this.authStore.isLoggedIn()) {
      this.uiStore.warning('Please log in to subscribe to meal plans.');
      this.router.navigate(['/login'], { queryParams: { returnUrl: '/subscriptions' } });
      return;
    }
    this.selectedPlanForCheckout = plan;
    this.checkoutDurationDays = plan.durationDays || 30;
    this.populateUserDetails();
  }

  closePlanCheckout() {
    this.selectedPlanForCheckout = null;
    this.subscribingCurated = false;
  }

  getCuratedCalculatedPrice(): { base: number; discount: number; total: number; discountPct: number } {
    if (!this.selectedPlanForCheckout) return { base: 0, discount: 0, total: 0, discountPct: 0 };
    const plan = this.selectedPlanForCheckout;
    const dailyRate = plan.durationDays > 0 ? plan.price / plan.durationDays : plan.price / 30;
    const base = Math.round(dailyRate * this.checkoutDurationDays);
    const discountPct = this.checkoutDurationDays >= 30 ? 20 : (this.checkoutDurationDays >= 14 ? 15 : 10);
    const discount = Math.round(base * (discountPct / 100));
    const total = base - discount;
    return { base, discount, total, discountPct };
  }

  confirmSubscribeCurated() {
    if (!this.selectedPlanForCheckout?.id) return;
    if (!this.checkoutAddress.trim()) {
      this.uiStore.error('Please enter your delivery address for meal drops.');
      return;
    }
    if (!this.checkoutPhone.trim()) {
      this.uiStore.error('Please provide a contact phone number.');
      return;
    }

    this.subscribingCurated = true;
    const outletId = this.outletService.getSelectedOutletId() || this.selectedPlanForCheckout.outletId;

    this.subscriptionService.subscribe({
      planId: this.selectedPlanForCheckout.id,
      durationDays: this.checkoutDurationDays,
      deliveryTimeSlot: this.checkoutDeliverySlot,
      deliveryDays: [this.checkoutDeliveryDays],
      deliveryAddress: this.checkoutAddress.trim(),
      customerPhone: this.checkoutPhone.trim(),
      customerName: this.checkoutName.trim(),
      specialInstructions: this.checkoutNotes.trim() || undefined,
      paymentMethod: this.checkoutPaymentMethod,
      outletId
    }).subscribe({
      next: (res) => {
        this.subscribingCurated = false;
        this.selectedPlanForCheckout = null;
        this.uiStore.success(res?.message || 'Subscribed successfully!');
        this.activeTab = 'my-subscriptions';
        this.loadMySubscriptions();
      },
      error: (err) => {
        this.subscribingCurated = false;
        this.uiStore.error(err?.error?.error || 'Failed to complete subscription');
      }
    });
  }

  // --- CUSTOM COMBO BUILDER ---
  loadMenuItems() {
    this.loadingMenuItems = true;
    this.menuService.getMenuItems().subscribe({
      next: (items) => {
        this.menuItems = (items || []).filter(i => i.isAvailable !== false);
        this.loadingMenuItems = false;
      },
      error: () => {
        this.loadingMenuItems = false;
      }
    });
  }

  get filteredMenuItems(): MenuItem[] {
    let list = this.menuItems;
    if (this.selectedMenuCategory !== 'all') {
      list = list.filter(i => (i.category || i.categoryName || '').toLowerCase() === this.selectedMenuCategory.toLowerCase());
    }
    if (this.menuSearchQuery.trim()) {
      const q = this.menuSearchQuery.toLowerCase();
      list = list.filter(i => i.name.toLowerCase().includes(q) || (i.description || '').toLowerCase().includes(q));
    }
    return list;
  }

  get availableCategories(): string[] {
    const cats = new Set<string>();
    for (const item of this.menuItems) {
      const c = item.category || item.categoryName;
      if (c) cats.add(c);
    }
    return Array.from(cats);
  }

  getItemQuantity(menuItemId: string): number {
    return this.comboItems.get(menuItemId)?.quantity || 0;
  }

  setSelectedMenuCategory(category: string): void {
    this.selectedMenuCategory = category;
    this.showComboCategoryDrawer = false;
  }

  openComboCategoryDrawer(): void {
    this.showComboCategoryDrawer = true;
  }

  closeComboCategoryDrawer(): void {
    this.showComboCategoryDrawer = false;
  }

  getComboCategoryCount(category: string): number {
    if (category === 'all') {
      return this.menuItems.length;
    }
    return this.menuItems.filter(i => (i.category || i.categoryName || '').toLowerCase() === category.toLowerCase()).length;
  }

  getComboDisplayPrice(item: MenuItem): number {
    return item.webPrice || item.onlinePrice || item.shopSellingPrice || 0;
  }

  getComboMenuImageUrl(item: MenuItem): string | undefined {
    return item.imageThumbnailUrl || item.imageUrl;
  }

  getComboDietaryType(item: MenuItem): 'veg' | 'non-veg' | 'egg' {
    const value = (item.dietaryType || '').toLowerCase().trim();
    if (value === 'non-veg' || value === 'non veg' || value === 'nonveg') {
      return 'non-veg';
    }
    if (value === 'egg') {
      return 'egg';
    }
    return 'veg';
  }

  addItemToCombo(item: MenuItem) {
    const existing = this.comboItems.get(item.id);
    if (existing) {
      if (existing.quantity < 10) {
        existing.quantity += 1;
      }
    } else {
      this.comboItems.set(item.id, { menuItem: item, quantity: 1 });
    }
  }

  removeItemFromCombo(menuItemId: string) {
    const existing = this.comboItems.get(menuItemId);
    if (existing) {
      if (existing.quantity > 1) {
        existing.quantity -= 1;
      } else {
        this.comboItems.delete(menuItemId);
      }
    }
  }

  clearCombo() {
    this.comboItems.clear();
  }

  get selectedComboItemsList(): SelectedComboItem[] {
    return Array.from(this.comboItems.values());
  }

  get comboDailySubtotal(): number {
    let sum = 0;
    for (const { menuItem, quantity } of this.selectedComboItemsList) {
      const price = menuItem.onlinePrice > 0 ? menuItem.onlinePrice : (menuItem.webPrice || menuItem.shopSellingPrice || 0);
      sum += (price * quantity);
    }
    return sum;
  }

  get comboActiveDeliveryDaysList(): string[] {
    if (this.comboDeliverySchedule === 'everyday') return ['Everyday'];
    if (this.comboDeliverySchedule === 'weekdays') return ['Mon', 'Tue', 'Wed', 'Thu', 'Fri'];
    return this.customDaysOfWeek.filter(d => d.selected).map(d => d.name);
  }

  get comboTotalDrops(): number {
    const daysPerWeek = this.comboDeliverySchedule === 'everyday'
      ? 7
      : (this.comboDeliverySchedule === 'weekdays' ? 5 : this.customDaysOfWeek.filter(d => d.selected).length);
    const validDays = Math.max(1, daysPerWeek);
    return Math.max(1, Math.round((this.comboDurationDays / 7.0) * validDays));
  }

  get comboDiscountPercentage(): number {
    if (this.comboDurationDays >= 30) return 20;
    if (this.comboDurationDays >= 14) return 15;
    return 10;
  }

  get comboPricingSummary(): { gross: number; discount: number; finalPayable: number } {
    const gross = this.comboDailySubtotal * this.comboTotalDrops;
    const discount = Math.round(gross * (this.comboDiscountPercentage / 100));
    const finalPayable = Math.max(0, gross - discount);
    return { gross, discount, finalPayable };
  }

  toggleCustomDay(day: { name: string; selected: boolean }) {
    day.selected = !day.selected;
  }

  setMobileComboStep(step: 'menu' | 'review'): void {
    this.mobileComboStep = step;
    if (step === 'review') {
      this.showComboCategoryDrawer = false;
    }
    if (this.isMobileViewport()) {
      this.scrollComboSectionIntoView(step);
    }
  }

  scrollToConfig(): void {
    this.setMobileComboStep('review');
  }

  confirmCreateCustomCombo() {
    if (!this.authStore.isLoggedIn()) {
      this.uiStore.warning('Please log in to create and activate a recurring meal combo.');
      this.router.navigate(['/login'], { queryParams: { returnUrl: '/subscriptions?tab=custom' } });
      return;
    }

    if (this.selectedComboItemsList.length === 0) {
      this.uiStore.error('Please pick at least one menu item for your daily combo.');
      return;
    }

    if (!this.comboDeliveryAddress.trim()) {
      this.uiStore.error('Please enter your delivery address for recurring drops.');
      return;
    }

    if (!this.comboCustomerPhone.trim()) {
      this.uiStore.error('Please provide a contact phone number.');
      return;
    }

    const deliveryDays = this.comboActiveDeliveryDaysList;
    if (deliveryDays.length === 0) {
      this.uiStore.error('Please select at least one delivery day of the week.');
      return;
    }

    this.submittingCustomCombo = true;
    const outletId = this.outletService.getSelectedOutletId() || 'default';

    const itemsPayload = this.selectedComboItemsList.map(i => ({
      menuItemId: i.menuItem.id,
      quantity: i.quantity
    }));

    this.subscriptionService.createCustomCombo({
      comboName: this.comboName.trim() || 'My Daily Meal Combo',
      items: itemsPayload,
      deliveryTimeSlot: this.comboDeliverySlot,
      deliveryDays: deliveryDays,
      durationDays: this.comboDurationDays,
      deliveryAddress: this.comboDeliveryAddress.trim(),
      customerPhone: this.comboCustomerPhone.trim(),
      customerName: this.comboCustomerName.trim() || undefined,
      specialInstructions: this.comboSpecialNotes.trim() || undefined,
      paymentMethod: this.comboPaymentMethod,
      outletId
    }).subscribe({
      next: (res) => {
        this.submittingCustomCombo = false;
        this.clearCombo();
        this.uiStore.success(res?.message || 'Custom meal combo activated successfully!');
        this.activeTab = 'my-subscriptions';
        this.loadMySubscriptions();
      },
      error: (err) => {
        this.submittingCustomCombo = false;
        this.uiStore.error(err?.error?.error || 'Failed to create custom combo subscription');
      }
    });
  }

  // --- MY SUBSCRIPTIONS ---
  loadMySubscriptions() {
    if (!this.authStore.isLoggedIn()) {
      this.activeSubscription = null;
      this.subscriptionHistory = [];
      return;
    }

    this.loadingMySubscriptions = true;
    this.subscriptionService.getMySubscription().subscribe({
      next: (res) => {
        this.activeSubscription = res?.active || null;
        this.subscriptionHistory = (res?.history || []).filter(s => s.id !== this.activeSubscription?.id);
        this.loadingMySubscriptions = false;
      },
      error: () => {
        this.loadingMySubscriptions = false;
      }
    });
  }

  pauseActiveSubscription() {
    if (!this.activeSubscription?.id || this.processingAction) return;
    if (!confirm('Pause deliveries? Your subscription will pause and your end date will be extended when you resume.')) return;

    this.processingAction = true;
    this.subscriptionService.pauseSubscription(this.activeSubscription.id).subscribe({
      next: (res) => {
        this.processingAction = false;
        this.activeSubscription = res.subscription;
        this.uiStore.success(res.message || 'Subscription paused.');
      },
      error: (err) => {
        this.processingAction = false;
        this.uiStore.error(err?.error?.error || 'Failed to pause subscription');
      }
    });
  }

  resumeActiveSubscription() {
    if (!this.activeSubscription?.id || this.processingAction) return;

    this.processingAction = true;
    this.subscriptionService.resumeSubscription(this.activeSubscription.id).subscribe({
      next: (res) => {
        this.processingAction = false;
        this.activeSubscription = res.subscription;
        this.uiStore.success(res.message || 'Subscription resumed and timeline extended!');
      },
      error: (err) => {
        this.processingAction = false;
        this.uiStore.error(err?.error?.error || 'Failed to resume subscription');
      }
    });
  }

  cancelActiveSubscription() {
    if (!this.activeSubscription?.id || this.processingAction) return;
    if (!confirm('Are you sure you want to cancel this recurring subscription?')) return;

    this.processingAction = true;
    this.subscriptionService.cancelSubscription(this.activeSubscription.id).subscribe({
      next: (res) => {
        this.processingAction = false;
        this.uiStore.success('Subscription cancelled.');
        this.loadMySubscriptions();
      },
      error: (err) => {
        this.processingAction = false;
        this.uiStore.error(err?.error?.error || 'Failed to cancel subscription');
      }
    });
  }

  getDaysRemaining(sub: CustomerSubscription): number {
    const end = new Date(sub.endDate).getTime();
    const now = new Date().getTime();
    const diff = Math.ceil((end - now) / (1000 * 60 * 60 * 24));
    return Math.max(0, diff);
  }

  getProgressPercentage(sub: CustomerSubscription): number {
    const total = sub.durationDays || 30;
    const remaining = this.getDaysRemaining(sub);
    const elapsed = Math.max(0, total - remaining);
    return Math.min(100, Math.round((elapsed / total) * 100));
  }

  formatDate(dateStr?: string): string {
    if (!dateStr) return '-';
    return new Date(dateStr).toLocaleDateString('en-IN', {
      day: '2-digit',
      month: 'short',
      year: 'numeric'
    });
  }
}
