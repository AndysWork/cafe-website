import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, tap, catchError, of } from 'rxjs';
import { environment } from '../../environments/environment';
import { Outlet } from '../models/outlet.model';

@Injectable({
  providedIn: 'root'
})
export class OutletService {
  private http = inject(HttpClient);
  private apiUrl = environment.apiUrl;

  // Currently selected outlet
  private selectedOutletSubject = new BehaviorSubject<Outlet | null>(null);
  public selectedOutlet$ = this.selectedOutletSubject.asObservable();

  // All available outlets for current user
  private availableOutletsSubject = new BehaviorSubject<Outlet[]>([]);
  public availableOutlets$ = this.availableOutletsSubject.asObservable();

  constructor() {
    // Load selected outlet from localStorage on init
    this.loadSelectedOutlet();
  }

  /**
   * Load selected outlet from localStorage
   */
  private loadSelectedOutlet(): void {
    const storedOutletId = localStorage.getItem('selectedOutletId');
    const storedOutlet = localStorage.getItem('selectedOutlet');

    if (storedOutletId && storedOutlet) {
      try {
        const outlet = JSON.parse(storedOutlet);
        this.selectedOutletSubject.next(outlet);
      } catch (e) {
        console.error('Failed to parse stored outlet:', e);
        localStorage.removeItem('selectedOutletId');
        localStorage.removeItem('selectedOutlet');
      }
    }
  }

  /**
   * Get all outlets (admin only)
   */
  getAllOutlets(): Observable<Outlet[]> {
    return this.http.get<Outlet[]>(`${this.apiUrl}/outlets`).pipe(
      tap(outlets => {
        this.availableOutletsSubject.next(outlets);
      }),
      catchError(error => {
        console.error('Error fetching outlets:', error);
        return of([]);
      })
    );
  }

  /**
   * Get active outlets
   */
  getActiveOutlets(): Observable<Outlet[]> {
    return this.http.get<Outlet[]>(`${this.apiUrl}/outlets/active`).pipe(
      tap(outlets => {
        this.availableOutletsSubject.next(outlets);

        // If no outlet is currently selected and there are active outlets, select the first one
        if (!this.selectedOutletSubject.value && outlets.length > 0) {
          this.selectOutlet(outlets[0]);
        }
      }),
      catchError(error => {
        console.error('Error fetching active outlets:', error);
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
    const outletId = outlet._id || outlet.id || '';
    console.log(`[OutletService] Selecting outlet: ${outlet.outletName} (ID: ${outletId})`);

    this.selectedOutletSubject.next(outlet);
    localStorage.setItem('selectedOutletId', outletId);
    localStorage.setItem('selectedOutlet', JSON.stringify(outlet));

    console.log(`[OutletService] Outlet selected. Components subscribed to selectedOutlet$ will reload data.`);
  }

  /**
   * Get currently selected outlet
   */
  getSelectedOutlet(): Outlet | null {
    return this.selectedOutletSubject.value;
  }

  /**
   * Get selected outlet ID for API requests
   */
  getSelectedOutletId(): string | null {
    const outlet = this.selectedOutletSubject.value;
    const outletId = outlet?._id || outlet?.id || null;
    return outletId;
  }

  /**
   * Clear selected outlet
   */
  clearSelectedOutlet(): void {
    this.selectedOutletSubject.next(null);
    localStorage.removeItem('selectedOutletId');
    localStorage.removeItem('selectedOutlet');
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
