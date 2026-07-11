import { Injectable } from '@angular/core';

export interface PartnerRequestNotification {
  orderId: string;
  deliveryAddress?: string;
  phoneNumber?: string;
  total?: number;
}

@Injectable({ providedIn: 'root' })
export class PartnerDeliveryAlertService {
  private audioCtx?: AudioContext;

  async initialize(): Promise<void> {
    if (typeof window === 'undefined' || !('serviceWorker' in navigator)) {
      return;
    }

    try {
      await navigator.serviceWorker.register('/partner-notification-sw.js', { scope: '/partner/' });
    } catch {
      // Ignore registration errors to keep dashboard usable without notification worker.
    }
  }

  async requestPermission(): Promise<NotificationPermission> {
    if (typeof window === 'undefined' || !('Notification' in window)) {
      return 'denied';
    }

    if (Notification.permission === 'granted') {
      return 'granted';
    }

    return Notification.requestPermission();
  }

  async notifyNewRequest(request: PartnerRequestNotification): Promise<void> {
    if (typeof window === 'undefined' || !('Notification' in window)) {
      return;
    }

    if (Notification.permission !== 'granted') {
      return;
    }

    const body = request.deliveryAddress
      ? `Order #${request.orderId} • ${request.deliveryAddress}`
      : `Order #${request.orderId} is available to accept.`;

    const actions = [
      { action: 'accept', title: 'Accept' },
      { action: 'navigate', title: 'Navigate' },
      { action: 'call', title: 'Call' }
    ];

    const options: NotificationOptions & {
      actions?: Array<{ action: string; title: string }>;
      vibrate?: number[];
    } = {
      body,
      requireInteraction: true,
      tag: `delivery-request-${request.orderId}`,
      vibrate: [300, 100, 300],
      icon: '/Logo.jpg',
      badge: '/favicon.ico',
      data: {
        orderId: request.orderId,
        deliveryAddress: request.deliveryAddress,
        phoneNumber: request.phoneNumber
      },
      actions
    };

    if ('serviceWorker' in navigator) {
      const reg = await navigator.serviceWorker.ready;
      await reg.showNotification('New Delivery Request', options);
    } else {
      // Fallback for browsers without service worker support.
      const n = new Notification('New Delivery Request', options);
      n.onclick = () => window.focus();
    }

    this.playAlertTone();
  }

  playAlertTone(): void {
    if (typeof window === 'undefined') {
      return;
    }

    try {
      this.audioCtx ??= new AudioContext();
      const osc = this.audioCtx.createOscillator();
      const gain = this.audioCtx.createGain();

      osc.type = 'triangle';
      osc.frequency.value = 880;
      gain.gain.value = 0.06;

      osc.connect(gain);
      gain.connect(this.audioCtx.destination);

      osc.start();
      gain.gain.exponentialRampToValueAtTime(0.001, this.audioCtx.currentTime + 0.25);
      osc.stop(this.audioCtx.currentTime + 0.25);
    } catch {
      // Best-effort alert tone.
    }
  }
}
