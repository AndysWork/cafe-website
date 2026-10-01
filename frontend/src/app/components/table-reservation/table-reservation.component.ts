import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { TableReservationService, TableReservation, CreateReservationRequest } from '../../services/table-reservation.service';
import { OutletService } from '../../services/outlet.service';
import { DineInService } from '../../services/dine-in.service';
import { AuthService, User } from '../../services/auth.service';
import { UIStore } from '../../store/ui.store';
import { getIstInputDate } from '../../utils/date-utils';

@Component({
  selector: 'app-table-reservation',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './table-reservation.component.html',
  styleUrls: ['./table-reservation.component.scss']
})
export class TableReservationComponent implements OnInit {
  private reservationService = inject(TableReservationService);
  private outletService = inject(OutletService);
  private dineInService = inject(DineInService);
  private authService = inject(AuthService);
  private uiStore = inject(UIStore);
  private router = inject(Router);

  activeTab: 'book' | 'my' = 'book';
  myReservations: TableReservation[] = [];
  loadingMy = false;
  submitting = false;
  submitted = false;
  checkingInId: string | null = null;
  outlets: any[] = [];

  // Check-In Table Modal & Validation
  showCheckInModal = false;
  selectedReservationForCheckIn: TableReservation | null = null;
  checkInTableInput = '';
  checkInAttempted = false;
  readonly allowedTables = ['1', '2', '3', '4', '5', '6', '7', '8', '9', '10'];

  form: CreateReservationRequest = {
    customerName: '',
    customerPhone: '',
    partySize: 2,
    reservationDate: '',
    timeSlot: '',
    specialRequests: '',
    outletId: undefined
  };

  timeSlots = [
    '11:00 AM', '11:30 AM', '12:00 PM', '12:30 PM', '1:00 PM', '1:30 PM',
    '2:00 PM', '6:00 PM', '6:30 PM', '7:00 PM', '7:30 PM', '8:00 PM',
    '8:30 PM', '9:00 PM', '9:30 PM'
  ];

  partySizes = [1, 2, 3, 4, 5, 6, 7, 8, 10, 12];

  ngOnInit() {
    const tomorrow = new Date();
    tomorrow.setDate(tomorrow.getDate() + 1);
    this.form.reservationDate = getIstInputDate(tomorrow);

    this.prefillUserDetails();

    this.outletService.getPublicOutlets().subscribe({
      next: (outlets: any[]) => {
        this.outlets = (outlets || []).filter(o => o.isActive !== false);
        const selected = this.outletService.getSelectedOutlet();
        if (selected) {
          this.form.outletId = selected.id || selected._id;
        } else if (this.outlets.length > 0) {
          this.form.outletId = this.outlets[0].id || this.outlets[0]._id;
        }
      }
    });
  }

  private prefillUserDetails(): void {
    const user = this.authService.getCurrentUser();
    if (user) {
      const fullName = [user.firstName, user.lastName].filter(Boolean).join(' ').trim();
      this.form.customerName = fullName || user.username || '';
      this.form.customerPhone = user.phoneNumber || '';
      if (user.email && !this.form.customerEmail) {
        this.form.customerEmail = user.email;
      }
    }
  }

  switchTab(tab: 'book' | 'my') {
    this.activeTab = tab;
    if (tab === 'my') this.loadMyReservations();
  }

  loadMyReservations() {
    this.loadingMy = true;
    this.reservationService.getMyReservations().subscribe({
      next: (data) => { this.myReservations = data; this.loadingMy = false; },
      error: () => { this.uiStore.error('Failed to load reservations'); this.loadingMy = false; }
    });
  }

  submitReservation() {
    if (!this.isDineInCurrentlyAvailable) {
      this.uiStore.warning(this.dineInClosedReason);
      return;
    }

    if (!this.form.customerName || !this.form.customerPhone || !this.form.reservationDate || !this.form.timeSlot) {
      this.uiStore.error('Please fill in all required fields');
      return;
    }
    if (!this.form.outletId) {
      this.form.outletId = this.outletService.getSelectedOutletId() || this.outlets[0]?.id || this.outlets[0]?._id;
    }
    this.submitting = true;
    this.reservationService.createReservation(this.form).subscribe({
      next: () => {
        this.uiStore.success('Reservation booked! We\'ll confirm shortly.');
        this.submitted = true;
        this.submitting = false;
      },
      error: () => {
        this.uiStore.error('Failed to book reservation');
        this.submitting = false;
      }
    });
  }

  bookAnother() {
    this.submitted = false;
    const defaultOutletId = this.outletService.getSelectedOutletId() || this.outlets[0]?.id || this.outlets[0]?._id;
    this.form = {
      customerName: '',
      customerPhone: '',
      partySize: 2,
      reservationDate: '',
      timeSlot: '',
      specialRequests: '',
      outletId: defaultOutletId
    };
    const tomorrow = new Date();
    tomorrow.setDate(tomorrow.getDate() + 1);
    this.form.reservationDate = getIstInputDate(tomorrow);
    this.prefillUserDetails();
  }

  cancelReservation(id: string) {
    this.reservationService.updateReservationStatus(id, 'cancelled').subscribe({
      next: () => { this.uiStore.success('Reservation cancelled'); this.loadMyReservations(); },
      error: () => this.uiStore.error('Failed to cancel')
    });
  }

  isReservationToday(reservationDate?: string): boolean {
    if (!reservationDate) return false;
    const resDate = new Date(reservationDate);
    const today = new Date();
    return resDate.getFullYear() === today.getFullYear() &&
           resDate.getMonth() === today.getMonth() &&
           resDate.getDate() === today.getDate();
  }

  isTableValid(val?: string | null): boolean {
    if (!val) return false;
    const num = Number(val.trim());
    return Number.isInteger(num) && num >= 1 && num <= 10;
  }

  get checkInTableValidationError(): string {
    if (!this.checkInTableInput && !this.checkInAttempted) return '';
    if (!this.checkInTableInput) return 'Please select or enter a table number.';
    const num = Number(this.checkInTableInput.trim());
    if (isNaN(num) || !Number.isInteger(num) || num < 1 || num > 10) {
      return 'Invalid table number. Only tables from 1 to 10 are valid.';
    }
    return '';
  }

  openCheckInModal(reservation: TableReservation): void {
    if (!this.isDineInCurrentlyAvailable) {
      this.uiStore.warning(this.dineInClosedReason);
      return;
    }

    if (!reservation.id) return;

    // If already seated, navigate directly to menu for that table
    if (reservation.status === 'seated' && reservation.tableNumber) {
      this.dineInService.setTableNumber(reservation.tableNumber);
      this.router.navigate(['/menu'], { queryParams: { table: reservation.tableNumber } });
      return;
    }

    this.selectedReservationForCheckIn = reservation;
    this.checkInTableInput = reservation.tableNumber || '';
    this.checkInAttempted = false;
    this.showCheckInModal = true;
  }

  closeCheckInModal(): void {
    this.showCheckInModal = false;
    this.selectedReservationForCheckIn = null;
    this.checkInTableInput = '';
    this.checkInAttempted = false;
  }

  selectCheckInTable(table: string): void {
    this.checkInTableInput = this.checkInTableInput === table ? '' : table;
  }

  clearCheckInTableSelection(): void {
    this.checkInTableInput = '';
    this.checkInAttempted = false;
  }

  confirmCheckIn(): void {
    if (!this.isDineInCurrentlyAvailable) {
      this.uiStore.warning(this.dineInClosedReason);
      return;
    }

    this.checkInAttempted = true;
    if (!this.selectedReservationForCheckIn?.id) return;

    if (!this.isTableValid(this.checkInTableInput)) {
      this.uiStore.error('Please enter a valid table number between 1 and 10');
      return;
    }

    const reservationId = this.selectedReservationForCheckIn.id;
    const targetTable = this.checkInTableInput.trim();

    this.checkingInId = reservationId;
    this.reservationService.checkInReservation(reservationId, targetTable).subscribe({
      next: (res) => {
        this.checkingInId = null;
        this.dineInService.setTableNumber(res.tableNumber);
        this.uiStore.success(res.message || `Checked in at Table ${res.tableNumber}!`);
        this.closeCheckInModal();
        // Navigate directly to menu to start ordering food in rounds
        this.router.navigate(['/menu'], { queryParams: { table: res.tableNumber } });
      },
      error: (err) => {
        this.checkingInId = null;
        this.uiStore.error(err?.error?.error || 'Failed to check in for reservation');
      }
    });
  }

  viewPaidBill(reservation: TableReservation): void {
    if (reservation.id) {
      this.dineInService.viewReservationBill(reservation.id, reservation.tableNumber, reservation.dineInSessionId);
    } else if (reservation.dineInSessionId) {
      this.dineInService.openBillModalWithSession(reservation.dineInSessionId, reservation.tableNumber);
    } else {
      this.uiStore.warning('No bill record found for this reservation.');
    }
  }

  formatTableDisplay(table?: string): string {
    if (!table) return '';
    return this.dineInService.sanitizeTableNumber(table);
  }

  getMinDate(): string {
    return getIstInputDate(new Date());
  }

  get isDineInCurrentlyAvailable(): boolean {
    const selectedOutlet = this.getSelectedOutletForReservation();
    if (!selectedOutlet) return true;

    if (selectedOutlet.isActive === false) {
      return false;
    }

    if (selectedOutlet.settings?.acceptsDineIn === false) {
      return false;
    }

    return this.isCurrentTimeWithinOutletHours(selectedOutlet.settings?.openingTime, selectedOutlet.settings?.closingTime);
  }

  get dineInClosedReason(): string {
    const selectedOutlet = this.getSelectedOutletForReservation();
    const outletName = selectedOutlet?.outletName || 'selected outlet';

    if (!selectedOutlet || selectedOutlet.isActive === false) {
      return `Shop is currently closed for ${outletName}.`;
    }

    if (selectedOutlet.settings?.acceptsDineIn === false) {
      return 'Dine-in reservations are currently turned off by the outlet admin.';
    }

    const openingTime = selectedOutlet.settings?.openingTime || 'opening time';
    const closingTime = selectedOutlet.settings?.closingTime || 'closing time';
    return `Shop is closed right now. Dine-in is available from ${openingTime} to ${closingTime}.`;
  }

  private getSelectedOutletForReservation(): any | null {
    const selectedOutletId = this.form.outletId || this.outletService.getSelectedOutletId();
    if (!selectedOutletId) {
      return this.outlets[0] || null;
    }

    return this.outlets.find(o => {
      const id = o?.id || o?._id;
      return id === selectedOutletId;
    }) || null;
  }

  private isCurrentTimeWithinOutletHours(openingTime?: string, closingTime?: string): boolean {
    const openingMinutes = this.parseTimeToMinutes(openingTime);
    const closingMinutes = this.parseTimeToMinutes(closingTime);

    if (openingMinutes === null || closingMinutes === null) {
      return true;
    }

    const now = new Date();
    const currentMinutes = (now.getHours() * 60) + now.getMinutes();

    if (closingMinutes > openingMinutes) {
      return currentMinutes >= openingMinutes && currentMinutes < closingMinutes;
    }

    // Supports overnight windows such as 18:00 to 02:00.
    return currentMinutes >= openingMinutes || currentMinutes < closingMinutes;
  }

  private parseTimeToMinutes(value?: string): number | null {
    if (!value) return null;

    const [hourString, minuteString] = value.split(':');
    const hour = Number(hourString);
    const minute = Number(minuteString);

    if (!Number.isInteger(hour) || !Number.isInteger(minute)) return null;
    if (hour < 0 || hour > 23 || minute < 0 || minute > 59) return null;

    return (hour * 60) + minute;
  }

  getStatusClass(status: string): string {
    return status;
  }

  formatDate(dateStr?: string): string {
    if (!dateStr) return '-';
    return new Date(dateStr).toLocaleDateString('en-IN', {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
      timeZone: 'Asia/Kolkata'
    });
  }
}
