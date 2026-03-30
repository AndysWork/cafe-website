import { HttpInterceptorFn, HttpErrorResponse, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, retry, timer, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';
import { UIStore } from '../store/ui.store';
import { OfflineQueueService } from '../services/offline-queue.service';

const TRANSIENT_STATUS_CODES = [0, 502, 503, 504];
const IDEMPOTENT_METHODS = ['GET', 'HEAD', 'OPTIONS', 'PUT', 'DELETE'];
const MAX_RETRIES_IDEMPOTENT = 2;
const MAX_RETRIES_NON_IDEMPOTENT = 1;

function isTransientError(status: number): boolean {
  return TRANSIENT_STATUS_CODES.includes(status);
}

function getMaxRetries(method: string): number {
  return IDEMPOTENT_METHODS.includes(method.toUpperCase())
    ? MAX_RETRIES_IDEMPOTENT
    : MAX_RETRIES_NON_IDEMPOTENT;
}

function isCriticalMutation(req: HttpRequest<unknown>): boolean {
  const method = req.method.toUpperCase();
  if (method === 'GET' || method === 'HEAD' || method === 'OPTIONS') return false;
  const url = req.url.toLowerCase();
  return url.includes('/orders') ||
    url.includes('/attendance') ||
    url.includes('/clock') ||
    url.includes('/sales') && method === 'POST';
}

function getErrorMessage(error: HttpErrorResponse, url: string): string {
  const serverMessage = error.error?.error || error.error?.message;
  if (error.status === 0) {
    return 'Network error — please check your connection';
  } else if (error.status === 400) {
    return serverMessage || 'Invalid request';
  } else if (error.status === 403) {
    return 'You do not have permission for this action';
  } else if (error.status === 404) {
    return 'The requested resource was not found';
  } else if (error.status === 409) {
    return serverMessage || 'Conflict — resource already exists';
  } else if (error.status === 429) {
    return 'Too many requests — please slow down';
  } else if (error.status === 502 || error.status === 504) {
    return 'Server is temporarily unavailable — retrying...';
  } else if (error.status >= 500) {
    return 'Server error — please try again later';
  }
  return serverMessage || error.message || 'An unexpected error occurred';
}

export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const router = inject(Router);
  const authService = inject(AuthService);
  const uiStore = inject(UIStore);
  const offlineQueue = inject(OfflineQueueService);

  const maxRetries = getMaxRetries(req.method);

  return next(req).pipe(
    retry({
      count: maxRetries,
      delay: (error: HttpErrorResponse, retryCount: number) => {
        if (isTransientError(error.status)) {
          // Exponential backoff: 1s, 2s, 4s...
          const delayMs = Math.min(1000 * Math.pow(2, retryCount - 1), 8000);
          console.warn(`[Retry ${retryCount}/${maxRetries}] ${req.method} ${req.url} — status ${error.status}, retrying in ${delayMs}ms`);
          return timer(delayMs);
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

      // Queue critical mutations for offline retry when network is down
      if (error.status === 0 && isCriticalMutation(req)) {
        offlineQueue.enqueue(req);
        uiStore.error('You are offline. Your request has been queued and will be sent when you reconnect.');
        return throwError(() => Object.assign(error, { userMessage: 'Queued for offline retry', queued: true }));
      }

      const userMessage = getErrorMessage(error, req.url);
      console.error(`[${req.method}] ${req.url} — ${error.status}: ${userMessage}`);

      // Show toast notification for user-facing errors (skip analytics & background)
      if (!req.url.includes('/analytics/') && error.status !== 401) {
        uiStore.error(userMessage);
      }

      const enrichedError = Object.assign(error, { userMessage });
      return throwError(() => enrichedError);
    })
  );
};
