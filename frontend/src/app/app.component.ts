import { Component, inject } from '@angular/core';
import { RouterOutlet, Router, NavigationEnd } from '@angular/router';
import { NavbarComponent } from './components/navbar/navbar.component';
import { CommonModule } from '@angular/common';
import { filter } from 'rxjs/operators';
import { AnalyticsTrackingService } from './services/analytics-tracking.service';
import { ToastContainerComponent } from './shared/toast-container/toast-container.component';
import { NetworkStatusService } from './services/network-status.service';
import { OfflineQueueService } from './services/offline-queue.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, NavbarComponent, CommonModule, ToastContainerComponent],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss'
})
export class AppComponent {
  title = 'Dashboard';
  showNavbar = true;
  private analyticsTracking = inject(AnalyticsTrackingService);
  networkStatus = inject(NetworkStatusService);
  offlineQueue = inject(OfflineQueueService);
  private readonly lastRouteStorageKey = 'lastAppRouteBeforeTranslate';

  constructor(private router: Router) {
    // Hide navbar on admin routes & track page views
    this.router.events.pipe(
      filter(event => event instanceof NavigationEnd)
    ).subscribe((event: any) => {
      const currentUrl = (event.urlAfterRedirects || event.url || '').split('?')[0] || '/';
      this.showNavbar = !currentUrl.startsWith('/admin');

      if (currentUrl !== '/' && currentUrl !== '') {
        sessionStorage.setItem(this.lastRouteStorageKey, currentUrl);
      }

      // Google Translate can rewrite URL to /#googtrans(...), which maps to home in SPA routing.
      if ((currentUrl === '/' || currentUrl === '') && window.location.hash.startsWith('#googtrans')) {
        const restoreUrl = sessionStorage.getItem(this.lastRouteStorageKey);
        if (restoreUrl && restoreUrl !== '/') {
          this.router.navigateByUrl(restoreUrl, { replaceUrl: true });
          return;
        }
      }

      // Track page view for analytics
      const pageName = this.getPageName(currentUrl);
      this.analyticsTracking.trackPageView(pageName);
    });
  }

  private getPageName(url: string): string {
    const cleaned = url.split('?')[0].split('#')[0];
    if (cleaned === '/' || cleaned === '') return 'Home';
    return cleaned.substring(1).split('/').map(
      s => s.charAt(0).toUpperCase() + s.slice(1)
    ).join(' > ');
  }
}
