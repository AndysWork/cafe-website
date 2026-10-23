import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { AuthService } from '../../services/auth.service';
import { OutletService } from '../../services/outlet.service';
import { AnalyticsTrackingService } from '../../services/analytics-tracking.service';
import { AuthStore } from '../../store/auth.store';
import { hasAnyRole } from '../../utils/roles';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './login.component.html',
  styleUrls: ['./login.component.scss']
})
export class LoginComponent {
  username = '';
  password = '';
  errorMessage = '';
  isLoading = false;
  showPassword = false;

  constructor(
    private authService: AuthService,
    private outletService: OutletService,
    private router: Router,
    private route: ActivatedRoute
  ) {}
  private analyticsTracking = inject(AnalyticsTrackingService);
  private authStore = inject(AuthStore);

  togglePassword(): void {
    this.showPassword = !this.showPassword;
  }

  onLogin(): void {
    this.errorMessage = '';
    this.isLoading = true;

    this.authService.login(this.username, this.password).subscribe({
      next: (response) => {
        this.analyticsTracking.trackLogin();
        // Initialize outlets after successful login
        const user = this.authService.getCurrentUser();

        // Load outlets for the user
        this.outletService.initializeOutlets(user?.assignedOutlets).subscribe({
          next: () => {
            this.isLoading = false;
            this.navigateAfterLogin(this.authStore.roles());
          },
          error: (outletError) => {
            console.error('Error loading outlets:', outletError);
            // Continue anyway even if outlet loading fails
            this.isLoading = false;
            this.navigateAfterLogin(this.authStore.roles());
          }
        });
      },
      error: (error) => {
        this.isLoading = false;
        this.errorMessage = error.error?.error || 'Invalid username or password';
        setTimeout(() => {
          this.errorMessage = '';
        }, 3000);
      }
    });
  }

  /** Lands the user on the screen for their highest-priority role. */
  private navigateAfterLogin(roles: string[]): void {
    const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');
    if (returnUrl && returnUrl.startsWith('/')) {
      this.router.navigateByUrl(returnUrl);
      return;
    }

    const has = (...r: string[]) => hasAnyRole(roles, r);

    if (has('admin')) {
      this.router.navigate(['/admin/dashboard']);
      return;
    }

    if (has('manager')) {
      this.router.navigate(['/manager/dashboard']);
      return;
    }

    if (has('cook', 'chef', 'sous-chef', 'kitchen', 'kitchen-staff', 'kitchen-helper')) {
      this.router.navigate(['/kitchen/dashboard']);
      return;
    }

    if (has('waiter', 'cashier')) {
      // Dedicated waiter/cashier screens are added in later phases.
      this.router.navigate(['/staff/attendance']);
      return;
    }

    if (has('partner', 'delivery-partner')) {
      this.router.navigate(['/partner/delivery']);
      return;
    }

    this.router.navigate(['/menu']);
  }
}
