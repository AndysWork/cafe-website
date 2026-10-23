import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { WaiterTakeawayComponent } from './waiter-takeaway.component';
import { MenuItem, MenuService } from '../../services/menu.service';
import { OutletService } from '../../services/outlet.service';
import { TakeawayService, WaiterTakeawayOrder } from '../../services/takeaway.service';
import { BillPrinterService } from '../../services/bill-printer.service';
import { ReceiptLine } from '../../utils/escpos';

describe('WaiterTakeawayComponent', () => {
  let component: WaiterTakeawayComponent;
  let takeaway: jasmine.SpyObj<TakeawayService>;
  let printer: jasmine.SpyObj<BillPrinterService>;
  let outletSettings: Record<string, unknown>;

  const tea: MenuItem = {
    id: 'tea', name: 'Tea', categoryId: 'drinks', onlinePrice: 30, webPrice: 25, shopSellingPrice: 20,
    addOns: [{ name: 'Extra ginger', price: 5, isActive: true } as any]
  };
  const order = (overrides: Partial<WaiterTakeawayOrder> = {}): WaiterTakeawayOrder => ({
    orderId: 'o1', token: 'K-007', status: 'ready', customerName: 'Asha',
    subtotal: 40, packagingCharge: 10, total: 50, paymentStatus: 'pending',
    createdAt: new Date().toISOString(), items: [], ...overrides
  });

  beforeEach(() => {
    outletSettings = { acceptsTakeaway: true, packagingCharge: 10 };
    takeaway = jasmine.createSpyObj<TakeawayService>('TakeawayService',
      ['getActiveOrders', 'createOrder', 'recordPayment', 'handOver', 'reprintKot', 'cancelOrder']);
    takeaway.getActiveOrders.and.returnValue(of({ orders: [] }));
    takeaway.createOrder.and.returnValue(of({ message: 'Sent', order: order({ status: 'pending' }) }));
    takeaway.handOver.and.returnValue(of({ message: 'Handed over', order: order({ status: 'delivered' }) }));
    printer = jasmine.createSpyObj<BillPrinterService>('BillPrinterService', ['autoPrint', 'print'], {
      settings: { mode: 'ble', paperWidth: 32, autoPrint: {} as any }
    });
    printer.autoPrint.and.resolveTo(true);
    printer.print.and.resolveTo();

    TestBed.configureTestingModule({
      providers: [
        { provide: TakeawayService, useValue: takeaway },
        { provide: BillPrinterService, useValue: printer },
        { provide: MenuService, useValue: { getMenuItems: () => of([tea]) } },
        { provide: OutletService, useValue: {
          getSelectedOutletId: () => 'outlet-1',
          getSelectedOutlet: () => ({ isActive: true, outletName: 'Salt Lake', settings: outletSettings })
        } }
      ]
    });
    component = TestBed.runInInjectionContext(() => new WaiterTakeawayComponent());
  });

  afterEach(() => component.ngOnDestroy());

  it('prices items from the counter price list and adds the packaging charge once', () => {
    component.addToCart(tea);
    component.addToCart(tea);
    expect(component.cartSubtotal).toBe(40);
    expect(component.cartTotal).toBe(50);
  });

  it('includes selected add-ons in the line price', () => {
    component.selectedAddOns[tea.id] = new Set(['Extra ginger']);
    component.addToCart(tea);
    expect(component.cartSubtotal).toBe(25);
  });

  it('requires a customer name and a UPI reference for upfront UPI payments', () => {
    component.addToCart(tea);
    expect(component.canSubmit).toBeFalse();
    component.customerName = 'Asha';
    expect(component.canSubmit).toBeTrue();
    component.paymentTiming = 'upfront';
    component.paymentMethod = 'upi-qr';
    expect(component.canSubmit).toBeFalse();
    component.upiReference = 'UPI123';
    expect(component.canSubmit).toBeTrue();
  });

  it('blocks new orders when takeaway is switched off for the outlet', () => {
    outletSettings['acceptsTakeaway'] = false;
    component.addToCart(tea);
    component.customerName = 'Asha';
    expect(component.canSubmit).toBeFalse();
    component.submitOrder();
    expect(takeaway.createOrder).not.toHaveBeenCalled();
    expect(component.errorMessage).toBeTruthy();
  });

  it('sends a pay-at-handover order without payment details and resets the form', () => {
    component.addToCart(tea);
    component.customerName = '  Asha ';
    component.phoneNumber = '9999999999';
    component.submitOrder();
    expect(takeaway.createOrder).toHaveBeenCalledWith(jasmine.objectContaining({
      customerName: 'Asha',
      phoneNumber: '9999999999',
      paymentTiming: 'handover',
      paymentMethod: undefined,
      items: [{ menuItemId: 'tea', quantity: 1, selectedVariantName: undefined, selectedAddOnNames: [] }]
    }));
    expect(component.cart.length).toBe(0);
    expect(component.message).toContain('K-007');
  });

  it('sends upfront payment details when the customer pays now', () => {
    component.addToCart(tea);
    component.customerName = 'Asha';
    component.paymentTiming = 'upfront';
    component.paymentMethod = 'upi-qr';
    component.upiReference = ' UPI123 ';
    component.submitOrder();
    expect(takeaway.createOrder).toHaveBeenCalledWith(jasmine.objectContaining({
      paymentTiming: 'upfront', paymentMethod: 'upi-qr', upiReference: 'UPI123'
    }));
  });

  it('allows handover only when the parcel is packed and paid', () => {
    expect(component.canHandOver(order({ status: 'preparing', paymentStatus: 'paid' }))).toBeFalse();
    expect(component.canHandOver(order({ status: 'ready', paymentStatus: 'pending' }))).toBeFalse();
    expect(component.canHandOver(order({ status: 'ready', paymentStatus: 'paid' }))).toBeTrue();

    component.handOver(order({ paymentStatus: 'pending' }));
    expect(takeaway.handOver).not.toHaveBeenCalled();
    component.handOver(order({ paymentStatus: 'paid' }));
    expect(takeaway.handOver).toHaveBeenCalledWith('o1');
  });

  it('allows cancelling only unpaid orders the kitchen has not started', () => {
    expect(component.canCancel(order({ status: 'pending' }))).toBeTrue();
    expect(component.canCancel(order({ status: 'preparing' }))).toBeFalse();
    expect(component.canCancel(order({ status: 'confirmed', paymentStatus: 'paid' }))).toBeFalse();
  });

  it('reports the number of packed parcels waiting for handover', () => {
    const counts: number[] = [];
    component.readyCountChange.subscribe(count => counts.push(count));
    takeaway.getActiveOrders.and.returnValue(of({
      orders: [order(), order({ orderId: 'o2', status: 'preparing' })]
    }));
    component.ngOnInit();
    expect(counts.at(-1)).toBe(1);
  });

  const printedText = async (callIndex = 0) => {
    const build = printer.autoPrint.calls.argsFor(callIndex)[1] as () => ReceiptLine[];
    return (await build()).map(line => line.text).join('\n');
  };

  it('auto-prints the bill when an upfront takeaway is paid, but not for pay-at-handover orders', async () => {
    component.addToCart(tea);
    component.customerName = 'Asha';
    component.submitOrder();
    expect(printer.autoPrint).not.toHaveBeenCalled();

    takeaway.createOrder.and.returnValue(of({ message: 'Sent', order: order({ status: 'pending', paymentStatus: 'paid' }) }));
    component.addToCart(tea);
    component.customerName = 'Asha';
    component.paymentTiming = 'upfront';
    component.submitOrder();
    expect(printer.autoPrint).toHaveBeenCalledOnceWith('takeawayPaid', jasmine.any(Function));
    const text = await printedText();
    expect(text).toContain('TOKEN: K-007');
    expect(text).toContain('Salt Lake');
  });

  it('auto-prints after collecting payment and after handover', () => {
    takeaway.recordPayment.and.returnValue(of({ message: 'Paid', order: order({ paymentStatus: 'paid' }) }));
    component.collectMethod = 'cash_at_counter';
    component.confirmPayment(order());
    expect(printer.autoPrint).toHaveBeenCalledWith('takeawayPaid', jasmine.any(Function));

    component.handOver(order({ paymentStatus: 'paid' }));
    expect(printer.autoPrint).toHaveBeenCalledWith('takeawayHandover', jasmine.any(Function));
  });

  it('prints a bill on demand and reports printer errors', async () => {
    await component.printBill(order());
    expect(printer.print).toHaveBeenCalledTimes(1);
    expect(component.message).toContain('K-007');

    printer.print.and.rejectWith(new Error('Printer offline'));
    await component.printBill(order());
    expect(component.errorMessage).toBe('Printer offline');
  });
});
