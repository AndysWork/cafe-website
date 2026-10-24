import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
  BulkCreateTablesRequest,
  CafeTable,
  CafeTableFloorView,
  CafeTableService,
  CancelledSessionCleanupResult,
  FloorSummary,
  TableHousekeepingStatus
} from '../../services/cafe-table.service';
import { UIStore } from '../../store/ui.store';
import { AuthService } from '../../services/auth.service';
import QRCode from 'qrcode';

interface TableDraft {
  tableNumber: string;
  area: string;
  capacity: number;
  sortOrder: number;
}

@Component({
  selector: 'app-admin-tables',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './admin-tables.component.html',
  styleUrls: ['./admin-tables.component.scss']
})
export class AdminTablesComponent implements OnInit, OnDestroy {
  private tableService = inject(CafeTableService);
  private uiStore = inject(UIStore);
  private authService = inject(AuthService);
  private refreshTimer?: ReturnType<typeof setInterval>;

  readonly statusLabels: Record<string, string> = {
    free: 'Free',
    occupied: 'Occupied',
    bill_requested: 'Bill Requested',
    cleaning: 'Cleaning',
    out_of_service: 'Out of Service',
    available: 'Available'
  };

  tab: 'floor' | 'setup' = 'floor';
  loading = signal(false);
  busyId = signal<string | null>(null);
  tables = signal<CafeTable[]>([]);
  floor = signal<CafeTableFloorView[]>([]);
  summary = signal<FloorSummary | null>(null);
  qrPreviewTable = '';
  qrPreviewUrl = '';
  qrPreviewDataUrl = '';
  qrGeneratingId: string | null = null;
  cleanupBusy = false;
  cleanupReport: CancelledSessionCleanupResult | null = null;
  readonly canCleanupCancelledSessions = this.authService.isAdmin();

  editingId: string | null = null;
  editDraft: TableDraft = { tableNumber: '', area: '', capacity: 4, sortOrder: 0 };

  newTable = { tableNumber: '', area: '', capacity: 4 };
  bulk: BulkCreateTablesRequest = { prefix: 'T', from: 1, to: 10, area: '', capacity: 4 };

  floorByArea = computed(() => {
    const groups = new Map<string, CafeTableFloorView[]>();
    for (const t of this.floor()) {
      const area = t.area || 'General';
      if (!groups.has(area)) groups.set(area, []);
      groups.get(area)!.push(t);
    }
    return [...groups.entries()].map(([area, tables]) => ({ area, tables }));
  });

  ngOnInit(): void {
    this.loadAll();
    this.refreshTimer = setInterval(() => {
      if (this.tab === 'floor') this.loadFloor(true);
    }, 15000);
  }

  ngOnDestroy(): void {
    if (this.refreshTimer) clearInterval(this.refreshTimer);
  }

  loadAll(): void {
    this.loadTables();
    this.loadFloor();
  }

  previewCancelledSessionCleanup(): void {
    if (!this.canCleanupCancelledSessions || this.cleanupBusy) return;
    this.cleanupBusy = true;
    this.cleanupReport = null;
    this.tableService.cleanupCancelledSessions(true).subscribe({
      next: result => {
        this.cleanupReport = result;
        this.cleanupBusy = false;
        if (!result.candidateCount) this.uiStore.success('No stale cancelled table sessions need cleanup.');
      },
      error: err => {
        this.cleanupBusy = false;
        this.showError(err, 'Failed to check stale cancelled sessions');
      }
    });
  }

  applyCancelledSessionCleanup(): void {
    const report = this.cleanupReport;
    if (!report?.candidateCount || this.cleanupBusy) return;
    const tables = report.candidates.map(candidate => candidate.tableNumber).join(', ');
    if (!window.confirm(
      `Close ${report.candidateCount} stale table session(s) for ${tables}? ` +
      'Only sessions with cancelled rounds and no payment activity will be closed.'
    )) return;

    this.cleanupBusy = true;
    this.tableService.cleanupCancelledSessions(false).subscribe({
      next: result => {
        this.cleanupReport = null;
        this.cleanupBusy = false;
        this.uiStore.success(
          `${result.closedCount} stale table session(s) closed` +
          (result.skippedCount ? `; ${result.skippedCount} changed and were skipped` : '')
        );
        this.loadFloor();
      },
      error: err => {
        this.cleanupBusy = false;
        this.showError(err, 'Failed to clean up stale cancelled sessions');
      }
    });
  }

  dismissCancelledSessionCleanup(): void {
    this.cleanupReport = null;
  }

  loadTables(): void {
    this.loading.set(true);
    this.tableService.getTables(true).subscribe({
      next: tables => {
        this.tables.set(tables);
        this.loading.set(false);
      },
      error: err => {
        this.loading.set(false);
        this.showError(err, 'Failed to load tables');
      }
    });
  }

  loadFloor(silent = false): void {
    this.tableService.getFloor().subscribe({
      next: ({ tables, summary }) => {
        this.floor.set(tables);
        this.summary.set(summary);
      },
      error: err => {
        if (!silent) this.showError(err, 'Failed to load floor');
      }
    });
  }

  addTable(): void {
    if (!this.newTable.tableNumber.trim()) return;
    this.busyId.set('new');
    this.tableService.createTable({
      tableNumber: this.newTable.tableNumber.trim(),
      area: this.newTable.area.trim() || undefined,
      capacity: this.newTable.capacity
    }).subscribe({
      next: t => {
        this.uiStore.success(`Table ${t.tableNumber} added`);
        this.newTable = { tableNumber: '', area: this.newTable.area, capacity: this.newTable.capacity };
        this.busyId.set(null);
        this.loadAll();
      },
      error: err => {
        this.busyId.set(null);
        this.showError(err, 'Failed to add table');
      }
    });
  }

  bulkCreate(): void {
    if (this.bulk.to < this.bulk.from) {
      this.uiStore.error('"To" must be greater than or equal to "From"');
      return;
    }
    this.busyId.set('bulk');
    this.tableService.bulkCreate({ ...this.bulk, area: this.bulk.area?.trim() || undefined }).subscribe({
      next: result => {
        const skipped = result.skipped.length ? ` (${result.skipped.length} already existed)` : '';
        this.uiStore.success(`${result.created.length} tables created${skipped}`);
        this.busyId.set(null);
        this.loadAll();
      },
      error: err => {
        this.busyId.set(null);
        this.showError(err, 'Failed to create tables');
      }
    });
  }

  startEdit(table: CafeTable): void {
    this.editingId = table.id;
    this.editDraft = {
      tableNumber: table.tableNumber,
      area: table.area ?? '',
      capacity: table.capacity,
      sortOrder: table.sortOrder
    };
  }

  cancelEdit(): void {
    this.editingId = null;
  }

  saveEdit(table: CafeTable): void {
    if (!this.editDraft.tableNumber.trim()) return;
    this.busyId.set(table.id);
    this.tableService.updateTable(table.id, {
      tableNumber: this.editDraft.tableNumber.trim(),
      area: this.editDraft.area.trim(),
      capacity: this.editDraft.capacity,
      sortOrder: this.editDraft.sortOrder
    }).subscribe({
      next: () => {
        this.uiStore.success('Table updated');
        this.editingId = null;
        this.busyId.set(null);
        this.loadAll();
      },
      error: err => {
        this.busyId.set(null);
        this.showError(err, 'Failed to update table');
      }
    });
  }

  toggleActive(table: CafeTable): void {
    this.busyId.set(table.id);
    this.tableService.updateTable(table.id, { isActive: !table.isActive }).subscribe({
      next: () => {
        this.busyId.set(null);
        this.loadAll();
      },
      error: err => {
        this.busyId.set(null);
        this.showError(err, 'Failed to update table');
      }
    });
  }

  setStatus(tableId: string | undefined, status: TableHousekeepingStatus): void {
    if (!tableId) return;
    this.busyId.set(tableId);
    this.tableService.setStatus(tableId, status).subscribe({
      next: () => {
        this.busyId.set(null);
        this.loadAll();
      },
      error: err => {
        this.busyId.set(null);
        this.showError(err, 'Failed to update status');
      }
    });
  }

  deleteTable(table: CafeTable): void {
    if (!confirm(`Delete table ${table.tableNumber}? This cannot be undone.`)) return;
    this.busyId.set(table.id);
    this.tableService.deleteTable(table.id).subscribe({
      next: () => {
        this.uiStore.success(`Table ${table.tableNumber} deleted`);
        this.busyId.set(null);
        this.loadAll();
      },
      error: err => {
        this.busyId.set(null);
        this.showError(err, 'Failed to delete table');
      }
    });
  }

  generateTableQr(table: CafeTable): void {
    if (!confirm(`Generate a new QR for ${table.tableNumber}? Any previously printed QR for this table will stop working.`)) return;

    this.qrGeneratingId = table.id;
    this.tableService.generateQrToken(table.id).subscribe({
      next: async result => {
        const url = new URL('/dine-in', window.location.origin);
        url.searchParams.set('table', result.tableNumber);
        url.searchParams.set('outletId', result.outletId);
        url.searchParams.set('qrToken', result.qrToken);
        try {
          this.qrPreviewUrl = url.toString();
          this.qrPreviewDataUrl = await QRCode.toDataURL(this.qrPreviewUrl, { width: 360, margin: 2 });
          this.qrPreviewTable = result.tableNumber;
          this.qrGeneratingId = null;
        } catch (error) {
          this.qrGeneratingId = null;
          this.uiStore.error('The QR token was saved, but the QR image could not be generated. Try generating a new QR.');
          console.error('Failed to render table QR code', error);
        }
      },
      error: err => {
        this.qrGeneratingId = null;
        this.showError(err, 'Failed to generate table QR');
      }
    });
  }

  closeQrPreview(): void {
    this.qrPreviewTable = '';
    this.qrPreviewUrl = '';
    this.qrPreviewDataUrl = '';
  }

  downloadTableQr(): void {
    if (!this.qrPreviewDataUrl) return;
    const link = document.createElement('a');
    link.href = this.qrPreviewDataUrl;
    link.download = `table-${this.qrPreviewTable}-qr.png`;
    link.click();
  }

  async copyTableQrUrl(): Promise<void> {
    if (!this.qrPreviewUrl) return;
    try {
      await navigator.clipboard.writeText(this.qrPreviewUrl);
      this.uiStore.success('Table QR link copied');
    } catch (error) {
      this.uiStore.error('Could not copy the table QR link. Use the download or print option instead.');
      console.error('Failed to copy table QR URL', error);
    }
  }

  printTableQr(): void {
    if (!this.qrPreviewDataUrl) return;
    const printWindow = window.open('', '_blank', 'width=520,height=640');
    if (!printWindow) {
      this.uiStore.error('The print window was blocked. Allow pop-ups and try again.');
      return;
    }

    const heading = printWindow.document.createElement('h1');
    heading.textContent = `Scan to order · Table ${this.qrPreviewTable}`;
    const image = printWindow.document.createElement('img');
    image.src = this.qrPreviewDataUrl;
    image.alt = `Order using the QR code for Table ${this.qrPreviewTable}`;
    const hint = printWindow.document.createElement('p');
    hint.textContent = 'Scan with your phone camera to view the menu and order.';
    const style = printWindow.document.createElement('style');
    style.textContent = 'body{font:16px Arial,sans-serif;text-align:center;padding:32px}img{width:300px;height:300px}';
    printWindow.document.head.append(style);
    printWindow.document.body.replaceChildren(heading, image, hint);
    printWindow.document.title = `Table ${this.qrPreviewTable} QR`;
    printWindow.addEventListener('load', () => printWindow.print());
  }

  private showError(err: any, fallback: string): void {
    const serverMessage = err?.originalError?.error?.error;
    this.uiStore.error(serverMessage || fallback);
  }
}
