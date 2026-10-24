import { ApplicationConfig, provideZoneChangeDetection, isDevMode } from '@angular/core';
import { DATE_PIPE_DEFAULT_OPTIONS } from '@angular/common';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideServiceWorker } from '@angular/service-worker';
import { authInterceptor } from './interceptors/auth.interceptor';
import { outletInterceptor } from './interceptors/outlet.interceptor';
import { analyticsInterceptor } from './interceptors/analytics.interceptor';
import { errorInterceptor } from './interceptors/error.interceptor';

import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    {
      provide: DATE_PIPE_DEFAULT_OPTIONS,
      useValue: { timezone: 'Asia/Kolkata' }
    },
    provideRouter(routes),
    provideHttpClient(withInterceptors([errorInterceptor, authInterceptor, outletInterceptor, analyticsInterceptor])),
    // Refresh existing workers too: their fetches retain the CSP from installation.
    provideServiceWorker('ngsw-worker.js?policy=blob-images-v1', {
      enabled: !isDevMode(),
      registrationStrategy: 'registerWhenStable:30000'
    })
  ]
};
