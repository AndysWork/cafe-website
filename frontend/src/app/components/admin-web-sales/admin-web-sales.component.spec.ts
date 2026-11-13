import { TestBed } from '@angular/core/testing';
import { HttpClient } from '@angular/common/http';
import { of } from 'rxjs';
import { AdminWebSalesComponent } from './admin-web-sales.component';
import { Order, OrderService } from '../../services/order.service';
import { DeliveryPartnerService } from '../../services/delivery-partner.service';
import { DineInService } from '../../services/dine-in.service';
import { MenuItem, MenuService } from '../../services/menu.service';
import { OutletService } from '../../services/outlet.service';
import { UIStore } from '../../store/ui.store';

describe('AdminWebSalesComponent reconciliation item picker', () => {
  let component: AdminWebSalesComponent;
  let orderService: jasmine.SpyObj<OrderService>;
  const menuItem: MenuItem = {
    id: 'menu-1',
    name: 'Coffee',
    categoryId: 'drinks',
    categoryName: 'Drinks',
    onlinePrice: 100,
    shopSellingPrice: 80,
    variants: [{ variantName: 'Large', price: 90 }]
  };
  const order: Order = {
    id: 'order-1', userId: 'user-1', username: 'guest',
    items: [{ menuItemId: 'old', name: 'Tea', quantity: 2, price: 50, total: 100 }],
    subtotal: 100, tax: 0, total: 100, status: 'delivered',
    paymentStatus: 'paid', paymentMethod: 'cash_at_counter',
    createdAt: '2026-11-08T12:00:00Z', updatedAt: '2026-11-08T12:00:00Z'
  };

  beforeEach(() => {
    orderService = jasmine.createSpyObj<OrderService>('OrderService', ['adminReconcileOrder']);
    TestBed.configureTestingModule({
      providers: [
        { provide: OutletService, useValue: {} },
        { provide: UIStore, useValue: { warning: jasmine.createSpy('warning'), success: jasmine.createSpy('success') } }
      ]
    });
    component = TestBed.runInInjectionContext(() => new AdminWebSalesComponent(
      orderService, {} as DeliveryPartnerService, {} as DineInService,
      {} as MenuService, {} as HttpClient
    ));
    component.menuCatalog = [menuItem];
  });

  it('adds a selected item with the catalog price and variant to the draft', () => {
    component.openReconcileItemPicker(order, new Event('click'));
    expect(component.getReconcileDraft(order).items.length).toBe(1);
    component.selectReconcileItem(menuItem);

    const items = component.getReconcileDraft(order).items;
    expect(items.length).toBe(2);
    expect(items[1]).toEqual(jasmine.objectContaining({
      menuItemId: 'menu-1', name: 'Coffee', menuItemSearch: 'Coffee',
      quantity: 1, baseUnitPrice: 80, selectedVariantName: 'Large'
    }));
    expect(component.menuPickerOrder).toBeNull();
  });

  it('cancels without adding an empty row', () => {
    component.openReconcileItemPicker(order, new Event('click'));
    component.menuPickerSearch = 'Coffee';
    component.closeReconcileItemPicker();
    expect(component.getReconcileDraft(order).items.length).toBe(1);
    expect(component.menuPickerSearch).toBe('');
  });

  it('changes an existing row without changing its quantity', () => {
    const row = component.getReconcileDraft(order).items[0];
    component.openReconcileItemPicker(order, new Event('click'), row);
    component.selectReconcileItem(menuItem);
    expect(component.getReconcileDraft(order).items.length).toBe(1);
    expect(row.name).toBe('Coffee');
    expect(row.quantity).toBe(2);
    expect(row.baseUnitPrice).toBe(80);
  });

  it('filters by name, category and ID and supports no matches', () => {
    expect(component.getFilteredMenuCatalog('drink')).toEqual([menuItem]);
    expect(component.getFilteredMenuCatalog('menu-1')).toEqual([menuItem]);
    expect(component.getFilteredMenuCatalog('coffee')).toEqual([menuItem]);
    expect(component.getFilteredMenuCatalog('missing')).toEqual([]);
  });

  it('refreshes dine-in balances after reconciling an order', async () => {
    const dineInOrder: Order = { ...order, orderType: 'dine-in' };
    component.getReconcileDraft(dineInOrder).reason = 'Correct items';
    orderService.adminReconcileOrder.and.returnValue(of(dineInOrder));
    spyOn(component, 'loadDashboardData').and.resolveTo();
    const pending = spyOn(component, 'loadPendingDineInPayments').and.resolveTo();
    const signals = spyOn(component, 'loadDineInOperationalSignals').and.resolveTo();

    component.submitReconciliation(dineInOrder);
    await new Promise(resolve => setTimeout(resolve, 0));

    expect(orderService.adminReconcileOrder).toHaveBeenCalledWith(
      dineInOrder.id, jasmine.objectContaining({ createdAt: undefined })
    );
    expect(pending).toHaveBeenCalled();
    expect(signals).toHaveBeenCalled();
    expect(component.saving).toBeFalse();
  });

  it('submits the created time when it was intentionally changed', async () => {
    const dineInOrder: Order = { ...order, orderType: 'dine-in' };
    const draft = component.getReconcileDraft(dineInOrder);
    draft.reason = 'Correct time';
    draft.createdAt = '2026-11-09T12:30';
    orderService.adminReconcileOrder.and.returnValue(of(dineInOrder));
    spyOn(component, 'loadDashboardData').and.resolveTo();
    spyOn(component, 'loadPendingDineInPayments').and.resolveTo();
    spyOn(component, 'loadDineInOperationalSignals').and.resolveTo();

    component.submitReconciliation(dineInOrder);
    await new Promise(resolve => setTimeout(resolve, 0));

    expect(orderService.adminReconcileOrder.calls.mostRecent().args[1].createdAt).toBeDefined();
  });
});
