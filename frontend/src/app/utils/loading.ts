import { Observable, finalize } from 'rxjs';

/**
 * Wraps an Observable to manage loading state automatically.
 * Sets the flag to true before subscribing and false when complete or errored.
 *
 * Usage in a component:
 *   withLoading(this.service.getData(), flag => this.isLoading = flag)
 *     .subscribe({ next: data => this.data = data, error: err => this.error = err });
 */
export function withLoading<T>(
  source$: Observable<T>,
  setLoading: (loading: boolean) => void
): Observable<T> {
  setLoading(true);
  return source$.pipe(
    finalize(() => setLoading(false))
  );
}
