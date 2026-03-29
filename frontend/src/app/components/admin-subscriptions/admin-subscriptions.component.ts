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
    return { name: '', description: '', price: 0, durationDays: 30, benefits: [] as string[], items: [{ menuItemId: '', menuItemName: '', dailyQuantity: 1 }], isActive: true };
  }

  loadPlans() {
    this.loading = true;
    this.subscriptionService.getAllPlans().subscribe({
      next: p => { this.plans = p; this.loading = false; },
      error: () => { this.uiStore.error('Failed to load plans'); this.loading = false; }
    });
  }

  openCreateModal() { this.isEditMode = false; this.planForm = this.getEmpty(); this.showModal = true; }
  closeModal() { this.showModal = false; }

  addBenefit() {
    if (this.benefitInput.trim()) {
      this.planForm.benefits.push(this.benefitInput.trim());
      this.benefitInput = '';
    }
  }

  removeBenefit(i: number) { this.planForm.benefits.splice(i, 1); }

  addItem() { this.planForm.items.push({ menuItemId: '', menuItemName: '', dailyQuantity: 1 }); }
  removeItem(i: number) { this.planForm.items.splice(i, 1); }

  savePlan() {
    this.subscriptionService.createPlan(this.planForm).subscribe({
      next: () => { this.uiStore.success('Plan created'); this.loadPlans(); this.closeModal(); },
      error: () => this.uiStore.error('Failed to create plan')
    });
  }

  trackById(_: number, item: SubscriptionPlan) { return item.id; }
  trackByIndex(i: number) { return i; }
}
