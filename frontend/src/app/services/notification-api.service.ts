import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export interface AppNotification {
  id: string;
  userId: string;
  type: string; // order_status, loyalty_points, offer, system, stock_alert
  title: string;
  message: string;
  data?: Record<string, string>;
  actionUrl?: string;
  imageUrl?: string;
  isRead: boolean;
  createdAt: string;
}

export interface NotificationListResponse {
  notifications: AppNotification[];
  unreadCount: number;
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface UnreadCountResponse {
  unreadCount: number;
}

export interface NotificationPreferences {
  orderUpdates: boolean;
  loyaltyPoints: boolean;
  offers: boolean;
  systemNotifications: boolean;
  emailNotifications: boolean;
  pushNotifications: boolean;
}

@Injectable({ providedIn: 'root' })
export class NotificationApiService {
  private http = inject(HttpClient);
  private apiUrl = environment.apiUrl;

  getNotifications(page: number = 1, pageSize: number = 20): Observable<NotificationListResponse> {
    const params = new HttpParams()
      .set('page', page.toString())
      .set('pageSize', pageSize.toString());
    return this.http.get<NotificationListResponse>(`${this.apiUrl}/notifications`, { params });
  }

  getUnreadCount(): Observable<UnreadCountResponse> {
    return this.http.get<UnreadCountResponse>(`${this.apiUrl}/notifications/unread-count`);
  }

  markAsRead(notificationId: string): Observable<any> {
    return this.http.put(`${this.apiUrl}/notifications/${notificationId}/read`, {});
  }

  markAllAsRead(): Observable<any> {
    return this.http.put(`${this.apiUrl}/notifications/read-all`, {});
  }

  deleteNotification(notificationId: string): Observable<any> {
    return this.http.delete(`${this.apiUrl}/notifications/${notificationId}`);
  }

  deleteAllNotifications(): Observable<any> {
    return this.http.delete(`${this.apiUrl}/notifications/all`);
  }

  getPreferences(): Observable<NotificationPreferences> {
    return this.http.get<NotificationPreferences>(`${this.apiUrl}/notifications/preferences`);
  }

  updatePreferences(prefs: Partial<NotificationPreferences>): Observable<any> {
    return this.http.put(`${this.apiUrl}/notifications/preferences`, prefs);
  }
}
