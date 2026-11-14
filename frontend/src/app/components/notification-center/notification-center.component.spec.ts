import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { NotificationCenterComponent } from './notification-center.component';
import { NotificationStore } from '../../store/notification.store';
import { AppNotification } from '../../services/notification-api.service';

describe('NotificationCenterComponent timestamps', () => {
  const notification: AppNotification = {
    id: 'notification', userId: 'staff', type: 'order_status',
    title: 'New phone order', message: 'Order received', isRead: false,
    createdAt: '2026-11-10T20:20:00Z'
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [NotificationCenterComponent],
      providers: [
        provideRouter([]),
        {
          provide: NotificationStore,
          useValue: {
            hasUnread: () => true,
            unreadCount: () => 1,
            displayBadge: () => '1',
            isOpen: () => true,
            isLoading: () => false,
            notifications: () => [notification],
            totalCount: () => 1
          }
        }
      ]
    }).compileComponents();
    spyOn(Date, 'now').and.returnValue(Date.parse('2026-11-10T14:56:00Z'));
  });

  it('renders the actual notification age in the bell dropdown', () => {
    const fixture = TestBed.createComponent(NotificationCenterComponent);
    fixture.detectChanges();

    const timestamp = fixture.nativeElement.querySelector('.notification-time').textContent;
    expect(timestamp).toContain('6m ago');
    expect(timestamp).toContain('10 Nov 2026, 08:20 pm IST');
  });

  it('renders marked UTC delivery notifications in IST without shifting their age', () => {
    const store = TestBed.inject(NotificationStore);
    spyOn(store, 'notifications').and.returnValue([{
      ...notification, createdAt: '2026-11-10T14:50:00Z', createdAtIsUtc: true
    }]);
    const fixture = TestBed.createComponent(NotificationCenterComponent);
    fixture.detectChanges();

    const timestamp = fixture.nativeElement.querySelector('.notification-time').textContent;
    expect(timestamp).toContain('6m ago');
    expect(timestamp).toContain('10 Nov 2026, 08:20 pm IST');
  });

  it('converts marked zero-offset dates across midnight and preserves explicit IST offsets', () => {
    const component = TestBed.createComponent(NotificationCenterComponent).componentInstance;
    expect(component.getNotificationTime('2026-11-10T18:45:00+00:00', true))
      .toBe('11 Nov 2026, 12:15 am IST');
    expect(component.getTimeAgo('2026-11-10T14:50:00+00:00', true)).toBe('6m ago');
    expect(component.getNotificationTime('2026-11-10T20:20:00+05:30', true))
      .toBe('10 Nov 2026, 08:20 pm IST');
    expect(component.getNotificationTime('invalid', true)).toBeNull();
  });

  it('interprets legacy Z, zero-offset and timezone-less values as IST wall-clock', () => {
    const component = TestBed.createComponent(NotificationCenterComponent).componentInstance;
    for (const timestamp of ['2026-11-10T20:20:00Z', '2026-11-10T20:20:00+00:00',
      '2026-11-10T20:20:00']) {
      expect(component.getTimeAgo(timestamp)).toBe('6m ago');
    }
  });

  it('does not shift timestamps that already include an IST offset', () => {
    const component = TestBed.createComponent(NotificationCenterComponent).componentInstance;
    expect(component.getTimeAgo('2026-11-10T20:20:00+05:30')).toBe('6m ago');
  });

  it('retains just-now, older, future and unavailable labels', () => {
    const component = TestBed.createComponent(NotificationCenterComponent).componentInstance;
    expect(component.getTimeAgo('2026-11-10T20:26:00Z')).toBe('Just now');
    expect(component.getTimeAgo('2026-11-10T18:20:00Z')).toBe('2h ago');
    expect(component.getTimeAgo('2026-11-09T20:20:00Z')).toBe('1d ago');
    expect(component.getTimeAgo('2026-11-10T20:30:00Z')).toBe('in 4m');
    expect(component.getTimeAgo('')).toBe('Time unavailable');
    expect(component.getTimeAgo('invalid')).toBe('Time unavailable');
    expect(component.getTimeAgo('0001-01-01T00:00:00Z')).toBe('Time unavailable');
  });

  it('formats older notifications using the IST calendar date', () => {
    const component = TestBed.createComponent(NotificationCenterComponent).componentInstance;
    expect(component.getTimeAgo('2026-11-01T00:15:00Z')).toBe('1 Nov');
  });
});
