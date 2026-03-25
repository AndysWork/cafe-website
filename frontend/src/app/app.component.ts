import { Component, inject } from '@angular/core';
import { RouterOutlet, Router, NavigationEnd } from '@angular/router';
import { NavbarComponent } from './components/navbar/navbar.component';
import { CommonModule } from '@angular/common';
import { filter } from 'rxjs/operators';
import { AnalyticsTrackingService } from './services/analytics-tracking.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, NavbarComponent, CommonModule],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss'
})
export class AppComponent {
  title = 'Dashboard';
  showNavbar = true;
  private analyticsTracking = inject(AnalyticsTrackingService);

  constructor(private router: Router) {
    // Hide navbar on admin routes & track page views
    this.router.events.pipe(
      filter(event => event instanceof NavigationEnd)
    ).subscribe((event: any) => {
      this.showNavbar = !event.url.startsWith('/admin');
      // Track page view for analytics
      const pageName = this.getPageName(event.url);
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
