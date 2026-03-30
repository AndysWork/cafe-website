import { Injectable, OnDestroy } from '@angular/core';
import { HttpClient, HttpRequest, HttpHeaders } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

interface QueuedRequest {
  id: string;
  method: string;
  url: string;
  body: unknown;
  headers: Record<string, string>;
  timestamp: number;
}

const STORAGE_KEY = 'offline_request_queue';
const MAX_QUEUE_SIZE = 50;
const MAX_AGE_MS = 24 * 60 * 60 * 1000; // 24 hours

@Injectable({ providedIn: 'root' })
export class OfflineQueueService implements OnDestroy {
  private onlineHandler = () => this.flush();

  constructor(private http: HttpClient) {
    window.addEventListener('online', this.onlineHandler);
    // Flush any stale items on startup if online
    if (navigator.onLine) {
      this.flush();
    }
  }

  ngOnDestroy(): void {
    window.removeEventListener('online', this.onlineHandler);
  }

  get isOnline(): boolean {
    return navigator.onLine;
  }

  get queueLength(): number {
    return this.getQueue().length;
  }

  enqueue(req: HttpRequest<unknown>): void {
    const queue = this.getQueue();

    if (queue.length >= MAX_QUEUE_SIZE) {
      console.warn('[OfflineQueue] Queue full, dropping oldest request');
      queue.shift();
    }

    const headers: Record<string, string> = {};
    req.headers.keys().forEach(key => {
      headers[key] = req.headers.get(key)!;
    });

    const item: QueuedRequest = {
      id: crypto.randomUUID(),
      method: req.method,
      url: req.urlWithParams,
      body: req.body,
      headers,
      timestamp: Date.now()
    };

    queue.push(item);
    this.saveQueue(queue);
    console.info(`[OfflineQueue] Enqueued ${req.method} ${req.url} (${queue.length} pending)`);
  }

  async flush(): Promise<void> {
    const queue = this.getQueue();
    if (queue.length === 0) return;

    console.info(`[OfflineQueue] Online — flushing ${queue.length} queued requests`);
    const now = Date.now();
    const remaining: QueuedRequest[] = [];

    for (const item of queue) {
      // Skip expired requests
      if (now - item.timestamp > MAX_AGE_MS) {
        console.warn(`[OfflineQueue] Dropping expired request: ${item.method} ${item.url}`);
        continue;
      }

      try {
        const request = new HttpRequest(
          item.method as string,
          item.url,
          item.body,
          { headers: new HttpHeaders(item.headers) }
        );
        await firstValueFrom(this.http.request(request));
        console.info(`[OfflineQueue] Replayed: ${item.method} ${item.url}`);
      } catch (err) {
        console.error(`[OfflineQueue] Failed to replay: ${item.method} ${item.url}`, err);
        // Keep in queue for next attempt if it's a transient error
        const status = (err as any)?.status;
        if (status === 0 || status === 502 || status === 503 || status === 504) {
          remaining.push(item);
        }
        // Non-transient errors (400, 409, etc.) are dropped — they won't succeed on retry
      }
    }

    this.saveQueue(remaining);
    if (remaining.length > 0) {
      console.warn(`[OfflineQueue] ${remaining.length} requests still pending`);
    }
  }

  clearQueue(): void {
    localStorage.removeItem(STORAGE_KEY);
  }

  private getQueue(): QueuedRequest[] {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      return raw ? JSON.parse(raw) : [];
    } catch {
      return [];
    }
  }

  private saveQueue(queue: QueuedRequest[]): void {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(queue));
  }
}
