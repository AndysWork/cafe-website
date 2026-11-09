import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { of } from 'rxjs';
import { TableReservationComponent } from './table-reservation.component';
import { TableReservationService } from '../../services/table-reservation.service';
import { AuthService } from '../../services/auth.service';
import { OutletService } from '../../services/outlet.service';
import { DineInService } from '../../services/dine-in.service';
import { UIStore } from '../../store/ui.store';

describe('public reservation booking', () => {
  const outlet = { id: 'cafe', isActive: true, settings: { acceptsDineIn: true } };
  let auth: jasmine.SpyObj<AuthService>;
  let http: HttpTestingController;
  let router: jasmine.Spy;

  beforeEach(() => {
    auth = jasmine.createSpyObj<AuthService>('auth', ['isLoggedIn', 'getCurrentUser']);
    auth.isLoggedIn.and.returnValue(false);
    auth.getCurrentUser.and.returnValue(null);
    TestBed.configureTestingModule({
      imports: [TableReservationComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        { provide: AuthService, useValue: auth },
        { provide: OutletService, useValue: {
          getPublicOutlets: () => of([outlet]), getSelectedOutlet: () => outlet,
          getSelectedOutletId: () => 'cafe'
        } },
        { provide: DineInService, useValue: {} },
        { provide: UIStore, useValue: jasmine.createSpyObj('ui', ['notify', 'error', 'warning', 'success']) }
      ]
    });
    http = TestBed.inject(HttpTestingController);
    router = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
  });

  afterEach(() => http.verify());

  it('shows the public form without loading private reservations', () => {
    const fixture = TestBed.createComponent(TableReservationComponent);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('form')).not.toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Sign in to Reserve');
    expect(fixture.nativeElement.querySelectorAll('.tabs .tab').length).toBe(1);
    http.expectNone(request => request.url.includes('/reservations'));
  });

  it('blocks guest submissions and keeps the draft across sign-in navigation', () => {
    const fixture = TestBed.createComponent(TableReservationComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;
    component.form.customerName = 'Guest';
    component.form.customerPhone = '1234567890';
    component.form.timeSlot = '12:00 PM';
    component.submitReservation();
    expect(router).toHaveBeenCalledWith(['/login'], { queryParams: { returnUrl: '/reservations' } });
    http.expectNone(request => request.method === 'POST');
    auth.isLoggedIn.and.returnValue(true);
    auth.getCurrentUser.and.returnValue({ username: 'customer', email: 'customer@example.com', role: 'user' });
    const returned = TestBed.createComponent(TableReservationComponent);
    returned.detectChanges();
    expect(returned.componentInstance.form.customerName).toBe('Guest');
    expect(returned.componentInstance.form.timeSlot).toBe('12:00 PM');
    expect(TestBed.inject(TableReservationService).takeBookingDraft()).toBeNull();
  });

  it('does not request private reservations for guests', () => {
    const fixture = TestBed.createComponent(TableReservationComponent);
    fixture.componentInstance.switchTab('my');
    expect(fixture.componentInstance.activeTab).toBe('book');
    http.expectNone(request => request.url.includes('/reservations/my'));
    expect(router).toHaveBeenCalled();
  });

  it('submits valid bookings only after sign-in and prevents duplicate requests', () => {
    auth.isLoggedIn.and.returnValue(true);
    const fixture = TestBed.createComponent(TableReservationComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;
    Object.assign(component.form, { customerName: 'Customer', customerPhone: '1234567890', timeSlot: '12:00 PM' });
    component.submitReservation();
    component.submitReservation();
    const request = http.expectOne(req => req.method === 'POST' && req.url.endsWith('/reservations'));
    expect(request.request.body.outletId).toBe('cafe');
    request.flush({ ...component.form, id: 'booking', status: 'pending' });
    expect(component.submitted).toBeTrue();
    expect(component.submitting).toBeFalse();
  });

  it('reports booking failures without displaying success', () => {
    auth.isLoggedIn.and.returnValue(true);
    const fixture = TestBed.createComponent(TableReservationComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;
    Object.assign(component.form, { customerName: 'Customer', customerPhone: '1234567890', timeSlot: '12:00 PM' });
    component.submitReservation();
    http.expectOne(req => req.method === 'POST').flush({}, { status: 500, statusText: 'Error' });
    expect(component.submitted).toBeFalse();
    expect(component.submitting).toBeFalse();
    expect(TestBed.inject(UIStore).error).toHaveBeenCalledWith('Failed to book reservation');
  });

  it('uses the same booking form in compact popups without management tabs', () => {
    const fixture = TestBed.createComponent(TableReservationComponent);
    fixture.componentRef.setInput('compact', true);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.compact-reservation form')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.tabs')).toBeNull();
    expect(fixture.nativeElement.querySelector('.page-hero')).toBeNull();
    expect(fixture.nativeElement.querySelector('.aside-photo')).toBeNull();
    expect(fixture.nativeElement.querySelector('.booking-tips')).not.toBeNull();
  });

  it('renders the redesigned booking controls with all existing slots and accessible selection', () => {
    auth.isLoggedIn.and.returnValue(true);
    const fixture = TestBed.createComponent(TableReservationComponent);
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;
    const component = fixture.componentInstance;
    expect(root.querySelector('.hero-photo')?.getAttribute('src')).toBe('/images/cafe/cafe-01.jpg');
    expect(root.querySelector('.aside-photo')).not.toBeNull();
    expect(root.querySelectorAll('.time-chip').length).toBe(component.timeSlots.length);
    const slots = root.querySelectorAll<HTMLButtonElement>('.time-chip');
    slots[1].click();
    fixture.detectChanges();
    expect(component.form.timeSlot).toBe('11:30 AM');
    expect(slots[1].getAttribute('aria-pressed')).toBe('true');
    expect(slots[0].getAttribute('aria-pressed')).toBe('false');
    expect(root.querySelector('label[for="reservation-name"]')).not.toBeNull();
    expect(root.querySelector('#reservation-name')).not.toBeNull();
    expect(root.querySelector('.btn-submit')?.textContent).toContain('Confirm Reservation');
    http.expectNone(request => request.method === 'POST');
  });

  it('switches to live reservation management and restores the booking layout', () => {
    auth.isLoggedIn.and.returnValue(true);
    const fixture = TestBed.createComponent(TableReservationComponent);
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;
    root.querySelectorAll<HTMLButtonElement>('.tabs .tab')[1].click();
    fixture.detectChanges();
    expect(root.querySelectorAll('.tabs .tab')[1].getAttribute('aria-pressed')).toBe('true');
    expect(root.querySelector('.booking-aside')).toBeNull();
    const request = http.expectOne(req => req.method === 'GET' && req.url.endsWith('/reservations/my'));
    request.flush([]);
    fixture.detectChanges();
    expect(root.querySelector('.empty-state')?.textContent).toContain('No reservations yet');
    root.querySelectorAll<HTMLButtonElement>('.tabs .tab')[0].click();
    fixture.detectChanges();
    expect(root.querySelector('.reservation-shell.booking-view')).not.toBeNull();
    expect(root.querySelector('.booking-aside')).not.toBeNull();
  });

  it('keeps time selection and confirmation disabled while dine-in is unavailable', () => {
    auth.isLoggedIn.and.returnValue(true);
    const fixture = TestBed.createComponent(TableReservationComponent);
    spyOnProperty(fixture.componentInstance, 'isDineInCurrentlyAvailable', 'get').and.returnValue(false);
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;
    expect([...root.querySelectorAll<HTMLButtonElement>('.time-chip')].every(button => button.disabled)).toBeTrue();
    expect(root.querySelector<HTMLButtonElement>('.btn-submit')?.disabled).toBeTrue();
    expect(root.querySelector('.btn-submit')?.textContent).toContain('Shop Closed');
    fixture.componentInstance.submitReservation();
    http.expectNone(request => request.method === 'POST');
    expect(TestBed.inject(UIStore).warning).toHaveBeenCalled();
  });
});
