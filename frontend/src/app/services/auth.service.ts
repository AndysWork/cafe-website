import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable, tap, catchError, of, map } from 'rxjs';
import { environment } from '../../environments/environment';
import { AuthStore } from '../store/auth.store';

export type UserRole = 'admin' | 'user';

export interface User {
  id?: string;
  username: string;
  email: string;
  role: UserRole;
  firstName?: string;
  lastName?: string;
  phoneNumber?: string;
  profilePictureUrl?: string;
  token?: string;
  defaultOutletId?: string;
  assignedOutlets?: string[];
}

export interface LoginRequest {
  username: string;
  password: string;
}

export interface RegisterRequest {
  username: string;
  email: string;
  password: string;
  firstName?: string;
  lastName?: string;
  phoneNumber?: string;
}

export interface LoginResponse {
  token: string;
  username: string;
  email: string;
  role: string;
  firstName?: string;
  lastName?: string;
  profilePictureUrl?: string;
  defaultOutletId?: string;
  assignedOutlets?: string[];
}

export interface ApiLoginResponse {
  success: boolean;
  data: LoginResponse;
  csrfToken?: string;
}

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private http = inject(HttpClient);
  private apiUrl = environment.apiUrl;
  private authStore = inject(AuthStore);

  /** Observable bridge — delegates to AuthStore signal via toObservable. */
  public currentUser$: Observable<User | null> = this.authStore.user$;

  private clearAuthData(): void {
    this.authStore.logout();
  }

  login(username: string, password: string): Observable<LoginResponse> {
    // Clear any existing auth data before login
    this.clearAuthData();

    return this.http.post<ApiLoginResponse>(`${this.apiUrl}/auth/login`, { username, password })
      .pipe(
        tap(apiResponse => {
          const response = apiResponse.data;
          if (response.token) {
            const user: User = {
              username: response.username,
              email: response.email,
              role: response.role as UserRole,
              firstName: response.firstName,
              lastName: response.lastName,
              profilePictureUrl: response.profilePictureUrl,
              token: response.token,
              defaultOutletId: response.defaultOutletId,
              assignedOutlets: response.assignedOutlets
            };
            this.authStore.login(user, response.token, apiResponse.csrfToken);
          }
        }),
        map(apiResponse => apiResponse.data)
      );
  }

  register(request: RegisterRequest): Observable<any> {
    return this.http.post(`${this.apiUrl}/auth/register`, request);
  }

  logout(): void {
    this.clearAuthData();
  }

  getToken(): string | null {
    return this.authStore.token();
  }

  getCurrentUser(): User | null {
    return this.authStore.user();
  }

  isAdmin(): boolean {
    return this.authStore.isAdmin();
  }

  isUser(): boolean {
    return this.authStore.isUser();
  }

  isLoggedIn(): boolean {
    return this.authStore.isLoggedIn();
  }

  getUserRole(): UserRole | 'guest' {
    return this.authStore.userRole();
  }

  validateToken(): Observable<boolean> {
    const token = this.getToken();
    if (!token) {
      return of(false);
    }

    return this.http.get<any>(`${this.apiUrl}/auth/validate`).pipe(
      tap(response => {
        if (!response.valid) {
          this.logout();
        }
      }),
      catchError(() => {
        this.logout();
        return of(false);
      })
    );
  }

  forgotPassword(email: string): Observable<any> {
    return this.http.post(`${this.apiUrl}/auth/password/forgot`, { email });
  }

  resetPassword(token: string, newPassword: string, confirmPassword: string): Observable<any> {
    return this.http.post(`${this.apiUrl}/auth/password/reset`, {
      resetToken: token,
      newPassword,
      confirmPassword
    });
  }

  updateProfile(data: { firstName?: string; lastName?: string; phoneNumber?: string }): Observable<any> {
    return this.http.put(`${this.apiUrl}/auth/profile`, data).pipe(
      tap((response: any) => {
        if (response.data) {
          this.authStore.updateProfile({
            firstName: response.data.firstName,
            lastName: response.data.lastName,
            phoneNumber: response.data.phoneNumber,
            profilePictureUrl: response.data.profilePictureUrl
          });
        }
      })
    );
  }

  uploadProfilePicture(file: File): Observable<{ profilePictureUrl: string; message: string }> {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post<{ profilePictureUrl: string; message: string }>(
      `${this.apiUrl}/auth/profile/picture`, formData
    ).pipe(
      tap((response) => {
        if (response.profilePictureUrl) {
          this.authStore.updateProfile({ profilePictureUrl: response.profilePictureUrl });
        }
      })
    );
  }

  deleteProfilePicture(): Observable<any> {
    return this.http.delete(`${this.apiUrl}/auth/profile/picture`).pipe(
      tap(() => {
        this.authStore.updateProfile({ profilePictureUrl: undefined });
      })
    );
  }

  changePassword(currentPassword: string, newPassword: string, confirmPassword: string): Observable<any> {
    return this.http.post(`${this.apiUrl}/auth/password/change`, {
      currentPassword,
      newPassword,
      confirmPassword
    });
  }
}
