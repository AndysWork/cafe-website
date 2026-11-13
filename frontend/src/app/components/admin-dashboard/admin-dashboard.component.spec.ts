import { TestBed } from '@angular/core/testing';
import { HttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { OrderService } from '../../services/order.service';
import { MenuService } from '../../services/menu.service';
import { SalesService } from '../../services/sales.service';
import { ExpenseService } from '../../services/expense.service';
import { OutletService } from '../../services/outlet.service';
import { AuthStore } from '../../store/auth.store';
import { getIstDateString, getIstInputDate } from '../../utils/date-utils';
import { Order } from '../../services/order.service';
import { AdminDashboardComponent } from './admin-dashboard.component';

describe('AdminDashboardComponent overview', () => {
  it('updates the displayed sales and trend when the period and metric change', () => {
    TestBed.configureTestingModule({
      imports: [AdminDashboardComponent],
      providers: [
        provideRouter([]),
        { provide: OrderService, useValue: {} },
        { provide: MenuService, useValue: {} },
        { provide: SalesService, useValue: {} },
        { provide: ExpenseService, useValue: {} },
        { provide: HttpClient, useValue: {} },
        { provide: AuthStore, useValue: { userName: () => 'Admin' } },
        { provide: OutletService, useValue: { selectedOutlet$: of(null), getSelectedOutlet: () => null } }
      ]
    });
    const fixture = TestBed.createComponent(AdminDashboardComponent);
    const component = fixture.componentInstance;
    const today = getIstDateString();
    const tenDaysAgo = getIstInputDate(new Date(Date.parse(`${today}T00:00:00+05:30`) - 10 * 86400000));
    component.onlineSales = [{
      platform: 'Zomato', orderId: 'one', customerName: 'Alex', orderAt: tenDaysAgo,
      orderedItems: [{ quantity: 1, itemName: 'Tea' }], billSubTotal: 100,
      packagingCharges: 0, discountAmount: 0, payout: 100, platformDeduction: 0
    }];
    component.allOrders = [{
      id: 'one', userId: '', username: '', items: [], subtotal: 100, tax: 0, total: 100,
      status: 'delivered', paymentStatus: 'paid', paymentMethod: 'cod',
      createdAt: `${tenDaysAgo}T13:00:00+05:30`, createdAtIsUtc: true, updatedAt: ''
    } satisfies Order];
    component.expenses = [{
      id: 'expense', date: tenDaysAgo, expenseType: 'Rent', expenseSource: 'Offline', amount: 50,
      paymentMethod: 'Cash', recordedBy: '', createdAt: '', updatedAt: ''
    }];
    component.isLoading = false;
    component.refreshOverview();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.overview-stats').textContent).toContain('₹100');
    expect(fixture.nativeElement.querySelector('.overview-list-row').textContent).toContain('Tea');
    expect(fixture.nativeElement.querySelector('.hourly-panel').textContent).toContain('1 PM–2 PM');
    expect(fixture.nativeElement.querySelectorAll('.hour-data tbody tr').length).toBe(24);
    expect(fixture.nativeElement.querySelector('.expenses-panel').textContent).toContain('Rent');
    const period = fixture.nativeElement.querySelector('.period-control select') as HTMLSelectElement;
    period.value = '7';
    period.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(component.overview.current.orders).toBe(0);
    expect(fixture.nativeElement.querySelector('.overview-stats').textContent).toContain('₹0');
    expect(fixture.nativeElement.querySelector('.hourly-panel').textContent).toContain('No timestamped app orders');
    expect(fixture.nativeElement.querySelector('.expenses-panel').textContent).toContain('No recorded expenses');
    const ordersButton = Array.from(fixture.nativeElement.querySelectorAll('.trend-tabs button') as NodeListOf<HTMLButtonElement>)
      .find(button => button.textContent?.includes('Records'))!;
    ordersButton.click();
    fixture.detectChanges();
    expect(ordersButton.getAttribute('aria-pressed')).toBe('true');
    expect(fixture.nativeElement.querySelector('.overview-chart').getAttribute('aria-label')).toContain('orders');
    fixture.destroy();
  });
});
