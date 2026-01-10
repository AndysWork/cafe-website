import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { BehaviorSubject, Observable, tap, catchError, of, map } from 'rxjs';
import { environment } from '../../environments/environment';

export type UserRole = 'admin' | 'user';

export interface User {
  id?: string;
  username: string;
  email: string;
  role: UserRole;
  firstName?: string;
  lastName?: string;
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

  private currentUserSubject = new BehaviorSubject<User | null>(null);
  public currentUser$: Observable<User | null> = this.currentUserSubject.asObservable();

  constructor() {
    // Check if user is already logged in from localStorage
    const token = this.getToken();
    const userJson = localStorage.getItem('currentUser');

    if (token && userJson) {
      try {
        const user = JSON.parse(userJson);
        // Validate that the user object has required fields
        if (user && user.username && user.email && user.role) {
          // Additional validation: check if token in user matches stored token
          if (user.token && user.token === token) {
            this.currentUserSubject.next(user);
          } else {
            // Token mismatch, clear stale data
            console.warn('Token mismatch detected, clearing auth data');
            this.clearAuthData();
          }
        } else {
          // Invalid user data, clear it
          this.clearAuthData();
        }
      } catch (e) {
        // Failed to parse user data, clear it
        console.error('Failed to parse user data:', e);
        this.clearAuthData();
      }
    } else if (token || userJson) {
      // Partial data exists (token without user or vice versa), clear everything
      console.warn('Incomplete auth data detected, clearing all');
      this.clearAuthData();
    }
  }

  private clearAuthData(): void {
    localStorage.removeItem('authToken');
    localStorage.removeItem('currentUser');
    localStorage.removeItem('csrfToken');
    // Clear outlet data as well
    localStorage.removeItem('selectedOutletId');
    localStorage.removeItem('selectedOutlet');
    this.currentUserSubject.next(null);
  }

  login(username: string, password: string): Observable<LoginResponse> {
    // Clear any existing auth data before login
    this.clearAuthData();

    return this.http.post<ApiLoginResponse>(`${this.apiUrl}/auth/login`, { username, password })
      .pipe(
        tap(apiResponse => {
          const response = apiResponse.data;
          if (response.token) {
            // Store token
            localStorage.setItem('authToken', response.token);

            // Store CSRF token if provided
            if (apiResponse.csrfToken) {
              localStorage.setItem('csrfToken', apiResponse.csrfToken);
            }

            // Store user info
            const user: User = {
              username: response.username,
              email: response.email,
              role: response.role as UserRole,
              firstName: response.firstName,
              lastName: response.lastName,
              token: response.token,
              defaultOutletId: response.defaultOutletId,
              assignedOutlets: response.assignedOutlets
            };

            localStorage.setItem('currentUser', JSON.stringify(user));
            this.currentUserSubject.next(user);
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
    return localStorage.getItem('authToken');
  }

  getCurrentUser(): User | null {
    return this.currentUserSubject.value;
  }

  isAdmin(): boolean {
    return this.currentUserSubject.value?.role === 'admin';
  }

  isUser(): boolean {
    const role = this.currentUserSubject.value?.role;
    return role === 'user' || role === 'admin';
  }

  isLoggedIn(): boolean {
    return this.currentUserSubject.value !== null && this.getToken() !== null;
  }

  getUserRole(): UserRole | 'guest' {
    return this.currentUserSubject.value?.role || 'guest';
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
        // Update current user in localStorage and subject
        const currentUser = this.getCurrentUser();
        if (currentUser && response.data) {
          const updatedUser = {
            ...currentUser,
            firstName: response.data.firstName,
            lastName: response.data.lastName
          };
          localStorage.setItem('currentUser', JSON.stringify(updatedUser));
          this.currentUserSubject.next(updatedUser);
        }
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
