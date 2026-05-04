import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ReportExportService } from '../../services/report-export.service';
import { GstReportService, GstSummary } from '../../services/gst-report.service';
import { DailyPerformanceService, DailyPerformanceEntry } from '../../services/daily-performance.service';
import { StaffService } from '../../services/staff.service';
import { Staff } from '../../models/staff.model';
import { OutletService } from '../../services/outlet.service';
import { UIStore } from '../../store/ui.store';
import { Subscription } from 'rxjs';
import { filter } from 'rxjs/operators';
import jsPDF from 'jspdf';
import autoTable from 'jspdf-autotable';
import { getIstInputDate } from '../../utils/date-utils';

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
  private dailyPerformanceService = inject(DailyPerformanceService);
  private staffService = inject(StaffService);
  private outletSub?: Subscription;

  startDate = '';
  endDate = '';
  exportFormat: 'csv' | 'excel' = 'excel';
  exporting = false;

  gstSummary: GstSummary | null = null;
  loadingGst = false;

  activeTab: 'reports' | 'gst' | 'staff-performance' = 'reports';

  // Staff Performance tab state
  staffList: Staff[] = [];
  selectedStaffId = '';
  staffPerformanceData: DailyPerformanceEntry[] = [];
  loadingStaffPerformance = false;
  staffPerformanceStartDate = '';
  staffPerformanceEndDate = '';

  constructor(
    private reportService: ReportExportService,
    private gstService: GstReportService
  ) {
    const now = new Date();
    const firstDay = new Date(now.getFullYear(), now.getMonth(), 1);
    this.startDate = getIstInputDate(firstDay);
    this.endDate = getIstInputDate(now);
    this.staffPerformanceStartDate = this.startDate;
    this.staffPerformanceEndDate = this.endDate;
  }

  ngOnInit() {
    this.outletSub = this.outletService.selectedOutlet$
      .pipe(filter(o => o !== null))
      .subscribe(() => {});
    this.loadStaffList();
  }

  ngOnDestroy() { this.outletSub?.unsubscribe(); }

  private loadStaffList() {
    this.staffService.getAllStaff(true).subscribe({
      next: (staff) => this.staffList = staff,
      error: () => this.uiStore.error('Failed to load staff list')
    });
  }

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

  // Staff Performance methods
  fetchStaffPerformance() {
    if (!this.staffPerformanceStartDate || !this.staffPerformanceEndDate) {
      this.uiStore.error('Please select start and end dates');
      return;
    }

    this.loadingStaffPerformance = true;
    this.staffPerformanceData = [];

    if (this.selectedStaffId) {
      this.dailyPerformanceService.getDailyPerformanceByStaff(
        this.selectedStaffId, this.staffPerformanceStartDate, this.staffPerformanceEndDate
      ).subscribe({
        next: (data) => { this.staffPerformanceData = data; this.loadingStaffPerformance = false; },
        error: () => { this.uiStore.error('Failed to load staff performance data'); this.loadingStaffPerformance = false; }
      });
    } else {
      this.dailyPerformanceService.getDailyPerformanceByDateRange(
        this.staffPerformanceStartDate, this.staffPerformanceEndDate
      ).subscribe({
        next: (data) => { this.staffPerformanceData = data; this.loadingStaffPerformance = false; },
        error: () => { this.uiStore.error('Failed to load staff performance data'); this.loadingStaffPerformance = false; }
      });
    }
  }

  private getPerformanceTableRows(entry: DailyPerformanceEntry): string[][] {
    const rows: string[][] = [];
    if (entry.shifts && entry.shifts.length > 0) {
      for (const shift of entry.shifts) {
        rows.push([
          entry.date,
          entry.staffName || entry.staffId,
          shift.shiftName,
          shift.inTime,
          shift.outTime,
          (shift.workingHours ?? 0).toString(),
          shift.totalOrdersPrepared.toString(),
          shift.goodOrdersCount.toString(),
          shift.badOrdersCount.toString(),
          shift.refundAmountRecovery.toString(),
          shift.notes || ''
        ]);
      }
    } else {
      rows.push([
        entry.date,
        entry.staffName || entry.staffId,
        '-',
        entry.inTime || '-',
        entry.outTime || '-',
        (entry.workingHours ?? 0).toString(),
        entry.totalOrdersPrepared.toString(),
        entry.goodOrdersCount.toString(),
        entry.badOrdersCount.toString(),
        entry.refundAmountRecovery.toString(),
        entry.notes || ''
      ]);
    }
    return rows;
  }

  private getPerformanceHeaders(): string[] {
    return ['Date', 'Staff Name', 'Shift', 'In Time', 'Out Time', 'Working Hrs', 'Orders', 'Good', 'Bad', 'Refund Amt', 'Notes'];
  }

  exportStaffPerformanceCsv() {
    if (this.staffPerformanceData.length === 0) {
      this.uiStore.error('No data to export. Please fetch data first.');
      return;
    }

    const headers = this.getPerformanceHeaders();
    const rows: string[][] = [];
    for (const entry of this.staffPerformanceData) {
      rows.push(...this.getPerformanceTableRows(entry));
    }

    const csvContent = [
      headers.join(','),
      ...rows.map(row => row.map(cell => `"${(cell || '').replace(/"/g, '""')}"`).join(','))
    ].join('\n');

    const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' });
    const staffLabel = this.selectedStaffId ? `staff-${this.selectedStaffId}` : 'all-staff';
    this.reportService.downloadBlob(blob, `staff-performance-${staffLabel}-${this.staffPerformanceStartDate}-to-${this.staffPerformanceEndDate}.csv`);
    this.uiStore.success('CSV exported successfully');
  }

  exportStaffPerformancePdf() {
    if (this.staffPerformanceData.length === 0) {
      this.uiStore.error('No data to export. Please fetch data first.');
      return;
    }

    const doc = new jsPDF({ orientation: 'landscape' });
    const staffLabel = this.selectedStaffId
      ? (this.staffList.find(s => (s.id || s._id) === this.selectedStaffId)?.firstName + ' ' +
         this.staffList.find(s => (s.id || s._id) === this.selectedStaffId)?.lastName)
      : 'All Staff';
    doc.setFontSize(16);
    doc.text('Daily Staff Performance Report', 14, 15);
    doc.setFontSize(10);
    doc.text(`Period: ${this.staffPerformanceStartDate} to ${this.staffPerformanceEndDate}`, 14, 22);
    doc.text(`Employee: ${staffLabel}`, 14, 28);

    const headers = this.getPerformanceHeaders();
    const rows: string[][] = [];
    for (const entry of this.staffPerformanceData) {
      rows.push(...this.getPerformanceTableRows(entry));
    }

    autoTable(doc, {
      head: [headers],
      body: rows,
      startY: 34,
      styles: { fontSize: 8 },
      headStyles: { fillColor: [244, 63, 94] }
    });

    const fileStaffLabel = this.selectedStaffId ? `staff-${this.selectedStaffId}` : 'all-staff';
    doc.save(`staff-performance-${fileStaffLabel}-${this.staffPerformanceStartDate}-to-${this.staffPerformanceEndDate}.pdf`);
    this.uiStore.success('PDF exported successfully');
  }

  getTotalOrders(): number {
    return this.staffPerformanceData.reduce((sum, e) => {
      if (e.shifts && e.shifts.length > 0) {
        return sum + e.shifts.reduce((s, sh) => s + sh.totalOrdersPrepared, 0);
      }
      return sum + e.totalOrdersPrepared;
    }, 0);
  }

  getTotalGoodOrders(): number {
    return this.staffPerformanceData.reduce((sum, e) => {
      if (e.shifts && e.shifts.length > 0) {
        return sum + e.shifts.reduce((s, sh) => s + sh.goodOrdersCount, 0);
      }
      return sum + e.goodOrdersCount;
    }, 0);
  }

  getTotalBadOrders(): number {
    return this.staffPerformanceData.reduce((sum, e) => {
      if (e.shifts && e.shifts.length > 0) {
        return sum + e.shifts.reduce((s, sh) => s + sh.badOrdersCount, 0);
      }
      return sum + e.badOrdersCount;
    }, 0);
  }

  getTotalRefundAmount(): number {
    return this.staffPerformanceData.reduce((sum, e) => {
      if (e.shifts && e.shifts.length > 0) {
        return sum + e.shifts.reduce((s, sh) => s + sh.refundAmountRecovery, 0);
      }
      return sum + e.refundAmountRecovery;
    }, 0);
  }
}
