import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { KitchenDisplayService, KitchenOrder, KitchenStats, KitchenChecklistItem } from '../../services/kitchen-display.service';
import { OutletService } from '../../services/outlet.service';
import { WebPushService } from '../../services/web-push.service';
import { UIStore } from '../../store/ui.store';
import { Subscription, interval } from 'rxjs';
import { filter } from 'rxjs/operators';
import { RouterModule } from '@angular/router';

@Component({
  selector: 'app-kitchen-display',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './kitchen-display.component.html',
  styleUrls: ['./kitchen-display.component.scss']
})
export class KitchenDisplayComponent implements OnInit, OnDestroy {
  private outletService = inject(OutletService);
  private uiStore = inject(UIStore);
  private outletSub?: Subscription;
  private pollSub?: Subscription;
  private webPush = inject(WebPushService);

  orders: KitchenOrder[] = [];
  stats: KitchenStats | null = null;
  loading = true;
  kotText = '';
  showKotModal = false;
  showChecklistModal = false;
  selectedOrderForChecklist: KitchenOrder | null = null;
  checklistItems: KitchenChecklistItem[] = [];

  constructor(private kitchenService: KitchenDisplayService) {}

  ngOnInit() {
    this.webPush.registerKitchenWebPush('kitchen-display').catch(() => {
      // Keep kitchen display functional even if push setup fails.
    });

    this.outletSub = this.outletService.selectedOutlet$
      .pipe(filter(o => o !== null))
      .subscribe(() => this.loadData());
    if (this.outletService.getSelectedOutlet()) this.loadData();

    // Auto-refresh every 15 seconds
    this.pollSub = interval(15000).subscribe(() => this.loadData());
  }

  ngOnDestroy() {
    this.outletSub?.unsubscribe();
    this.pollSub?.unsubscribe();
  }

  loadData() {
    this.kitchenService.getKitchenOrders().subscribe({
      next: o => { this.orders = o; this.loading = false; },
      error: () => { this.loading = false; }
    });
    this.kitchenService.getKitchenStats().subscribe({
      next: s => this.stats = s,
      error: () => {}
    });
  }

  updateStatus(orderId: string, status: string) {
    if (status === 'ready') {
      this.openChecklist(orderId);
      return;
    }

    this.kitchenService.updateOrderStatus(orderId, status).subscribe({
      next: () => { this.uiStore.success('Status updated'); this.loadData(); },
      error: () => this.uiStore.error('Failed to update status')
    });
  }

  openChecklist(orderId: string) {
    const order = this.orders.find(o => o.id === orderId);
    if (!order) {
      this.uiStore.error('Order not found');
      return;
    }

    const fallbackChecklist: KitchenChecklistItem[] = [
      { label: 'Item quantity rechecked', isCompleted: false },
      { label: 'Plating and garnish completed', isCompleted: false },
      { label: 'Temperature and freshness verified', isCompleted: false },
      { label: 'Packaging/sealing verified', isCompleted: false },
      { label: 'Special instructions verified', isCompleted: false }
    ];

    this.selectedOrderForChecklist = order;
    this.checklistItems = (order.kitchenChecklist?.length ? order.kitchenChecklist : fallbackChecklist)
      .map(i => ({ id: i.id, label: i.label, isCompleted: i.isCompleted }));
    this.showChecklistModal = true;
  }

  closeChecklist() {
    this.showChecklistModal = false;
    this.selectedOrderForChecklist = null;
    this.checklistItems = [];
  }

  completeReadyStatus() {
    if (!this.selectedOrderForChecklist) {
      return;
    }

    const incomplete = this.checklistItems.filter(item => !item.isCompleted);
    if (incomplete.length > 0) {
      this.uiStore.error('Complete all checklist items before marking ready');
      return;
    }

    this.kitchenService.updateOrderStatus(this.selectedOrderForChecklist.id, 'ready', this.checklistItems).subscribe({
      next: (res) => {
        const message = res?.deliveryNotificationQueued
          ? 'Order marked ready. Delivery partners notified.'
          : 'Order marked ready with checklist completed';
        this.uiStore.success(message);
        this.closeChecklist();
        this.loadData();
      },
      error: () => this.uiStore.error('Failed to mark order ready')
    });
  }

  getChecklistProgress(): number {
    if (!this.checklistItems.length) return 0;
    const completed = this.checklistItems.filter(i => i.isCompleted).length;
    return Math.round((completed / this.checklistItems.length) * 100);
  }

  printKot(orderId: string) {
    this.kitchenService.getKot(orderId).subscribe({
      next: (res) => { this.kotText = res.kotText; this.showKotModal = true; },
      error: () => this.uiStore.error('Failed to generate KOT')
    });
  }

  printKotWindow() {
    const win = window.open('', '_blank', 'width=300,height=500');
    if (win) {
      win.document.write(`<pre style="font-family: monospace; font-size: 12px; width: 80mm;">${this.kotText}</pre>`);
      win.document.close();
      win.print();
    }
  }

  getOrdersByStatus(status: string): KitchenOrder[] {
    return this.orders.filter(o => o.status === status);
  }

  getTimeSince(dateStr: string): string {
    const diff = Math.floor((Date.now() - new Date(dateStr).getTime()) / 60000);
    if (diff < 1) return 'Just now';
    if (diff < 60) return `${diff}m ago`;
    return `${Math.floor(diff / 60)}h ${diff % 60}m ago`;
  }

  getStatusColor(status: string): string {
    const colors: Record<string, string> = {
      pending: '#f59e0b',
      confirmed: '#3b82f6',
      preparing: '#8b5cf6',
      ready: '#10b981',
      'out-for-delivery': '#0ea5e9',
      delivered: '#059669'
    };
    return colors[status] || '#6b7280';
  }
}
