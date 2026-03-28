import { Injectable, inject } from '@angular/core';
import { AuthStore } from './auth.store';
import { CartStore } from './cart.store';
import { OutletStore } from './outlet.store';
import { UIStore } from './ui.store';
import { NotificationStore } from './notification.store';

/**
 * AppStore — Façade that aggregates all domain stores.
 * Provides a single injection point for components that need
 * cross-cutting state access.
 *
 * Usage:
 *   readonly store = inject(AppStore);
 *   // Access any sub-store:
 *   store.auth.isLoggedIn()
 *   store.cart.total()
 *   store.outlet.selectedOutletName()
 *   store.ui.isLoading()
 */
@Injectable({ providedIn: 'root' })
export class AppStore {
  readonly auth = inject(AuthStore);
  readonly cart = inject(CartStore);
  readonly outlet = inject(OutletStore);
  readonly ui = inject(UIStore);
  readonly notifications = inject(NotificationStore);
}
