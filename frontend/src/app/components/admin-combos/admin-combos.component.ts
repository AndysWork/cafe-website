import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ComboMealService, ComboMeal, CreateComboRequest } from '../../services/combo-meal.service';
import { OutletService } from '../../services/outlet.service';
import { UIStore } from '../../store/ui.store';
import { Subscription } from 'rxjs';
import { filter } from 'rxjs/operators';

@Component({
  selector: 'app-admin-combos',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './admin-combos.component.html',
  styleUrls: ['./admin-combos.component.scss']
})
export class AdminCombosComponent implements OnInit, OnDestroy {
  private outletService = inject(OutletService);
  private uiStore = inject(UIStore);
  private outletSub?: Subscription;

  combos: ComboMeal[] = [];
  loading = true;
  showModal = false;
  isEditMode = false;
  currentCombo: ComboMeal | null = null;

  comboForm: CreateComboRequest = this.getEmpty();

  constructor(private comboService: ComboMealService) {}

  ngOnInit() {
    this.outletSub = this.outletService.selectedOutlet$
      .pipe(filter(o => o !== null))
      .subscribe(() => this.loadCombos());
    if (this.outletService.getSelectedOutlet()) this.loadCombos();
  }

  ngOnDestroy() { this.outletSub?.unsubscribe(); }

  getEmpty(): CreateComboRequest {
    return { name: '', description: '', items: [{ menuItemId: '', quantity: 1 }], comboPrice: 0 };
  }

  loadCombos() {
    this.loading = true;
    this.comboService.getAllCombos().subscribe({
      next: c => { this.combos = c; this.loading = false; },
      error: () => { this.uiStore.error('Failed to load combos'); this.loading = false; }
    });
  }

  openCreateModal() { this.isEditMode = false; this.comboForm = this.getEmpty(); this.showModal = true; }

  openEditModal(c: ComboMeal) {
    this.isEditMode = true; this.currentCombo = c;
    this.comboForm = { name: c.name, description: c.description, items: c.items.map(i => ({ menuItemId: i.menuItemId, quantity: i.quantity })), comboPrice: c.comboPrice };
    this.showModal = true;
  }

  closeModal() { this.showModal = false; this.currentCombo = null; }

  addItem() { this.comboForm.items.push({ menuItemId: '', quantity: 1 }); }
  removeItem(i: number) { this.comboForm.items.splice(i, 1); }

  saveCombo() {
    if (this.isEditMode && this.currentCombo?.id) {
      this.comboService.updateCombo(this.currentCombo.id, this.comboForm).subscribe({
        next: () => { this.uiStore.success('Combo updated'); this.loadCombos(); this.closeModal(); },
        error: () => this.uiStore.error('Failed to update combo')
      });
    } else {
      this.comboService.createCombo(this.comboForm).subscribe({
        next: () => { this.uiStore.success('Combo created'); this.loadCombos(); this.closeModal(); },
        error: () => this.uiStore.error('Failed to create combo')
      });
    }
  }

  deleteCombo(id: string) {
    if (!confirm('Delete this combo?')) return;
    this.comboService.deleteCombo(id).subscribe({
      next: () => { this.uiStore.success('Combo deleted'); this.loadCombos(); },
      error: () => this.uiStore.error('Failed to delete combo')
    });
  }

  trackById(_: number, item: ComboMeal) { return item.id; }
  trackByIndex(i: number) { return i; }
}
