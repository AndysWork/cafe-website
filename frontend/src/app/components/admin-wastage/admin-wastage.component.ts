import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { WastageService, WastageRecord, WastageSummary } from '../../services/wastage.service';
import { OutletService } from '../../services/outlet.service';
import { UIStore } from '../../store/ui.store';
import { Subscription } from 'rxjs';
import { filter } from 'rxjs/operators';

@Component({
  selector: 'app-admin-wastage',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './admin-wastage.component.html',
  styleUrls: ['./admin-wastage.component.scss']
})
export class AdminWastageComponent implements OnInit, OnDestroy {
  private outletService = inject(OutletService);
  private uiStore = inject(UIStore);
  private outletSub?: Subscription;

  records: WastageRecord[] = [];
  summary: WastageSummary | null = null;
  loading = true;
  showModal = false;
  startDate = '';
  endDate = '';
  activeTab: 'records' | 'summary' = 'records';

  wastageForm = { items: [{ menuItemId: '', menuItemName: '', quantity: 1, unitCost: 0 }], reason: '' };

  constructor(private wastageService: WastageService) {
    const now = new Date();
    const firstDay = new Date(now.getFullYear(), now.getMonth(), 1);
    this.startDate = firstDay.toISOString().split('T')[0];
    this.endDate = now.toISOString().split('T')[0];
  }

  ngOnInit() {
    this.outletSub = this.outletService.selectedOutlet$
      .pipe(filter(o => o !== null))
      .subscribe(() => this.loadRecords());
    if (this.outletService.getSelectedOutlet()) this.loadRecords();
  }

  ngOnDestroy() { this.outletSub?.unsubscribe(); }

  loadRecords() {
    this.loading = true;
    this.wastageService.getWastageRecords(this.startDate, this.endDate).subscribe({
      next: r => { this.records = r; this.loading = false; },
      error: () => { this.uiStore.error('Failed to load wastage records'); this.loading = false; }
    });
  }

  loadSummary() {
    this.wastageService.getWastageSummary(this.startDate, this.endDate).subscribe({
      next: s => this.summary = s,
      error: () => this.uiStore.error('Failed to load wastage summary')
    });
  }

  openCreateModal() {
    this.wastageForm = { items: [{ menuItemId: '', menuItemName: '', quantity: 1, unitCost: 0 }], reason: '' };
    this.showModal = true;
  }

  closeModal() { this.showModal = false; }

  addItem() { this.wastageForm.items.push({ menuItemId: '', menuItemName: '', quantity: 1, unitCost: 0 }); }

  removeItem(i: number) { this.wastageForm.items.splice(i, 1); }

  saveWastage() {
    this.wastageService.createWastageRecord(this.wastageForm).subscribe({
      next: () => { this.uiStore.success('Wastage recorded'); this.loadRecords(); this.closeModal(); },
      error: () => this.uiStore.error('Failed to record wastage')
    });
  }

  getTotalValue(): number {
    return this.wastageForm.items.reduce((sum, i) => sum + (i.quantity * i.unitCost), 0);
  }

  trackById(i: number) { return i; }
}
