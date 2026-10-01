import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { handleServiceError } from '../utils/error-handler';

export interface UpiConfigResponse {
  configured: boolean;
  upiId: string;
  payeeName: string;
  upiQrEnabled: boolean;
}

@Injectable({ providedIn: 'root' })
export class PaymentService {
  private http = inject(HttpClient);
  private apiUrl = environment.apiUrl;

  getUpiConfig(): Observable<UpiConfigResponse> {
    return this.http.get<UpiConfigResponse>(`${this.apiUrl}/payments/upi-config`).pipe(
      catchError(handleServiceError('PaymentService.getUpiConfig'))
    );
  }
}
