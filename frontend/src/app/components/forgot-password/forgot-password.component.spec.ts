import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Subject, throwError } from 'rxjs';
import { ForgotPasswordComponent } from './forgot-password.component';
import { AuthService } from '../../services/auth.service';

describe('ForgotPasswordComponent', () => {
  let component: ForgotPasswordComponent;
  let fixture: ComponentFixture<ForgotPasswordComponent>;
  let auth: jasmine.SpyObj<AuthService>;

  beforeEach(async () => {
    auth = jasmine.createSpyObj<AuthService>('AuthService', ['forgotPassword']);
    await TestBed.configureTestingModule({
      imports: [ForgotPasswordComponent],
      providers: [provideRouter([]), { provide: AuthService, useValue: auth }]
    }).compileComponents();
    fixture = TestBed.createComponent(ForgotPasswordComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('renders the cafe artwork, accessible email field and login link', () => {
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelector('.recovery-logo')?.getAttribute('aria-label')).toBe('Maa Tara Cafe');
    expect(root.querySelector('.recovery-food')).not.toBeNull();
    expect(root.querySelector('label')?.getAttribute('for')).toBe('email');
    expect(root.querySelector<HTMLInputElement>('#email')?.autocomplete).toBe('email');
    expect(root.querySelector('.back-link a')?.getAttribute('href')).toBe('/login');
    expect(root.querySelector<HTMLButtonElement>('button[type="submit"]')?.disabled).toBeTrue();
  });

  it('preserves reset submission, loading feedback and success confirmation', () => {
    const pending = new Subject<void>();
    auth.forgotPassword.and.returnValue(pending);
    component.email = 'customer@example.com';
    component.onSubmit();
    fixture.detectChanges();
    expect(auth.forgotPassword).toHaveBeenCalledWith('customer@example.com');
    expect(fixture.nativeElement.querySelector('button[type="submit"]').disabled).toBeTrue();
    expect(fixture.nativeElement.textContent).toContain('Sending...');
    pending.next();
    pending.complete();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('form')).toBeNull();
    expect(fixture.nativeElement.querySelector('[role="status"]').textContent).toContain('Email Sent!');
    expect(component.isLoading).toBeFalse();
  });

  it('shows server errors and makes retry available', fakeAsync(() => {
    auth.forgotPassword.and.returnValue(throwError(() => ({ error: { error: 'Please try again later' } })));
    component.email = 'customer@example.com';
    component.onSubmit();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain('Please try again later');
    expect(fixture.nativeElement.querySelector('button[type="submit"]').disabled).toBeFalse();
    expect(component.emailSent).toBeFalse();
    tick(5000);
  }));
});
