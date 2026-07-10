import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap, catchError, of, shareReplay, map } from 'rxjs';
import { environment } from '../../environments/environment';
import { Outlet } from '../models/outlet.model';
import { OutletStore } from '../store/outlet.store';

@Injectable({
  providedIn: 'root'
})
export class OutletService {
    private normalizeOutlet(raw: any): Outlet {
      const settings = raw?.settings ?? raw?.Settings ?? {};

      return {
        _id: raw?._id,
        id: raw?.id ?? raw?.Id ?? raw?._id ?? '',
        outletCode: raw?.outletCode ?? raw?.OutletCode ?? '',
        outletName: raw?.outletName ?? raw?.OutletName ?? '',
        address: raw?.address ?? raw?.Address ?? '',
        city: raw?.city ?? raw?.City ?? '',
        state: raw?.state ?? raw?.State ?? '',
        phoneNumber: raw?.phoneNumber ?? raw?.PhoneNumber,
        email: raw?.email ?? raw?.Email,
        managerName: raw?.managerName ?? raw?.ManagerName,
        isActive: raw?.isActive ?? raw?.IsActive ?? true,
        settings: {
          openingTime: settings?.openingTime ?? settings?.OpeningTime ?? '',
          closingTime: settings?.closingTime ?? settings?.ClosingTime ?? '',
          acceptsOnlineOrders: settings?.acceptsOnlineOrders ?? settings?.AcceptsOnlineOrders ?? false,
          acceptsDineIn: settings?.acceptsDineIn ?? settings?.AcceptsDineIn ?? false,
          acceptsTakeaway: settings?.acceptsTakeaway ?? settings?.AcceptsTakeaway ?? false,
          taxPercentage: settings?.taxPercentage ?? settings?.TaxPercentage ?? 0,
          deliveryRadius: settings?.deliveryRadius ?? settings?.DeliveryRadius,
          minimumOrderAmount: settings?.minimumOrderAmount ?? settings?.MinimumOrderAmount
        },
        createdBy: raw?.createdBy ?? raw?.CreatedBy,
        createdDate: raw?.createdDate ?? raw?.CreatedDate,
        lastUpdatedBy: raw?.lastUpdatedBy ?? raw?.LastUpdatedBy,
        lastUpdated: raw?.lastUpdated ?? raw?.LastUpdated
      };
    }

    private normalizeOutletsResponse(response: any): Outlet[] {
      if (Array.isArray(response)) {
        return response.map(outlet => this.normalizeOutlet(outlet));
      }

      if (Array.isArray(response?.data)) {
        return response.data.map((outlet: any) => this.normalizeOutlet(outlet));
      }

      return [];
    }

  private http = inject(HttpClient);
  private apiUrl = environment.apiUrl;
  private outletStore = inject(OutletStore);

  /** Observable bridges — delegate to OutletStore signals via toObservable. */
  public selectedOutlet$ = this.outletStore.selectedOutlet$;
  public availableOutlets$ = this.outletStore.availableOutlets$;

  /**
   * Get all outlets (admin only)
   */
  getAllOutlets(): Observable<Outlet[]> {
    return this.http.get<any>(`${this.apiUrl}/outlets`).pipe(
      map(response => this.normalizeOutletsResponse(response)),
      tap(outlets => {
        this.outletStore.setAvailableOutlets(outlets);
      }),
      catchError(error => {
        console.error('Error fetching outlets:', error);
        return of([]);
      }),
      shareReplay({ bufferSize: 1, refCount: true })
    );
  }

  /**
   * Get active outlets
   */
  getActiveOutlets(): Observable<Outlet[]> {
    return this.http.get<any>(`${this.apiUrl}/outlets/active`).pipe(
      map(response => this.normalizeOutletsResponse(response)),
      tap(outlets => {
        this.outletStore.autoSelectIfEmpty(outlets);
      }),
      catchError(error => {
        console.error('Error fetching active outlets:', error);
        return of([]);
      }),
      shareReplay({ bufferSize: 1, refCount: true })
    );
  }

  /**
   * Get public outlet info for landing page (no auth required)
   */
  getPublicOutlets(): Observable<Outlet[]> {
    return this.http.get<any>(`${this.apiUrl}/public/outlets`).pipe(
      map(response => this.normalizeOutletsResponse(response)),
      catchError(error => {
        console.error('Error fetching public outlets:', error);
        return of([]);
      })
    );
  }

  /**
   * Get outlet by ID
   */
  getOutletById(id: string): Observable<Outlet> {
    return this.http.get<Outlet>(`${this.apiUrl}/outlets/${id}`);
  }

  /**
   * Get outlet by code
   */
  getOutletByCode(code: string): Observable<Outlet> {
    return this.http.get<Outlet>(`${this.apiUrl}/outlets/code/${code}`);
  }

  /**
   * Create new outlet (admin only)
   */
  createOutlet(outlet: Partial<Outlet>): Observable<Outlet> {
    return this.http.post<Outlet>(`${this.apiUrl}/outlets`, outlet);
  }

  /**
   * Update outlet (admin only)
   */
  updateOutlet(id: string, outlet: Partial<Outlet>): Observable<any> {
    return this.http.put(`${this.apiUrl}/outlets/${id}`, outlet);
  }

  /**
   * Delete outlet (admin only)
   */
  deleteOutlet(id: string): Observable<any> {
    return this.http.delete(`${this.apiUrl}/outlets/${id}`);
  }

  /**
   * Toggle outlet status (admin only)
   */
  toggleOutletStatus(id: string): Observable<any> {
    return this.http.post(`${this.apiUrl}/outlets/${id}/toggle-status`, {});
  }

  /**
   * Select an outlet for the current session
   */
  selectOutlet(outlet: Outlet): void {
    this.outletStore.selectOutlet(outlet);
  }

  /**
   * Get currently selected outlet
   */
  getSelectedOutlet(): Outlet | null {
    return this.outletStore.selectedOutlet();
  }

  /**
   * Get selected outlet ID for API requests
   */
  getSelectedOutletId(): string | null {
    return this.outletStore.selectedOutletId();
  }

  /**
   * Clear selected outlet
   */
  clearSelectedOutlet(): void {
    this.outletStore.clearSelectedOutlet();
  }

  /**
   * Initialize outlets for a user
   * Called after login to load available outlets
   */
  initializeOutlets(assignedOutletIds?: string[]): Observable<Outlet[]> {
    // If user has assigned outlets, fetch only those
    // Otherwise, fetch all active outlets
    return this.getActiveOutlets();
  }
}
