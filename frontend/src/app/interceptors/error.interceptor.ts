import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, retry, timer, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';

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

      return throwError(() => error);
    })
  );
};
