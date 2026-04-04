import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { handleServiceError } from '../utils/error-handler';

export interface TableReservation {
  id?: string;
  userId?: string;
  customerName: string;
  customerPhone: string;
  partySize: number;
  tableNumber?: string;
  reservationDate: string;
  timeSlot: string;
  status: 'pending' | 'confirmed' | 'cancelled' | 'completed' | 'no-show';
  specialRequests?: string;
  outletId?: string;
  createdAt?: string;
}

export interface CreateReservationRequest {
  customerName: string;
  customerPhone: string;
  partySize: number;
  tableNumber?: string;
  reservationDate: string;
  timeSlot: string;
  specialRequests?: string;
}

@Injectable({ providedIn: 'root' })
export class TableReservationService {
  private http = inject(HttpClient);
  private apiUrl = environment.apiUrl;

  createReservation(reservation: CreateReservationRequest): Observable<TableReservation> {
    return this.http.post<TableReservation>(`${this.apiUrl}/reservations`, reservation).pipe(
      catchError(handleServiceError('TableReservationService.createReservation'))
    );
  }

  getReservations(date?: string): Observable<TableReservation[]> {
    const params = date ? `?date=${date}` : '';
    return this.http.get<TableReservation[]>(`${this.apiUrl}/reservations${params}`).pipe(
      catchError(handleServiceError('TableReservationService.getReservations'))
    );
  }

  getMyReservations(): Observable<TableReservation[]> {
    return this.http.get<TableReservation[]>(`${this.apiUrl}/reservations/my`).pipe(
      catchError(handleServiceError('TableReservationService.getMyReservations'))
    );
  }

  updateReservationStatus(id: string, status: string): Observable<{ message: string }> {
    return this.http.put<{ message: string }>(`${this.apiUrl}/reservations/${id}/status`, { status }).pipe(
      catchError(handleServiceError('TableReservationService.updateReservationStatus'))
    );
  }
}
