import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { environment } from '../../environments/environment';
import { JourneyDashboard, JourneyLive, JourneySessionList, JourneyTimeline } from '../models/customer-journey.model';

@Injectable({ providedIn: 'root' })
export class CustomerJourneyService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/analytics/journey`;

  dashboard(from: string, to: string) {
    return this.http.get<JourneyDashboard>(`${this.base}/dashboard`, { params: { from, to } });
  }
  live() { return this.http.get<JourneyLive>(`${this.base}/live`); }
  sessions(from: string, to: string, segment: string, q: string, page: number) {
    return this.http.get<JourneySessionList>(`${this.base}/sessions`,
      { params: { from, to, segment, q, page } });
  }
  timeline(id: string, from: string, to: string, page: number) {
    return this.http.get<JourneyTimeline>(`${this.base}/sessions/${encodeURIComponent(id)}`,
      { params: { from, to, page } });
  }
  export(from: string, to: string, report: string, segment: string, q: string) {
    return this.http.get(`${this.base}/export`, {
      params: new HttpParams({ fromObject: { from, to, report, segment, q } }), responseType: 'blob'
    });
  }
}
