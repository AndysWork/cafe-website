import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { CartStore } from '../store/cart.store';
import { AuthService } from './auth.service';

describe('guest login continuation', () => {
  beforeEach(() => {
    for (const key of ['authToken', 'currentUser', 'csrfToken', 'cart', 'active_dinein_table', 'active_dinein_table_qr_token']) {
      localStorage.removeItem(key);
    }
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    for (const key of ['authToken', 'currentUser', 'csrfToken', 'cart', 'active_dinein_table', 'active_dinein_table_qr_token']) {
      localStorage.removeItem(key);
    }
  });

  it('retains the saved guest cart and table across login and a reload', () => {
    const cart = TestBed.inject(CartStore);
    cart.addItem({ menuItemId: 'item', name: 'Tea', price: 25 });
    localStorage.setItem('active_dinein_table', '1');
    localStorage.setItem('active_dinein_table_qr_token', 'qr');

    TestBed.inject(AuthService).login('customer', 'password').subscribe();
    TestBed.inject(HttpTestingController).expectOne(`${environment.apiUrl}/auth/login`).flush({
      success: true,
      data: { token: 'token', username: 'customer', email: 'customer@example.com', role: 'user' }
    });

    expect(localStorage.getItem('active_dinein_table')).toBe('1');
    expect(localStorage.getItem('active_dinein_table_qr_token')).toBe('qr');
    const reloadedCart = TestBed.runInInjectionContext(() => new CartStore());
    expect(reloadedCart.itemCount()).toBe(1);
    expect(reloadedCart.items()[0].name).toBe('Tea');
  });
});
