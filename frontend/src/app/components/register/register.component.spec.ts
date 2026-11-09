import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { RegisterComponent } from './register.component';
import { AuthService } from '../../services/auth.service';

describe('Register presentation', () => {
  let fixture: ComponentFixture<RegisterComponent>;
  let component: RegisterComponent;
  let auth: jasmine.SpyObj<AuthService>;

  beforeEach(async () => {
    auth = jasmine.createSpyObj<AuthService>('AuthService', ['register']);
    await TestBed.configureTestingModule({
      imports: [RegisterComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: auth },
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap({ returnUrl: '/orders' }) } } }
      ]
    }).compileComponents();
    fixture = TestBed.createComponent(RegisterComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
  });

  function fillRequiredFields(): void {
    component.username = 'customer';
    component.email = 'customer@example.com';
    component.phoneNumber = '9876543210';
    component.password = 'Password1!';
    component.confirmPassword = component.password;
  }

  it('renders mobile artwork, all fields, and return-aware sign-in navigation', () => {
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelector('.mobile-register-header h1')?.textContent).toBe('Create Account');
    expect(root.querySelector('.mobile-register-food')).not.toBeNull();
    expect(root.querySelectorAll('input').length).toBe(8);
    expect(root.querySelector('.switch-auth a')?.getAttribute('href')).toBe('/login?returnUrl=%2Forders');
    expect(root.querySelector('.back-home')?.getAttribute('aria-label')).toBe('Back to Home');
  });

  it('makes both password visibility controls accessible without submitting', () => {
    const root: HTMLElement = fixture.nativeElement;
    const toggles = root.querySelectorAll<HTMLButtonElement>('.toggle-password');
    toggles.forEach(toggle => { expect(toggle.tabIndex).toBe(0); toggle.click(); });
    fixture.detectChanges();
    expect(root.querySelector<HTMLInputElement>('#password')?.type).toBe('text');
    expect(root.querySelector<HTMLInputElement>('#confirmPassword')?.type).toBe('text');
    expect(toggles[1].getAttribute('aria-label')).toBe('Hide confirm password');
    expect(auth.register).not.toHaveBeenCalled();
  });

  it('preserves mismatch validation and visible server errors', () => {
    fillRequiredFields();
    component.confirmPassword = 'different';
    component.onSubmit();
    expect(component.errorMessage).toBe('Passwords do not match');
    expect(auth.register).not.toHaveBeenCalled();
    component.confirmPassword = component.password;
    auth.register.and.returnValue(throwError(() => ({ error: { error: 'Username already exists' } })));
    component.onSubmit();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain('Username already exists');
    expect(component.isLoading).toBeFalse();
  });

  it('preserves registration success and the return URL on login redirect', fakeAsync(() => {
    fillRequiredFields();
    auth.register.and.returnValue(of({}));
    const navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    component.onSubmit();
    fixture.detectChanges();
    expect(auth.register).toHaveBeenCalledWith(jasmine.objectContaining({ username: 'customer', phoneNumber: '9876543210' }));
    expect(fixture.nativeElement.querySelector('.success-email').textContent).toContain(component.email);
    tick(4000);
    expect(navigate).toHaveBeenCalledWith(['/login'], { queryParams: { returnUrl: '/orders' } });
  }));
});
