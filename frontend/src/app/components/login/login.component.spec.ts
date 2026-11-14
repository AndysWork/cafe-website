import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of, Subject, throwError } from 'rxjs';
import { LoginComponent } from './login.component';
import { AuthService, LoginResponse } from '../../services/auth.service';
import { OutletService } from '../../services/outlet.service';
import { AnalyticsTrackingService } from '../../services/analytics-tracking.service';
import { AuthStore } from '../../store/auth.store';

describe('Login presentation', () => {
  let fixture: ComponentFixture<LoginComponent>;
  let component: LoginComponent;
  let auth: jasmine.SpyObj<AuthService>;
  let outlets: jasmine.SpyObj<OutletService>;
  let analytics: jasmine.SpyObj<AnalyticsTrackingService>;
  let roles: string[];
  const response: LoginResponse = { token: 'test-token', username: 'customer', email: 'customer@example.com', role: 'user' };
  const route = { snapshot: { queryParamMap: convertToParamMap({}) } };

  beforeEach(async () => {
    roles = ['user'];
    route.snapshot.queryParamMap = convertToParamMap({});
    auth = jasmine.createSpyObj<AuthService>('AuthService', ['login', 'getCurrentUser']);
    auth.login.and.returnValue(of(response));
    auth.getCurrentUser.and.returnValue({ username: 'customer', email: 'customer@example.com', role: 'user', assignedOutlets: ['cafe'] });
    outlets = jasmine.createSpyObj<OutletService>('OutletService', ['initializeOutlets']);
    outlets.initializeOutlets.and.returnValue(of([]));
    analytics = jasmine.createSpyObj<AnalyticsTrackingService>('AnalyticsTrackingService', ['trackLogin']);
    await TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [
        provideRouter([]),
        { provide: ActivatedRoute, useValue: route },
        { provide: AuthService, useValue: auth },
        { provide: OutletService, useValue: outlets },
        { provide: AnalyticsTrackingService, useValue: analytics },
        { provide: AuthStore, useValue: { roles: () => roles } }
      ]
    }).compileComponents();
    fixture = TestBed.createComponent(LoginComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('uses the supplied background and branded sign-in card with required accessible fields', () => {
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelector('.auth-background image')?.getAttribute('href')).toBe('/images/cafe/login-background.png');
    expect(root.querySelector('h1')?.textContent).toBe('Sign In');
    expect(root.querySelector('.form-header p')?.textContent).toContain('Maa Tara Cafe');
    expect(root.querySelector<HTMLInputElement>('#username')?.required).toBeTrue();
    expect(root.querySelector<HTMLInputElement>('#password')?.autocomplete).toBe('current-password');
    expect(root.querySelector('.auth-visual')).toBeNull();
    expect(root.querySelector('.back-home')?.getAttribute('href')).toBe('/');
    expect(root.querySelector('.back-home')?.getAttribute('aria-label')).toBe('Back to Home');
    expect(root.querySelectorAll('.back-home svg').length).toBe(2);
    expect(root.querySelector('.mobile-welcome image')?.getAttribute('clip-path')).toBe('url(#login-welcome-crop)');
    const crop = root.querySelector('#login-welcome-crop rect');
    expect(crop?.getAttribute('x')).toBe('90');
    expect(crop?.getAttribute('width')).toBe('490');
    expect(crop?.getAttribute('height')).toBe('310');
  });

  it('allows keyboard-accessible password visibility without submitting', () => {
    const root: HTMLElement = fixture.nativeElement;
    const toggle = root.querySelector<HTMLButtonElement>('.toggle-password')!;
    expect(toggle.tabIndex).toBe(0);
    expect(toggle.type).toBe('button');
    toggle.click();
    fixture.detectChanges();
    expect(root.querySelector<HTMLInputElement>('#password')?.type).toBe('text');
    expect(toggle.getAttribute('aria-label')).toBe('Hide password');
    expect(toggle.getAttribute('aria-pressed')).toBe('true');
    toggle.click();
    fixture.detectChanges();
    expect(root.querySelector<HTMLInputElement>('#password')?.type).toBe('password');
    expect(auth.login).not.toHaveBeenCalled();
  });

  it('preserves recovery links and return URL on account creation', () => {
    route.snapshot.queryParamMap = convertToParamMap({ returnUrl: '/reservations' });
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelector('.forgot-link')?.getAttribute('href')).toBe('/forgot-password');
    expect(root.querySelector('.switch-auth a')?.getAttribute('href')).toBe('/register?returnUrl=%2Freservations');
  });

  it('keeps entered credentials, loading feedback, outlet initialization and return navigation', () => {
    const pending = new Subject<LoginResponse>();
    auth.login.and.returnValue(pending);
    route.snapshot.queryParamMap = convertToParamMap({ returnUrl: '/orders' });
    const navigate = spyOn(TestBed.inject(Router), 'navigateByUrl').and.resolveTo(true);
    component.username = 'customer';
    component.password = 'test-password';
    component.onLogin();
    fixture.detectChanges();
    expect(auth.login).toHaveBeenCalledWith('customer', 'test-password');
    expect(fixture.nativeElement.querySelector('.btn-primary').disabled).toBeTrue();
    expect(fixture.nativeElement.textContent).toContain('Signing in...');
    pending.next(response);
    pending.complete();
    fixture.detectChanges();
    expect(outlets.initializeOutlets).toHaveBeenCalledWith(['cafe']);
    expect(analytics.trackLogin).toHaveBeenCalled();
    expect(navigate).toHaveBeenCalledWith('/orders');
    expect(component.isLoading).toBeFalse();
  });

  it('retains role-based navigation when no return URL is present', () => {
    roles = ['admin'];
    const navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    component.onLogin();
    expect(navigate).toHaveBeenCalledWith(['/admin/dashboard']);
  });

  for (const assignedRoles of [['delivery-partner'], ['partner'], ['cashier', 'waiter', 'delivery-partner'], ['cook', 'delivery-partner']]) {
    it(`lands ${assignedRoles.join('/')} on delivery after login`, () => {
      roles = assignedRoles;
      const navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
      component.onLogin();
      expect(navigate).toHaveBeenCalledWith(['/partner/delivery']);
    });
  }

  for (const managementRole of ['admin', 'manager']) {
    it(`preserves ${managementRole} landing when a delivery role is also assigned`, () => {
      roles = [managementRole, 'delivery-partner'];
      const navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
      component.onLogin();
      expect(navigate).toHaveBeenCalledWith([`/${managementRole}/dashboard`]);
    });
  }

  it('displays authentication errors and restores the submit button', fakeAsync(() => {
    auth.login.and.returnValue(throwError(() => ({ error: { error: 'Invalid credentials' } })));
    component.onLogin();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain('Invalid credentials');
    expect(fixture.nativeElement.querySelector('.btn-primary').disabled).toBeFalse();
    expect(analytics.trackLogin).not.toHaveBeenCalled();
    tick(3000);
  }));
});
