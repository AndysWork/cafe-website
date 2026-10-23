import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
  AdminUserRoleService,
  AdminUserSummary,
  MANAGEABLE_USER_ROLES
} from '../../services/admin-user-role.service';
import { UIStore } from '../../store/ui.store';
import { AuthStore } from '../../store/auth.store';
import { normalizeRoles } from '../../utils/roles';

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
  roleDrafts: Record<string, string[]> = {};

  filteredUsers = computed(() => {
    const term = this.search.trim().toLowerCase();
    const roleFilter = this.roleFilter;

    return this.users()
      .filter(user => {
        if (roleFilter !== 'all' && !this.getUserRoles(user).includes(roleFilter)) {
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
            this.roleDrafts[user.id] = [...this.getUserRoles(user)];
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

  getUserRoles(user: AdminUserSummary): string[] {
    return normalizeRoles(user.roles?.length ? user.roles : [user.role]);
  }

  getDraftRoles(user: AdminUserSummary): string[] {
    return this.roleDrafts[user.id] ?? this.getUserRoles(user);
  }

  isRoleSelected(user: AdminUserSummary, role: string): boolean {
    return this.getDraftRoles(user).includes(role);
  }

  toggleDraftRole(user: AdminUserSummary, role: string, checked: boolean): void {
    let next = this.getDraftRoles(user).filter(r => r !== role);
    if (checked) {
      next.push(role);
      // 'user' is the customer role; it is dropped when a staff role is selected.
      if (role !== 'user') next = next.filter(r => r !== 'user');
      else next = ['user'];
    }
    this.roleDrafts[user.id] = normalizeRoles(next);
  }

  hasRoleChanged(user: AdminUserSummary): boolean {
    const draft = normalizeRoles(this.getDraftRoles(user));
    const current = this.getUserRoles(user);
    return draft.length !== current.length || draft.some((r, i) => r !== current[i]);
  }

  canUpdateRole(user: AdminUserSummary): boolean {
    const current = this.authStore.user();
    const draft = this.getDraftRoles(user);

    if (!this.hasRoleChanged(user) || draft.length === 0) {
      return false;
    }

    // Prevent removing own admin role in UI as an extra guard.
    if (current?.username === user.username && !draft.includes('admin')) {
      return false;
    }

    return true;
  }

  updateRole(user: AdminUserSummary): void {
    if (!this.canUpdateRole(user)) {
      return;
    }

    const nextRoles = normalizeRoles(this.getDraftRoles(user));
    this.savingUserId = user.id;

    this.roleService.updateUserRoles(user.id, nextRoles).subscribe({
      next: response => {
        this.uiStore.success(response?.message || `Roles updated to ${nextRoles.join(', ')}`);
        this.loadUsers();
        this.savingUserId = null;
      },
      error: () => {
        this.savingUserId = null;
        this.uiStore.error('Failed to update roles');
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
