import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { tap } from 'rxjs';
import { AnalyticsTrackingService } from '../services/analytics-tracking.service';

/**
 * HTTP Interceptor that tracks API call response times for analytics.
 * Excludes analytics endpoints themselves to avoid infinite loops.
 */
export const analyticsInterceptor: HttpInterceptorFn = (req, next) => {
  // Skip tracking for analytics endpoints to avoid recursion
  if (req.url.includes('/analytics/')) {
    return next(req);
  }

  // Only track API calls
  if (!req.url.includes('/api/')) {
    return next(req);
  }

  const analyticsService = inject(AnalyticsTrackingService);
  const startTime = Date.now();

  // Extract a short endpoint name from the URL
  const urlObj = new URL(req.url, window.location.origin);
  const endpoint = urlObj.pathname.replace(/^.*\/api\//, '');

  return next(req).pipe(
    tap({
      next: (event: any) => {
        if (event.status !== undefined) {
          const responseTime = Date.now() - startTime;
          analyticsService.trackApiCall(endpoint, req.method, event.status, responseTime);
        }
      },
      error: (error: any) => {
        const responseTime = Date.now() - startTime;
        analyticsService.trackApiCall(endpoint, req.method, error.status || 0, responseTime);
      }
    })
  );
};
