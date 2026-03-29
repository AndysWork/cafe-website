import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { handleServiceError } from '../utils/error-handler';

export interface CustomerReview {
  id: string;
  orderId: string;
  rating: number;
  comment?: string;
  username: string;
  createdAt: string;
}

export interface CreateReviewRequest {
  orderId: string;
  rating: number;
  comment?: string;
}

export interface ReviewResponse {
  success: boolean;
  message: string;
  review: CustomerReview;
}

export interface ReviewCheckResponse {
  exists: boolean;
  review?: CustomerReview;
}

export interface AllReviewsResponse {
  success: boolean;
  data: CustomerReview[];
  averageRating: number;
  count: number;
}

@Injectable({
  providedIn: 'root'
})
export class CustomerReviewService {
  private http = inject(HttpClient);
  private apiUrl = environment.apiUrl;

  createReview(request: CreateReviewRequest): Observable<ReviewResponse> {
    return this.http.post<ReviewResponse>(`${this.apiUrl}/reviews`, request).pipe(
      catchError(handleServiceError('CustomerReviewService.createReview'))
    );
  }

  getReviewByOrder(orderId: string): Observable<ReviewCheckResponse> {
    return this.http.get<ReviewCheckResponse>(`${this.apiUrl}/reviews/order/${orderId}`).pipe(
      catchError(handleServiceError('CustomerReviewService.getReviewByOrder'))
    );
  }

  getAllReviews(page = 1, pageSize = 50): Observable<AllReviewsResponse> {
    return this.http.get<AllReviewsResponse>(`${this.apiUrl}/reviews?page=${page}&pageSize=${pageSize}`).pipe(
      catchError(handleServiceError('CustomerReviewService.getAllReviews'))
    );
  }
}
