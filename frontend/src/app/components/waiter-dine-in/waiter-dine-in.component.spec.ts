import { TestBed } from '@angular/core/testing';
import { of, Subject, throwError } from 'rxjs';
import { WaiterDineInComponent } from './waiter-dine-in.component';
import { CafeTableService, CafeTableFloorView } from '../../services/cafe-table.service';
import { DineInService, DineInSessionOrder } from '../../services/dine-in.service';
import { MenuService, MenuItem } from '../../services/menu.service';
import { OutletService } from '../../services/outlet.service';

describe('WaiterDineInComponent customer attribution', () => {
  let component: WaiterDineInComponent;
  let dineIn: jasmine.SpyObj<DineInService>;
  let tables: jasmine.SpyObj<CafeTableService>;
  const summary = { total: 1, free: 1, occupied: 0, billRequested: 0, cleaning: 0, outOfService: 0 };
  const menuItem: MenuItem = {
    id: 'item-1', name: 'Tea', categoryId: 'drinks', onlinePrice: 20, shopSellingPrice: 20
  };
  const table = (tableNumber: string, customerName?: string): CafeTableFloorView => ({
    tableNumber, customerName, capacity: 4, sortOrder: 1, isActive: true,
    housekeepingStatus: 'available', status: 'free', roundsCount: 0, grandTotal: 0, amountDue: 0
  });
  const round = (orderId: string, status = 'pending'): DineInSessionOrder => ({
    orderId, tableNumber: 'T1', roundNumber: 1, status,
    kotReprintCount: 0, createdAt: '2026-10-24T00:00:00Z', total: 20,
    itemCount: 1, items: [], canCancelKot: status === 'pending'
  });

  beforeEach(() => {
    dineIn = jasmine.createSpyObj<DineInService>('DineInService',
      ['placeWaiterDineInOrder', 'cancelDineInKot', 'getDineInSessionOrders']);
    dineIn.placeWaiterDineInOrder.and.returnValue(of({ message: 'Sent' }));
    dineIn.cancelDineInKot.and.returnValue(of({ message: 'KOT cancelled', orderId: 'o1' }));
    dineIn.getDineInSessionOrders.and.returnValue(of({ orders: [] }));
    tables = jasmine.createSpyObj<CafeTableService>('CafeTableService', ['getFloor']);
    tables.getFloor.and.returnValue(of({ tables: [], summary }));
    TestBed.configureTestingModule({
      providers: [
        { provide: DineInService, useValue: dineIn },
        { provide: CafeTableService, useValue: tables },
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

  it('removes cancelled KOTs from the operational round list', () => {
    component.tables = [{ ...table('T1'), status: 'occupied', sessionId: 's1' }];
    dineIn.getDineInSessionOrders.and.returnValue(of({ orders: [round('o1', 'cancelled'), round('o2')] }));

    component.selectTable('T1');

    expect(component.currentSessionOrders.map(order => order.orderId)).toEqual(['o2']);
  });

  it('clears the round list and old customer when the last KOT cancellation frees the table', () => {
    spyOn(window, 'prompt').and.returnValue('Customer left');
    component.tables = [{ ...table('T1', 'Asha'), status: 'occupied', sessionId: 's1' }];
    dineIn.getDineInSessionOrders.and.returnValue(of({ orders: [round('o1')] }));
    component.selectTable('T1');
    tables.getFloor.and.returnValue(of({ tables: [table('T1')], summary }));

    component.cancelKot(round('o1'));

    expect(dineIn.cancelDineInKot).toHaveBeenCalledWith('o1', 'Customer left');
    expect(component.currentSessionOrders).toEqual([]);
    expect(component.selectedTable?.status).toBe('free');
    expect(component.selectedTable?.sessionId).toBeUndefined();
    expect(component.customerName).toBe('');
    expect(dineIn.getDineInSessionOrders).toHaveBeenCalledTimes(1);
  });

  it('keeps other rounds and the occupied table after cancelling just one KOT', () => {
    spyOn(window, 'prompt').and.returnValue('Duplicate');
    const occupied: CafeTableFloorView = { ...table('T1', 'Asha'), status: 'occupied', sessionId: 's1' };
    component.tables = [occupied];
    dineIn.getDineInSessionOrders.and.returnValues(
      of({ orders: [round('o1'), round('o2')] }), of({ orders: [round('o2')] }));
    component.selectTable('T1');
    tables.getFloor.and.returnValue(of({ tables: [occupied], summary }));

    component.cancelKot(round('o1'));

    expect(component.currentSessionOrders.map(order => order.orderId)).toEqual(['o2']);
    expect(component.selectedTable?.status).toBe('occupied');
    expect(component.customerName).toBe('Asha');
  });

  it('does not restore cancelled rounds from a stale request after the table is freed', () => {
    spyOn(window, 'prompt').and.returnValue('Customer left');
    const staleOrders = new Subject<{ orders: DineInSessionOrder[] }>();
    component.tables = [{ ...table('T1'), status: 'occupied', sessionId: 's1' }];
    dineIn.getDineInSessionOrders.and.returnValue(staleOrders);
    component.selectTable('T1');
    tables.getFloor.and.returnValue(of({ tables: [table('T1')], summary }));

    component.cancelKot(round('o1'));
    staleOrders.next({ orders: [round('o1')] });

    expect(component.currentSessionOrders).toEqual([]);
  });

  it('ignores a previous table order response after selecting a different table', () => {
    const staleOrders = new Subject<{ orders: DineInSessionOrder[] }>();
    component.tables = [
      { ...table('T1'), status: 'occupied', sessionId: 's1' },
      { ...table('T2'), status: 'occupied', sessionId: 's2' }
    ];
    dineIn.getDineInSessionOrders.and.returnValues(staleOrders, of({ orders: [round('o2')] }));

    component.selectTable('T1');
    component.selectTable('T2');
    staleOrders.next({ orders: [round('o1')] });

    expect(component.currentSessionOrders.map(order => order.orderId)).toEqual(['o2']);
  });

  it('does not reopen a freed table from a stale floor refresh', () => {
    spyOn(window, 'prompt').and.returnValue('Duplicate');
    const occupied: CafeTableFloorView = { ...table('T1'), status: 'occupied', sessionId: 's1' };
    const staleFloor = new Subject<{ tables: CafeTableFloorView[]; summary: typeof summary }>();
    component.tables = [occupied];
    dineIn.getDineInSessionOrders.and.returnValue(of({ orders: [round('o1'), round('o2')] }));
    component.selectTable('T1');
    tables.getFloor.and.returnValues(staleFloor, of({ tables: [table('T1')], summary }));

    component.cancelKot(round('o1'));
    component.cancelKot(round('o2'));
    staleFloor.next({ tables: [occupied], summary });

    expect(component.selectedTable?.status).toBe('free');
    expect(component.currentSessionOrders).toEqual([]);
  });

  it('preserves the table and round when cancellation fails', () => {
    spyOn(window, 'prompt').and.returnValue('Customer left');
    component.tables = [{ ...table('T1'), status: 'occupied', sessionId: 's1' }];
    dineIn.getDineInSessionOrders.and.returnValue(of({ orders: [round('o1')] }));
    component.selectTable('T1');
    dineIn.cancelDineInKot.and.returnValue(throwError(() => new Error('Preparation has started')));

    component.cancelKot(round('o1'));

    expect(component.currentSessionOrders.map(order => order.orderId)).toEqual(['o1']);
    expect(component.selectedTable?.status).toBe('occupied');
    expect(component.errorMessage).toBe('Preparation has started');
  });
});
