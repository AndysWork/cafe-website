import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
  AdminUserRoleService,
  AdminUserSummary,
  MANAGEABLE_USER_ROLES,
  ManageableUserRole
} from '../../services/admin-user-role.service';
import { UIStore } from '../../store/ui.store';
import { AuthStore } from '../../store/auth.store';

@Component({
  selector: 'app-admin-user-roles',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './admin-user-roles.component.html',
  styleUrls: ['./admin-user-roles.component.scss']
})
export class AdminUserRolesComponent implements OnInit {
  private roleService = inject(AdminUserRoleService);
  private uiStore = inject(UIStore);
  private authStore = inject(AuthStore);

  readonly roles = MANAGEABLE_USER_ROLES;

  loading = false;
  savingUserId: string | null = null;
  search = '';
  roleFilter = 'all';

  users = signal<AdminUserSummary[]>([]);
  roleDrafts: Record<string, ManageableUserRole | string> = {};

  filteredUsers = computed(() => {
    const term = this.search.trim().toLowerCase();
    const roleFilter = this.roleFilter;

    return this.users()
      .filter(user => {
        if (roleFilter !== 'all' && user.role !== roleFilter) {
          return false;
        }

        if (!term) {
          return true;
        }

        const fullName = `${user.firstName || ''} ${user.lastName || ''}`.trim().toLowerCase();
        return (
          user.username.toLowerCase().includes(term) ||
          user.email.toLowerCase().includes(term) ||
          (user.phoneNumber || '').toLowerCase().includes(term) ||
          fullName.includes(term)
        );
      })
      .sort((a, b) => a.username.localeCompare(b.username));
  });

  ngOnInit(): void {
    this.loadUsers();
  }

  loadUsers(): void {
    this.loading = true;
    this.roleService.getUsers().subscribe({
      next: users => {
        this.users.set(users);
        this.roleDrafts = {};
        for (const user of users) {
          if (user.id) {
            this.roleDrafts[user.id] = user.role;
          }
        }
        this.loading = false;
      },
      error: () => {
        this.loading = false;
        this.uiStore.error('Failed to load users');
      }
    });
  }

  getDisplayName(user: AdminUserSummary): string {
    const fullName = `${user.firstName || ''} ${user.lastName || ''}`.trim();
    return fullName || user.username;
  }

  getDraftRole(user: AdminUserSummary): string {
    return this.roleDrafts[user.id] || user.role;
  }

  hasRoleChanged(user: AdminUserSummary): boolean {
    return this.getDraftRole(user) !== user.role;
  }

  canUpdateRole(user: AdminUserSummary): boolean {
    const current = this.authStore.user();
    const draft = this.getDraftRole(user);

    if (!this.hasRoleChanged(user)) {
      return false;
    }

    // Prevent changing own role away from admin in UI as an extra guard.
    if (current?.username === user.username && draft !== 'admin') {
      return false;
    }

    return true;
  }

  updateRole(user: AdminUserSummary): void {
    if (!this.canUpdateRole(user)) {
      return;
    }

    const nextRole = this.getDraftRole(user) as ManageableUserRole;
    this.savingUserId = user.id;

    this.roleService.updateUserRole(user.id, nextRole).subscribe({
      next: response => {
        this.uiStore.success(response?.message || `Role updated to ${nextRole}`);
        this.loadUsers();
        this.savingUserId = null;
      },
      error: () => {
        this.savingUserId = null;
        this.uiStore.error('Failed to update role');
      }
    });
  }

  toggleUserStatus(user: AdminUserSummary): void {
    this.savingUserId = user.id;
    this.roleService.toggleUserStatus(user.id).subscribe({
      next: response => {
        this.uiStore.success(response?.message || 'User status updated');
        this.loadUsers();
        this.savingUserId = null;
      },
      error: () => {
        this.savingUserId = null;
        this.uiStore.error('Failed to update user status');
      }
    });
  }
}
