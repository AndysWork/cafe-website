import { Component, OnInit } from '@angular/core';
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
  }
}

@Component({
  selector: 'app-kitchen-staff-dashboard',
  standalone: true,
  imports: [CommonModule, RouterModule, FormsModule],
  templateUrl: './kitchen-staff-dashboard.component.html',
  styleUrls: ['./kitchen-staff-dashboard.component.scss']
})
export class KitchenStaffDashboardComponent implements OnInit {
  period: 'day' | 'week' | 'month' | 'year' = 'day';
  dashboard: KitchenStaffDashboard | null = null;
  loading = true;
  voiceDraft = '';
  transcript = '';
  listening = false;
  speechSupported = false;
  private recognition: any | null = null;

  constructor(
    private kitchenService: KitchenDisplayService,
    private voiceRequestService: KitchenVoiceStockRequestService,
    private uiStore: UIStore,
    private webPush: WebPushService
  ) {}

  ngOnInit(): void {
    this.initSpeechRecognition();
    this.webPush.registerKitchenWebPush('kitchen-dashboard').catch(() => {
      // Keep dashboard functional even if push setup fails.
    });
    this.loadDashboard();
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
}
