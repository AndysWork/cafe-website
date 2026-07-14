import { Component, OnInit, OnDestroy, AfterViewInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { KitchenDisplayService, KitchenOrder, KitchenStats, KitchenChecklistItem } from '../../services/kitchen-display.service';
import { OutletService } from '../../services/outlet.service';
import { WebPushService } from '../../services/web-push.service';
import { UIStore } from '../../store/ui.store';
import { Subscription, interval } from 'rxjs';
import { filter } from 'rxjs/operators';
import { RouterModule } from '@angular/router';

declare global {
  interface Window {
    google?: any;
    googleTranslateElementInit?: () => void;
  }
}

@Component({
  selector: 'app-kitchen-display',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './kitchen-display.component.html',
  styleUrls: ['./kitchen-display.component.scss']
})
export class KitchenDisplayComponent implements OnInit, OnDestroy, AfterViewInit {
  private outletService = inject(OutletService);
  private uiStore = inject(UIStore);
  private outletSub?: Subscription;
  private pollSub?: Subscription;
  private webPush = inject(WebPushService);

  orders: KitchenOrder[] = [];
  stats: KitchenStats | null = null;
  loading = true;
  kotText = '';
  showKotModal = false;
  showChecklistModal = false;
  selectedOrderForChecklist: KitchenOrder | null = null;
  checklistItems: KitchenChecklistItem[] = [];
  activeSpeechKey: string | null = null;
  selectedLanguage = 'en';
  private knownIncomingOrderIds = new Set<string>();
  private hasHydratedOrders = false;
  private audioContext: AudioContext | null = null;
  private audioUnlocked = false;
  private readonly unlockAudioHandler = () => this.unlockAudio();

  constructor(private kitchenService: KitchenDisplayService) {}

  ngAfterViewInit(): void {
    this.initializeGoogleTranslate('kitchen-google-translate-display');
  }

  ngOnInit() {
    this.selectedLanguage = this.getCurrentTranslateLanguage();
    this.registerAudioUnlockListeners();

    this.webPush.registerKitchenWebPush('kitchen-display').catch(() => {
      // Keep kitchen display functional even if push setup fails.
    });

    this.outletSub = this.outletService.selectedOutlet$
      .pipe(filter(o => o !== null))
      .subscribe(() => this.loadData());
    if (this.outletService.getSelectedOutlet()) this.loadData();

    // Auto-refresh every 15 seconds
    this.pollSub = interval(15000).subscribe(() => this.loadData());
  }

  ngOnDestroy() {
    this.outletSub?.unsubscribe();
    this.pollSub?.unsubscribe();
    this.unregisterAudioUnlockListeners();
    if (this.audioContext && this.audioContext.state !== 'closed') {
      this.audioContext.close().catch(() => {
        // Ignore teardown errors.
      });
    }
    if (typeof window !== 'undefined' && 'speechSynthesis' in window) {
      window.speechSynthesis.cancel();
    }
  }

  private registerAudioUnlockListeners(): void {
    if (typeof window === 'undefined') {
      return;
    }

    window.addEventListener('pointerdown', this.unlockAudioHandler, { passive: true });
    window.addEventListener('keydown', this.unlockAudioHandler);
    window.addEventListener('touchstart', this.unlockAudioHandler, { passive: true });
  }

  private unregisterAudioUnlockListeners(): void {
    if (typeof window === 'undefined') {
      return;
    }

    window.removeEventListener('pointerdown', this.unlockAudioHandler);
    window.removeEventListener('keydown', this.unlockAudioHandler);
    window.removeEventListener('touchstart', this.unlockAudioHandler);
  }

  private unlockAudio(): void {
    if (this.audioUnlocked) {
      return;
    }

    const AudioContextCtor = typeof window !== 'undefined'
      ? (window.AudioContext || (window as Window & { webkitAudioContext?: typeof AudioContext }).webkitAudioContext)
      : undefined;

    if (!AudioContextCtor) {
      return;
    }

    if (!this.audioContext) {
      this.audioContext = new AudioContextCtor();
    }

    this.audioContext.resume()
      .then(() => {
        this.audioUnlocked = this.audioContext?.state === 'running';
        if (this.audioUnlocked) {
          this.unregisterAudioUnlockListeners();
        }
      })
      .catch(() => {
        // Browser may block audio until an allowed interaction.
      });
  }

  private playNewOrderAlert(): void {
    if (!this.audioContext || this.audioContext.state !== 'running') {
      return;
    }

    const now = this.audioContext.currentTime;
    const pattern = [0, 0.18, 0.36];

    for (const offset of pattern) {
      const oscillator = this.audioContext.createOscillator();
      const gain = this.audioContext.createGain();

      oscillator.type = 'triangle';
      oscillator.frequency.setValueAtTime(920, now + offset);

      gain.gain.setValueAtTime(0.0001, now + offset);
      gain.gain.exponentialRampToValueAtTime(0.22, now + offset + 0.02);
      gain.gain.exponentialRampToValueAtTime(0.0001, now + offset + 0.14);

      oscillator.connect(gain);
      gain.connect(this.audioContext.destination);

      oscillator.start(now + offset);
      oscillator.stop(now + offset + 0.15);
    }
  }

  private initializeGoogleTranslate(containerId: string): void {
    if (typeof window === 'undefined' || typeof document === 'undefined') {
      return;
    }

    const renderWidget = () => {
      if (!window.google?.translate?.TranslateElement) {
        return;
      }

      const container = document.getElementById(containerId);
      if (!container || container.childElementCount > 0) {
        return;
      }

      new window.google.translate.TranslateElement(
        {
          pageLanguage: 'en',
          includedLanguages: 'en,hi,ta,te,kn,ml,mr,bn,gu,pa,ur',
          layout: window.google.translate.TranslateElement.InlineLayout.SIMPLE,
          autoDisplay: false
        },
        containerId
      );
    };

    window.googleTranslateElementInit = renderWidget;

    if (window.google?.translate?.TranslateElement) {
      renderWidget();
      return;
    }

    const existingScript = document.getElementById('google-translate-script');
    if (existingScript) {
      return;
    }

    const script = document.createElement('script');
    script.id = 'google-translate-script';
    script.src = 'https://translate.google.com/translate_a/element.js?cb=googleTranslateElementInit';
    script.async = true;
    script.defer = true;
    document.body.appendChild(script);
  }

  applySelectedLanguage(): void {
    if (typeof window === 'undefined' || typeof document === 'undefined') {
      return;
    }

    const targetLang = (this.selectedLanguage || 'en').toLowerCase();
    const cookieValue = `/en/${targetLang}`;

    document.cookie = `googtrans=${cookieValue};path=/;max-age=31536000`;
    document.cookie = `googtrans=${cookieValue};path=/;max-age=31536000;domain=${window.location.hostname}`;

    const sameRouteUrl = `${window.location.pathname}${window.location.search}`;
    window.location.replace(sameRouteUrl);
  }

  private getCurrentTranslateLanguage(): string {
    if (typeof document === 'undefined') {
      return 'en';
    }

    const translateCookie = document.cookie
      .split(';')
      .map(part => part.trim())
      .find(part => part.startsWith('googtrans='));

    if (!translateCookie) {
      return 'en';
    }

    const value = decodeURIComponent(translateCookie.split('=')[1] || '');
    const parts = value.split('/').filter(Boolean);
    return parts[1] || 'en';
  }

  loadData() {
    this.kitchenService.getKitchenOrders().subscribe({
      next: o => {
        const incomingOrderIds = new Set(
          o
            .filter(order => ['pending', 'confirmed', 'preparing'].includes(order.status))
            .map(order => order.id)
        );

        const hasNewIncomingOrder = this.hasHydratedOrders
          && Array.from(incomingOrderIds).some(id => !this.knownIncomingOrderIds.has(id));

        this.orders = o;
        this.loading = false;
        this.knownIncomingOrderIds = incomingOrderIds;

        if (this.hasHydratedOrders && hasNewIncomingOrder) {
          this.playNewOrderAlert();
        }

        this.hasHydratedOrders = true;
      },
      error: () => { this.loading = false; }
    });
    this.kitchenService.getKitchenStats().subscribe({
      next: s => this.stats = s,
      error: () => {}
    });
  }

  updateStatus(orderId: string, status: string) {
    if (status === 'ready') {
      this.openChecklist(orderId);
      return;
    }

    this.kitchenService.updateOrderStatus(orderId, status).subscribe({
      next: () => { this.uiStore.success('Status updated'); this.loadData(); },
      error: (err) => this.uiStore.error(err?.error?.error || 'Failed to update status')
    });
  }

  openChecklist(orderId: string) {
    const order = this.orders.find(o => o.id === orderId);
    if (!order) {
      this.uiStore.error('Order not found');
      return;
    }

    const fallbackChecklist: KitchenChecklistItem[] = [
      { label: 'Item quantity rechecked', isCompleted: false },
      { label: 'Plating and garnish completed', isCompleted: false },
      { label: 'Temperature and freshness verified', isCompleted: false },
      { label: 'Packaging/sealing verified', isCompleted: false },
      { label: 'Special instructions verified', isCompleted: false }
    ];

    this.selectedOrderForChecklist = order;
    this.checklistItems = (order.kitchenChecklist?.length ? order.kitchenChecklist : fallbackChecklist)
      .map(i => ({ id: i.id, label: i.label, isCompleted: i.isCompleted }));
    this.showChecklistModal = true;
  }

  closeChecklist() {
    this.showChecklistModal = false;
    this.selectedOrderForChecklist = null;
    this.checklistItems = [];
  }

  completeReadyStatus() {
    if (!this.selectedOrderForChecklist) {
      return;
    }

    const incomplete = this.checklistItems.filter(item => !item.isCompleted);
    if (incomplete.length > 0) {
      this.uiStore.error('Complete all checklist items before marking ready');
      return;
    }

    this.kitchenService.updateOrderStatus(this.selectedOrderForChecklist.id, 'ready', this.checklistItems).subscribe({
      next: (res) => {
        const message = res?.deliveryNotificationQueued
          ? 'Order marked ready. Delivery partners notified.'
          : 'Order marked ready with checklist completed';
        this.uiStore.success(message);
        this.closeChecklist();
        this.loadData();
      },
      error: () => this.uiStore.error('Failed to mark order ready')
    });
  }

  getChecklistProgress(): number {
    if (!this.checklistItems.length) return 0;
    const completed = this.checklistItems.filter(i => i.isCompleted).length;
    return Math.round((completed / this.checklistItems.length) * 100);
  }

  printKot(orderId: string) {
    this.kitchenService.getKot(orderId).subscribe({
      next: (res) => { this.kotText = res.kotText; this.showKotModal = true; },
      error: () => this.uiStore.error('Failed to generate KOT')
    });
  }

  printKotWindow() {
    const win = window.open('', '_blank', 'width=300,height=500');
    if (win) {
      win.document.write(`<pre style="font-family: monospace; font-size: 12px; width: 80mm;">${this.kotText}</pre>`);
      win.document.close();
      win.print();
    }
  }

  getOrdersByStatus(status: string): KitchenOrder[] {
    return this.orders.filter(o => o.status === status);
  }

  getTimeSince(dateStr: string): string {
    const diff = Math.floor((Date.now() - new Date(dateStr).getTime()) / 60000);
    if (diff < 1) return 'Just now';
    if (diff < 60) return `${diff}m ago`;
    return `${Math.floor(diff / 60)}h ${diff % 60}m ago`;
  }

  getStatusColor(status: string): string {
    const colors: Record<string, string> = {
      pending: '#f59e0b',
      confirmed: '#3b82f6',
      preparing: '#8b5cf6',
      ready: '#10b981',
      'out-for-delivery': '#0ea5e9',
      delivered: '#059669'
    };
    return colors[status] || '#6b7280';
  }

  canSpeakOrder(order: KitchenOrder): boolean {
    return ['confirmed', 'preparing', 'ready'].includes(order.status);
  }

  speakOrder(order: KitchenOrder): void {
    if (!this.canSpeakOrder(order)) {
      return;
    }

    if (typeof window === 'undefined' || !('speechSynthesis' in window)) {
      this.uiStore.error('Text-to-speech is not supported in this browser');
      return;
    }

    const speechKey = `order-${order.id}`;
    const synth = window.speechSynthesis;

    if (this.activeSpeechKey === speechKey && synth.speaking) {
      synth.cancel();
      this.activeSpeechKey = null;
      return;
    }

    synth.cancel();

    const orderItemsText = (order.items || [])
      .map(i => `${i.quantity} ${i.quantity > 1 ? 'items' : 'item'} of ${i.name}`)
      .join(', ');

    const utterance = new SpeechSynthesisUtterance(
      `Order ${order.id.slice(-6)}. ${orderItemsText}.`
    );
    utterance.rate = 1;
    utterance.pitch = 1;

    // Prefer Indian English voice when available for clearer kitchen pronunciation.
    const voices = synth.getVoices();
    const preferredVoice = voices.find(v => /en-IN/i.test(v.lang)) || voices.find(v => /^en-/i.test(v.lang));
    if (preferredVoice) {
      utterance.voice = preferredVoice;
      utterance.lang = preferredVoice.lang;
    } else {
      utterance.lang = 'en-IN';
    }

    this.activeSpeechKey = speechKey;
    utterance.onend = () => {
      if (this.activeSpeechKey === speechKey) {
        this.activeSpeechKey = null;
      }
    };
    utterance.onerror = () => {
      if (this.activeSpeechKey === speechKey) {
        this.activeSpeechKey = null;
      }
      this.uiStore.error('Unable to play order audio');
    };

    synth.speak(utterance);
  }
}
