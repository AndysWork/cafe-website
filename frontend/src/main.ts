import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { AppComponent } from './app/app.component';

const LAST_ROUTE_STORAGE_KEY = 'lastAppRouteBeforeTranslate';

try {
  const currentHash = window.location.hash || '';
  const savedRoute = sessionStorage.getItem(LAST_ROUTE_STORAGE_KEY);
  const hasGoogleTranslateHash = currentHash.startsWith('#googtrans');

  if (hasGoogleTranslateHash && window.location.pathname === '/' && savedRoute && savedRoute !== '/') {
    const normalizedRoute = savedRoute.startsWith('/') ? savedRoute : `/${savedRoute}`;
    window.history.replaceState(window.history.state, '', `${normalizedRoute}${currentHash}`);
  }
} catch {
  // Non-blocking: app should continue even if storage/history access fails.
}

bootstrapApplication(AppComponent, appConfig)
  .catch((err) => console.error(err));
