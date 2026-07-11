import { CommonModule } from '@angular/common';
import { Component, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import jsPDF from 'jspdf';
import { AttendanceService, MyPayslipResponse } from '../../services/attendance.service';
import { UIStore } from '../../store/ui.store';

@Component({
  selector: 'app-staff-payslip',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './staff-payslip.component.html',
  styleUrls: ['./staff-payslip.component.scss']
})
export class StaffPayslipComponent implements OnInit {
  private attendanceService = inject(AttendanceService);
  private uiStore = inject(UIStore);

  loading = true;
  selectedMonth = this.currentMonthValue();
  payslip: MyPayslipResponse | null = null;

  ngOnInit(): void {
    this.loadPayslip();
  }

  loadPayslip(): void {
    this.loading = true;
    this.attendanceService.getMyPayslip(this.selectedMonth).subscribe({
      next: res => {
        this.payslip = res;
        this.loading = false;
      },
      error: () => {
        this.loading = false;
        this.uiStore.error('Unable to load payslip details');
      }
    });
  }

  downloadPdf(): void {
    if (!this.payslip) {
      this.uiStore.error('Load payslip first');
      return;
    }

    const p = this.payslip;
    const doc = new jsPDF({ unit: 'pt', format: 'a4' });
    let y = 48;

    doc.setFontSize(18);
    doc.text('Staff Payslip Summary', 40, y);

    y += 24;
    doc.setFontSize(10);
    doc.text(`Generated: ${new Date().toLocaleString()}`, 40, y);

    y += 22;
    doc.setFontSize(12);
    doc.text('Staff Information', 40, y);
    y += 16;
    doc.setFontSize(10);
    doc.text(`Name: ${p.staff.name}`, 40, y); y += 14;
    doc.text(`Position: ${p.staff.position}`, 40, y); y += 14;
    doc.text(`Employee ID: ${p.staff.employeeId || 'N/A'}`, 40, y); y += 14;
    doc.text(`Salary Plan: ${this.formatMoney(p.staff.salary)} (${p.staff.salaryType})`, 40, y);

    y += 24;
    doc.setFontSize(12);
    doc.text('Current Period Estimate', 40, y);
    y += 16;
    doc.setFontSize(10);
    doc.text(`Period: ${p.current.period}`, 40, y); y += 14;
    doc.text(`Worked Days: ${p.current.workedDays}`, 40, y); y += 14;
    doc.text(`Worked Hours: ${p.current.workedHours.toFixed(2)}`, 40, y); y += 14;
    doc.text(`Base Earnings: ${this.formatMoney(p.current.baseEarnings)}`, 40, y); y += 14;
    doc.text(`Bonus: ${this.formatMoney(p.current.bonusAmount)}`, 40, y); y += 14;
    doc.text(`Estimated Total: ${this.formatMoney(p.current.estimatedTotalEarnings)}`, 40, y);

    if (p.current.eligibleForBonus && p.current.bonusConfigurations.length > 0) {
      y += 14;
      doc.text(`Bonus Eligibility: ${p.current.bonusConfigurations.join(', ')}`, 40, y);
    }

    y += 26;
    doc.setFontSize(12);
    doc.text('Historic Payslips', 40, y);
    y += 16;
    doc.setFontSize(10);

    if (!p.history.length) {
      doc.text('No historical payslips available.', 40, y);
    } else {
      doc.text('Period | Days | Hours | Base | Bonus | Total', 40, y);
      y += 12;
      doc.line(40, y, 560, y);
      y += 12;

      for (const row of p.history.slice(0, 18)) {
        if (y > 780) {
          doc.addPage();
          y = 48;
          doc.setFontSize(10);
        }

        doc.text(
          `${row.period} | ${row.workedDays} | ${row.workedHours.toFixed(2)} | ${this.formatMoney(row.baseEarnings)} | ${this.formatMoney(row.bonusAmount)} | ${this.formatMoney(row.totalEarnings)}`,
          40,
          y
        );
        y += 14;
      }
    }

    const safeName = p.staff.name.replace(/[^a-zA-Z0-9_-]/g, '_');
    doc.save(`payslip_${safeName}_${p.current.period}.pdf`);
    this.uiStore.success('Payslip PDF downloaded');
  }

  private formatMoney(value: number): string {
    return `INR ${value.toLocaleString('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
  }

  private currentMonthValue(): string {
    const now = new Date();
    const month = String(now.getMonth() + 1).padStart(2, '0');
    return `${now.getFullYear()}-${month}`;
  }
}
