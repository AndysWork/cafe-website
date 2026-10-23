/** Role groups mirrored from the API's RoleHelper. A user can hold multiple roles. */
export const ROLE_PRIORITY = [
  'admin',
  'manager',
  'chef',
  'sous-chef',
  'cook',
  'kitchen',
  'cashier',
  'waiter',
  'partner',
  'delivery-partner',
  'user'
] as const;

export const KITCHEN_ROLES = ['admin', 'manager', 'cook', 'chef', 'sous-chef', 'kitchen', 'kitchen-staff', 'kitchen-helper'];
export const WAITER_ROLES = ['admin', 'manager', 'waiter'];
export const CASHIER_ROLES = ['admin', 'manager', 'cashier'];
export const MANAGER_ROLES = ['admin', 'manager'];
export const PARTNER_ROLES = ['partner', 'delivery-partner'];

export function normalizeRole(role: string | null | undefined): string {
  return (role ?? '').trim().toLowerCase().replace(/[_\s]+/g, '-');
}

export function normalizeRoles(roles: Array<string | null | undefined> | null | undefined): string[] {
  const seen = new Set<string>();
  for (const role of roles ?? []) {
    const r = normalizeRole(role);
    if (r) seen.add(r);
  }
  const priority = (r: string) => {
    const i = (ROLE_PRIORITY as readonly string[]).indexOf(r);
    return i === -1 ? Number.MAX_SAFE_INTEGER : i;
  };
  return [...seen].sort((a, b) => priority(a) - priority(b));
}

export function hasAnyRole(userRoles: string[], allowed: string[]): boolean {
  return userRoles.some(r => allowed.includes(normalizeRole(r)));
}
