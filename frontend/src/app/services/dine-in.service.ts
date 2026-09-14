import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, of, tap, catchError } from 'rxjs';
import { environment } from '../../environments/environment';
import { DineInBill, StartDineInSessionRequest, SettleDineInBillRequest } from '../models/dine-in.model';
import { OutletService } from './outlet.service';
import { AuthService } from './auth.service';

@Injectable({
  providedIn: 'root'
})
export class DineInService {
  private http = inject(HttpClient);
  private outletService = inject(OutletService);
  private authService = inject(AuthService);
  private apiUrl = environment.apiUrl;

  private readonly TABLE_STORAGE_KEY = 'active_dinein_table';
  private readonly SESSION_STORAGE_KEY = 'active_dinein_session_id';

  private activeTableSubject = new BehaviorSubject<string>(this.loadStoredTable());
  public activeTable$ = this.activeTableSubject.asObservable();

  private activeBillSubject = new BehaviorSubject<DineInBill | null>(null);
  public activeBill$: Observable<DineInBill | null> = this.activeBillSubject.asObservable();

  private isBillModalOpenSubject = new BehaviorSubject<boolean>(false);
  public isBillModalOpen$ = this.isBillModalOpenSubject.asObservable();

  constructor() {
    const table = this.loadStoredTable();
    if (table) {
      this.refreshActiveSession();
    }
  }

  public get currentTable(): string {
    return this.activeTableSubject.value;
  }

  public get currentBill(): DineInBill | null {
    return this.activeBillSubject.value;
  }

  public setTableNumber(table: string): void {
    const trimmed = (table || '').trim();
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
  }

  public openBillModal(): void {
    this.isBillModalOpenSubject.next(true);
    this.refreshActiveSession();
  }

  public closeBillModal(): void {
    this.isBillModalOpenSubject.next(false);
  }

  public refreshActiveSession(): Observable<any> {
    const table = this.currentTable;
    if (!table) {
      this.activeBillSubject.next(null);
      return of(null);
    }

    const outletId = this.outletService.getSelectedOutletId() || '';
    const url = `${this.apiUrl}/dine-in/session/active?tableNumber=${encodeURIComponent(table)}${outletId ? `&outletId=${outletId}` : ''}`;

    return this.http.get<{ session: DineInBill | null }>(url).pipe(
      tap((res) => {
        const bill = res?.session || null;
        this.activeBillSubject.next(bill);
        if (bill?.sessionId) {
          localStorage.setItem(this.SESSION_STORAGE_KEY, bill.sessionId);
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
      return localStorage.getItem(this.TABLE_STORAGE_KEY) || '';
    }
    return '';
  }
}
