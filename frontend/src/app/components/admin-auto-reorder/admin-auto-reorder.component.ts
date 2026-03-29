import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AutoReorderService, PurchaseOrder } from '../../services/auto-reorder.service';
import { OutletService } from '../../services/outlet.service';
import { UIStore } from '../../store/ui.store';
import { Subscription } from 'rxjs';
import { filter } from 'rxjs/operators';

@Component({
  selector: 'app-admin-auto-reorder',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './admin-auto-reorder.component.html',
  styleUrls: ['./admin-auto-reorder.component.scss']
})
export class AdminAutoReorderComponent implements OnInit, OnDestroy {
  private outletService = inject(OutletService);
  private uiStore = inject(UIStore);
  private outletSub?: Subscription;

  purchaseOrders: PurchaseOrder[] = [];
  loading = true;
  triggering = false;
  filterStatus = '';

  constructor(private reorderService: AutoReorderService) {}

  ngOnInit() {
    this.outletSub = this.outletService.selectedOutlet$
      .pipe(filter(o => o !== null))
      .subscribe(() => this.loadOrders());
    if (this.outletService.getSelectedOutlet()) this.loadOrders();
  }

  ngOnDestroy() { this.outletSub?.unsubscribe(); }

  loadOrders() {
    this.loading = true;
    this.reorderService.getPurchaseOrders(this.filterStatus || undefined).subscribe({
      next: o => { this.purchaseOrders = o; this.loading = false; },
      error: () => { this.uiStore.error('Failed to load purchase orders'); this.loading = false; }
    });
  }

  triggerAutoReorder() {
    this.triggering = true;
    this.reorderService.triggerAutoReorder().subscribe({
      next: (res) => {
        this.uiStore.success(res.message || 'Auto-reorder triggered');
        this.triggering = false;
        this.loadOrders();
      },
      error: () => { this.uiStore.error('Failed to trigger auto-reorder'); this.triggering = false; }
    });
  }

  updateStatus(id: string, status: string) {
    this.reorderService.updatePurchaseOrderStatus(id, status).subscribe({
      next: () => { this.uiStore.success('Status updated'); this.loadOrders(); },
      error: () => this.uiStore.error('Failed to update status')
    });
  }

  trackById(_: number, item: PurchaseOrder) { return item.id; }
}
