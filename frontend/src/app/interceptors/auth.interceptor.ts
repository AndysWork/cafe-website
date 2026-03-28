import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { AuthService } from '../services/auth.service';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);
  const token = authService.getToken();

  // Skip adding token for login and register requests
  if (req.url.includes('/auth/login') || req.url.includes('/auth/register')) {
    return next(req);
  }

  // Build headers: auth token + CSRF token for mutating requests
  const headers: Record<string, string> = {};

  if (token) {
    headers['Authorization'] = `Bearer ${token}`;
  }

  // Attach CSRF token for state-changing requests
  const mutatingMethods = ['POST', 'PUT', 'DELETE', 'PATCH'];
  if (mutatingMethods.includes(req.method.toUpperCase())) {
    const csrfToken = localStorage.getItem('csrfToken');
    if (csrfToken) {
      headers['X-CSRF-Token'] = csrfToken;
    }
  }

  if (Object.keys(headers).length > 0) {
    const clonedRequest = req.clone({ setHeaders: headers });
    return next(clonedRequest);
  }

  return next(req);
};
