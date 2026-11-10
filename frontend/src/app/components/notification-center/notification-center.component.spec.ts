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

    expect(fixture.nativeElement.querySelector('.notification-time').textContent).toBe('6m ago');
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
