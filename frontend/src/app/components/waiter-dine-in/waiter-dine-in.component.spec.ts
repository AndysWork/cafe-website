import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { WaiterDineInComponent } from './waiter-dine-in.component';
import { CafeTableService, CafeTableFloorView } from '../../services/cafe-table.service';
import { DineInService } from '../../services/dine-in.service';
import { MenuService, MenuItem } from '../../services/menu.service';
import { OutletService } from '../../services/outlet.service';

describe('WaiterDineInComponent customer attribution', () => {
  let component: WaiterDineInComponent;
  let dineIn: jasmine.SpyObj<DineInService>;
  const menuItem: MenuItem = {
    id: 'item-1', name: 'Tea', categoryId: 'drinks', onlinePrice: 20, shopSellingPrice: 20
  };
  const table = (tableNumber: string, customerName?: string): CafeTableFloorView => ({
    tableNumber, customerName, capacity: 4, sortOrder: 1, isActive: true,
    housekeepingStatus: 'available', status: 'free', roundsCount: 0, grandTotal: 0, amountDue: 0
  });

  beforeEach(() => {
    dineIn = jasmine.createSpyObj<DineInService>('DineInService', ['placeWaiterDineInOrder']);
    dineIn.placeWaiterDineInOrder.and.returnValue(of({ message: 'Sent' }));
    TestBed.configureTestingModule({
      providers: [
        { provide: DineInService, useValue: dineIn },
        { provide: CafeTableService, useValue: { getFloor: () => of({ tables: [], summary: {} }) } },
        { provide: MenuService, useValue: {} },
        { provide: OutletService, useValue: {
          getSelectedOutletId: () => 'outlet-1',
          getSelectedOutlet: () => ({ isActive: true, settings: { acceptsDineIn: true } })
        } }
      ]
    });
    component = TestBed.runInInjectionContext(() => new WaiterDineInComponent());
    component.tables = [table('T1'), table('T2', 'Existing customer')];
  });

  afterEach(() => component.ngOnDestroy());

  it('sends the entered customer name without using the waiter as a customer', () => {
    component.selectTable('T1');
    component.customerName = '  Asha  ';
    component.addToCart(menuItem);
    component.submitOrder();
    expect(dineIn.placeWaiterDineInOrder).toHaveBeenCalledWith(jasmine.objectContaining({
      tableNumber: 'T1', customerName: 'Asha'
    }));
  });

  it('leaves unnamed walk-ins unlinked to a customer account', () => {
    component.selectTable('T1');
    component.addToCart(menuItem);
    component.submitOrder();
    expect(dineIn.placeWaiterDineInOrder.calls.mostRecent().args[0].customerName).toBeUndefined();
  });

  it('restores the active customer when selecting a table and clears it for a new table', () => {
    component.selectTable('T2');
    expect(component.customerName).toBe('Existing customer');
    component.selectTable('T1');
    expect(component.customerName).toBe('');
  });
});
