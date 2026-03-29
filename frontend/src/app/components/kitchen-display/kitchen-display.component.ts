import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { KitchenDisplayService, KitchenOrder, KitchenStats } from '../../services/kitchen-display.service';
import { OutletService } from '../../services/outlet.service';
import { UIStore } from '../../store/ui.store';
import { Subscription, interval } from 'rxjs';
import { filter, switchMap } from 'rxjs/operators';

@Component({
  selector: 'app-kitchen-display',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './kitchen-display.component.html',
  styleUrls: ['./kitchen-display.component.scss']
})
export class KitchenDisplayComponent implements OnInit, OnDestroy {
  private outletService = inject(OutletService);
  private uiStore = inject(UIStore);
  private outletSub?: Subscription;
  private pollSub?: Subscription;

  orders: KitchenOrder[] = [];
  stats: KitchenStats | null = null;
  loading = true;
  kotText = '';
  showKotModal = false;

  constructor(private kitchenService: KitchenDisplayService) {}

  ngOnInit() {
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
    this.kitchenService.updateOrderStatus(orderId, status).subscribe({
      next: () => { this.uiStore.success('Status updated'); this.loadData(); },
      error: () => this.uiStore.error('Failed to update status')
    });
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
    const colors: Record<string, string> = { pending: '#f59e0b', confirmed: '#3b82f6', preparing: '#8b5cf6', ready: '#10b981' };
    return colors[status] || '#6b7280';
  }
}
