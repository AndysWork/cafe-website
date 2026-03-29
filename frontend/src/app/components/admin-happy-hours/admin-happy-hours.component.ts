import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HappyHourService, HappyHourRule } from '../../services/happy-hour.service';
import { OutletService } from '../../services/outlet.service';
import { UIStore } from '../../store/ui.store';
import { Subscription } from 'rxjs';
import { filter } from 'rxjs/operators';

@Component({
  selector: 'app-admin-happy-hours',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './admin-happy-hours.component.html',
  styleUrls: ['./admin-happy-hours.component.scss']
})
export class AdminHappyHoursComponent implements OnInit, OnDestroy {
  private outletService = inject(OutletService);
  private uiStore = inject(UIStore);
  private outletSub?: Subscription;

  rules: HappyHourRule[] = [];
  loading = true;
  showModal = false;
  isEditMode = false;
  currentRule: HappyHourRule | null = null;

  ruleForm: Partial<HappyHourRule> = this.getEmpty();
  dayNames = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];

  constructor(private happyHourService: HappyHourService) {}

  ngOnInit() {
    this.outletSub = this.outletService.selectedOutlet$
      .pipe(filter(o => o !== null))
      .subscribe(() => this.loadRules());
    if (this.outletService.getSelectedOutlet()) this.loadRules();
  }

  ngOnDestroy() { this.outletSub?.unsubscribe(); }

  getEmpty(): Partial<HappyHourRule> {
    return { name: '', startTime: '14:00', endTime: '17:00', daysOfWeek: [1, 2, 3, 4, 5], discountType: 'percentage', discountValue: 20, maxDiscount: 100, isActive: true };
  }

  loadRules() {
    this.loading = true;
    this.happyHourService.getAllHappyHours().subscribe({
      next: r => { this.rules = r; this.loading = false; },
      error: () => { this.uiStore.error('Failed to load happy hours'); this.loading = false; }
    });
  }

  openCreateModal() { this.isEditMode = false; this.ruleForm = this.getEmpty(); this.showModal = true; }

  openEditModal(r: HappyHourRule) {
    this.isEditMode = true; this.currentRule = r; this.ruleForm = { ...r }; this.showModal = true;
  }

  closeModal() { this.showModal = false; this.currentRule = null; }

  toggleDay(day: number) {
    const days = this.ruleForm.daysOfWeek || [];
    const idx = days.indexOf(day);
    if (idx >= 0) days.splice(idx, 1); else days.push(day);
    this.ruleForm.daysOfWeek = [...days];
  }

  isDaySelected(day: number): boolean {
    return (this.ruleForm.daysOfWeek || []).includes(day);
  }

  saveRule() {
    if (this.isEditMode && this.currentRule?.id) {
      this.happyHourService.updateHappyHour(this.currentRule.id, this.ruleForm).subscribe({
        next: () => { this.uiStore.success('Happy hour updated'); this.loadRules(); this.closeModal(); },
        error: () => this.uiStore.error('Failed to update')
      });
    } else {
      this.happyHourService.createHappyHour(this.ruleForm).subscribe({
        next: () => { this.uiStore.success('Happy hour created'); this.loadRules(); this.closeModal(); },
        error: () => this.uiStore.error('Failed to create')
      });
    }
  }

  deleteRule(id: string) {
    if (!confirm('Delete this happy hour rule?')) return;
    this.happyHourService.deleteHappyHour(id).subscribe({
      next: () => { this.uiStore.success('Deleted'); this.loadRules(); },
      error: () => this.uiStore.error('Failed to delete')
    });
  }

  getDaysLabel(days: number[]): string {
    return days.sort().map(d => this.dayNames[d]).join(', ');
  }

  trackById(_: number, item: HappyHourRule) { return item.id; }
}
