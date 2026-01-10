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
    return next(modifiedReq);
  }

  // No outlet selected, proceed with original request
  return next(req);
};
