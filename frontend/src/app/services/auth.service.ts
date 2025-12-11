import { Injectable } from '@angular/core';
import { BehaviorSubject, Observable } from 'rxjs';

export type UserRole = 'guest' | 'user' | 'admin';

export interface User {
  username: string;
  role: UserRole;
}

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private currentUserSubject = new BehaviorSubject<User | null>(null);
  public currentUser$: Observable<User | null> = this.currentUserSubject.asObservable();

  constructor() {
    // Check if user is already logged in from localStorage
    const userJson = localStorage.getItem('currentUser');
    if (userJson) {
      try {
        const user = JSON.parse(userJson);
        this.currentUserSubject.next(user);
      } catch (e) {
        localStorage.removeItem('currentUser');
      }
    }
  }

  login(username: string, password: string): boolean {
    // Simple authentication - replace with actual API call
    let user: User | null = null;

    if (username === 'admin' && password === 'admin123') {
      user = { username: 'admin', role: 'admin' };
    } else if (username === 'user' && password === 'user123') {
      user = { username: username, role: 'user' };
    } else if (password === 'demo') {
      // Allow any username with 'demo' password for testing
      user = { username: username, role: 'user' };
    }

    if (user) {
      this.currentUserSubject.next(user);
      localStorage.setItem('currentUser', JSON.stringify(user));
      return true;
    }
    return false;
  }

  logout(): void {
    this.currentUserSubject.next(null);
    localStorage.removeItem('currentUser');
  }

  getCurrentUser(): User | null {
    return this.currentUserSubject.value;
  }

  isAdmin(): boolean {
    return this.currentUserSubject.value?.role === 'admin';
  }

  isUser(): boolean {
    return this.currentUserSubject.value?.role === 'user';
  }

  isLoggedIn(): boolean {
    return this.currentUserSubject.value !== null;
  }

  getUserRole(): UserRole {
    return this.currentUserSubject.value?.role || 'guest';
  }
}
