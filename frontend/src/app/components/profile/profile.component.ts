import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { AuthService, User } from '../../services/auth.service';

@Component({
  selector: 'app-profile',
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './profile.component.html',
  styleUrl: './profile.component.scss'
})
export class ProfileComponent implements OnInit {
  user: User | null = null;

  // Profile update
  firstName = '';
  lastName = '';
  phoneNumber = '';
  profileMessage = '';
  profileError = '';
  isUpdatingProfile = false;

  // Change password
  currentPassword = '';
  newPassword = '';
  confirmPassword = '';
  passwordMessage = '';
  passwordError = '';
  isChangingPassword = false;

  activeTab: 'profile' | 'password' = 'profile';

  constructor(
    private authService: AuthService,
    private router: Router
  ) {}

  ngOnInit(): void {
    this.user = this.authService.getCurrentUser();
    if (this.user) {
      this.firstName = this.user.firstName || '';
      this.lastName = this.user.lastName || '';
    }
  }

  switchTab(tab: 'profile' | 'password'): void {
    this.activeTab = tab;
    this.clearMessages();
  }

  clearMessages(): void {
    this.profileMessage = '';
    this.profileError = '';
    this.passwordMessage = '';
    this.passwordError = '';
  }

  updateProfile(): void {
    this.clearMessages();
    this.isUpdatingProfile = true;

    this.authService.updateProfile({
      firstName: this.firstName,
      lastName: this.lastName,
      phoneNumber: this.phoneNumber || undefined
    }).subscribe({
      next: () => {
        this.isUpdatingProfile = false;
        this.profileMessage = 'Profile updated successfully!';
        this.user = this.authService.getCurrentUser();
        setTimeout(() => this.profileMessage = '', 3000);
      },
      error: (error) => {
        this.isUpdatingProfile = false;
        this.profileError = error.error?.error || 'Failed to update profile';
        setTimeout(() => this.profileError = '', 5000);
      }
    });
  }

  changePassword(): void {
    this.clearMessages();

    if (this.newPassword !== this.confirmPassword) {
      this.passwordError = 'Passwords do not match';
      return;
    }

    if (this.newPassword.length < 6) {
      this.passwordError = 'Password must be at least 6 characters';
      return;
    }

    this.isChangingPassword = true;

    this.authService.changePassword(
      this.currentPassword,
      this.newPassword,
      this.confirmPassword
    ).subscribe({
      next: () => {
        this.isChangingPassword = false;
        this.passwordMessage = 'Password changed successfully!';
        this.currentPassword = '';
        this.newPassword = '';
        this.confirmPassword = '';
        setTimeout(() => this.passwordMessage = '', 3000);
      },
      error: (error) => {
        this.isChangingPassword = false;
        this.passwordError = error.error?.error || 'Failed to change password';
        setTimeout(() => this.passwordError = '', 5000);
      }
    });
  }
}
