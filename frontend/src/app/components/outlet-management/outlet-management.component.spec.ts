import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { of, Subject } from 'rxjs';
import { OutletManagementComponent } from './outlet-management.component';
import { OutletService } from '../../services/outlet.service';
import { Outlet } from '../../models/outlet.model';

describe('OutletManagementComponent ordering controls', () => {
  let component: OutletManagementComponent;
  let service: jasmine.SpyObj<OutletService>;
  let result: Subject<object>;
  let outlet: Outlet;

  beforeEach(() => {
    service = jasmine.createSpyObj<OutletService>('OutletService', ['updateOutlet', 'getAllOutlets']);
    result = new Subject<object>();
    service.updateOutlet.and.returnValue(result);
    TestBed.configureTestingModule({
      providers: [
        { provide: OutletService, useValue: service },
        { provide: Router, useValue: {} }
      ]
    });
    component = TestBed.runInInjectionContext(() => new OutletManagementComponent());
    outlet = {
      id: 'outlet-1', outletCode: 'MT01', outletName: 'Main',
      address: '', city: '', state: '', isActive: true,
      settings: {
        openingTime: '08:00', closingTime: '22:00', taxPercentage: 5,
        acceptsOnlineOrders: true, acceptsTakeaway: true,
        acceptsDineIn: true, acceptsCounterSales: true
      }
    };
  });

  it('changes only the selected channel after the API confirms persistence', () => {
    component.toggleOrdering(outlet, 'acceptsCounterSales', 'Counter sales');
    expect(service.updateOutlet).toHaveBeenCalledWith('outlet-1', {
      settings: { ...outlet.settings, acceptsCounterSales: false }
    });
    expect(outlet.settings.acceptsCounterSales).toBeTrue();
    result.next({});
    result.complete();
    expect(outlet.settings.acceptsCounterSales).toBeFalse();
    expect(outlet.settings.acceptsDineIn).toBeTrue();
    expect(component.togglingOrderingOutletId).toBeNull();
  });

  it('leaves the channel unchanged and shows API errors on failure', () => {
    component.toggleOrdering(outlet, 'acceptsDineIn', 'Dine-in');
    result.error({ error: { error: 'Admin access required' } });
    expect(outlet.settings.acceptsDineIn).toBeTrue();
    expect(component.errorMessage).toBe('Admin access required');
    expect(component.togglingOrderingOutletId).toBeNull();
  });

  it('blocks overlapping toggles while a save is pending', () => {
    component.toggleOrdering(outlet, 'acceptsTakeaway', 'Takeaway');
    component.toggleOrdering(outlet, 'acceptsDineIn', 'Dine-in');
    expect(service.updateOutlet).toHaveBeenCalledTimes(1);
  });

  it('editing and cancelling does not mutate live settings', () => {
    component.openEditModal(outlet);
    component.outletForm.settings!.acceptsDineIn = false;
    component.closeModal();
    expect(outlet.settings.acceptsDineIn).toBeTrue();
  });

  it('renders four independent switches and reflects a confirmed toggle', () => {
    service.getAllOutlets.and.returnValue(of([outlet]));
    const fixture = TestBed.createComponent(OutletManagementComponent);
    fixture.detectChanges();
    const element: HTMLElement = fixture.nativeElement;
    const buttons = element.querySelectorAll<HTMLButtonElement>('button[aria-pressed]');
    expect(buttons.length).toBe(4);
    expect(buttons[3].getAttribute('aria-label')).toContain('Counter sales');
    buttons[3].click();
    fixture.detectChanges();
    expect(Array.from(buttons).every(button => button.disabled)).toBeTrue();
    result.next({});
    result.complete();
    fixture.detectChanges();
    expect(buttons[3].getAttribute('aria-pressed')).toBe('false');
    expect(buttons[3].textContent).toContain('on');
    expect(buttons[0].getAttribute('aria-pressed')).toBe('true');
  });
});
