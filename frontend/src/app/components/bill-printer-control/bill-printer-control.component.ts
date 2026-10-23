import { CommonModule } from '@angular/common';
import { Component, Input, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  BILL_PRINT_TRIGGER_LABELS,
  BillPrintTrigger,
  BillPrinterMode,
  BillPrinterService
} from '../../services/bill-printer.service';
import { ReceiptWidth } from '../../utils/bill-receipt';
import { ReceiptLine } from '../../utils/escpos';

/** Compact per-device Bluetooth bill printer setup used on the cashier desk and waiter takeaway screens. */
@Component({
  selector: 'app-bill-printer-control',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="bill-printer" *ngIf="printer.status$ | async as status">
      <div class="bill-printer-bar">
        <span class="bill-printer-pill" [class.ok]="isUsable(status.connected)" [class.off]="printer.settings.mode === 'off'">
          🖨️ {{ statusText(status.connected, status.deviceName) }}
        </span>
        <button type="button" class="bp-btn primary"
                *ngIf="printer.settings.mode === 'ble' && !status.connected"
                [disabled]="connecting" (click)="connect()">
          {{ connecting ? 'Connecting…' : 'Connect printer' }}
        </button>
        <button type="button" class="bp-btn" *ngIf="printer.settings.mode === 'ble' && status.connected" (click)="printer.disconnect()">
          Disconnect
        </button>
        <button type="button" class="bp-btn" (click)="expanded = !expanded" [attr.aria-expanded]="expanded">
          {{ expanded ? 'Hide settings' : 'Printer settings' }}
        </button>
      </div>

      <div class="bill-printer-settings" *ngIf="expanded">
        <label>
          Connection
          <select [ngModel]="printer.settings.mode" (ngModelChange)="setMode($event)" name="billPrinterMode">
            <option value="ble">Bluetooth (BLE) in Chrome</option>
            <option value="rawbt">RawBT app (classic Bluetooth)</option>
            <option value="off">Off on this device</option>
          </select>
        </label>
        <label>
          Paper width
          <select [ngModel]="printer.settings.paperWidth" (ngModelChange)="setWidth($event)" name="billPrinterWidth">
            <option [ngValue]="32">58 mm (32 characters)</option>
            <option [ngValue]="48">80 mm (48 characters)</option>
          </select>
        </label>
        <fieldset *ngIf="triggers.length">
          <legend>Print automatically</legend>
          <label class="bp-check" *ngFor="let trigger of triggers">
            <input type="checkbox" [checked]="printer.settings.autoPrint[trigger]" (change)="toggleTrigger(trigger, $event)" />
            {{ labels[trigger] }}
          </label>
        </fieldset>
        <p class="bp-hint" *ngIf="printer.settings.mode === 'ble' && !printer.isWebBluetoothSupported">
          This browser has no Web Bluetooth. Use Chrome on Android, or choose RawBT.
        </p>
        <p class="bp-hint" *ngIf="printer.settings.mode === 'rawbt'">
          Install the RawBT app and pair the printer in it. RawBT prints only after a tap, so bill requests show a
          “Print” prompt instead of printing on their own.
        </p>
        <button type="button" class="bp-btn" [disabled]="printer.settings.mode === 'off' || status.printing" (click)="testPrint()">
          Test print
        </button>
      </div>

      <p class="bp-error" *ngIf="error">{{ error }}</p>
    </div>
  `,
  styles: [`
    .bill-printer { display: flex; flex-direction: column; gap: 6px; font-size: 0.85rem; }
    .bill-printer-bar { display: flex; flex-wrap: wrap; align-items: center; gap: 6px; }
    .bill-printer-pill { padding: 4px 10px; border-radius: 999px; background: #fdecea; color: #a12622; font-weight: 600; }
    .bill-printer-pill.ok { background: #e6f4ea; color: #1e7b34; }
    .bill-printer-pill.off { background: #eceff1; color: #546e7a; }
    .bp-btn { padding: 4px 10px; border-radius: 6px; border: 1px solid #c7ccd1; background: #fff; cursor: pointer; font-size: 0.8rem; }
    .bp-btn.primary { background: #1565c0; border-color: #1565c0; color: #fff; }
    .bp-btn:disabled { opacity: 0.6; cursor: not-allowed; }
    .bill-printer-settings { display: flex; flex-direction: column; gap: 8px; padding: 10px; border: 1px solid #e0e0e0; border-radius: 8px; background: #fafafa; }
    .bill-printer-settings label { display: flex; flex-direction: column; gap: 2px; }
    .bill-printer-settings select { padding: 4px; }
    fieldset { border: 1px solid #e0e0e0; border-radius: 6px; padding: 6px 10px; }
    .bp-check { flex-direction: row !important; align-items: center; gap: 6px !important; }
    .bp-hint { margin: 0; color: #5f6368; }
    .bp-error { margin: 0; color: #c62828; }
  `]
})
export class BillPrinterControlComponent {
  readonly printer = inject(BillPrinterService);
  readonly labels = BILL_PRINT_TRIGGER_LABELS;

  /** Auto-print triggers relevant to the hosting screen. */
  @Input() triggers: BillPrintTrigger[] = [];

  expanded = false;
  connecting = false;
  error = '';

  isUsable(connected: boolean): boolean {
    const mode = this.printer.settings.mode;
    return mode === 'rawbt' || (mode === 'ble' && connected);
  }

  statusText(connected: boolean, deviceName?: string): string {
    switch (this.printer.settings.mode) {
      case 'off': return 'Bill printing off';
      case 'rawbt': return 'Bill printer: RawBT';
      default: return connected ? `Bill printer: ${deviceName || 'connected'}` : 'Bill printer not connected';
    }
  }

  async connect(): Promise<void> {
    this.error = '';
    this.connecting = true;
    try {
      await this.printer.connect();
    } catch (error: any) {
      if (error?.name !== 'NotFoundError') this.error = error?.message || 'Could not connect to the printer.';
    } finally {
      this.connecting = false;
    }
  }

  setMode(mode: BillPrinterMode): void {
    this.error = '';
    if (mode !== 'ble') this.printer.disconnect();
    this.printer.updateSettings({ mode });
  }

  setWidth(width: ReceiptWidth): void {
    this.printer.updateSettings({ paperWidth: Number(width) === 48 ? 48 : 32 });
  }

  toggleTrigger(trigger: BillPrintTrigger, event: Event): void {
    this.printer.updateSettings({
      autoPrint: { [trigger]: (event.target as HTMLInputElement).checked } as Record<BillPrintTrigger, boolean>
    });
  }

  async testPrint(): Promise<void> {
    this.error = '';
    const width = this.printer.settings.paperWidth;
    const lines: ReceiptLine[] = [
      { text: 'MAA TARA CAFE', style: 'large' },
      { text: 'Bill printer test', style: 'bold' },
      { text: '-'.repeat(width) },
      { text: `Paper width: ${width === 48 ? '80' : '58'} mm (${width} chars)` },
      { text: '1234567890'.repeat(5).slice(0, width) },
      { text: `Printed ${new Date().toLocaleString('en-IN')}` }
    ];
    try {
      await this.printer.print(lines);
    } catch (error: any) {
      this.error = error?.message || 'Test print failed.';
    }
  }
}
