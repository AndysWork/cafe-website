import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DeliveryPartnerService, PartnerPayoutSummary } from '../../services/delivery-partner.service';
import { AttendanceService } from '../../services/attendance.service';
import { UIStore } from '../../store/ui.store';

@Component({
  selector: 'app-partner-payout-access',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './partner-payout-access.component.html',
  styleUrls: ['./partner-payout-access.component.scss']
})
export class PartnerPayoutAccessComponent implements OnInit {
  private partnerService = inject(DeliveryPartnerService);
  private attendanceService = inject(AttendanceService);
  private uiStore = inject(UIStore);

  loading = true;
  dailySummary: PartnerPayoutSummary | null = null;
  monthlySummary: PartnerPayoutSummary | null = null;
  monthlySalary = 0;
  salaryType = 'Monthly';
  salaryLoaded = false;
  fuelForm = {
    date: this.getTodayDateString(),
    petrolPricePerLitre: ''
  };
  submittingFuelPrice = false;

  ngOnInit(): void {
    this.loadSummaries();
    this.loadSalaryFromStaffData();
  }

  loadSummaries(): void {
    this.loading = true;

    this.partnerService.getMyPayoutSummary('day').subscribe({
      next: (daily) => {
        this.dailySummary = daily;
        this.partnerService.getMyPayoutSummary('month').subscribe({
          next: (monthly) => {
            this.monthlySummary = monthly;
            this.loading = false;
          },
          error: () => {
            this.uiStore.error('Failed to load monthly payout summary');
            this.loading = false;
          }
        });
      },
      error: () => {
        this.uiStore.error('Failed to load daily payout summary');
        this.loading = false;
      }
    });
  }

  get estimatedMonthlyTotal(): number {
    const distancePayout = this.monthlySummary?.payoutAmount ?? 0;
    return (this.monthlySalary || 0) + distancePayout;
  }

  submitMyFuelPrice(): void {
    const petrolPricePerLitre = Number(this.fuelForm.petrolPricePerLitre);
    if (!this.fuelForm.date || !Number.isFinite(petrolPricePerLitre) || petrolPricePerLitre <= 0) {
      this.uiStore.error('Enter valid date and fuel price');
      return;
    }

    this.submittingFuelPrice = true;
    this.partnerService.setMyDailyFuelPrice({
      date: this.fuelForm.date,
      petrolPricePerLitre
    }).subscribe({
      next: () => {
        this.uiStore.success('Fuel price submitted for the day');
        this.submittingFuelPrice = false;
        this.loadSummaries();
      },
      error: (error) => {
        this.submittingFuelPrice = false;
        this.uiStore.error(error?.error?.error || 'Fuel price already set for this day');
      }
    });
  }

  private loadSalaryFromStaffData(): void {
    this.attendanceService.getMyPayslip().subscribe({
      next: (payslip) => {
        const salary = Number(payslip?.staff?.salary ?? 0);
        const salaryType = (payslip?.staff?.salaryType || 'Monthly').trim();

        this.monthlySalary = this.estimateMonthlySalary(salary, salaryType);
        this.salaryType = salaryType;
        this.salaryLoaded = true;
      },
      error: () => {
        this.salaryLoaded = false;
        this.monthlySalary = 0;
        this.uiStore.error('Unable to fetch salary from staff data');
      }
    });
  }

  private estimateMonthlySalary(salary: number, salaryType: string): number {
    if (!Number.isFinite(salary) || salary <= 0) {
      return 0;
    }

    switch (salaryType.toLowerCase()) {
      case 'daily':
        return salary * 30;
      case 'hourly':
        return salary * 8 * 30;
      default:
        return salary;
    }
  }

  private getTodayDateString(): string {
    const now = new Date();
    const y = now.getFullYear();
    const m = String(now.getMonth() + 1).padStart(2, '0');
    const d = String(now.getDate()).padStart(2, '0');
    return `${y}-${m}-${d}`;
  }
}
