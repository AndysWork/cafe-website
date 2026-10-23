import { CASHIER_ROLES, KITCHEN_ROLES, MANAGER_ROLES, PARTNER_ROLES, WAITER_ROLES, hasAnyRole, normalizeRoles } from './roles';

export interface RoleNavigationItem {
  label: string;
  route: string;
  icon: string;
  account?: boolean;
  group?: 'Dashboards' | 'Operations' | 'Staff' | 'My Account';
}

export interface RoleNavigationGroup {
  label: NonNullable<RoleNavigationItem['group']>;
  items: RoleNavigationItem[];
}

export function groupRoleNavigationItems(items: RoleNavigationItem[]): RoleNavigationGroup[] {
  const labels: RoleNavigationGroup['label'][] = ['Dashboards', 'Operations', 'Staff', 'My Account'];
  return labels.map(label => ({
    label,
    items: items.filter(item => (item.group ?? (item.account ? 'My Account' : 'Operations')) === label)
  })).filter(group => group.items.length > 0);
}

export const CUSTOMER_ACCOUNT_NAVIGATION: RoleNavigationItem[] = [
  { label: 'My Orders', route: '/orders', icon: '🛒', account: true },
  { label: 'Reservations', route: '/reservations', icon: '📅', account: true },
  { label: 'Subscriptions', route: '/subscriptions', icon: '🔁', account: true },
  { label: 'Loyalty Points', route: '/loyalty', icon: '⭐', account: true }
];

/** Navigation is additive across roles; landing-page priority must not hide another workspace. */
export function getRoleNavigationItems(userRoles: string[]): RoleNavigationItem[] {
  const roles = normalizeRoles(userRoles);
  const items: RoleNavigationItem[] = [];
  const add = (allowed: string[], item: RoleNavigationItem) => {
    if (hasAnyRole(roles, allowed)) items.push(item);
  };
  const admin = roles.includes('admin');

  add(['admin'], { label: 'Admin Dashboard', route: '/admin/dashboard', icon: '📊', group: 'Dashboards' });
  add(['manager'], { label: 'Manager Dashboard', route: '/manager/dashboard', icon: '📊', group: 'Dashboards' });
  add(KITCHEN_ROLES, { label: 'Kitchen Dashboard', route: '/kitchen/dashboard', icon: '📊', group: 'Dashboards' });
  add(PARTNER_ROLES, { label: 'Delivery Dashboard', route: '/partner/delivery', icon: '🛵', group: 'Dashboards' });
  add(['user'], { label: 'My Dashboard', route: '/dashboard', icon: '📊', group: 'Dashboards' });
  add(MANAGER_ROLES, { label: 'Dine-In Report', route: admin ? '/admin/dine-in-report' : '/manager/dine-in-report', icon: '📈' });
  add(CASHIER_ROLES, { label: 'Dine-In Payments', route: '/staff/cashier', icon: '💳' });
  add(WAITER_ROLES, { label: 'Waiter Station', route: '/staff/dine-in', icon: '🍽️' });
  add(KITCHEN_ROLES, { label: 'Kitchen Display', route: admin ? '/admin/kitchen-display' : '/kitchen/display', icon: '🍳' });
  add(MANAGER_ROLES, { label: 'Counter Sale', route: admin ? '/admin/counter-sale' : '/manager/counter-sale', icon: '🛒' });
  add(['manager'], { label: 'Delivery Tracking', route: '/manager/operations', icon: '🛵' });
  add(PARTNER_ROLES, { label: 'Payout Access', route: '/partner/payout', icon: '💰' });
  const staffRoles = [...KITCHEN_ROLES, ...WAITER_ROLES, ...CASHIER_ROLES];
  add(staffRoles, { label: 'Attendance', route: '/staff/attendance', icon: '🕐', group: 'Staff' });
  add(staffRoles, { label: 'Payslip Access', route: '/staff/payslip', icon: '💸', group: 'Staff' });
  add(['admin', 'user'], { label: 'Menu', route: '/menu', icon: '🍽️' });
  add(['user'], { label: 'Cart', route: '/cart', icon: '🛒', group: 'My Account' });
  if (roles.includes('user')) items.push(...CUSTOMER_ACCOUNT_NAVIGATION);

  return items.filter((item, index) => items.findIndex(other => other.route === item.route) === index);
}
