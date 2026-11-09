import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { OrdersComponent } from './orders.component';
import { Order, OrderService } from '../../services/order.service';
import { AuthService } from '../../services/auth.service';
import { CartService } from '../../services/cart.service';
import { MenuItem, MenuService } from '../../services/menu.service';
import { DineInService } from '../../services/dine-in.service';
import { TableReservationService } from '../../services/table-reservation.service';
import { UIStore } from '../../store/ui.store';

describe('My Orders presentation', () => {
  let fixture: ComponentFixture<OrdersComponent>;
  let component: OrdersComponent;
  let service: jasmine.SpyObj<OrderService>;
  let cart: jasmine.SpyObj<CartService>;
  let ui: jasmine.SpyObj<UIStore>;
  let auth: jasmine.SpyObj<AuthService>;
  const menu: MenuItem = {
    id: 'pizza', name: 'Cafe Pizza', categoryId: 'pizza', onlinePrice: 120,
    imageThumbnailUrl: '/pizza-thumb.jpg', imageUrl: '/pizza.jpg', packagingCharge: 5
  };
  const delivered: Order = {
    id: 'order-123456', userId: 'customer', username: 'customer',
    items: [{ menuItemId: 'pizza', name: 'Cafe Pizza', quantity: 2, price: 120, total: 240 }],
    subtotal: 240, tax: 0, total: 240, status: 'delivered', paymentStatus: 'paid',
    paymentMethod: 'upi-qr', orderType: 'delivery',
    createdAt: '2026-11-02T10:00:00Z', updatedAt: '2026-11-02T10:00:00Z'
  };
  const inFlight: Order = { ...delivered, id: 'order-654321', status: 'out-for-delivery' };
  const dineIn: Order = { ...delivered, id: 'order-777777', orderType: 'dine-in', tableNumber: '4', dineInSessionId: 'session' };
  const cancelled: Order = { ...delivered, id: 'order-999999', status: 'cancelled', orderType: 'pickup' };

  beforeEach(async () => {
    service = jasmine.createSpyObj<OrderService>('OrderService', [
      'getMyOrders', 'getAllOrders', 'getStatusDisplayText', 'canCancelOrder'
    ]);
    service.getMyOrders.and.returnValue(of([delivered, inFlight, dineIn, cancelled]));
    service.getAllOrders.and.returnValue(of([delivered]));
    service.getStatusDisplayText.and.callFake(status => status);
    service.canCancelOrder.and.returnValue(false);
    auth = jasmine.createSpyObj<AuthService>('AuthService', ['isAdmin']);
    auth.isAdmin.and.returnValue(false);
    cart = jasmine.createSpyObj<CartService>('CartService', ['addItem']);
    ui = jasmine.createSpyObj<UIStore>('UIStore', ['success', 'error']);
    await TestBed.configureTestingModule({
      imports: [OrdersComponent],
      providers: [
        provideRouter([]),
        { provide: OrderService, useValue: service },
        { provide: AuthService, useValue: auth },
        { provide: CartService, useValue: cart },
        { provide: MenuService, useValue: { getMenuItems: () => of([menu]) } },
        { provide: DineInService, useValue: {} },
        { provide: TableReservationService, useValue: {
          getMyReservations: () => of([{
            id: 'booking-123', customerName: 'Customer', customerPhone: '1234567890',
            partySize: 2, reservationDate: '2026-11-10', timeSlot: '12:00 PM',
            tableNumber: '4', status: 'confirmed'
          }])
        } },
        { provide: UIStore, useValue: ui }
      ]
    }).compileComponents();
    fixture = TestBed.createComponent(OrdersComponent);
    component = fixture.componentInstance;
  });

  async function render(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('renders live quantities, totals, menu photos and real order links', async () => {
    await render();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelectorAll('.customer-order-summary').length).toBe(4);
    expect(root.querySelector('.customer-order-title')?.textContent).toBe('Order #123456');
    expect(root.querySelector('.customer-order-title')?.getAttribute('href')).toBe('/orders/order-123456');
    expect(root.querySelector('.customer-order-meta')?.textContent).toContain('2 Items');
    expect(root.querySelector('.customer-order-total')?.textContent).toBe('₹240');
    expect(root.querySelector('.order-thumbnail img')?.getAttribute('src')).toBe('/pizza-thumb.jpg');
    expect(root.querySelector('.order-preview-items')?.textContent).toContain('Cafe Pizza (2)');
    expect(root.querySelectorAll('.summary-reorder').length).toBe(2);
    expect(root.querySelectorAll('.summary-details')[1].textContent).toBe('Track Order');
  });

  it('combines case-insensitive ID/item searches with fulfillment and status filters', async () => {
    await render();
    component.searchQuery = '  CAFE PIZZA ';
    expect(component.filteredOrders.length).toBe(4);
    component.setOrderTypeFilter('delivery');
    component.activeFilter = 'active';
    expect(component.filteredOrders.map(order => order.id)).toEqual([inFlight.id]);
    component.searchQuery = '123456';
    expect(component.filteredOrders.length).toBe(0);
    component.activeFilter = 'all';
    expect(component.filteredOrders.map(order => order.id)).toEqual([delivered.id]);
    expect(component.getOrderTypeCount('all')).toBe(4);
  });

  it('binds search input and clears every filter from the no-match state', async () => {
    await render();
    const root: HTMLElement = fixture.nativeElement;
    const search = root.querySelector<HTMLInputElement>('.order-search input')!;
    search.value = 'missing order';
    search.dispatchEvent(new Event('input'));
    await render();
    expect(component.filteredOrders.length).toBe(0);
    expect(root.querySelector('.empty-state.small')).not.toBeNull();
    root.querySelector<HTMLButtonElement>('.empty-state.small .btn-link')!.click();
    await render();
    expect(component.searchQuery).toBe('');
    expect(root.querySelectorAll('.customer-order-summary').length).toBe(4);
  });

  it('expands existing order details accessibly without navigating or reordering', async () => {
    await render();
    const root: HTMLElement = fixture.nativeElement;
    const expand = root.querySelector<HTMLButtonElement>('.order-expand')!;
    expand.click();
    fixture.detectChanges();
    expect(expand.getAttribute('aria-expanded')).toBe('true');
    expect(root.querySelector('#order-details-order-123456 .items-section')?.textContent).toContain('Cafe Pizza');
    expect(root.querySelector('#order-details-order-123456 .payment-badge')).not.toBeNull();
    expect(cart.addItem).not.toHaveBeenCalled();
    expand.click();
    fixture.detectChanges();
    expect(root.querySelector('#order-details-order-123456')).toBeNull();
  });

  it('shows delivery progress only for active deliveries and identifies the current step', async () => {
    await render();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelectorAll('.delivery-progress').length).toBe(1);
    expect(root.querySelector('.delivery-progress [aria-current="step"]')?.textContent).toContain('Out for Delivery');
    expect(root.querySelectorAll('.delivery-progress .complete').length).toBe(2);
    expect(component.getDeliveryStep({ ...delivered, status: 'preparing' })).toBe(1);
    expect(component.getDeliveryStep({ ...delivered, status: 'ready' })).toBe(1);
    expect(component.getDeliveryStep({ ...delivered, status: 'pending' })).toBe(0);
    expect(component.getDeliveryStep(delivered)).toBe(3);
  });

  it('retains existing reorder quantities, packaging and cart navigation', async () => {
    const navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    await render();
    fixture.nativeElement.querySelector('.summary-reorder').click();
    expect(cart.addItem).toHaveBeenCalledWith(jasmine.objectContaining({
      menuItemId: 'pizza', price: 120, packagingCharge: 5, imageThumbnailUrl: '/pizza-thumb.jpg'
    }), 2);
    expect(navigate).toHaveBeenCalledWith(['/cart']);
    expect(ui.success).toHaveBeenCalled();
  });

  it('preserves booking filters and searches without showing an unrelated empty-orders message', async () => {
    service.getMyOrders.and.returnValue(of([]));
    await render();
    component.setOrderTypeFilter('reservation');
    component.searchQuery = 'CUSTOMER';
    fixture.detectChanges();
    expect(component.filteredReservations.length).toBe(1);
    expect(fixture.nativeElement.textContent).not.toContain("You haven't placed any orders yet");
    component.searchQuery = 'booking-123';
    expect(component.filteredReservations.length).toBe(1);
    component.searchQuery = 'no match';
    expect(component.filteredReservations.length).toBe(0);
  });

  it('uses a non-photo fallback when menu imagery is unavailable', async () => {
    await render();
    component.orders = [{ ...delivered, items: [{ ...delivered.items[0], menuItemId: 'unavailable' }] }];
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.order-thumbnail img')).toBeNull();
    expect(fixture.nativeElement.querySelector('.order-thumbnail span')).not.toBeNull();
  });

  it('retains the admin header, status controls and all-orders endpoint', async () => {
    fixture.destroy();
    auth.isAdmin.and.returnValue(true);
    fixture = TestBed.createComponent(OrdersComponent);
    component = fixture.componentInstance;
    await render();
    const root: HTMLElement = fixture.nativeElement;
    expect(service.getAllOrders).toHaveBeenCalled();
    expect(root.querySelector('.customer-orders')).toBeNull();
    expect(root.querySelector('.customer-order-summary')).toBeNull();
    expect(root.querySelector('.card-header')).not.toBeNull();
    component.toggleExpand(delivered.id);
    fixture.detectChanges();
    expect(root.querySelector('.admin-controls')).not.toBeNull();
  });

  it('keeps explicit order-load errors and retry behavior', async () => {
    service.getMyOrders.and.returnValue(throwError(() => ({ error: { error: 'Unable to load orders' } })));
    spyOn(console, 'error');
    await render();
    expect(fixture.nativeElement.querySelector('.error-state')?.textContent).toContain('Unable to load orders');
    service.getMyOrders.and.returnValue(of([delivered]));
    fixture.nativeElement.querySelector('.btn-retry').click();
    await render();
    expect(fixture.nativeElement.querySelector('.error-state')).toBeNull();
    expect(fixture.nativeElement.querySelector('.customer-order-summary')).not.toBeNull();
  });
});
