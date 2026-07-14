import { inject } from '@angular/core';
import { Router, CanActivateFn } from '@angular/router';
import { AuthStore } from '../store/auth.store';

export const authGuard: CanActivateFn = (route, state) => {
  const authStore = inject(AuthStore);
  const router = inject(Router);

  if (authStore.isLoggedIn()) {
    return true;
  }

  router.navigate(['/login'], { queryParams: { returnUrl: state.url } });
  return false;
};

export const adminGuard: CanActivateFn = (route, state) => {
  const authStore = inject(AuthStore);
  const router = inject(Router);

  if (!authStore.isLoggedIn()) {
    router.navigate(['/login'], { queryParams: { returnUrl: state.url } });
    return false;
  }

  const role = authStore.userRole();
  if (role === 'admin') {
    return true;
  }

  router.navigate(['/home']);
  return false;
};

export const kitchenGuard: CanActivateFn = (route, state) => {
  const authStore = inject(AuthStore);
  const router = inject(Router);

  if (!authStore.isLoggedIn()) {
    router.navigate(['/login'], { queryParams: { returnUrl: state.url } });
    return false;
  }

  const role = authStore.userRole();
  const allowed = ['admin', 'manager', 'assistant-manager', 'cook', 'chef', 'sous-chef', 'kitchen', 'kitchen-staff', 'kitchen-helper'];
  if (allowed.includes(role)) {
    return true;
  }

  router.navigate(['/home']);
  return false;
};

export const partnerGuard: CanActivateFn = (route, state) => {
  const authStore = inject(AuthStore);
  const router = inject(Router);

  if (!authStore.isLoggedIn()) {
    router.navigate(['/login'], { queryParams: { returnUrl: state.url } });
    return false;
  }

  const role = authStore.userRole();
  const allowed = ['partner', 'delivery-partner'];
  if (allowed.includes(role)) {
    return true;
  }

  router.navigate(['/home']);
  return false;
};

export const managerGuard: CanActivateFn = (route, state) => {
  const authStore = inject(AuthStore);
  const router = inject(Router);

  if (!authStore.isLoggedIn()) {
    router.navigate(['/login'], { queryParams: { returnUrl: state.url } });
    return false;
  }

  const role = authStore.userRole();
  if (role === 'manager') {
    return true;
  }

  router.navigate(['/home']);
  return false;
};
