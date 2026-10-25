import { inject } from '@angular/core';
import { Router, CanActivateFn, CanDeactivateFn } from '@angular/router';
import type { PhoneOrdersComponent } from '../components/phone-orders/phone-orders.component';
import { AuthStore } from '../store/auth.store';
import { CASHIER_ROLES, KITCHEN_ROLES, PARTNER_ROLES, WAITER_ROLES } from '../utils/roles';

export const authGuard: CanActivateFn = (route, state) => {
  const authStore = inject(AuthStore);
  const router = inject(Router);

  if (authStore.isLoggedIn()) {
    return true;
  }

  router.navigate(['/login'], { queryParams: { returnUrl: state.url } });
  return false;
};

/** Builds a guard that allows the user if they hold any of the given roles. */
export function roleGuard(allowedRoles: string[]): CanActivateFn {
  return (route, state) => {
    const authStore = inject(AuthStore);
    const router = inject(Router);

    if (!authStore.isLoggedIn()) {
      router.navigate(['/login'], { queryParams: { returnUrl: state.url } });
      return false;
    }

    if (authStore.hasAnyRole(allowedRoles)) {
      return true;
    }

    router.navigate(['/home']);
    return false;
  };
}

export const adminGuard: CanActivateFn = roleGuard(['admin']);

export const kitchenGuard: CanActivateFn = roleGuard(KITCHEN_ROLES);

export const partnerGuard: CanActivateFn = roleGuard(PARTNER_ROLES);

export const managerGuard: CanActivateFn = roleGuard(['manager']);

export const waiterGuard: CanActivateFn = roleGuard(WAITER_ROLES);

export const cashierGuard: CanActivateFn = roleGuard(CASHIER_ROLES);

export const phoneOrderDraftGuard: CanDeactivateFn<PhoneOrdersComponent> = component =>
  !component.hasDraft || window.confirm('Leave this page and discard the unfinished phone order?');
