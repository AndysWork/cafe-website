import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { map } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { handleServiceError } from '../utils/error-handler';

export interface DeliveryPartner {
  id?: string;
  name: string;
  phone: string;
  vehicleType: string;
  vehicleNumber?: string;
  userId?: string;
  mileageKmpl?: number;
  codAllowed?: boolean;
  payoutEnabled?: boolean;
  licenseNumber?: string;
  emergencyContactName?: string;
  emergencyContactPhone?: string;
  bankOrUpi?: string;
  status: 'available' | 'on-delivery' | 'offline';
  currentOrderId?: string;
  currentLatitude?: number;
  currentLongitude?: number;
  lastLocationUpdatedAt?: string;
  totalDeliveries?: number;
  rating?: number;
  outletId?: string;
  createdAt?: string;
}

export interface AssignDeliveryRequest {
  orderId: string;
  deliveryPartnerId?: string;
}

export interface DeliveryShift {
  id?: string;
  partnerId: string;
  outletId: string;
  shiftDate: string;
  startedAt: string;
  endedAt?: string;
  startOdometerKm: number;
  endOdometerKm?: number;
  totalDistanceKm: number;
  status: 'active' | 'completed' | 'cancelled';
}

export interface PartnerTrip {
  id?: string;
  shiftId: string;
  partnerId: string;
  tripType: 'delivery' | 'outlet-transfer' | 'market-stop' | 'misc';
  orderId?: string;
  startOdometerKm: number;
  endOdometerKm: number;
  distanceKm: number;
  startPointLabel?: string;
  endPointLabel?: string;
  notes?: string;
}

export interface ParcelTask {
  id?: string;
  outletId: string;
  partnerId: string;
  partnerName?: string;
  startPoint: string;
  endPoint: string;
  distanceKm: number;
  isRoundTrip: boolean;
  billableDistanceKm: number;
  etaMinutes?: number;
  routeMapUrl?: string;
  routeShortCode?: string;
  routeShortUrl?: string;
  notes?: string;
  status: 'assigned' | 'accepted' | 'completed' | 'cancelled';
  acceptedAt?: string;
  completedAt?: string;
  createdAt?: string;
  updatedAt?: string;
}

export interface ParcelRouteQuote {
  distanceKm: number;
  billableDistanceKm: number;
  isRoundTrip: boolean;
  etaMinutes?: number;
  mapUrl?: string;
}

export interface PartnerDashboard {
  profile: DeliveryPartner | null;
  activeShift: DeliveryShift | null;
  recentShifts: DeliveryShift[];
  activeOrders: Array<{
    id?: string;
    status: string;
    total: number;
    phoneNumber?: string;
    deliveryAddress?: string;
    deliveryRouteShortUrl?: string;
    deliveryRouteUrl?: string;
    deliveryDistanceKm?: number;
    deliveryEtaMinutes?: number;
    deliveryPartnerId?: string;
    paymentMethod?: 'cod' | 'razorpay' | 'upi-qr';
    paymentStatus?: 'pending' | 'paid' | 'refunded';
  }>;
  pendingRequests: Array<{
    id?: string;
    status: string;
    total: number;
    phoneNumber?: string;
    deliveryAddress?: string;
    createdAt?: string;
    deliveryDistanceKm?: number;
    deliveryEtaMinutes?: number;
  }>;
  activeParcelTasks: ParcelTask[];
  pendingParcelTasks: ParcelTask[];
  todayDistanceKm: number;
  todayPayout: number;
  codOutstanding: number;
  averageRating: number;
  reviewsCount: number;
  recentReviews: Array<{
    orderId?: string;
    rating: number;
    review?: string;
    createdAt: string;
  }>;
}

export interface PartnerPayoutSummary {
  periodType: string;
  periodStart: string;
  periodEnd: string;
  totalDistanceKm: number;
  tripDistanceKm: number;
  shiftDistanceKm: number;
  totalDeliveries: number;
  mileageKmpl: number;
  fuelPricePerLitre: number;
  litresConsumed: number;
  payoutAmount: number;
}

export interface AssignableUser {
  id: string;
  username: string;
  email: string;
  role: string;
  firstName?: string;
  lastName?: string;
  phoneNumber?: string;
  isActive: boolean;
}

@Injectable({ providedIn: 'root' })
export class DeliveryPartnerService {
  private http = inject(HttpClient);
  private apiUrl = environment.apiUrl;

  getDeliveryPartners(): Observable<DeliveryPartner[]> {
    return this.http.get<DeliveryPartner[]>(`${this.apiUrl}/manage/delivery-partners`).pipe(
      catchError(handleServiceError('DeliveryPartnerService.getDeliveryPartners'))
    );
  }

  getAssignableUsers(): Observable<AssignableUser[]> {
    return this.http.get<{ success: boolean; data: AssignableUser[] }>(`${this.apiUrl}/users`).pipe(
      map(response => response?.data || []),
      catchError(handleServiceError('DeliveryPartnerService.getAssignableUsers'))
    );
  }

  createDeliveryPartner(partner: Partial<DeliveryPartner>): Observable<DeliveryPartner> {
    return this.http.post<DeliveryPartner>(`${this.apiUrl}/manage/delivery-partners`, partner).pipe(
      catchError(handleServiceError('DeliveryPartnerService.createDeliveryPartner'))
    );
  }

  assignDeliveryPartner(request: AssignDeliveryRequest, channel?: 'web' | 'shop' | 'partner'): Observable<{ message: string }> {
    let params = new HttpParams();
    if (channel) {
      params = params.set('channel', channel);
    }

    return this.http.post<{ message: string }>(`${this.apiUrl}/manage/delivery-partners/assign`, request, { params }).pipe(
      catchError(handleServiceError('DeliveryPartnerService.assignDeliveryPartner'))
    );
  }

  completeDelivery(partnerId: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.apiUrl}/manage/delivery-partners/${partnerId}/complete`, {}).pipe(
      catchError(handleServiceError('DeliveryPartnerService.completeDelivery'))
    );
  }

  updatePartnerStatus(partnerId: string, status: string): Observable<{ message: string }> {
    return this.http.put<{ message: string }>(`${this.apiUrl}/manage/delivery-partners/${partnerId}/status`, { status }).pipe(
      catchError(handleServiceError('DeliveryPartnerService.updatePartnerStatus'))
    );
  }

  updatePartner(partnerId: string, partner: Partial<DeliveryPartner>): Observable<{ message: string }> {
    return this.http.put<{ message: string }>(`${this.apiUrl}/manage/delivery-partners/${partnerId}`, partner).pipe(
      catchError(handleServiceError('DeliveryPartnerService.updatePartner'))
    );
  }

  updatePartnerLocation(partnerId: string, latitude: number, longitude: number): Observable<{ message: string }> {
    return this.http.put<{ message: string }>(
      `${this.apiUrl}/manage/delivery-partners/${partnerId}/location`,
      { latitude, longitude }
    ).pipe(
      catchError(handleServiceError('DeliveryPartnerService.updatePartnerLocation'))
    );
  }

  deletePartner(partnerId: string): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`${this.apiUrl}/manage/delivery-partners/${partnerId}`).pipe(
      catchError(handleServiceError('DeliveryPartnerService.deletePartner'))
    );
  }

  getPartnerDashboard(partnerId?: string): Observable<PartnerDashboard> {
    let params = new HttpParams();
    if (partnerId) {
      params = params.set('partnerId', partnerId);
    }
    return this.http.get<PartnerDashboard>(`${this.apiUrl}/partner/delivery/dashboard`, { params }).pipe(
      catchError(handleServiceError('DeliveryPartnerService.getPartnerDashboard'))
    );
  }

  startShift(partnerId: string, payload: { startOdometerKm: number; startLatitude?: number; startLongitude?: number; notes?: string }): Observable<DeliveryShift> {
    return this.http.post<DeliveryShift>(`${this.apiUrl}/manage/delivery-partners/${partnerId}/shift/start`, payload).pipe(
      catchError(handleServiceError('DeliveryPartnerService.startShift'))
    );
  }

  endShift(partnerId: string, shiftId: string, payload: { endOdometerKm: number; endLatitude?: number; endLongitude?: number; notes?: string }): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.apiUrl}/manage/delivery-partners/${partnerId}/shift/${shiftId}/end`, payload).pipe(
      catchError(handleServiceError('DeliveryPartnerService.endShift'))
    );
  }

  createTrip(partnerId: string, payload: {
    shiftId: string;
    tripType: string;
    orderId?: string;
    startOdometerKm: number;
    endOdometerKm: number;
    startPointLabel?: string;
    endPointLabel?: string;
    startLatitude?: number;
    startLongitude?: number;
    endLatitude?: number;
    endLongitude?: number;
    notes?: string;
  }): Observable<PartnerTrip> {
    return this.http.post<PartnerTrip>(`${this.apiUrl}/manage/delivery-partners/${partnerId}/trips`, payload).pipe(
      catchError(handleServiceError('DeliveryPartnerService.createTrip'))
    );
  }

  createParcelTask(payload: {
    partnerId: string;
    startPoint: string;
    endPoint: string;
    isRoundTrip?: boolean;
    notes?: string;
  }): Observable<ParcelTask> {
    return this.http.post<ParcelTask>(`${this.apiUrl}/manage/delivery-partners/parcel-tasks`, payload).pipe(
      catchError(handleServiceError('DeliveryPartnerService.createParcelTask'))
    );
  }

  getParcelRouteQuote(payload: {
    startPoint: string;
    endPoint: string;
    isRoundTrip?: boolean;
  }): Observable<ParcelRouteQuote> {
    return this.http.post<ParcelRouteQuote>(`${this.apiUrl}/manage/delivery-partners/parcel-tasks/route-quote`, payload).pipe(
      catchError(handleServiceError('DeliveryPartnerService.getParcelRouteQuote'))
    );
  }

  upsertFuelPrice(payload: { date: string; petrolPricePerLitre: number }): Observable<{ id?: string; date: string; petrolPricePerLitre: number }> {
    return this.http.put<{ id?: string; date: string; petrolPricePerLitre: number }>(`${this.apiUrl}/manage/delivery-partners/fuel-price`, payload).pipe(
      catchError(handleServiceError('DeliveryPartnerService.upsertFuelPrice'))
    );
  }

  confirmCodCollection(partnerId: string, payload: { orderId: string; amount: number; collectionReference?: string; notes?: string }): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.apiUrl}/manage/delivery-partners/${partnerId}/cod/confirm`, payload).pipe(
      catchError(handleServiceError('DeliveryPartnerService.confirmCodCollection'))
    );
  }

  confirmMyCodCollection(orderId: string, payload: { amount: number; collectionReference?: string; notes?: string }): Observable<{ message: string; paymentStatus: string }> {
    return this.http.post<{ message: string; paymentStatus: string }>(
      `${this.apiUrl}/partner/delivery/orders/${orderId}/cod/confirm`,
      { orderId, ...payload }
    ).pipe(
      catchError(handleServiceError('DeliveryPartnerService.confirmMyCodCollection'))
    );
  }

  pickupAssignedOrder(orderId: string): Observable<{ message: string; status: string }> {
    return this.http.post<{ message: string; status: string }>(
      `${this.apiUrl}/partner/delivery/orders/${orderId}/pickup`,
      {}
    ).pipe(
      catchError(handleServiceError('DeliveryPartnerService.pickupAssignedOrder'))
    );
  }

  markOrderDelivered(orderId: string): Observable<{ message: string; status: string }> {
    return this.http.post<{ message: string; status: string }>(
      `${this.apiUrl}/partner/delivery/orders/${orderId}/delivered`,
      {}
    ).pipe(
      catchError(handleServiceError('DeliveryPartnerService.markOrderDelivered'))
    );
  }

  acceptDeliveryOrder(orderId: string): Observable<{ message: string; assigned: boolean; orderId: string; partnerId: string }> {
    return this.http.post<{ message: string; assigned: boolean; orderId: string; partnerId: string }>(
      `${this.apiUrl}/partner/delivery/orders/${orderId}/accept`,
      {}
    ).pipe(
      catchError(handleServiceError('DeliveryPartnerService.acceptDeliveryOrder'))
    );
  }

  addPartnerReview(payload: { orderId: string; partnerId: string; rating: number; review?: string }): Observable<{ id?: string }> {
    return this.http.post<{ id?: string }>(`${this.apiUrl}/delivery-partners/reviews`, payload).pipe(
      catchError(handleServiceError('DeliveryPartnerService.addPartnerReview'))
    );
  }

  getPartnerPayoutSummary(partnerId: string, periodType: 'day' | 'week' | 'month' | 'year', date?: string): Observable<PartnerPayoutSummary> {
    let params = new HttpParams().set('periodType', periodType);
    if (date) {
      params = params.set('date', date);
    }
    return this.http.get<PartnerPayoutSummary>(`${this.apiUrl}/manage/delivery-partners/${partnerId}/payout`, { params }).pipe(
      catchError(handleServiceError('DeliveryPartnerService.getPartnerPayoutSummary'))
    );
  }

  startMyShift(payload: { startOdometerKm: number; startLatitude?: number; startLongitude?: number; notes?: string }): Observable<DeliveryShift> {
    return this.http.post<DeliveryShift>(`${this.apiUrl}/partner/delivery/shift/start`, payload).pipe(
      catchError(handleServiceError('DeliveryPartnerService.startMyShift'))
    );
  }

  endMyShift(shiftId: string, payload: { endOdometerKm: number; endLatitude?: number; endLongitude?: number; notes?: string }): Observable<{ message: string; shift?: DeliveryShift }> {
    return this.http.post<{ message: string; shift?: DeliveryShift }>(`${this.apiUrl}/partner/delivery/shift/${shiftId}/end`, payload).pipe(
      catchError(handleServiceError('DeliveryPartnerService.endMyShift'))
    );
  }

  createMyTrip(payload: {
    shiftId: string;
    tripType: string;
    orderId?: string;
    startOdometerKm: number;
    endOdometerKm: number;
    startPointLabel?: string;
    endPointLabel?: string;
    startLatitude?: number;
    startLongitude?: number;
    endLatitude?: number;
    endLongitude?: number;
    notes?: string;
  }): Observable<PartnerTrip> {
    return this.http.post<PartnerTrip>(`${this.apiUrl}/partner/delivery/trips`, payload).pipe(
      catchError(handleServiceError('DeliveryPartnerService.createMyTrip'))
    );
  }

  acceptParcelTask(taskId: string): Observable<{ message: string; status: string }> {
    return this.http.post<{ message: string; status: string }>(
      `${this.apiUrl}/partner/delivery/parcel-tasks/${taskId}/accept`,
      {}
    ).pipe(
      catchError(handleServiceError('DeliveryPartnerService.acceptParcelTask'))
    );
  }

  completeParcelTask(taskId: string): Observable<{ message: string; status: string; tripDistanceKm: number }> {
    return this.http.post<{ message: string; status: string; tripDistanceKm: number }>(
      `${this.apiUrl}/partner/delivery/parcel-tasks/${taskId}/complete`,
      {}
    ).pipe(
      catchError(handleServiceError('DeliveryPartnerService.completeParcelTask'))
    );
  }

  getMyPayoutSummary(periodType: 'day' | 'week' | 'month' | 'year', date?: string): Observable<PartnerPayoutSummary> {
    let params = new HttpParams().set('periodType', periodType);
    if (date) {
      params = params.set('date', date);
    }
    return this.http.get<PartnerPayoutSummary>(`${this.apiUrl}/partner/delivery/payout`, { params }).pipe(
      catchError(handleServiceError('DeliveryPartnerService.getMyPayoutSummary'))
    );
  }

  setMyDailyFuelPrice(payload: { date: string; petrolPricePerLitre: number }): Observable<{ id?: string; date: string; petrolPricePerLitre: number }> {
    return this.http.put<{ id?: string; date: string; petrolPricePerLitre: number }>(`${this.apiUrl}/partner/delivery/fuel-price`, payload).pipe(
      catchError(handleServiceError('DeliveryPartnerService.setMyDailyFuelPrice'))
    );
  }
}
