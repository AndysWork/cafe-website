import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export interface HomeContentConfig {
  id?: string;
  outletId?: string;
  announcementTitle?: string;
  announcementMessage?: string;
  announcementEnabled: boolean;
  featuredMenuItemIds: string[];
  updatedBy?: string;
  updatedAt?: string;
}

interface ApiResponse<T> {
  success: boolean;
  data: T;
  message?: string;
}

@Injectable({ providedIn: 'root' })
export class HomeContentConfigService {
  private apiUrl = environment.apiUrl;

  constructor(private http: HttpClient) {}

  getPublicConfig(): Observable<ApiResponse<HomeContentConfig>> {
    return this.http.get<ApiResponse<HomeContentConfig>>(`${this.apiUrl}/public/home-content`);
  }

  getAdminConfig(): Observable<ApiResponse<HomeContentConfig>> {
    return this.http.get<ApiResponse<HomeContentConfig>>(`${this.apiUrl}/home-content-config`);
  }

  updateConfig(payload: Partial<HomeContentConfig>): Observable<ApiResponse<HomeContentConfig>> {
    return this.http.put<ApiResponse<HomeContentConfig>>(`${this.apiUrl}/home-content-config`, payload);
  }
}
