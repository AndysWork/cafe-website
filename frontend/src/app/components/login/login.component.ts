import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { AuthService } from '../../services/auth.service';
import { OutletService } from '../../services/outlet.service';
import { AnalyticsTrackingService } from '../../services/analytics-tracking.service';

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
            this.navigateAfterLogin(user?.role);
          },
          error: (outletError) => {
            console.error('Error loading outlets:', outletError);
            // Continue anyway even if outlet loading fails
            this.isLoading = false;
            this.navigateAfterLogin(user?.role);
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

  private navigateAfterLogin(role?: 'admin' | 'manager' | 'assistant-manager' | 'partner' | 'delivery-partner' | 'cook' | 'chef' | 'sous-chef' | 'kitchen' | 'kitchen-staff' | 'kitchen-helper' | 'user'): void {
    const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');
    if (returnUrl && returnUrl.startsWith('/')) {
      this.router.navigateByUrl(returnUrl);
      return;
    }

    if (role === 'admin') {
      this.router.navigate(['/admin/dashboard']);
      return;
    }

    if (role === 'manager') {
      this.router.navigate(['/manager/dashboard']);
      return;
    }

    if (role === 'partner' || role === 'delivery-partner') {
      this.router.navigate(['/partner/delivery']);
      return;
    }

    if (role === 'cook' || role === 'chef' || role === 'sous-chef' || role === 'kitchen' || role === 'kitchen-staff' || role === 'kitchen-helper') {
      this.router.navigate(['/kitchen/dashboard']);
      return;
    }

    this.router.navigate(['/menu']);
  }
}
