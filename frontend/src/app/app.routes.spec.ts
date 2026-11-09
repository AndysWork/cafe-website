import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from './app.routes';
import { authGuard } from './guards/auth.guard';
import { AuthStore } from './store/auth.store';

@Component({ standalone: true, template: '' })
class RouteStubComponent {}

describe('customer route access', () => {
  it('keeps menu, cart and reservations public and requires sign-in for checkout', () => {
    for (const path of ['menu', 'cart', 'reservations']) {
      expect(routes.find(route => route.path === path)?.canActivate).toBeUndefined();
    }
    for (const path of ['checkout']) {
      expect(routes.find(route => route.path === path)?.canActivate).toContain(authGuard);
    }
  });

  it('lets guests browse but redirects protected routes back to login with a return URL', async () => {
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthStore, useValue: { isLoggedIn: () => false } },
        provideRouter([
          { path: 'menu', component: RouteStubComponent },
          { path: 'cart', component: RouteStubComponent },
          { path: 'checkout', component: RouteStubComponent, canActivate: [authGuard] },
          { path: 'reservations', component: RouteStubComponent },
          { path: 'login', component: RouteStubComponent }
        ])
      ]
    });
    const harness = await RouterTestingHarness.create();
    const router = TestBed.inject(Router);
    await harness.navigateByUrl('/menu?search=pizza');
    expect(router.url).toBe('/menu?search=pizza');
    await harness.navigateByUrl('/cart');
    expect(router.url).toBe('/cart');
    await harness.navigateByUrl('/checkout');
    expect(router.parseUrl(router.url).queryParams['returnUrl']).toBe('/checkout');
    expect(router.parseUrl(router.url).root.children['primary'].segments[0].path).toBe('login');
    await harness.navigateByUrl('/reservations');
    expect(router.url).toBe('/reservations');
  });
});
