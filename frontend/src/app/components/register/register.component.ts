import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { AuthService } from '../../services/auth.service';

@Component({
  selector: 'app-register',
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './register.component.html',
  styleUrl: './register.component.scss'
})
export class RegisterComponent {
  username = '';
  email = '';
  password = '';
  confirmPassword = '';
  firstName = '';
  lastName = '';
  phoneNumber = '';
  gender = '';
  dateOfBirth = '';
  errorMessage = '';
  isLoading = false;
  showPassword = false;
  showConfirmPassword = false;
  passwordStrength = 0;
  passwordStrengthLabel = '';
  registrationSuccess = false;
  registeredEmail = '';

  constructor(
    private authService: AuthService,
    private router: Router
  ) {}

  togglePassword(): void {
    this.showPassword = !this.showPassword;
  }

  toggleConfirmPassword(): void {
    this.showConfirmPassword = !this.showConfirmPassword;
  }

  onPasswordChange(): void {
    this.calculatePasswordStrength();
  }

  private calculatePasswordStrength(): void {
    const pwd = this.password;
    let score = 0;
    if (pwd.length >= 6) score++;
    if (pwd.length >= 10) score++;
    if (/[A-Z]/.test(pwd)) score++;
    if (/[0-9]/.test(pwd)) score++;
    if (/[^A-Za-z0-9]/.test(pwd)) score++;

    this.passwordStrength = Math.min(score, 4);
    const labels = ['', 'Weak', 'Fair', 'Good', 'Strong'];
    this.passwordStrengthLabel = pwd.length > 0 ? labels[this.passwordStrength] || 'Weak' : '';
  }

  onSubmit() {
    this.errorMessage = '';

    // Validation
    if (!this.username || !this.email || !this.password || !this.confirmPassword || !this.phoneNumber) {
      this.errorMessage = 'Please fill in all required fields';
      return;
    }

    if (this.password !== this.confirmPassword) {
      this.errorMessage = 'Passwords do not match';
      return;
    }

    if (this.password.length < 6) {
      this.errorMessage = 'Password must be at least 6 characters long';
      return;
    }

    if (!this.isValidEmail(this.email)) {
      this.errorMessage = 'Please enter a valid email address';
      return;
    }

    if (!this.isValidPhone(this.phoneNumber)) {
      this.errorMessage = 'Please enter a valid 10-digit Indian phone number';
      return;
    }

    if (this.dateOfBirth && !this.isValidDateOfBirth(this.dateOfBirth)) {
      this.errorMessage = 'Please enter a valid date of birth';
      return;
    }

    const normalizedGender = this.normalizeGender(this.gender);
    if (this.gender && !normalizedGender) {
      this.errorMessage = 'Please select a valid gender';
      return;
    }

    this.isLoading = true;
    this.authService.register({
      username: this.username,
      email: this.email,
      password: this.password,
      firstName: this.firstName,
      lastName: this.lastName,
      phoneNumber: this.phoneNumber,
      gender: normalizedGender || undefined,
      dateOfBirth: this.dateOfBirth || undefined
    }).subscribe({
      next: () => {
        this.isLoading = false;
        this.registrationSuccess = true;
        this.registeredEmail = this.email;
        // Redirect to login after showing success message
        setTimeout(() => {
          this.router.navigate(['/login']);
        }, 4000);
      },
      error: (error) => {
        this.isLoading = false;
        this.errorMessage = error.error?.error || 'Registration failed. Please try again.';
      }
    });
  }

  private isValidEmail(email: string): boolean {
    const emailRegex = /^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$/;
    return emailRegex.test(email);
  }

  private isValidPhone(phone: string): boolean {
    const phoneRegex = /^[6-9]\d{9}$/;
    return phoneRegex.test(phone.replace(/\s+/g, ''));
  }

  private isValidDateOfBirth(value: string): boolean {
    const dob = new Date(value);
    if (Number.isNaN(dob.getTime())) return false;

    const today = new Date();
    today.setHours(0, 0, 0, 0);
    if (dob > today) return false;

    const age = today.getFullYear() - dob.getFullYear() - ((today.getMonth() < dob.getMonth() || (today.getMonth() === dob.getMonth() && today.getDate() < dob.getDate())) ? 1 : 0);
    return age >= 5 && age <= 120;
  }

  private normalizeGender(value: string): string | null {
    const normalized = value?.trim().toLowerCase();
    if (!normalized) return null;

    const allowed: Record<string, string> = {
      male: 'male',
      female: 'female',
      other: 'other',
      prefer_not_to_say: 'prefer_not_to_say'
    };

    return allowed[normalized] ?? null;
  }
}
