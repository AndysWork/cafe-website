import { Injectable } from '@angular/core';
import { BehaviorSubject } from 'rxjs';
import { ReceiptLine, encodeEscPos } from '../utils/escpos';
import { ReceiptWidth } from '../utils/bill-receipt';

export type BillPrinterMode = 'ble' | 'rawbt' | 'off';
export type BillPrintTrigger = 'dineInBillRequested' | 'dineInSettled' | 'takeawayPaid' | 'takeawayHandover';

export interface BillPrinterSettings {
  mode: BillPrinterMode;
  paperWidth: ReceiptWidth;
  autoPrint: Record<BillPrintTrigger, boolean>;
}

export interface BillPrinterStatus {
  connected: boolean;
  deviceName?: string;
  printing: boolean;
}

export const BILL_PRINT_TRIGGER_LABELS: Record<BillPrintTrigger, string> = {
  dineInBillRequested: 'Dine-in: when a bill is requested',
  dineInSettled: 'Dine-in: when the table is fully settled',
  takeawayPaid: 'Takeaway: when payment is recorded',
  takeawayHandover: 'Takeaway: at handover'
};

const SETTINGS_KEY = 'billPrinterSettings';

/** GATT services used by common BLE ESC/POS printers; Web Bluetooth only exposes listed services. */
const PRINTER_SERVICES = [
  '000018f0-0000-1000-8000-00805f9b34fb',
  'e7810a71-73ae-499d-8c15-faa9aef0c3f2',
  '49535343-fe7d-4ae5-8fa9-9fafd205e455',
  '0000ff00-0000-1000-8000-00805f9b34fb',
  '0000ffe0-0000-1000-8000-00805f9b34fb',
  '0000ae30-0000-1000-8000-00805f9b34fb',
  '0000fee7-0000-1000-8000-00805f9b34fb'
];

const BLE_CHUNK_BYTES = 20;
const BLE_CHUNK_DELAY_MS = 15;

const DEFAULT_SETTINGS: BillPrinterSettings = {
  mode: 'ble',
  paperWidth: 32,
  autoPrint: { dineInBillRequested: true, dineInSettled: true, takeawayPaid: true, takeawayHandover: true }
};

const delay = (ms: number) => new Promise(resolve => setTimeout(resolve, ms));

/**
 * Prints bills on a Bluetooth thermal printer paired with this device, either directly
 * through Web Bluetooth (BLE printers, Chrome) or through the RawBT Android app (classic Bluetooth).
 * Settings are stored per device because each cashier/waiter device has its own printer.
 */
@Injectable({ providedIn: 'root' })
export class BillPrinterService {
  private device: any = null;
  private characteristic: any = null;
  private queue: Promise<unknown> = Promise.resolve();
  private readonly statusSubject = new BehaviorSubject<BillPrinterStatus>({ connected: false, printing: false });
  private readonly settingsSubject = new BehaviorSubject<BillPrinterSettings>(this.loadSettings());

  readonly status$ = this.statusSubject.asObservable();
  readonly settings$ = this.settingsSubject.asObservable();

  get settings(): BillPrinterSettings {
    return this.settingsSubject.value;
  }

  get status(): BillPrinterStatus {
    return this.statusSubject.value;
  }

  get isWebBluetoothSupported(): boolean {
    return typeof navigator !== 'undefined' && !!(navigator as any).bluetooth;
  }

  /** Whether bills can be sent now without user interaction (auto-print). */
  get isReady(): boolean {
    const { mode } = this.settings;
    return mode === 'rawbt' || (mode === 'ble' && !!this.device);
  }

  updateSettings(changes: Partial<BillPrinterSettings>): void {
    const next: BillPrinterSettings = {
      ...this.settings,
      ...changes,
      autoPrint: { ...this.settings.autoPrint, ...(changes.autoPrint || {}) }
    };
    this.settingsSubject.next(next);
    try {
      localStorage.setItem(SETTINGS_KEY, JSON.stringify(next));
    } catch {
      // Storage may be unavailable (private mode); settings still apply for this session.
    }
  }

  /** Opens the browser's Bluetooth chooser. Must be called from a click handler. */
  async connect(): Promise<string> {
    const bluetooth = (navigator as any).bluetooth;
    if (!bluetooth) {
      throw new Error('This browser does not support Web Bluetooth. Use Chrome on Android, or switch to RawBT mode.');
    }
    const device = await bluetooth.requestDevice({ acceptAllDevices: true, optionalServices: PRINTER_SERVICES });
    this.disconnect();
    this.device = device;
    device.addEventListener('gattserverdisconnected', () => {
      this.characteristic = null;
      this.setStatus({ connected: false });
    });
    await this.openChannel();
    return device.name || 'Bluetooth printer';
  }

  disconnect(): void {
    if (this.device?.gatt?.connected) this.device.gatt.disconnect();
    this.device = null;
    this.characteristic = null;
    this.setStatus({ connected: false, deviceName: undefined });
  }

  /** Prints receipt lines. Jobs run one at a time so receipts never interleave. */
  print(lines: ReceiptLine[]): Promise<void> {
    const data = encodeEscPos(lines);
    const job = this.queue.then(() => this.send(data));
    this.queue = job.catch(() => undefined);
    return job;
  }

  /**
   * Prints automatically when this trigger is enabled and the printer is usable.
   * RawBT opens an Android app, which browsers only allow right after a user tap,
   * so background events (userInitiated = false) are skipped in RawBT mode.
   * Returns true when a print was sent.
   */
  async autoPrint(trigger: BillPrintTrigger, buildLines: () => ReceiptLine[] | Promise<ReceiptLine[]>, userInitiated = true): Promise<boolean> {
    const { mode, autoPrint } = this.settings;
    if (mode === 'off' || !autoPrint[trigger] || !this.isReady) return false;
    if (mode === 'rawbt' && !userInitiated) return false;
    await this.print(await buildLines());
    return true;
  }

  private async send(data: Uint8Array): Promise<void> {
    const { mode } = this.settings;
    if (mode === 'off') throw new Error('Bill printing is turned off on this device.');
    this.setStatus({ printing: true });
    try {
      if (mode === 'rawbt') this.sendToRawBt(data);
      else await this.sendBle(data);
    } finally {
      this.setStatus({ printing: false });
    }
  }

  private sendToRawBt(data: Uint8Array): void {
    let binary = '';
    data.forEach(byte => { binary += String.fromCharCode(byte); });
    window.location.href = `intent:base64,${btoa(binary)}#Intent;scheme=rawbt;package=ru.a402d.rawbtprinter;end;`;
  }

  private async sendBle(data: Uint8Array): Promise<void> {
    if (!this.device) throw new Error('Connect the Bluetooth bill printer first.');
    if (!this.device.gatt?.connected || !this.characteristic) await this.openChannel();

    const characteristic = this.characteristic;
    const withResponse = !!characteristic.properties?.write;
    for (let offset = 0; offset < data.length; offset += BLE_CHUNK_BYTES) {
      const chunk = data.slice(offset, offset + BLE_CHUNK_BYTES);
      if (withResponse) {
        await (characteristic.writeValueWithResponse
          ? characteristic.writeValueWithResponse(chunk)
          : characteristic.writeValue(chunk));
      } else {
        await characteristic.writeValueWithoutResponse(chunk);
        await delay(BLE_CHUNK_DELAY_MS);
      }
    }
  }

  private async openChannel(): Promise<void> {
    const server = await this.device.gatt.connect();
    let services: any[] = [];
    try {
      services = await server.getPrimaryServices();
    } catch {
      services = [];
    }
    for (const service of services) {
      for (const characteristic of await service.getCharacteristics()) {
        if (characteristic.properties?.write || characteristic.properties?.writeWithoutResponse) {
          this.characteristic = characteristic;
          this.setStatus({ connected: true, deviceName: this.device.name || 'Bluetooth printer' });
          return;
        }
      }
    }
    this.device.gatt.disconnect();
    throw new Error('This Bluetooth device has no printer channel. It may be a classic Bluetooth printer: use RawBT mode.');
  }

  private setStatus(changes: Partial<BillPrinterStatus>): void {
    this.statusSubject.next({ ...this.statusSubject.value, ...changes });
  }

  private loadSettings(): BillPrinterSettings {
    try {
      const stored = JSON.parse(localStorage.getItem(SETTINGS_KEY) || 'null');
      if (stored && typeof stored === 'object') {
        return {
          mode: ['ble', 'rawbt', 'off'].includes(stored.mode) ? stored.mode : DEFAULT_SETTINGS.mode,
          paperWidth: stored.paperWidth === 48 ? 48 : 32,
          autoPrint: { ...DEFAULT_SETTINGS.autoPrint, ...(stored.autoPrint || {}) }
        };
      }
    } catch {
      // Fall back to defaults when stored settings are unreadable.
    }
    return { ...DEFAULT_SETTINGS, autoPrint: { ...DEFAULT_SETTINGS.autoPrint } };
  }
}
