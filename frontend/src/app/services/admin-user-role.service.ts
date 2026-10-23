import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map, catchError } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { handleServiceError } from '../utils/error-handler';

export type ManageableUserRole =
  | 'admin'
  | 'manager'
  | 'cashier'
  | 'waiter'
  | 'kitchen'
  | 'partner'
  | 'delivery-partner'
  | 'cook'
  | 'chef'
  | 'sous-chef'
  | 'user';

export const MANAGEABLE_USER_ROLES: ManageableUserRole[] = [
  'user',
  'waiter',
  'cashier',
  'kitchen',
  'cook',
  'chef',
  'sous-chef',
  'delivery-partner',
  'partner',
  'manager',
  'admin'
];

export interface AdminUserSummary {
  id: string;
  username: string;
  email: string;
  role: ManageableUserRole | string;
  roles?: string[];
  firstName?: string;
  lastName?: string;
  phoneNumber?: string;
  isActive: boolean;
  createdAt?: string;
  lastLoginAt?: string;
}

@Injectable({ providedIn: 'root' })
export class AdminUserRoleService {
  private http = inject(HttpClient);
  private apiUrl = environment.apiUrl;

  getUsers(): Observable<AdminUserSummary[]> {
    return this.http
      .get<{ success: boolean; data: AdminUserSummary[] }>(`${this.apiUrl}/users`)
      .pipe(
        map(response => response?.data || []),
        catchError(handleServiceError('AdminUserRoleService.getUsers'))
      );
  }

  updateUserRole(userId: string, role: ManageableUserRole): Observable<{ success: boolean; message: string }> {
    return this.http
      .put<{ success: boolean; message: string }>(`${this.apiUrl}/users/${userId}/role`, { role })
      .pipe(catchError(handleServiceError('AdminUserRoleService.updateUserRole')));
  }

  /** Replaces all roles of a user (a staff member may hold several roles). */
  updateUserRoles(userId: string, roles: string[]): Observable<{ success: boolean; message: string }> {
    return this.http
      .put<{ success: boolean; message: string }>(`${this.apiUrl}/users/${userId}/roles`, { roles })
      .pipe(catchError(handleServiceError('AdminUserRoleService.updateUserRoles')));
  }

  toggleUserStatus(userId: string): Observable<{ success: boolean; message: string }> {
    return this.http
      .post<{ success: boolean; message: string }>(`${this.apiUrl}/users/${userId}/toggle-status`, {})
      .pipe(catchError(handleServiceError('AdminUserRoleService.toggleUserStatus')));
  }
}
