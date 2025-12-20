import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export interface Review {
  id: string;
  platform: string;
  orderId: string;
  customerName?: string;
  orderAt: Date;
  rating: number;
  review?: string;
  orderedItems: Array<{
    quantity: number;
    itemName: string;
  }>;
}

export interface ReviewsResponse {
  success: boolean;
  data: Review[];
}

@Injectable({
  providedIn: 'root'
})
export class ReviewsService {
  private apiUrl = `${environment.apiUrl}/online-sales/reviews`;

  constructor(private http: HttpClient) {}

  getFiveStarReviews(limit: number = 10): Observable<ReviewsResponse> {
    return this.http.get<ReviewsResponse>(`${this.apiUrl}/five-star?limit=${limit}`);
  }
}
