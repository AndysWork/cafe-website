import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, of, tap, catchError } from 'rxjs';
import { environment } from '../../environments/environment';
import { DineInBill, StartDineInSessionRequest, SettleDineInBillRequest } from '../models/dine-in.model';
import { OutletService } from './outlet.service';
import { AuthService } from './auth.service';
import { UIStore } from '../store/ui.store';

@Injectable({
  providedIn: 'root'
})
export class DineInService {
  private http = inject(HttpClient);
  private outletService = inject(OutletService);
  private authService = inject(AuthService);
  private uiStore = inject(UIStore);
  private apiUrl = environment.apiUrl;

  private readonly TABLE_STORAGE_KEY = 'active_dinein_table';
  private readonly SESSION_STORAGE_KEY = 'active_dinein_session_id';

  private activeTableSubject = new BehaviorSubject<string>(this.loadStoredTable());
  public activeTable$ = this.activeTableSubject.asObservable();

  private activeBillSubject = new BehaviorSubject<DineInBill | null>(null);
  public activeBill$: Observable<DineInBill | null> = this.activeBillSubject.asObservable();

  private displayedBillSubject = new BehaviorSubject<DineInBill | null>(null);
  public displayedBill$: Observable<DineInBill | null> = this.displayedBillSubject.asObservable();

  private isBillModalOpenSubject = new BehaviorSubject<boolean>(false);
  public isBillModalOpen$ = this.isBillModalOpenSubject.asObservable();

  private isBillLoadingSubject = new BehaviorSubject<boolean>(false);
  public isBillLoading$ = this.isBillLoadingSubject.asObservable();

  public isViewingSpecificSession = false;
  private viewingSessionId: string | null = null;
  public viewingTableNumber: string | null = null;

  constructor() {
    const table = this.loadStoredTable();
    if (table) {
      this.refreshActiveSession();
    }
  }

  public sanitizeTableNumber(table: string): string {
    let trimmed = (table || '').trim();
    if (trimmed.toLowerCase().startsWith('table')) {
      trimmed = trimmed.substring(5).trim();
    }
    return trimmed;
  }

  public get currentTable(): string {
    return this.activeTableSubject.value;
  }

  public get currentBill(): DineInBill | null {
    return this.activeBillSubject.value;
  }

  public setTableNumber(table: string): void {
    const trimmed = this.sanitizeTableNumber(table);
    if (trimmed) {
      localStorage.setItem(this.TABLE_STORAGE_KEY, trimmed);
      this.activeTableSubject.next(trimmed);
      this.refreshActiveSession();
    } else {
      this.clearTableSession();
    }
  }

  public clearTableSession(): void {
    localStorage.removeItem(this.TABLE_STORAGE_KEY);
    localStorage.removeItem(this.SESSION_STORAGE_KEY);
    this.activeTableSubject.next('');
    this.activeBillSubject.next(null);
    if (!this.isViewingSpecificSession) {
      this.displayedBillSubject.next(null);
    }
  }

  public openBillModal(): void {
    this.isViewingSpecificSession = false;
    this.viewingSessionId = null;
    this.viewingTableNumber = null;
    this.displayedBillSubject.next(this.activeBillSubject.value);
    this.isBillModalOpenSubject.next(true);
    this.refreshActiveSession();
  }

  public openBillModalWithSession(sessionId: string, tableNumber?: string): void {
    this.isViewingSpecificSession = true;
    this.viewingSessionId = sessionId;
    this.viewingTableNumber = tableNumber ? this.sanitizeTableNumber(tableNumber) : null;
    this.isBillLoadingSubject.next(true);
    this.isBillModalOpenSubject.next(true);
    this.getSessionBill(sessionId).subscribe({
      next: (bill) => {
        this.isBillLoadingSubject.next(false);
        this.displayedBillSubject.next(bill);
      },
      error: (err) => {
        this.isBillLoadingSubject.next(false);
        console.warn('Could not load session bill', err);
        this.uiStore.error('Could not load bill details for this dining visit.');
      }
    });
  }

  public viewReservationBill(reservationId: string, tableNumber?: string, fallbackSessionId?: string): void {
    this.isViewingSpecificSession = true;
    this.viewingSessionId = fallbackSessionId || null;
    this.viewingTableNumber = tableNumber ? this.sanitizeTableNumber(tableNumber) : null;
    this.isBillLoadingSubject.next(true);
    this.isBillModalOpenSubject.next(true);

    this.http.get<DineInBill>(`${this.apiUrl}/dine-in/reservations/${reservationId}/bill`).subscribe({
      next: (bill) => {
        this.isBillLoadingSubject.next(false);
        this.viewingSessionId = bill?.sessionId || fallbackSessionId || null;
        this.displayedBillSubject.next(bill);
      },
      error: (err) => {
        console.warn('Could not load reservation bill by reservation ID, trying fallback', err);
        if (fallbackSessionId) {
          this.getSessionBill(fallbackSessionId).subscribe({
            next: (bill) => {
              this.isBillLoadingSubject.next(false);
              this.viewingSessionId = bill?.sessionId || fallbackSessionId;
              this.displayedBillSubject.next(bill);
            },
            error: (err2) => {
              this.isBillLoadingSubject.next(false);
              console.error('Could not load fallback session bill', err2);
              this.uiStore.error('No bill details found for this visit.');
            }
          });
        } else {
          this.isBillLoadingSubject.next(false);
          this.uiStore.error(err?.error?.error || 'No bill details recorded for this reservation yet.');
        }
      }
    });
  }

  public closeBillModal(): void {
    this.isViewingSpecificSession = false;
    this.viewingSessionId = null;
    this.viewingTableNumber = null;
    this.displayedBillSubject.next(null);
    this.isBillLoadingSubject.next(false);
    this.isBillModalOpenSubject.next(false);
  }

  public refreshActiveSession(): Observable<any> {
    if (this.isViewingSpecificSession && this.viewingSessionId) {
      return this.getSessionBill(this.viewingSessionId).pipe(
        tap((bill) => {
          this.displayedBillSubject.next(bill);
        })
      );
    }

    const table = this.currentTable;
    if (!table) {
      this.activeBillSubject.next(null);
      if (!this.isViewingSpecificSession) {
        this.displayedBillSubject.next(null);
      }
      return of(null);
    }

    const outletId = this.outletService.getSelectedOutletId() || '';
    const url = `${this.apiUrl}/dine-in/session/active?tableNumber=${encodeURIComponent(table)}${outletId ? `&outletId=${outletId}` : ''}`;

    return this.http.get<{ session: DineInBill | null; isTableSettled?: boolean }>(url).pipe(
      tap((res) => {
        const bill = res?.session || null;
        this.activeBillSubject.next(bill);
        if (!this.isViewingSpecificSession) {
          this.displayedBillSubject.next(bill);
        }
        if (bill?.sessionId) {
          localStorage.setItem(this.SESSION_STORAGE_KEY, bill.sessionId);
        } else if (res?.isTableSettled || !bill) {
          // Table was already paid/completed, clear it so it doesn't linger!
          this.clearTableSession();
        }
      }),
      catchError((err) => {
        console.warn('Could not fetch active dine-in session', err);
        return of(null);
      })
    );
  }

  public startSession(request: StartDineInSessionRequest): Observable<any> {
    return this.http.post<{ message: string; session: DineInBill }>(`${this.apiUrl}/dine-in/session/start`, request).pipe(
      tap((res) => {
        if (res?.session) {
          this.setTableNumber(res.session.tableNumber);
          this.activeBillSubject.next(res.session);
        }
      })
    );
  }

  public placeDineInOrder(payload: {
    items: Array<{
      menuItemId: string;
      quantity: number;
      selectedVariantName?: string;
      selectedAddOnNames?: string[];
    }>;
    tableNumber?: string;
    notes?: string;
    preparationNotes?: string;
    phoneNumber?: string;
  }): Observable<any> {
    const table = payload.tableNumber || this.currentTable;
    const outletId = this.outletService.getSelectedOutletId() || undefined;

    const requestBody = {
      items: payload.items,
      tableNumber: table,
      outletId,
      orderType: 'dine-in',
      paymentMethod: 'dine_in_tab',
      channel: 'shop',
      notes: payload.notes,
      preparationNotes: payload.preparationNotes,
      phoneNumber: payload.phoneNumber
    };

    return this.http.post<any>(`${this.apiUrl}/dine-in/order`, requestBody).pipe(
      tap((res) => {
        if (res?.session) {
          this.activeBillSubject.next(res.session);
          if (table) {
            localStorage.setItem(this.TABLE_STORAGE_KEY, table);
            this.activeTableSubject.next(table);
          }
        }
      })
    );
  }

  public getSessionBill(sessionId: string): Observable<DineInBill> {
    return this.http.get<DineInBill>(`${this.apiUrl}/dine-in/session/${sessionId}/bill`).pipe(
      tap((bill) => this.activeBillSubject.next(bill))
    );
  }

  public requestFinalBill(sessionId: string): Observable<any> {
    return this.http.post<{ message: string; session: DineInBill }>(
      `${this.apiUrl}/dine-in/session/${sessionId}/request-bill`,
      {}
    ).pipe(
      tap((res) => {
        if (res?.session) {
          this.activeBillSubject.next(res.session);
        }
      })
    );
  }

  public applyCoupon(sessionId: string, couponCode: string): Observable<any> {
    return this.http.post<{ message: string; session: DineInBill }>(
      `${this.apiUrl}/dine-in/session/${sessionId}/apply-coupon`,
      { couponCode }
    ).pipe(
      tap((res) => {
        if (res?.session) {
          this.activeBillSubject.next(res.session);
        }
      })
    );
  }

  public removeCoupon(sessionId: string): Observable<any> {
    return this.http.post<{ message: string; session: DineInBill }>(
      `${this.apiUrl}/dine-in/session/${sessionId}/remove-coupon`,
      {}
    ).pipe(
      tap((res) => {
        if (res?.session) {
          this.activeBillSubject.next(res.session);
        }
      })
    );
  }

  public settleBill(sessionId: string, settleReq: SettleDineInBillRequest): Observable<any> {
    return this.http.post<any>(
      `${this.apiUrl}/dine-in/session/${sessionId}/settle`,
      settleReq
    ).pipe(
      tap((res) => {
        if (res?.session) {
          this.activeBillSubject.next(res.session);
        }
      })
    );
  }

  private loadStoredTable(): string {
    if (typeof localStorage !== 'undefined') {
      const raw = localStorage.getItem(this.TABLE_STORAGE_KEY) || '';
      return this.sanitizeTableNumber(raw);
    }
    return '';
  }
}
