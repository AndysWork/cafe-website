import { Injectable, signal, OnDestroy } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class NetworkStatusService implements OnDestroy {
  readonly isOnline = signal<boolean>(navigator.onLine);

  private onlineHandler = () => {
    this.isOnline.set(true);
    console.info('[NetworkStatus] Back online');
  };

  private offlineHandler = () => {
    this.isOnline.set(false);
    console.warn('[NetworkStatus] Went offline');
  };

  constructor() {
    window.addEventListener('online', this.onlineHandler);
    window.addEventListener('offline', this.offlineHandler);
  }

  ngOnDestroy(): void {
    window.removeEventListener('online', this.onlineHandler);
    window.removeEventListener('offline', this.offlineHandler);
  }
}
