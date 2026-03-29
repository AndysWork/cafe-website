import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ReportExportService } from '../../services/report-export.service';
import { GstReportService, GstSummary } from '../../services/gst-report.service';
import { OutletService } from '../../services/outlet.service';
import { UIStore } from '../../store/ui.store';
import { Subscription } from 'rxjs';
import { filter } from 'rxjs/operators';

@Component({
  selector: 'app-admin-report-export',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './admin-report-export.component.html',
  styleUrls: ['./admin-report-export.component.scss']
})
export class AdminReportExportComponent implements OnInit, OnDestroy {
  private outletService = inject(OutletService);
  private uiStore = inject(UIStore);
  private outletSub?: Subscription;

  startDate = '';
  endDate = '';
  exportFormat: 'csv' | 'excel' = 'excel';
  exporting = false;

  gstSummary: GstSummary | null = null;
  loadingGst = false;

  activeTab: 'reports' | 'gst' = 'reports';

  constructor(
    private reportService: ReportExportService,
    private gstService: GstReportService
  ) {
    const now = new Date();
    const firstDay = new Date(now.getFullYear(), now.getMonth(), 1);
    this.startDate = firstDay.toISOString().split('T')[0];
    this.endDate = now.toISOString().split('T')[0];
  }

  ngOnInit() {
    this.outletSub = this.outletService.selectedOutlet$
      .pipe(filter(o => o !== null))
      .subscribe(() => {});
  }

  ngOnDestroy() { this.outletSub?.unsubscribe(); }

  exportSalesReport() {
    this.exporting = true;
    this.reportService.exportSalesReport(this.startDate, this.endDate, this.exportFormat).subscribe({
      next: (blob) => {
        const ext = this.exportFormat === 'csv' ? 'csv' : 'xlsx';
        this.reportService.downloadBlob(blob, `sales-report-${this.startDate}-to-${this.endDate}.${ext}`);
        this.uiStore.success('Sales report downloaded');
        this.exporting = false;
      },
      error: () => { this.uiStore.error('Failed to export sales report'); this.exporting = false; }
    });
  }

  exportExpenseReport() {
    this.exporting = true;
    this.reportService.exportExpenseReport(this.startDate, this.endDate, this.exportFormat).subscribe({
      next: (blob) => {
        const ext = this.exportFormat === 'csv' ? 'csv' : 'xlsx';
        this.reportService.downloadBlob(blob, `expense-report-${this.startDate}-to-${this.endDate}.${ext}`);
        this.uiStore.success('Expense report downloaded');
        this.exporting = false;
      },
      error: () => { this.uiStore.error('Failed to export expense report'); this.exporting = false; }
    });
  }

  exportOrdersReport() {
    this.exporting = true;
    this.reportService.exportOrdersReport(this.startDate, this.endDate, this.exportFormat).subscribe({
      next: (blob) => {
        const ext = this.exportFormat === 'csv' ? 'csv' : 'xlsx';
        this.reportService.downloadBlob(blob, `orders-report-${this.startDate}-to-${this.endDate}.${ext}`);
        this.uiStore.success('Orders report downloaded');
        this.exporting = false;
      },
      error: () => { this.uiStore.error('Failed to export orders report'); this.exporting = false; }
    });
  }

  exportProfitLossReport() {
    this.exporting = true;
    this.reportService.exportProfitLossReport(this.startDate, this.endDate).subscribe({
      next: (blob) => {
        this.reportService.downloadBlob(blob, `profit-loss-report-${this.startDate}-to-${this.endDate}.xlsx`);
        this.uiStore.success('P&L report downloaded');
        this.exporting = false;
      },
      error: () => { this.uiStore.error('Failed to export P&L report'); this.exporting = false; }
    });
  }

  loadGstSummary() {
    this.loadingGst = true;
    this.gstService.getGstSummary(this.startDate, this.endDate).subscribe({
      next: (s) => { this.gstSummary = s; this.loadingGst = false; },
      error: () => { this.uiStore.error('Failed to load GST summary'); this.loadingGst = false; }
    });
  }

  exportGstr1() {
    this.exporting = true;
    this.gstService.exportGstr1(this.startDate, this.endDate).subscribe({
      next: (blob) => {
        this.reportService.downloadBlob(blob, `GSTR1-${this.startDate}-to-${this.endDate}.xlsx`);
        this.uiStore.success('GSTR-1 report downloaded');
        this.exporting = false;
      },
      error: () => { this.uiStore.error('Failed to export GSTR-1'); this.exporting = false; }
    });
  }
}
