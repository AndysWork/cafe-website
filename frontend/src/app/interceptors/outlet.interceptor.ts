import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { OutletService } from '../services/outlet.service';

/**
 * HTTP Interceptor that adds X-Outlet-Id header to all API requests
 * This allows the backend to filter data by outlet
 * If X-Outlet-Id is already present (even if empty), it won't be overridden
 */
export const outletInterceptor: HttpInterceptorFn = (req, next) => {
  const outletService = inject(OutletService);

  // If X-Outlet-Id header is already set (even to empty string), don't override it
  // Empty string is used to request all outlets data
  if (req.headers.has('X-Outlet-Id')) {
    const existingOutletId = req.headers.get('X-Outlet-Id');
    console.log(`[OutletInterceptor] ${req.method} ${req.url} - X-Outlet-Id already set: "${existingOutletId}" ${existingOutletId === '' ? '(ALL OUTLETS)' : ''}`);
    return next(req);
  }

  // Get the currently selected outlet ID
  const outletId = outletService.getSelectedOutletId();

  // If outlet is selected, add it to the request headers
  if (outletId) {
    const modifiedReq = req.clone({
      headers: req.headers.set('X-Outlet-Id', outletId)
    });

    // Debug logging - remove after testing
    if (req.url.includes('/api/')) {
      console.log(`[OutletInterceptor] ${req.method} ${req.url} - Outlet ID: ${outletId}`);
    }

    return next(modifiedReq);
  }

  // Debug logging for requests without outlet context
  if (req.url.includes('/api/') && !req.url.includes('/auth/') && !req.url.includes('/outlets/') && !req.url.includes('/public/') && !req.url.includes('/reviews/') && !req.url.includes('/analytics/')) {
    console.warn(`[OutletInterceptor] ${req.method} ${req.url} - NO OUTLET ID! This may cause data inconsistency.`);
  }

  // No outlet selected, proceed with original request
  return next(req);
};
