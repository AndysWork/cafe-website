import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DeliveryZoneService, DeliveryZone } from '../../services/delivery-zone.service';
import { OutletService } from '../../services/outlet.service';
import { UIStore } from '../../store/ui.store';
import { Subscription } from 'rxjs';
import { filter } from 'rxjs/operators';

@Component({
  selector: 'app-admin-delivery-zones',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './admin-delivery-zones.component.html',
  styleUrls: ['./admin-delivery-zones.component.scss']
})
export class AdminDeliveryZonesComponent implements OnInit, OnDestroy {
  private outletService = inject(OutletService);
  private uiStore = inject(UIStore);
  private outletSub?: Subscription;

  zones: DeliveryZone[] = [];
  loading = true;
  showModal = false;
  isEditMode = false;
  currentZone: DeliveryZone | null = null;
  searchTerm = '';
  filterActive = '';

  zoneForm: DeliveryZone = this.getEmptyZone();

  constructor(private zoneService: DeliveryZoneService) {}

  get filteredZones(): DeliveryZone[] {
    return this.zones.filter(z => {
      const matchesSearch = !this.searchTerm || z.zoneName.toLowerCase().includes(this.searchTerm.toLowerCase());
      const matchesActive = this.filterActive === '' || (this.filterActive === 'active' ? z.isActive : !z.isActive);
      return matchesSearch && matchesActive;
    });
  }

  get activeCount(): number { return this.zones.filter(z => z.isActive).length; }
  get inactiveCount(): number { return this.zones.filter(z => !z.isActive).length; }
  get avgFee(): number {
    const active = this.zones.filter(z => z.isActive);
    return active.length ? active.reduce((sum, z) => sum + z.deliveryFee, 0) / active.length : 0;
  }

  ngOnInit() {
    this.outletSub = this.outletService.selectedOutlet$
      .pipe(filter(o => o !== null))
      .subscribe(() => this.loadZones());
    if (this.outletService.getSelectedOutlet()) this.loadZones();
  }

  ngOnDestroy() { this.outletSub?.unsubscribe(); }

  getEmptyZone(): DeliveryZone {
    return { zoneName: '', minDistance: 0, maxDistance: 5, deliveryFee: 30, freeDeliveryAbove: 500, estimatedMinutes: 30, isActive: true };
  }

  loadZones() {
    this.loading = true;
    this.zoneService.getDeliveryZones().subscribe({
      next: z => { this.zones = z; this.loading = false; },
      error: () => { this.uiStore.error('Failed to load delivery zones'); this.loading = false; }
    });
  }

  openCreateModal() { this.isEditMode = false; this.zoneForm = this.getEmptyZone(); this.showModal = true; }

  openEditModal(zone: DeliveryZone) {
    this.isEditMode = true; this.currentZone = zone; this.zoneForm = { ...zone }; this.showModal = true;
  }

  closeModal() { this.showModal = false; this.currentZone = null; }

  saveZone() {
    if (this.isEditMode && this.currentZone?.id) {
      this.zoneService.updateDeliveryZone(this.currentZone.id, this.zoneForm).subscribe({
        next: () => { this.uiStore.success('Zone updated'); this.loadZones(); this.closeModal(); },
        error: () => this.uiStore.error('Failed to update zone')
      });
    } else {
      this.zoneService.createDeliveryZone(this.zoneForm).subscribe({
        next: () => { this.uiStore.success('Zone created'); this.loadZones(); this.closeModal(); },
        error: () => this.uiStore.error('Failed to create zone')
      });
    }
  }

  deleteZone(id: string) {
    if (!confirm('Delete this delivery zone?')) return;
    this.zoneService.deleteDeliveryZone(id).subscribe({
      next: () => { this.uiStore.success('Zone deleted'); this.loadZones(); },
      error: () => this.uiStore.error('Failed to delete zone')
    });
  }

  trackById(_: number, item: DeliveryZone) { return item.id; }
}
