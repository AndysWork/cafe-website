import { TestBed } from '@angular/core/testing';
import { KitchenDisplayComponent } from './kitchen-display.component';
import { KitchenDisplayService } from '../../services/kitchen-display.service';
import { OutletService } from '../../services/outlet.service';
import { WebPushService } from '../../services/web-push.service';
import { UIStore } from '../../store/ui.store';

describe('Kitchen display elapsed time', () => {
  let component: KitchenDisplayComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [KitchenDisplayComponent,
        ...[KitchenDisplayService, OutletService, WebPushService, UIStore]
          .map(service => ({ provide: service, useValue: {} }))
      ]
    });
    component = TestBed.inject(KitchenDisplayComponent);
    spyOn(Date, 'now').and.returnValue(Date.parse('2026-11-09T12:06:00Z'));
  });

  it('uses the authoritative server wait time', () => {
    expect(component.getTimeSince('2026-11-09T06:30:00Z', 6)).toBe('6m ago');
  });

  it('handles explicit IST and UTC timestamps without an extra offset', () => {
    expect(component.getTimeSince('2026-11-09T17:30:00+05:30')).toBe('6m ago');
    expect(component.getTimeSince('2026-11-09T12:00:00Z')).toBe('6m ago');
  });

  it('interprets a timezone-less fallback as IST, not the device timezone', () => {
    expect(component.getTimeSince('2026-11-09T17:30:00')).toBe('6m ago');
  });

  it('preserves long waits and just-now labels', () => {
    expect(component.getTimeSince('2026-11-09T12:00:00Z', 336)).toBe('5h 36m ago');
    expect(component.getTimeSince('2026-11-09T12:06:00Z')).toBe('Just now');
    expect(component.getTimeSince('2026-11-09T12:07:00Z')).toBe('Just now');
  });
});
