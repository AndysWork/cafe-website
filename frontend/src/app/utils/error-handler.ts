import { HttpErrorResponse } from '@angular/common/http';
import { Observable, throwError } from 'rxjs';

export function handleServiceError(context: string) {
  return (error: HttpErrorResponse): Observable<never> => {
    let message: string;

    if (error.status === 0) {
      message = `[${context}] Network error — please check your connection`;
    } else if (error.status === 404) {
      message = `[${context}] Resource not found`;
    } else if (error.status === 400) {
      message = `[${context}] Invalid request: ${error.error?.message || error.message}`;
    } else if (error.status === 403) {
      message = `[${context}] Access denied`;
    } else if (error.status === 409) {
      message = `[${context}] Conflict: ${error.error?.message || 'resource already exists'}`;
    } else if (error.status === 500) {
      message = `[${context}] Server error — please try again later`;
    } else {
      message = `[${context}] Error (${error.status}): ${error.error?.message || error.message}`;
    }

    console.error(message, error);
    return throwError(() => ({ status: error.status, message, originalError: error }));
  };
}
