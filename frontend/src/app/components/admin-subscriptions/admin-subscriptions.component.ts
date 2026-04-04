import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { SubscriptionService, SubscriptionPlan } from '../../services/subscription.service';
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
  private outletSub?: Subscription;

  plans: SubscriptionPlan[] = [];
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
    if (this.outletService.getSelectedOutlet()) this.loadPlans();
  }

  ngOnDestroy() { this.outletSub?.unsubscribe(); }

  getEmpty() {
    return { name: '', description: '', price: 0, durationDays: 30, benefits: [] as string[], includedItems: [{ menuItemId: '', menuItemName: '', dailyQuantity: 1 }], isActive: true };
  }

  loadPlans() {
    this.loading = true;
    this.subscriptionService.getAllPlans().subscribe({
      next: p => { this.plans = p; this.loading = false; },
      error: () => { this.uiStore.error('Failed to load plans'); this.loading = false; }
    });
  }

  openCreateModal() {
    this.isEditMode = false;
    this.currentPlan = null;
    this.planForm = this.getEmpty();
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
      includedItems: plan.includedItems?.length ? plan.includedItems.map(i => ({ ...i })) : [{ menuItemId: '', menuItemName: '', dailyQuantity: 1 }],
      isActive: plan.isActive
    };
    this.benefitInput = '';
    this.showModal = true;
  }

  closeModal() { this.showModal = false; this.currentPlan = null; }

  addBenefit() {
    if (this.benefitInput.trim()) {
      this.planForm.benefits.push(this.benefitInput.trim());
      this.benefitInput = '';
    }
  }

  removeBenefit(i: number) { this.planForm.benefits.splice(i, 1); }

  addItem() { this.planForm.includedItems.push({ menuItemId: '', menuItemName: '', dailyQuantity: 1 }); }
  removeItem(i: number) { this.planForm.includedItems.splice(i, 1); }

  savePlan() {
    if (this.isEditMode && this.currentPlan?.id) {
      this.subscriptionService.updatePlan(this.currentPlan.id, this.planForm).subscribe({
        next: () => { this.uiStore.success('Plan updated'); this.loadPlans(); this.closeModal(); },
        error: () => this.uiStore.error('Failed to update plan')
      });
    } else {
      this.subscriptionService.createPlan(this.planForm).subscribe({
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
}
