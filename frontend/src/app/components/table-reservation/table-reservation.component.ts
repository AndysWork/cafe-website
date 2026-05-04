import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TableReservationService, TableReservation, CreateReservationRequest } from '../../services/table-reservation.service';
import { OutletService } from '../../services/outlet.service';
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
  private uiStore = inject(UIStore);

  activeTab: 'book' | 'my' = 'book';
  myReservations: TableReservation[] = [];
  loadingMy = false;
  submitting = false;
  submitted = false;
  outlets: any[] = [];

  form: CreateReservationRequest = {
    customerName: '',
    customerPhone: '',
    partySize: 2,
    reservationDate: '',
    timeSlot: '',
    specialRequests: ''
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

    this.outletService.getAllOutlets().subscribe({
      next: (outlets: any[]) => this.outlets = outlets
    });
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
    if (!this.form.customerName || !this.form.customerPhone || !this.form.reservationDate || !this.form.timeSlot) {
      this.uiStore.error('Please fill in all required fields');
      return;
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
    this.form = { customerName: '', customerPhone: '', partySize: 2, reservationDate: '', timeSlot: '', specialRequests: '' };
    const tomorrow = new Date();
    tomorrow.setDate(tomorrow.getDate() + 1);
    this.form.reservationDate = getIstInputDate(tomorrow);
  }

  cancelReservation(id: string) {
    this.reservationService.updateReservationStatus(id, 'cancelled').subscribe({
      next: () => { this.uiStore.success('Reservation cancelled'); this.loadMyReservations(); },
      error: () => this.uiStore.error('Failed to cancel')
    });
  }

  getMinDate(): string {
    return getIstInputDate(new Date());
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
