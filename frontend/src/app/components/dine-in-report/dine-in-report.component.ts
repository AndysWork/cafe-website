import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { DineInSalesReport, DineInService } from '../../services/dine-in.service';

@Component({
  selector: 'app-dine-in-report',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './dine-in-report.component.html',
  styleUrls: ['./dine-in-report.component.scss']
})
export class DineInReportComponent implements OnInit, OnDestroy {
  private readonly dineInService = inject(DineInService);
  private readonly subscriptions = new Subscription();

  startDate = '';
  endDate = '';
  report: DineInSalesReport | null = null;
  loading = false;
  errorMessage = '';

  ngOnInit(): void {
    this.endDate = this.toLocalDateInput(new Date());
    const start = new Date();
    start.setDate(start.getDate() - 6);
    this.startDate = this.toLocalDateInput(start);
    this.loadReport();
  }

  ngOnDestroy(): void {
    this.subscriptions.unsubscribe();
  }

  loadReport(): void {
    if (!this.startDate || !this.endDate || this.startDate > this.endDate) {
      this.errorMessage = 'Choose a valid start and end date.';
      return;
    }
    this.loading = true;
    this.errorMessage = '';
    this.subscriptions.add(this.dineInService.getDineInSalesReport(this.startDate, this.endDate).subscribe({
      next: report => {
        this.report = report;
        this.loading = false;
      },
      error: error => {
        this.errorMessage = error?.error?.error || error?.message || 'Could not load the dine-in report.';
        this.loading = false;
      }
    }));
  }

  private toLocalDateInput(date: Date): string {
    const local = new Date(date.getTime() - date.getTimezoneOffset() * 60_000);
    return local.toISOString().slice(0, 10);
  }
}
