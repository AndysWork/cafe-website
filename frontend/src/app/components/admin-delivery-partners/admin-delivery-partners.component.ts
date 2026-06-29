import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DeliveryPartnerService, DeliveryPartner } from '../../services/delivery-partner.service';
import { OutletService } from '../../services/outlet.service';
import { UIStore } from '../../store/ui.store';
import { Subscription } from 'rxjs';
import { filter } from 'rxjs/operators';

@Component({
  selector: 'app-admin-delivery-partners',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './admin-delivery-partners.component.html',
  styleUrls: ['./admin-delivery-partners.component.scss']
})
export class AdminDeliveryPartnersComponent implements OnInit, OnDestroy {
  private outletService = inject(OutletService);
  private uiStore = inject(UIStore);
  private outletSub?: Subscription;

  partners: DeliveryPartner[] = [];
  loading = true;
  showModal = false;
  isEditMode = false;
  currentPartner: DeliveryPartner | null = null;

  partnerForm: Partial<DeliveryPartner> = { name: '', phone: '', vehicleType: 'bike' };

  showAssignModal = false;
  assignForm = { orderId: '', deliveryPartnerId: '' };
  locationDraft: Record<string, { latitude: string; longitude: string }> = {};

  constructor(private partnerService: DeliveryPartnerService) {}

  ngOnInit() {
    this.outletSub = this.outletService.selectedOutlet$
      .pipe(filter(o => o !== null))
      .subscribe(() => this.loadPartners());
    if (this.outletService.getSelectedOutlet()) this.loadPartners();
  }

  ngOnDestroy() { this.outletSub?.unsubscribe(); }

  loadPartners() {
    this.loading = true;
    this.partnerService.getDeliveryPartners().subscribe({
      next: p => {
        this.partners = p;
        this.locationDraft = {};
        for (const partner of this.partners) {
          if (!partner.id) continue;
          this.locationDraft[partner.id] = {
            latitude: partner.currentLatitude != null ? String(partner.currentLatitude) : '',
            longitude: partner.currentLongitude != null ? String(partner.currentLongitude) : ''
          };
        }
        this.loading = false;
      },
      error: () => { this.uiStore.error('Failed to load delivery partners'); this.loading = false; }
    });
  }

  openCreateModal() { this.isEditMode = false; this.currentPartner = null; this.partnerForm = { name: '', phone: '', vehicleType: 'bike' }; this.showModal = true; }

  openEditModal(partner: DeliveryPartner) {
    this.isEditMode = true;
    this.currentPartner = partner;
    this.partnerForm = { name: partner.name, phone: partner.phone, vehicleType: partner.vehicleType };
    this.showModal = true;
  }

  closeModal() { this.showModal = false; this.currentPartner = null; }

  savePartner() {
    if (this.isEditMode && this.currentPartner?.id) {
      this.partnerService.updatePartner(this.currentPartner.id, this.partnerForm).subscribe({
        next: () => { this.uiStore.success('Partner updated'); this.loadPartners(); this.closeModal(); },
        error: () => this.uiStore.error('Failed to update partner')
      });
    } else {
      this.partnerService.createDeliveryPartner(this.partnerForm).subscribe({
        next: () => { this.uiStore.success('Partner added'); this.loadPartners(); this.closeModal(); },
        error: () => this.uiStore.error('Failed to add partner')
      });
    }
  }

  deletePartner(id: string) {
    if (!confirm('Delete this delivery partner?')) return;
    this.partnerService.deletePartner(id).subscribe({
      next: () => { this.uiStore.success('Partner deleted'); this.loadPartners(); },
      error: () => this.uiStore.error('Failed to delete partner')
    });
  }

  updateStatus(partnerId: string, status: string) {
    this.partnerService.updatePartnerStatus(partnerId, status).subscribe({
      next: () => { this.uiStore.success('Status updated'); this.loadPartners(); },
      error: () => this.uiStore.error('Failed to update status')
    });
  }

  completeDelivery(partnerId: string) {
    this.partnerService.completeDelivery(partnerId).subscribe({
      next: () => { this.uiStore.success('Delivery completed'); this.loadPartners(); },
      error: () => this.uiStore.error('Failed to complete delivery')
    });
  }

  openAssignModal() { this.assignForm = { orderId: '', deliveryPartnerId: '' }; this.showAssignModal = true; }

  assignPartner() {
    this.partnerService.assignDeliveryPartner(this.assignForm).subscribe({
      next: () => { this.uiStore.success('Partner assigned'); this.showAssignModal = false; this.loadPartners(); },
      error: () => this.uiStore.error('Failed to assign partner')
    });
  }

  updateLocation(partner: DeliveryPartner) {
    const partnerId = partner.id;
    if (!partnerId) return;

    const draft = this.locationDraft[partnerId] || { latitude: '', longitude: '' };
    const latitude = Number(draft.latitude);
    const longitude = Number(draft.longitude);

    if (!Number.isFinite(latitude) || !Number.isFinite(longitude)) {
      this.uiStore.error('Enter valid latitude and longitude values');
      return;
    }

    if (latitude < -90 || latitude > 90 || longitude < -180 || longitude > 180) {
      this.uiStore.error('Latitude must be between -90 and 90, longitude between -180 and 180');
      return;
    }

    this.partnerService.updatePartnerLocation(partnerId, latitude, longitude).subscribe({
      next: () => {
        this.uiStore.success('Live location updated');
        this.loadPartners();
      },
      error: () => this.uiStore.error('Failed to update location')
    });
  }

  trackById(_: number, item: DeliveryPartner) { return item.id; }
}
