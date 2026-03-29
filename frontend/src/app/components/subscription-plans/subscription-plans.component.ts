import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { SubscriptionService, SubscriptionPlan, CustomerSubscription } from '../../services/subscription.service';
import { UIStore } from '../../store/ui.store';

@Component({
  selector: 'app-subscription-plans',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './subscription-plans.component.html',
  styleUrls: ['./subscription-plans.component.scss']
})
export class SubscriptionPlansComponent implements OnInit {
  private subscriptionService = inject(SubscriptionService);
  private uiStore = inject(UIStore);

  plans: SubscriptionPlan[] = [];
  mySubscription: CustomerSubscription | null = null;
  loading = true;
  subscribing = false;

  ngOnInit() {
    this.loadData();
  }

  loadData() {
    this.loading = true;
    this.subscriptionService.getPlans().subscribe({
      next: (plans) => {
        this.plans = plans;
        this.loading = false;
      },
      error: () => {
        this.uiStore.error('Failed to load plans');
        this.loading = false;
      }
    });

    this.subscriptionService.getMySubscription().subscribe({
      next: (sub) => this.mySubscription = sub,
      error: () => {}
    });
  }

  subscribe(plan: SubscriptionPlan) {
    if (!plan.id) return;
    this.subscribing = true;
    this.subscriptionService.subscribe({ planId: plan.id, paymentMethod: 'online' }).subscribe({
      next: (sub) => {
        this.mySubscription = sub;
        this.uiStore.success(`Subscribed to ${plan.name}!`);
        this.subscribing = false;
      },
      error: () => {
        this.uiStore.error('Subscription failed');
        this.subscribing = false;
      }
    });
  }

  getDailyValue(plan: SubscriptionPlan): string {
    const daily = plan.price / plan.durationDays;
    return '₹' + daily.toFixed(0);
  }

  isCurrentPlan(plan: SubscriptionPlan): boolean {
    return this.mySubscription?.planId === plan.id && this.mySubscription?.status === 'active';
  }
}
