import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../environments/environment';

export interface UserMetrics {
  totalRegisteredUsers: number;
  currentlyActiveUsers: number;
  todayLogins: number;
  weekLogins: number;
  monthLogins: number;
  totalLoginEvents: number;
  uniqueUsersToday: number;
  uniqueUsersThisWeek: number;
  uniqueUsersThisMonth: number;
}

export interface FeatureUsageStat {
  featureName: string;
  usageCount: number;
  uniqueUsers: number;
}

export interface ApiPerformanceStat {
  endpoint: string;
  totalCalls: number;
  avgResponseTimeMs: number;
  maxResponseTimeMs: number;
  minResponseTimeMs: number;
  p95ResponseTimeMs: number;
  errorCount: number;
}

export interface CartAnalytics {
  totalCartViews: number;
  totalAddToCart: number;
  totalCartRemovals: number;
  uniqueUsersWhoCarted: number;
  uniqueUsersWhoBrowsed: number;
  topCartedItems: CartItemStat[];
}

export interface CartItemStat {
  itemName: string;
  addCount: number;
}

export interface DailyActiveUserStat {
  date: string;
  activeUsers: number;
  loginCount: number;
}

export interface HourlyActivityStat {
  hour: number;
  eventCount: number;
}

export interface RecentSessionInfo {
  username: string;
  userRole: string;
  loginTime: string;
  logoutTime: string | null;
  lastActiveTime: string;
  isActive: boolean;
}

export interface AnalyticsDashboard {
  userMetrics: UserMetrics;
  topFeatures: FeatureUsageStat[];
  apiPerformance: ApiPerformanceStat[];
  cartAnalytics: CartAnalytics;
  dailyActiveUsers: DailyActiveUserStat[];
  hourlyActivity: HourlyActivityStat[];
  recentSessions: RecentSessionInfo[];
}

@Injectable({
  providedIn: 'root'
})
export class UserAnalyticsService {
  private http = inject(HttpClient);
  private apiUrl = environment.apiUrl;

  getDashboard(): Observable<AnalyticsDashboard> {
    return this.http.get<{ success: boolean; data: AnalyticsDashboard }>(
      `${this.apiUrl}/analytics/dashboard`
    ).pipe(map(res => res.data));
  }

  getSessions(): Observable<RecentSessionInfo[]> {
    return this.http.get<{ success: boolean; data: RecentSessionInfo[] }>(
      `${this.apiUrl}/analytics/sessions`
    ).pipe(map(res => res.data));
  }

  initIndexes(): Observable<any> {
    return this.http.post(`${this.apiUrl}/analytics/init-indexes`, {});
  }
}
