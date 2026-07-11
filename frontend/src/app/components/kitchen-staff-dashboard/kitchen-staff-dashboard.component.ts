import { Component, OnInit, AfterViewInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { KitchenDisplayService, KitchenStaffDashboard } from '../../services/kitchen-display.service';
import { KitchenVoiceStockRequestService } from '../../services/kitchen-voice-stock-request.service';
import { WebPushService } from '../../services/web-push.service';
import { UIStore } from '../../store/ui.store';
import { FormsModule } from '@angular/forms';

declare global {
  interface Window {
    SpeechRecognition?: any;
    webkitSpeechRecognition?: any;
    google?: any;
    googleTranslateElementInit?: () => void;
  }
}

@Component({
  selector: 'app-kitchen-staff-dashboard',
  standalone: true,
  imports: [CommonModule, RouterModule, FormsModule],
  templateUrl: './kitchen-staff-dashboard.component.html',
  styleUrls: ['./kitchen-staff-dashboard.component.scss']
})
export class KitchenStaffDashboardComponent implements OnInit, AfterViewInit {
  period: 'day' | 'week' | 'month' | 'year' = 'day';
  dashboard: KitchenStaffDashboard | null = null;
  loading = true;
  voiceDraft = '';
  transcript = '';
  listening = false;
  speechSupported = false;
  selectedLanguage = 'en';
  private recognition: any | null = null;

  constructor(
    private kitchenService: KitchenDisplayService,
    private voiceRequestService: KitchenVoiceStockRequestService,
    private uiStore: UIStore,
    private webPush: WebPushService
  ) {}

  ngOnInit(): void {
    this.selectedLanguage = this.getCurrentTranslateLanguage();
    this.initSpeechRecognition();
    this.webPush.registerKitchenWebPush('kitchen-dashboard').catch(() => {
      // Keep dashboard functional even if push setup fails.
    });
    this.loadDashboard();
  }

  ngAfterViewInit(): void {
    this.initializeGoogleTranslate('kitchen-google-translate-dashboard');
  }

  setPeriod(period: 'day' | 'week' | 'month' | 'year'): void {
    this.period = period;
    this.loadDashboard();
  }

  loadDashboard(): void {
    this.loading = true;
    this.kitchenService.getKitchenStaffDashboard(this.period).subscribe({
      next: (data) => {
        this.dashboard = data;
        this.loading = false;
      },
      error: () => {
        this.loading = false;
        this.uiStore.error('Unable to load dashboard');
      }
    });
  }

  shiftIn(): void {
    this.kitchenService.shiftIn().subscribe({
      next: () => {
        this.uiStore.success('Shift started');
        this.loadDashboard();
      },
      error: () => this.uiStore.error('Failed to start shift')
    });
  }

  shiftOut(): void {
    this.kitchenService.shiftOut().subscribe({
      next: () => {
        this.uiStore.success('Shift ended');
        this.loadDashboard();
      },
      error: () => this.uiStore.error('Failed to end shift')
    });
  }

  markAttendance(): void {
    this.kitchenService.markAttendance().subscribe({
      next: () => {
        this.uiStore.success('Attendance marked');
        this.loadDashboard();
      },
      error: () => this.uiStore.error('Failed to mark attendance')
    });
  }

  get performance() {
    return this.dashboard?.kitchenPerformance ?? {
      totalOrdersPrepared: 0,
      goodOrdersPrepared: 0,
      badOrdersPrepared: 0,
      avgKitchenPreparationTimeMinutes: 0
    };
  }

  get goodOrderRate(): number {
    const perf = this.performance;
    if (!perf || perf.totalOrdersPrepared <= 0) {
      return 0;
    }

    return (perf.goodOrdersPrepared / perf.totalOrdersPrepared) * 100;
  }

  get badOrderRate(): number {
    const perf = this.performance;
    if (!perf || perf.totalOrdersPrepared <= 0) {
      return 0;
    }

    return (perf.badOrdersPrepared / perf.totalOrdersPrepared) * 100;
  }

  private initSpeechRecognition(): void {
    const SpeechRecognitionCtor = window.SpeechRecognition || window.webkitSpeechRecognition;
    this.speechSupported = !!SpeechRecognitionCtor;

    if (!SpeechRecognitionCtor) {
      return;
    }

    this.recognition = new SpeechRecognitionCtor();
    this.recognition.lang = 'en-IN';
    this.recognition.interimResults = false;
    this.recognition.continuous = false;

    this.recognition.onresult = (event: any) => {
      const spoken = Array.from(event.results || [])
        .map((r: any) => (r?.[0]?.transcript || '').trim())
        .filter(Boolean)
        .join(' ');

      if (spoken) {
        this.transcript = spoken;
        this.voiceDraft = this.voiceDraft ? `${this.voiceDraft}, ${spoken}` : spoken;
      }
    };

    this.recognition.onerror = () => {
      this.listening = false;
      this.uiStore.error('Voice capture failed. You can still type items manually.');
    };

    this.recognition.onend = () => {
      this.listening = false;
    };
  }

  startListening(): void {
    if (!this.speechSupported || !this.recognition || this.listening) {
      return;
    }

    this.transcript = '';
    this.listening = true;
    this.recognition.start();
  }

  stopListening(): void {
    if (this.recognition && this.listening) {
      this.recognition.stop();
    }
    this.listening = false;
  }

  submitVoiceRequest(): void {
    const raw = (this.voiceDraft || '').trim();
    if (!raw) {
      this.uiStore.error('Speak or type at least one item before sending request');
      return;
    }

    const requestedItems = raw
      .replace(/\band\b/gi, ',')
      .split(',')
      .map(item => item.trim())
      .filter(Boolean);

    this.voiceRequestService.createVoiceRequest({
      transcriptText: this.transcript || raw,
      requestedItems,
      sttProvider: 'web-speech'
    }).subscribe({
      next: () => {
        this.uiStore.success('Stock request sent to admin for approval');
        this.voiceDraft = '';
        this.transcript = '';
      },
      error: () => this.uiStore.error('Failed to send stock request')
    });
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
}
