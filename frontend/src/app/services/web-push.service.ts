import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../environments/environment';

@Injectable({ providedIn: 'root' })
export class WebPushService {
  private http = inject(HttpClient);
  private apiUrl = environment.apiUrl;

  async registerPartnerWebPush(deviceLabel = 'partner-mobile'): Promise<boolean> {
    if (typeof window === 'undefined' || !('serviceWorker' in navigator) || !('PushManager' in window)) {
      return false;
    }

    const permission = await Notification.requestPermission();
    if (permission !== 'granted') {
      return false;
    }

    const reg = await navigator.serviceWorker.register('/partner-notification-sw.js', { scope: '/partner/' });
    const keyRes = await firstValueFrom(this.http.get<{ publicKey: string }>(`${this.apiUrl}/notifications/webpush/public-key`));
    const appServerKey = this.base64ToUint8Array(keyRes.publicKey);

    const sub = await reg.pushManager.subscribe({
      userVisibleOnly: true,
      applicationServerKey: appServerKey
    });

    const json = sub.toJSON();
    const endpoint = json.endpoint;
    const p256Dh = json.keys?.['p256dh'];
    const auth = json.keys?.['auth'];

    if (!endpoint || !p256Dh || !auth) {
      return false;
    }

    await firstValueFrom(this.http.post(`${this.apiUrl}/notifications/webpush/subscribe`, {
      endpoint,
      p256Dh,
      auth,
      userAgent: navigator.userAgent,
      deviceLabel
    }));

    return true;
  }

  async unregisterWebPush(): Promise<void> {
    if (typeof window === 'undefined' || !('serviceWorker' in navigator)) {
      return;
    }

    const reg = await navigator.serviceWorker.getRegistration('/partner/');
    if (!reg) {
      return;
    }

    const sub = await reg.pushManager.getSubscription();
    if (!sub) {
      return;
    }

    const endpoint = sub.endpoint;
    await firstValueFrom(this.http.post(`${this.apiUrl}/notifications/webpush/unsubscribe`, { endpoint }));
    await sub.unsubscribe();
  }

  private base64ToUint8Array(base64String: string): Uint8Array {
    const padding = '='.repeat((4 - (base64String.length % 4)) % 4);
    const base64 = (base64String + padding).replace(/-/g, '+').replace(/_/g, '/');
    const rawData = atob(base64);
    const outputArray = new Uint8Array(rawData.length);

    for (let i = 0; i < rawData.length; ++i) {
      outputArray[i] = rawData.charCodeAt(i);
    }

    return outputArray;
  }
}
