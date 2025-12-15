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
    if (token) {
      const userJson = localStorage.getItem('currentUser');
      if (userJson) {
        try {
          const user = JSON.parse(userJson);
          this.currentUserSubject.next(user);
        } catch (e) {
          this.logout();
        }
      }
    }
  }

  login(username: string, password: string): Observable<LoginResponse> {
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
              token: response.token
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
    localStorage.removeItem('authToken');
    localStorage.removeItem('currentUser');
    this.currentUserSubject.next(null);
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
}
