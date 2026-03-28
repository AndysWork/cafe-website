import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, retry, timer, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';

function getErrorMessage(error: HttpErrorResponse, url: string): string {
  if (error.status === 0) {
    return 'Network error — please check your connection';
  } else if (error.status === 400) {
    return error.error?.message || 'Invalid request';
  } else if (error.status === 403) {
    return 'You do not have permission for this action';
  } else if (error.status === 404) {
    return 'The requested resource was not found';
  } else if (error.status === 409) {
    return error.error?.message || 'Conflict — resource already exists';
  } else if (error.status === 429) {
    return 'Too many requests — please slow down';
  } else if (error.status >= 500) {
    return 'Server error — please try again later';
  }
  return error.error?.message || error.message || 'An unexpected error occurred';
}

export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const router = inject(Router);
  const authService = inject(AuthService);

  return next(req).pipe(
    retry({
      count: 1,
      delay: (error: HttpErrorResponse) => {
        // Only retry on network errors (status 0) or 503 Service Unavailable
        if (error.status === 0 || error.status === 503) {
          return timer(1000);
        }
        return throwError(() => error);
      }
    }),
    catchError((error: HttpErrorResponse) => {
      if (error.status === 401) {
        // Skip auto-logout for login/register requests
        if (!req.url.includes('/auth/login') && !req.url.includes('/auth/register')) {
          authService.logout();
          router.navigate(['/login'], { queryParams: { returnUrl: router.url } });
        }
      }

      const userMessage = getErrorMessage(error, req.url);
      console.error(`[${req.method}] ${req.url} — ${error.status}: ${userMessage}`);

      const enrichedError = Object.assign(error, { userMessage });
      return throwError(() => enrichedError);
    })
  );
};
