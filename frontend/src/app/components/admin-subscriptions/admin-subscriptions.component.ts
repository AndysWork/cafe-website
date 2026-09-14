import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { SubscriptionService, SubscriptionPlan } from '../../services/subscription.service';
import { MenuItem, MenuService } from '../../services/menu.service';
import { OutletService } from '../../services/outlet.service';
import { UIStore } from '../../store/ui.store';
import { Subscription } from 'rxjs';
import { filter } from 'rxjs/operators';

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

  plans: SubscriptionPlan[] = [];
  menuItems: MenuItem[] = [];
  loadingMenuItems = false;
  activeSuggestionRow: number | null = null;
  loading = true;
  showModal = false;
  isEditMode = false;
  currentPlan: SubscriptionPlan | null = null;

  planForm: any = this.getEmpty();
  benefitInput = '';

  constructor(private subscriptionService: SubscriptionService) {}

  ngOnInit() {
    this.outletSub = this.outletService.selectedOutlet$
      .pipe(filter(o => o !== null))
      .subscribe(() => this.loadPlans());
    this.loadMenuItems();
    if (this.outletService.getSelectedOutlet()) this.loadPlans();
  }

  ngOnDestroy() { this.outletSub?.unsubscribe(); }

  getEmpty() {
    return { name: '', description: '', price: 0, durationDays: 30, benefits: [] as string[], includedItems: [{ menuItemId: '', menuItemName: '', menuItemSearch: '', dailyQuantity: 1 }], isActive: true };
  }

  loadPlans() {
    this.loading = true;
    this.subscriptionService.getAllPlans().subscribe({
      next: p => { this.plans = p; this.loading = false; },
      error: () => { this.uiStore.error('Failed to load plans'); this.loading = false; }
    });
  }

  loadMenuItems() {
    this.loadingMenuItems = true;
    this.menuService.getMenuItems().subscribe({
      next: (items) => {
        const source = items || [];
        this.menuItems = source
          .filter(i => !!i.id && !!i.name)
          .sort((a, b) => a.name.localeCompare(b.name));
        this.loadingMenuItems = false;
      },
      error: () => {
        this.uiStore.error('Failed to load menu items');
        this.loadingMenuItems = false;
      }
    });
  }

  openCreateModal() {
    this.isEditMode = false;
    this.currentPlan = null;
    this.planForm = this.getEmpty();
    this.activeSuggestionRow = null;
    this.benefitInput = '';
    this.showModal = true;
  }

  openEditModal(plan: SubscriptionPlan) {
    this.isEditMode = true;
    this.currentPlan = plan;
    this.planForm = {
      name: plan.name,
      description: plan.description || '',
      price: plan.price,
      durationDays: plan.durationDays,
      benefits: [...(plan.benefits || [])],
      includedItems: plan.includedItems?.length
        ? plan.includedItems.map(i => {
          const matched = this.menuItems.find(m => m.id === i.menuItemId);
          return {
            ...i,
            menuItemSearch: matched ? this.getMenuItemDisplay(matched) : (i.menuItemName || '')
          };
        })
        : [{ menuItemId: '', menuItemName: '', menuItemSearch: '', dailyQuantity: 1 }],
      isActive: plan.isActive
    };
    this.activeSuggestionRow = null;
    this.benefitInput = '';
    this.showModal = true;
  }

  closeModal() {
    this.showModal = false;
    this.currentPlan = null;
    this.activeSuggestionRow = null;
  }

  addBenefit() {
    if (this.benefitInput.trim()) {
      this.planForm.benefits.push(this.benefitInput.trim());
      this.benefitInput = '';
    }
  }

  removeBenefit(i: number) { this.planForm.benefits.splice(i, 1); }

  addItem() { this.planForm.includedItems.push({ menuItemId: '', menuItemName: '', menuItemSearch: '', dailyQuantity: 1 }); }

  removeItem(i: number) {
    this.planForm.includedItems.splice(i, 1);
    if (this.activeSuggestionRow === i) {
      this.activeSuggestionRow = null;
    } else if (this.activeSuggestionRow !== null && this.activeSuggestionRow > i) {
      this.activeSuggestionRow -= 1;
    }
  }

  selectMenuItemForRow(index: number, menuItem: MenuItem, event?: MouseEvent) {
    event?.preventDefault();
    const item = this.planForm.includedItems[index];
    item.menuItemId = menuItem.id;
    item.menuItemName = menuItem.name;
    item.menuItemSearch = this.getMenuItemDisplay(menuItem);
    this.activeSuggestionRow = null;
  }

  openMenuSuggestions(index: number) {
    this.activeSuggestionRow = index;
  }

  onMenuSearchInput(index: number) {
    const item = this.planForm.includedItems[index];
    item.menuItemId = '';
    item.menuItemName = '';
    this.activeSuggestionRow = index;
  }

  onMenuSearchBlur(index: number) {
    setTimeout(() => {
      if (this.activeSuggestionRow === index) {
        this.activeSuggestionRow = null;
      }
    }, 120);
  }

  clearMenuItem(index: number) {
    const item = this.planForm.includedItems[index];
    item.menuItemId = '';
    item.menuItemName = '';
    item.menuItemSearch = '';
    this.activeSuggestionRow = index;
  }

  getRowMenuSuggestions(index: number): MenuItem[] {
    const item = this.planForm.includedItems[index];
    const query = (item?.menuItemSearch || '').toLowerCase().trim();

    if (!query) {
      return this.menuItems.slice(0, 25);
    }

    return this.menuItems
      .filter(m => {
        const category = (m.categoryName || m.category || '').toLowerCase();
        const subCategory = (m.subCategoryName || '').toLowerCase();
        return m.name.toLowerCase().includes(query)
          || category.includes(query)
          || subCategory.includes(query);
      })
      .slice(0, 25);
  }

  getMenuItemDisplay(item: MenuItem): string {
    const category = item.categoryName || item.category;
    if (!category) return item.name;
    return `${item.name} (${category})`;
  }

  getMenuItemNameById(menuItemId: string): string {
    return this.menuItems.find(m => m.id === menuItemId)?.name || '';
  }

  private buildPlanPayload() {
    const includedItems = (this.planForm.includedItems || [])
      .filter((item: any) => !!item.menuItemId)
      .map((item: any) => ({
        menuItemId: item.menuItemId,
        menuItemName: item.menuItemName || this.getMenuItemNameById(item.menuItemId),
        dailyQuantity: Math.max(1, Number(item.dailyQuantity) || 1)
      }));

    return {
      ...this.planForm,
      includedItems
    };
  }

  savePlan() {
    const payload = this.buildPlanPayload();
    if (!payload.includedItems.length) {
      this.uiStore.error('Please select at least one menu item for this plan.');
      return;
    }

    if (this.isEditMode && this.currentPlan?.id) {
      this.subscriptionService.updatePlan(this.currentPlan.id, payload).subscribe({
        next: () => { this.uiStore.success('Plan updated'); this.loadPlans(); this.closeModal(); },
        error: () => this.uiStore.error('Failed to update plan')
      });
    } else {
      this.subscriptionService.createPlan(payload).subscribe({
        next: () => { this.uiStore.success('Plan created'); this.loadPlans(); this.closeModal(); },
        error: () => this.uiStore.error('Failed to create plan')
      });
    }
  }

  toggleActive(plan: SubscriptionPlan) {
    if (!plan.id) return;
    const updated = { ...plan, isActive: !plan.isActive };
    this.subscriptionService.updatePlan(plan.id, updated).subscribe({
      next: () => { this.uiStore.success(updated.isActive ? 'Plan activated' : 'Plan deactivated'); this.loadPlans(); },
      error: () => this.uiStore.error('Failed to update status')
    });
  }

  deletePlan(plan: SubscriptionPlan) {
    if (!plan.id) return;
    if (!confirm(`Delete "${plan.name}"? This cannot be undone.`)) return;
    this.subscriptionService.deletePlan(plan.id).subscribe({
      next: () => { this.uiStore.success('Plan deleted'); this.loadPlans(); },
      error: () => this.uiStore.error('Failed to delete — plan may have active subscribers')
    });
  }

  trackById(_: number, item: SubscriptionPlan) { return item.id; }
  trackByIndex(i: number) { return i; }
  trackByMenuItemId(_: number, item: MenuItem) { return item.id; }
}
