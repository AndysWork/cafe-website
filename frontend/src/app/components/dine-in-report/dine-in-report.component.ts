import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { DineInSalesReport, DineInService } from '../../services/dine-in.service';
import { getIstDateString } from '../../utils/date-utils';

@Component({
  selector: 'app-dine-in-report',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './dine-in-report.component.html',
  styleUrls: ['./dine-in-report.component.scss']
})
export class DineInReportComponent implements OnInit, OnDestroy {
  private readonly dineInService = inject(DineInService);
  private reportRequest?: Subscription;

  startDate = '';
  endDate = '';
  report: DineInSalesReport | null = null;
  loading = false;
  errorMessage = '';

  ngOnInit(): void {
    this.endDate = getIstDateString();
    this.startDate = this.shiftDateInput(this.endDate, -6);
    this.loadReport();
  }

  ngOnDestroy(): void {
    this.reportRequest?.unsubscribe();
  }

  loadReport(): void {
    if (!this.startDate || !this.endDate || this.startDate > this.endDate) {
      this.errorMessage = 'Choose a valid start and end date.';
      return;
    }
    this.loading = true;
    this.errorMessage = '';
    this.report = null;
    this.reportRequest?.unsubscribe();
    this.reportRequest = this.dineInService.getDineInSalesReport(this.startDate, this.endDate).subscribe({
      next: report => {
        this.report = report;
        this.loading = false;
      },
      error: error => {
        this.errorMessage = error?.error?.error || error?.message || 'Could not load the dine-in report.';
        this.loading = false;
      }
    });
  }

  private shiftDateInput(date: string, days: number): string {
    const [year, month, day] = date.split('-').map(Number);
    const shifted = new Date(Date.UTC(year, month - 1, day + days));
    return [
      shifted.getUTCFullYear(),
      String(shifted.getUTCMonth() + 1).padStart(2, '0'),
      String(shifted.getUTCDate()).padStart(2, '0')
    ].join('-');
  }
}
