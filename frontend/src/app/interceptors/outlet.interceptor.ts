import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { OutletService } from '../services/outlet.service';

/**
 * HTTP Interceptor that adds X-Outlet-Id header to all API requests
 * This allows the backend to filter data by outlet
 */
export const outletInterceptor: HttpInterceptorFn = (req, next) => {
  const outletService = inject(OutletService);

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
  if (req.url.includes('/api/') && !req.url.includes('/auth/') && !req.url.includes('/outlets/')) {
    console.warn(`[OutletInterceptor] ${req.method} ${req.url} - NO OUTLET ID! This may cause data inconsistency.`);
  }

  // No outlet selected, proceed with original request
  return next(req);
};
