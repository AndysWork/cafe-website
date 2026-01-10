import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { AuthService } from '../../services/auth.service';
import { OutletService } from '../../services/outlet.service';

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

  constructor(
    private authService: AuthService,
    private outletService: OutletService,
    private router: Router
  ) {}

  onLogin(): void {
    this.errorMessage = '';
    this.isLoading = true;

    this.authService.login(this.username, this.password).subscribe({
      next: (response) => {
        // Initialize outlets after successful login
        const user = this.authService.getCurrentUser();

        // Load outlets for the user
        this.outletService.initializeOutlets(user?.assignedOutlets).subscribe({
          next: () => {
            this.isLoading = false;
            if (user?.role === 'admin') {
              this.router.navigate(['/admin/dashboard']);
            } else {
              this.router.navigate(['/menu']);
            }
          },
          error: (outletError) => {
            console.error('Error loading outlets:', outletError);
            // Continue anyway even if outlet loading fails
            this.isLoading = false;
            if (user?.role === 'admin') {
              this.router.navigate(['/admin/dashboard']);
            } else {
              this.router.navigate(['/menu']);
            }
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
}
