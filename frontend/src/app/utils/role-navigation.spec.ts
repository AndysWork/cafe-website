import { getRoleNavigationItems } from './role-navigation';

describe('Role navigation', () => {
  const routesFor = (...roles: string[]) => getRoleNavigationItems(roles).map(item => item.route);

  it('combines waiter, kitchen, and cashier destinations without duplicate shared links', () => {
    const routes = routesFor('waiter', 'cook', 'cashier');
    expect(routes).toContain('/staff/dine-in');
    expect(routes).toContain('/staff/cashier');
    expect(routes).toContain('/kitchen/dashboard');
    expect(routes).toContain('/kitchen/display');
    expect(routes).toContain('/staff/attendance');
    expect(routes).toContain('/staff/payslip');
    expect(new Set(routes).size).toBe(routes.length);
    expect(routes).not.toContain('/admin/dashboard');
    expect(routes).not.toContain('/manager/dashboard');
    expect(routes).not.toContain('/partner/delivery');
  });

  it('keeps every assigned dashboard instead of choosing one by priority', () => {
    const routes = routesFor('admin', 'manager', 'chef', 'partner', 'user');
    for (const route of ['/admin/dashboard', '/manager/dashboard', '/kitchen/dashboard', '/partner/delivery', '/dashboard']) {
      expect(routes).toContain(route);
    }
  });

  it('preserves customer navigation when user is a secondary role', () => {
    const routes = routesFor('manager', 'user');
    for (const route of ['/manager/operations', '/manager/counter-sale', '/menu', '/cart', '/orders', '/reservations', '/subscriptions', '/loyalty']) {
      expect(routes).toContain(route);
    }
  });

  it('does not grant roles just to display their menus', () => {
    expect(routesFor('cashier')).toEqual(['/staff/cashier', '/staff/attendance', '/staff/payslip']);
    expect(routesFor('waiter')).toEqual(['/staff/dine-in', '/staff/attendance', '/staff/payslip']);
    expect(routesFor('delivery-partner')).toEqual(['/partner/delivery', '/partner/payout']);
    expect(routesFor('user')).toEqual(['/dashboard', '/menu', '/cart', '/orders', '/reservations', '/subscriptions', '/loyalty']);
    expect(routesFor()).toEqual([]);
    expect(routesFor('unknown')).toEqual([]);
  });

  it('normalizes aliases and casing, and deduplicates overlapping role permissions', () => {
    expect(routesFor('Kitchen_Helper', ' CASHIER ', 'cashier')).toEqual(routesFor('kitchen-helper', 'cashier'));
    const routes = routesFor('manager', 'chef', 'cook', 'kitchen-staff', 'waiter', 'cashier', 'partner', 'delivery-partner');
    expect(new Set(routes).size).toBe(routes.length);
    expect(routes.filter(route => route === '/staff/cashier').length).toBe(1);
    expect(routes.filter(route => route === '/partner/delivery').length).toBe(1);
  });

  it('uses guarded admin destinations without exposing manager-only routes to admin-only accounts', () => {
    const routes = routesFor('admin');
    expect(routes).toContain('/admin/dine-in-report');
    expect(routes).toContain('/admin/counter-sale');
    expect(routes).toContain('/admin/kitchen-display');
    expect(routes.some(route => route.startsWith('/manager/'))).toBeFalse();
  });
});
