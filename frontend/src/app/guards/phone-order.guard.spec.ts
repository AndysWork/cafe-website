import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot } from '@angular/router';
import { AuthStore } from '../store/auth.store';
import { phoneOrderDraftGuard, roleGuard } from './auth.guard';
import { PhoneOrdersComponent } from '../components/phone-orders/phone-orders.component';

describe('Phone order route guards', () => {
  const route = {} as ActivatedRouteSnapshot;
  const state = { url: '/phone-orders' } as RouterStateSnapshot;

  it('allows Admin or Manager roles and denies other staff roles', () => {
    const store = { isLoggedIn: () => true, hasAnyRole: jasmine.createSpy().and.returnValue(true) };
    TestBed.configureTestingModule({ providers: [
      { provide: AuthStore, useValue: store },
      { provide: Router, useValue: { navigate: jasmine.createSpy() } }
    ] });
    const guard = roleGuard(['admin', 'manager']);
    expect(TestBed.runInInjectionContext(() => guard(route, state))).toBeTrue();
    expect(store.hasAnyRole).toHaveBeenCalledWith(['admin', 'manager']);
    store.hasAnyRole.and.returnValue(false);
    expect(TestBed.runInInjectionContext(() => guard(route, state))).toBeFalse();
  });

  it('asks before discarding a draft and honours cancellation', () => {
    const confirm = spyOn(window, 'confirm').and.returnValue(false);
    const component = { hasDraft: true } as PhoneOrdersComponent;
    expect(phoneOrderDraftGuard(component, route, state, state)).toBeFalse();
    expect(confirm).toHaveBeenCalled();
    confirm.and.returnValue(true);
    expect(phoneOrderDraftGuard(component, route, state, state)).toBeTrue();
  });

  it('does not interrupt navigation without a draft', () => {
    const confirm = spyOn(window, 'confirm');
    expect(phoneOrderDraftGuard({ hasDraft: false } as PhoneOrdersComponent, route, state, state)).toBeTrue();
    expect(confirm).not.toHaveBeenCalled();
  });
});
