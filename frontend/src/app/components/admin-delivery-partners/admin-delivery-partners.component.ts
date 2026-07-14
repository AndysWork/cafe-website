import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DeliveryPartnerService, DeliveryPartner, PartnerPayoutSummary, AssignableUser } from '../../services/delivery-partner.service';
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
  searchTerm = '';
  statusFilter: 'all' | 'available' | 'on-delivery' | 'offline' = 'all';
  sortBy: 'name' | 'rating' | 'deliveries' | 'recent' = 'name';
  showModal = false;
  isEditMode = false;
  currentPartner: DeliveryPartner | null = null;
  assignableUsers: AssignableUser[] = [];
  filteredUsers: AssignableUser[] = [];
  userSearch = '';

  partnerForm: Partial<DeliveryPartner> = {
    name: '',
    phone: '',
    vehicleType: 'bike',
    userId: '',
    mileageKmpl: 40,
    codAllowed: true,
    payoutEnabled: true
  };

  showAssignModal = false;
  assignForm = { orderId: '', deliveryPartnerId: '' };
  locationDraft: Record<string, { latitude: string; longitude: string }> = {};
  showShiftModal = false;
  shiftForm = {
    partnerId: '',
    mode: 'start' as 'start' | 'end',
    shiftId: '',
    startOdometerKm: '',
    endOdometerKm: '',
    notes: ''
  };
  showTripModal = false;
  tripForm = {
    partnerId: '',
    shiftId: '',
    tripType: 'delivery',
    orderId: '',
    startOdometerKm: '',
    endOdometerKm: '',
    startPointLabel: '',
    endPointLabel: '',
    notes: ''
  };
  showParcelModal = false;
  parcelForm = {
    partnerId: '',
    startPoint: '',
    endPoint: '',
    isRoundTrip: false,
    notes: ''
  };
  showFuelModal = false;
  fuelForm = { date: '', petrolPricePerLitre: '' };
  quickFuelSubmitting = false;
  showCodModal = false;
  codForm = { partnerId: '', orderId: '', amount: '', collectionReference: '', notes: '' };
  payoutPeriodType: 'day' | 'week' | 'month' | 'year' = 'day';
  payoutSummaryByPartner: Record<string, PartnerPayoutSummary | null> = {};

  constructor(private partnerService: DeliveryPartnerService) {}

  get totalPartners(): number {
    return this.partners.length;
  }

  get availablePartnersCount(): number {
    return this.partners.filter(p => p.status === 'available').length;
  }

  get onDeliveryPartnersCount(): number {
    return this.partners.filter(p => p.status === 'on-delivery').length;
  }

  get offlinePartnersCount(): number {
    return this.partners.filter(p => p.status === 'offline').length;
  }

  get avgRating(): number {
    if (this.partners.length === 0) return 0;
    const total = this.partners.reduce((sum, p) => sum + (p.rating || 0), 0);
    return total / this.partners.length;
  }

  get filteredPartners(): DeliveryPartner[] {
    const query = this.searchTerm.trim().toLowerCase();

    const filtered = this.partners.filter(partner => {
      const statusOk = this.statusFilter === 'all' || partner.status === this.statusFilter;
      if (!statusOk) {
        return false;
      }

      if (!query) {
        return true;
      }

      const searchBlob = [
        partner.name,
        partner.phone,
        partner.vehicleType,
        partner.vehicleNumber,
        partner.currentOrderId
      ]
        .filter(Boolean)
        .join(' ')
        .toLowerCase();

      return searchBlob.includes(query);
    });

    return filtered.sort((a, b) => {
      if (this.sortBy === 'rating') {
        return (b.rating || 0) - (a.rating || 0);
      }

      if (this.sortBy === 'deliveries') {
        return (b.totalDeliveries || 0) - (a.totalDeliveries || 0);
      }

      if (this.sortBy === 'recent') {
        const da = new Date(a.createdAt || 0).getTime();
        const db = new Date(b.createdAt || 0).getTime();
        return db - da;
      }

      return (a.name || '').localeCompare(b.name || '');
    });
  }

  ngOnInit() {
    this.outletSub = this.outletService.selectedOutlet$
      .pipe(filter(o => o !== null))
      .subscribe(() => this.loadPartners());
    if (this.outletService.getSelectedOutlet()) this.loadPartners();
    this.loadAssignableUsers();
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

  loadAssignableUsers() {
    this.partnerService.getAssignableUsers().subscribe({
      next: users => {
        this.assignableUsers = users.filter(u =>
          u.isActive && (u.role === 'partner' || u.role === 'delivery-partner')
        );
        this.applyUserFilter();
      },
      error: () => this.uiStore.error('Failed to load partner users')
    });
  }

  applyUserFilter() {
    const q = this.userSearch.trim().toLowerCase();
    if (!q) {
      this.filteredUsers = [...this.assignableUsers];
      return;
    }

    this.filteredUsers = this.assignableUsers.filter(u => {
      const fullName = `${u.firstName || ''} ${u.lastName || ''}`.trim().toLowerCase();
      return (
        u.username.toLowerCase().includes(q) ||
        u.email.toLowerCase().includes(q) ||
        fullName.includes(q) ||
        (u.phoneNumber || '').toLowerCase().includes(q)
      );
    });
  }

  openCreateModal() {
    this.isEditMode = false;
    this.currentPartner = null;
    this.partnerForm = {
      name: '',
      phone: '',
      vehicleType: 'bike',
      userId: '',
      mileageKmpl: 40,
      codAllowed: true,
      payoutEnabled: true
    };
    this.showModal = true;
    this.userSearch = '';
    this.applyUserFilter();
  }

  openEditModal(partner: DeliveryPartner) {
    this.isEditMode = true;
    this.currentPartner = partner;
    this.partnerForm = {
      name: partner.name,
      phone: partner.phone,
      vehicleType: partner.vehicleType,
      userId: partner.userId || '',
      mileageKmpl: partner.mileageKmpl || 40,
      codAllowed: partner.codAllowed ?? true,
      payoutEnabled: partner.payoutEnabled ?? true
    };
    this.showModal = true;
    this.userSearch = '';
    this.applyUserFilter();
  }

  onSelectUser(userId: string) {
    this.partnerForm.userId = userId;
  }

  getUserLabel(user: AssignableUser): string {
    const fullName = `${user.firstName || ''} ${user.lastName || ''}`.trim();
    const name = fullName || user.username;
    return `${name} (${user.email})`;
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

  openShiftModal(partnerId: string, mode: 'start' | 'end') {
    this.shiftForm = {
      partnerId,
      mode,
      shiftId: '',
      startOdometerKm: '',
      endOdometerKm: '',
      notes: ''
    };
    this.showShiftModal = true;
  }

  submitShiftAction() {
    if (!this.shiftForm.partnerId) return;

    if (this.shiftForm.mode === 'start') {
      const startOdometerKm = Number(this.shiftForm.startOdometerKm);
      if (!Number.isFinite(startOdometerKm) || startOdometerKm < 0) {
        this.uiStore.error('Enter valid start odometer');
        return;
      }

      this.partnerService.startShift(this.shiftForm.partnerId, {
        startOdometerKm,
        notes: this.shiftForm.notes || undefined
      }).subscribe({
        next: () => {
          this.uiStore.success('Shift started');
          this.showShiftModal = false;
          this.loadPartners();
        },
        error: () => this.uiStore.error('Failed to start shift')
      });
      return;
    }

    const endOdometerKm = Number(this.shiftForm.endOdometerKm);
    if (!this.shiftForm.shiftId.trim()) {
      this.uiStore.error('Shift ID is required to end shift');
      return;
    }
    if (!Number.isFinite(endOdometerKm) || endOdometerKm < 0) {
      this.uiStore.error('Enter valid end odometer');
      return;
    }

    this.partnerService.endShift(this.shiftForm.partnerId, this.shiftForm.shiftId.trim(), {
      endOdometerKm,
      notes: this.shiftForm.notes || undefined
    }).subscribe({
      next: () => {
        this.uiStore.success('Shift ended');
        this.showShiftModal = false;
        this.loadPartners();
      },
      error: () => this.uiStore.error('Failed to end shift')
    });
  }

  openTripModal(partnerId: string) {
    this.tripForm = {
      partnerId,
      shiftId: '',
      tripType: 'delivery',
      orderId: '',
      startOdometerKm: '',
      endOdometerKm: '',
      startPointLabel: '',
      endPointLabel: '',
      notes: ''
    };
    this.showTripModal = true;
  }

  openParcelModal() {
    this.parcelForm = {
      partnerId: '',
      startPoint: '',
      endPoint: '',
      isRoundTrip: false,
      notes: ''
    };
    this.showParcelModal = true;
  }

  submitParcelTask() {
    if (!this.parcelForm.partnerId) {
      this.uiStore.error('Select a delivery partner');
      return;
    }
    if (!this.parcelForm.startPoint.trim() || !this.parcelForm.endPoint.trim()) {
      this.uiStore.error('Start and end points are required');
      return;
    }

    this.partnerService.createParcelTask({
      partnerId: this.parcelForm.partnerId,
      startPoint: this.parcelForm.startPoint.trim(),
      endPoint: this.parcelForm.endPoint.trim(),
      isRoundTrip: this.parcelForm.isRoundTrip,
      notes: this.parcelForm.notes.trim() || undefined
    }).subscribe({
      next: (task) => {
        this.uiStore.success(`Parcel task assigned (${task.billableDistanceKm} km)`);
        this.showParcelModal = false;
      },
      error: () => this.uiStore.error('Failed to assign parcel task')
    });
  }

  submitTrip() {
    if (!this.tripForm.partnerId || !this.tripForm.shiftId.trim()) {
      this.uiStore.error('Partner and shift ID are required');
      return;
    }

    const startOdometerKm = Number(this.tripForm.startOdometerKm);
    const endOdometerKm = Number(this.tripForm.endOdometerKm);

    if (!Number.isFinite(startOdometerKm) || !Number.isFinite(endOdometerKm) || startOdometerKm < 0 || endOdometerKm < 0) {
      this.uiStore.error('Enter valid odometer values');
      return;
    }

    this.partnerService.createTrip(this.tripForm.partnerId, {
      shiftId: this.tripForm.shiftId.trim(),
      tripType: this.tripForm.tripType,
      orderId: this.tripForm.orderId.trim() || undefined,
      startOdometerKm,
      endOdometerKm,
      startPointLabel: this.tripForm.startPointLabel.trim() || undefined,
      endPointLabel: this.tripForm.endPointLabel.trim() || undefined,
      notes: this.tripForm.notes.trim() || undefined
    }).subscribe({
      next: () => {
        this.uiStore.success('Trip logged successfully');
        this.showTripModal = false;
      },
      error: () => this.uiStore.error('Failed to log trip')
    });
  }

  openFuelModal() {
    const today = new Date();
    const date = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`;
    this.fuelForm = { date, petrolPricePerLitre: '' };
    this.showFuelModal = true;
  }

  submitQuickFuelPrice() {
    const petrolPricePerLitre = Number(this.fuelForm.petrolPricePerLitre);
    if (!this.fuelForm.date || !Number.isFinite(petrolPricePerLitre) || petrolPricePerLitre <= 0) {
      this.uiStore.error('Enter valid date and fuel price');
      return;
    }

    this.quickFuelSubmitting = true;
    this.partnerService.upsertFuelPrice({
      date: this.fuelForm.date,
      petrolPricePerLitre
    }).subscribe({
      next: () => {
        this.uiStore.success('Fuel price updated');
        this.quickFuelSubmitting = false;
        Object.keys(this.payoutSummaryByPartner).forEach(partnerId => this.loadPayout(partnerId));
      },
      error: () => {
        this.quickFuelSubmitting = false;
        this.uiStore.error('Failed to update fuel price');
      }
    });
  }

  submitFuelPrice() {
    const petrolPricePerLitre = Number(this.fuelForm.petrolPricePerLitre);
    if (!this.fuelForm.date || !Number.isFinite(petrolPricePerLitre) || petrolPricePerLitre <= 0) {
      this.uiStore.error('Enter valid date and fuel price');
      return;
    }

    this.partnerService.upsertFuelPrice({
      date: this.fuelForm.date,
      petrolPricePerLitre
    }).subscribe({
      next: () => {
        this.uiStore.success('Fuel price updated');
        this.showFuelModal = false;
      },
      error: () => this.uiStore.error('Failed to update fuel price')
    });
  }

  openCodModal(partnerId: string) {
    this.codForm = { partnerId, orderId: '', amount: '', collectionReference: '', notes: '' };
    this.showCodModal = true;
  }

  submitCod() {
    if (!this.codForm.partnerId || !this.codForm.orderId.trim()) {
      this.uiStore.error('Partner and order ID are required');
      return;
    }

    const amount = Number(this.codForm.amount);
    if (!Number.isFinite(amount) || amount < 0) {
      this.uiStore.error('Enter valid amount');
      return;
    }

    this.partnerService.confirmCodCollection(this.codForm.partnerId, {
      orderId: this.codForm.orderId.trim(),
      amount,
      collectionReference: this.codForm.collectionReference.trim() || undefined,
      notes: this.codForm.notes.trim() || undefined
    }).subscribe({
      next: () => {
        this.uiStore.success('COD confirmed');
        this.showCodModal = false;
      },
      error: () => this.uiStore.error('Failed to confirm COD')
    });
  }

  loadPayout(partnerId: string) {
    this.partnerService.getPartnerPayoutSummary(partnerId, this.payoutPeriodType).subscribe({
      next: summary => {
        this.payoutSummaryByPartner[partnerId] = summary;
        this.uiStore.success('Payout summary loaded');
      },
      error: () => this.uiStore.error('Failed to load payout summary')
    });
  }

  setStatusFilter(status: 'all' | 'available' | 'on-delivery' | 'offline') {
    this.statusFilter = status;
  }

  trackById(_: number, item: DeliveryPartner) { return item.id; }
}
