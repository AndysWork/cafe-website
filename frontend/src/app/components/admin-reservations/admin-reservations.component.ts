import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TableReservationService, TableReservation } from '../../services/table-reservation.service';
import { OutletService } from '../../services/outlet.service';
import { UIStore } from '../../store/ui.store';
import { Subscription } from 'rxjs';
import { filter } from 'rxjs/operators';

@Component({
  selector: 'app-admin-reservations',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './admin-reservations.component.html',
  styleUrls: ['./admin-reservations.component.scss']
})
export class AdminReservationsComponent implements OnInit, OnDestroy {
  private outletService = inject(OutletService);
  private uiStore = inject(UIStore);
  private outletSub?: Subscription;

  reservations: TableReservation[] = [];
  loading = true;
  filterDate = new Date().toISOString().split('T')[0];

  constructor(private reservationService: TableReservationService) {}

  ngOnInit() {
    this.outletSub = this.outletService.selectedOutlet$
      .pipe(filter(o => o !== null))
      .subscribe(() => this.loadReservations());
    if (this.outletService.getSelectedOutlet()) this.loadReservations();
  }

  ngOnDestroy() { this.outletSub?.unsubscribe(); }

  loadReservations() {
    this.loading = true;
    this.reservationService.getReservations(this.filterDate).subscribe({
      next: r => { this.reservations = r; this.loading = false; },
      error: () => { this.uiStore.error('Failed to load reservations'); this.loading = false; }
    });
  }

  updateStatus(id: string, status: string) {
    this.reservationService.updateReservationStatus(id, status).subscribe({
      next: () => { this.uiStore.success('Status updated'); this.loadReservations(); },
      error: () => this.uiStore.error('Failed to update status')
    });
  }

  getStatusClass(status: string): string {
    return status;
  }

  trackById(_: number, item: TableReservation) { return item.id; }
}
