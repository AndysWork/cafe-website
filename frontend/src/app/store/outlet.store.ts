import { Injectable, computed, signal } from '@angular/core';
import { toObservable } from '@angular/core/rxjs-interop';
import { Outlet } from '../models/outlet.model';

/**
 * Centralized Outlet State Store using Angular Signals.
 * Single source of truth for outlet selection and available outlets.
 */
@Injectable({ providedIn: 'root' })
export class OutletStore {
  // ── Private writable signals ──
  private readonly _selectedOutlet = signal<Outlet | null>(null);
  private readonly _availableOutlets = signal<Outlet[]>([]);

  // ── Public readonly signals ──
  readonly selectedOutlet = this._selectedOutlet.asReadonly();
  readonly availableOutlets = this._availableOutlets.asReadonly();

  // ── Computed signals (derived state) ──
  readonly selectedOutletId = computed(() => {
    const outlet = this._selectedOutlet();
    return outlet?._id || outlet?.id || null;
  });
  readonly selectedOutletName = computed(() => this._selectedOutlet()?.outletName ?? '');
  readonly hasOutletSelected = computed(() => this._selectedOutlet() !== null);
  readonly activeOutlets = computed(() => this._availableOutlets().filter(o => o.isActive));
  readonly outletCount = computed(() => this._availableOutlets().length);

  // ── Observable bridges (backward compatibility) ──
  readonly selectedOutlet$ = toObservable(this._selectedOutlet);
  readonly availableOutlets$ = toObservable(this._availableOutlets);

  // ── Constructor: hydrate from localStorage ──
  constructor() {
    this.hydrate();
  }

  // ── Actions ──

  selectOutlet(outlet: Outlet): void {
    const outletId = outlet._id || outlet.id || '';
    this._selectedOutlet.set(outlet);
    localStorage.setItem('selectedOutletId', outletId);
    localStorage.setItem('selectedOutlet', JSON.stringify(outlet));
  }

  clearSelectedOutlet(): void {
    this._selectedOutlet.set(null);
    localStorage.removeItem('selectedOutletId');
    localStorage.removeItem('selectedOutlet');
  }

  setAvailableOutlets(outlets: Outlet[]): void {
    this._availableOutlets.set(outlets);
  }

  /**
   * Auto-select first outlet if none is currently selected.
   * Called after fetching active outlets.
   */
  autoSelectIfEmpty(outlets: Outlet[]): void {
    this._availableOutlets.set(outlets);
    if (!this._selectedOutlet() && outlets.length > 0) {
      this.selectOutlet(outlets[0]);
    }
  }

  // ── Private ──

  private hydrate(): void {
    const storedOutletId = localStorage.getItem('selectedOutletId');
    const storedOutlet = localStorage.getItem('selectedOutlet');

    if (storedOutletId && storedOutlet) {
      try {
        this._selectedOutlet.set(JSON.parse(storedOutlet));
      } catch {
        localStorage.removeItem('selectedOutletId');
        localStorage.removeItem('selectedOutlet');
      }
    }
  }
}
