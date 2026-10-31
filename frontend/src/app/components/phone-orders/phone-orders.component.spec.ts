import { TestBed } from '@angular/core/testing';
import { Component } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { BehaviorSubject, of, Subject, throwError } from 'rxjs';
import { PhoneOrdersComponent } from './phone-orders.component';
import { PhoneDeliveryOrder, PhoneOrderQuote, PhoneOrderService } from '../../services/phone-order.service';
import { OrderService } from '../../services/order.service';
import { MenuItem, MenuService } from '../../services/menu.service';
import { OutletService } from '../../services/outlet.service';
import { DeliveryPartnerService } from '../../services/delivery-partner.service';
import { Outlet } from '../../models/outlet.model';
import { OutletSelectorComponent } from '../outlet-selector/outlet-selector.component';

@Component({ selector: 'app-outlet-selector', standalone: true, template: '' })
class StubOutletSelectorComponent {}

describe('PhoneOrdersComponent', () => {
  let component: PhoneOrdersComponent;
  let service: jasmine.SpyObj<PhoneOrderService>;
  let selectedOutlet: BehaviorSubject<Outlet>;
  let partnerService: jasmine.SpyObj<DeliveryPartnerService>;
  let ordersService: jasmine.SpyObj<OrderService>;
  const item: MenuItem = {
    id: 'item-1', name: 'Sandwich', categoryId: 'category-1', onlinePrice: 120, shopSellingPrice: 100,
    variants: [{ variantName: 'Large', price: 150 }], addOns: [{ name: 'Cheese', price: 20, isActive: true }]
  };
  const quote: PhoneOrderQuote = { subtotal: 170, tax: 0, platformCharge: 0, deliveryFee: 30, total: 200 };
  const outlet = { id: 'outlet-1', isActive: true, settings: { acceptsOnlineOrders: true } } as Outlet;

  beforeEach(() => {
    selectedOutlet = new BehaviorSubject(outlet);
    service = jasmine.createSpyObj('PhoneOrderService', ['getOrders', 'quote', 'create']);
    service.getOrders.and.returnValue(of({ orders: [] }));
    service.quote.and.returnValue(of(quote));
    partnerService = jasmine.createSpyObj('DeliveryPartnerService', ['getDeliveryPartners', 'assignDeliveryPartner']);
    partnerService.getDeliveryPartners.and.returnValue(of([]));
    ordersService = jasmine.createSpyObj('OrderService', ['updateOrderStatus']);
    ordersService.updateOrderStatus.and.returnValue(of({ message: 'Order status updated successfully', status: 'cancelled' }));
    TestBed.configureTestingModule({
      providers: [
        { provide: PhoneOrderService, useValue: service },
        { provide: OrderService, useValue: ordersService },
        { provide: MenuService, useValue: { getMenuItems: () => of([item]) } },
        { provide: DeliveryPartnerService, useValue: partnerService },
        { provide: OutletService, useValue: {
          selectedOutlet$: selectedOutlet.asObservable(),
          getSelectedOutlet: () => selectedOutlet.value,
          getSelectedOutletId: () => selectedOutlet.value.id
        } }
      ]
    });
    TestBed.overrideComponent(PhoneOrdersComponent, {
      remove: { imports: [OutletSelectorComponent] },
      add: { imports: [StubOutletSelectorComponent] }
    });
    component = TestBed.runInInjectionContext(() => new PhoneOrdersComponent());
    component.ngOnInit();
  });
  afterEach(() => component.ngOnDestroy());

  function fill(): void {
    component.customerName = 'Test Customer';
    component.phoneNumber = '9876543210';
    component.deliveryAddress = 'Station Road, near bank';
    component.deliveryLocationUrl = 'https://maps.app.goo.gl/location';
    component.selectedVariants[item.id] = 'Large';
    component.selectedAddOns[item.id] = ['Cheese'];
    component.addItem(item);
  }

  it('requires customer details, location and a quote before submission', () => {
    expect(component.canSubmit).toBeFalse();
    fill();
    expect(component.validDetails).toBeTrue();
    expect(component.canSubmit).toBeFalse();
    component.reviewTotal();
    expect(component.canSubmit).toBeTrue();
    component.paymentMethod = 'upi-qr';
    expect(component.canSubmit).toBeFalse();
    component.upiReference = 'UPI123';
    expect(component.canSubmit).toBeTrue();
  });

  it('rejects non-Google and unsafe location links', () => {
    fill();
    for (const url of ['http://maps.google.com', 'javascript:alert(1)', 'https://google.com.evil.test/maps', 'https://www.google.com/search?q=x', 'https://user@maps.google.com']) {
      component.deliveryLocationUrl = url;
      expect(component.validMapsUrl).withContext(url).toBeFalse();
    }
  });

  it('requires real phone digits rather than punctuation alone', () => {
    fill();
    component.phoneNumber = '----------';
    expect(component.validDetails).toBeFalse();
  });

  it('accepts formatted Indian numbers and normalizes them for requests', () => {
    fill();
    component.phoneNumber = '+91 98765-43210';
    expect(component.validDetails).toBeTrue();
    component.reviewTotal();
    service.create.and.returnValue(of({ order: { id: 'order-1', trackingPath: '/track-order/order-1?token=secret' } as PhoneDeliveryOrder, message: 'Sent to kitchen' }));
    component.submit();
    const request = service.create.calls.mostRecent().args[0];
    expect(request.phoneNumber).toBe('9876543210');
  });

  it('requires both manual override values when one is entered', () => {
    fill();
    component.manualApproximateDistanceKm = '3.0';
    expect(component.manualOverrideValid).toBeFalse();
    component.reviewTotal();
    expect(service.quote).not.toHaveBeenCalled();
    expect(component.error).toContain('manual override');
  });

  it('prices variants and add-ons, and invalidates totals after cart changes', () => {
    fill();
    expect(component.subtotal).toBe(170);
    component.reviewTotal();
    component.changeQuantity(component.cart[0], 1);
    expect(component.subtotal).toBe(340);
    expect(component.quote).toBeNull();
    expect(component.canSubmit).toBeFalse();
  });

  it('cancels an obsolete quote before accepting changes', () => {
    fill();
    const pendingQuote = new Subject<PhoneOrderQuote>();
    service.quote.and.returnValue(pendingQuote);
    component.reviewTotal();
    component.invalidateQuote();
    pendingQuote.next(quote);
    expect(component.quote).toBeNull();
    expect(component.quoteBusy).toBeFalse();
  });

  it('sends item selections and the reviewed total, records prepaid UPI and exposes tracking', () => {
    fill();
    component.manualApproximateDistanceKm = '3.1';
    component.manualDeliveryFee = '55';
    component.reviewTotal();
    component.paymentMethod = 'upi-qr';
    component.upiReference = 'UPI123';
    const order = { id: 'order-1', trackingPath: '/track-order/order-1?token=secret' } as PhoneDeliveryOrder;
    service.create.and.returnValue(of({ order, message: 'Sent to kitchen' }));
    component.submit();
    const request = service.create.calls.mostRecent().args[0];
    expect(request.expectedTotal).toBe(200);
    expect(request.manualApproximateDistanceKm).toBe(3.1);
    expect(request.manualDeliveryFee).toBe(55);
    expect(request.paymentMethod).toBe('upi-qr');
    expect(request.upiReference).toBe('UPI123');
    expect(request.items[0]).toEqual({
      menuItemId: item.id, quantity: 1, selectedVariantName: 'Large', selectedAddOnNames: ['Cheese']
    });
    expect(component.hasDraft).toBeFalse();
    expect(component.lastCreated).toEqual(order);
    expect(component.trackingUrl(order)).toContain('/track-order/order-1?token=secret');
  });

  it('reuses an idempotency key after an ambiguous network failure', () => {
    fill();
    component.reviewTotal();
    service.create.and.returnValue(throwError(() => new HttpErrorResponse({ status: 0 })));
    component.submit();
    component.submit();
    expect(service.create.calls.count()).toBe(2);
    expect(service.create.calls.argsFor(0)[1]).toEqual(service.create.calls.argsFor(1)[1]);
    expect(component.hasDraft).toBeTrue();
  });

  it('preserves the draft but prevents submitting it at a different outlet', () => {
    fill();
    component.reviewTotal();
    selectedOutlet.next({ ...outlet, id: 'outlet-2' });
    expect(component.cart.length).toBe(1);
    expect(component.canSubmit).toBeFalse();
    expect(component.error).toContain('Outlet changed');
    component.clearDraft();
    expect(component.loadedOutletId).toBe('outlet-2');
  });

  it('assigns through the existing shop delivery workflow', () => {
    partnerService.assignDeliveryPartner.and.returnValue(of({ message: 'Assigned' }));
    component.assignments['order-1'] = 'partner-1';
    component.assign({ id: 'order-1' } as PhoneDeliveryOrder);
    expect(partnerService.assignDeliveryPartner).toHaveBeenCalledWith({
      orderId: 'order-1', deliveryPartnerId: 'partner-1'
    }, 'shop');
  });

  it('cancels an active phone order through order status update', () => {
    spyOn(window, 'confirm').and.returnValue(true);
    component.cancelOrder({ id: 'order-1', status: 'pending' } as PhoneDeliveryOrder);
    expect(ordersService.updateOrderStatus).toHaveBeenCalledWith('order-1', 'cancelled', 'shop');
  });

  it('warns before unloading an unfinished order', () => {
    fill();
    const event = new Event('beforeunload', { cancelable: true }) as BeforeUnloadEvent;
    component.onBeforeUnload(event);
    expect(event.defaultPrevented).toBeTrue();
  });

  it('renders the entry form, menu selection and reviewed delivery total', () => {
    const fixture = TestBed.createComponent(PhoneOrdersComponent);
    const rendered = fixture.componentInstance;
    fixture.detectChanges();
    const element: HTMLElement = fixture.nativeElement;
    expect(element.textContent).toContain('Phone Delivery Orders');
    expect(element.textContent).toContain('Sandwich');
    rendered.customerName = 'Test Customer';
    rendered.phoneNumber = '9876543210';
    rendered.deliveryAddress = 'Station Road beside the bank';
    rendered.deliveryLocationUrl = 'https://maps.app.goo.gl/location';
    rendered.addItem(item);
    rendered.reviewTotal();
    fixture.detectChanges();
    expect(element.querySelector('.totals')?.textContent).toContain('200.00');
    expect(element.querySelector<HTMLButtonElement>('button[type="submit"]')?.disabled).toBeFalse();
    fixture.destroy();
  });

  it('shows saved manual delivery override details in tracking cards', () => {
    service.getOrders.and.returnValue(of({
      orders: [{
        id: 'order-1',
        userId: 'guest-id',
        username: 'Guest',
        orderSource: 'phone',
        trackingPath: '/track-order/order-1?token=abc',
        customerName: 'Guest Customer',
        status: 'pending',
        items: [{ menuItemId: item.id, name: item.name, quantity: 1, price: 100, total: 100 }],
        subtotal: 100,
        tax: 0,
        total: 155,
        deliveryFee: 55,
        paymentMethod: 'cod',
        paymentStatus: 'pending',
        phoneNumber: '9876543210',
        deliveryAddress: 'Station Road',
        deliveryLocationUrl: 'https://maps.app.goo.gl/location',
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
        manualDeliveryFeeOverride: 55,
        manualDeliveryDistanceKmOverride: 3.1
      } as PhoneDeliveryOrder]
    }));
    const fixture = TestBed.createComponent(PhoneOrdersComponent);
    fixture.detectChanges();
    const element: HTMLElement = fixture.nativeElement;
    expect(element.textContent).toContain('Manual override saved:');
    expect(element.textContent).toContain('3.1 km');
    fixture.destroy();
  });
});
