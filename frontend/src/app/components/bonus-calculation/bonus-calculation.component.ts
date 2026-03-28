import { Component, OnInit, inject } from '@angular/core';
import { downloadFile } from '../../utils/file-download';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { StaffService } from '../../services/staff.service';
import { Staff } from '../../models/staff.model';
import { BonusConfigurationService, BonusConfiguration, BonusRule } from '../../services/bonus-configuration.service';
import { DailyPerformanceEntry } from '../../services/daily-performance.service';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { forkJoin, Observable } from 'rxjs';
import { BonusCalculationEngineService } from '../../services/bonus-calculation-engine.service';
import {
  BonusCalculation,
  BonusMetricInput,
  BonusWeights,
  DEFAULT_BONUS_WEIGHTS,
  DEFAULT_BONUS_TIERS,
  BonusTier,
} from '../../models/bonus.model';

@Component({
  selector: 'app-bonus-calculation',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './bonus-calculation.component.html',
  styleUrls: ['./bonus-calculation.component.scss'],
})
export class BonusCalculationComponent implements OnInit {
  private staffService = inject(StaffService);
  private router = inject(Router);
  private bonusConfigService = inject(BonusConfigurationService);
  private http = inject(HttpClient);
  private bonusEngine = inject(BonusCalculationEngineService);

  staff: Staff[] = [];
  selectedStaff: Staff | null = null;
  isLoading = false;
  errorMessage = '';
  successMessage = '';

  // Bonus Configurations
  bonusConfigurations: BonusConfiguration[] = [];
  selectedConfiguration: BonusConfiguration | null = null;

  // Staff salary editing
  editingSalary = false;
  tempSalary: number = 0;

  // Date range for calculation period
  startDate: string = '';
  endDate: string = '';

  // Buffer hours - overtime below this threshold won't get bonus
  bufferHours: number = 10;

  // Performance data from Daily Performance Entry
  performanceData: DailyPerformanceEntry[] = [];

  // Calculated metrics (auto-populated)
  calculatedMetrics = {
    expectedHours: 0,       // Expected hours based on shift times (inTime to outTime)
    workedHours: 0,         // Actual worked hours from shift entries
    overtimeHours: 0,       // Positive difference (worked - expected)
    undertimeHours: 0,      // Negative difference (expected - worked)
    totalLeaveHours: 0,     // Total leave hours in the period
    adjustedExpectedHours: 0, // Expected hours after deducting leave hours
    totalOrdersPrepared: 0,
    goodOrdersCount: 0,
    badOrdersCount: 0,
    totalRefundAmount: 0,
    averageRating: 0
  };

  // Metric inputs
  metrics: BonusMetricInput = {
    availableTime: 0,
    kpt: 0,
    deliveredOrderCount: 0,
    complaintCount: 0,
    refundGiven: 0,
    ratingsReceived: 0,
    stockMaintenance: 0,
    promptAction: 0,
    wastageReduced: 0,
    cleanliness: 0,
  };

  // Bonus breakdown by rule
  bonusBreakdown: Array<{
    ruleName: string;
    ruleType: string;
    calculation: string;
    amount: number;
    isBonus: boolean;
  }> = [];

  // Getter for filtered bonus breakdown (only non-zero amounts)
  get filteredBonusBreakdown() {
    return this.bonusBreakdown.filter(item => item.amount !== 0);
  }

  // Weights (customizable)
  weights: BonusWeights = { ...DEFAULT_BONUS_WEIGHTS };
  bonusTiers: BonusTier[] = [...DEFAULT_BONUS_TIERS ];

  // Calculation result
  calculationResult: BonusCalculation | null = null;

  // UI state
  showWeightSettings = false;
  showCalculationDetails = false;
  calculatedBonuses: BonusCalculation[] = [];
  totalBonusAmount = '0';

  ngOnInit(): void {
    this.loadStaff();
    this.loadBonusConfigurations();
    this.setDefaultDates();
  }

  setDefaultDates(): void {
    const today = new Date();
    const firstDayOfMonth = new Date(today.getFullYear(), today.getMonth(), 1);
    const lastDayOfMonth = new Date(
      today.getFullYear(),
      today.getMonth() + 1,
      0
    );

    this.startDate = firstDayOfMonth.toISOString().split('T')[0];
    this.endDate = lastDayOfMonth.toISOString().split('T')[0];
  }

  loadStaff(): void {
    this.isLoading = true;
    this.staffService.getAllStaff(true).subscribe({
      next: (staff) => {
        this.staff = staff;
        this.isLoading = false;
      },
      error: (error) => {
        this.errorMessage = 'Failed to load staff members';
        this.isLoading = false;
        console.error('Error loading staff:', error);
      },
    });
  }

  loadBonusConfigurations(): void {
    this.bonusConfigService.getBonusConfigurations().subscribe({
      next: (configs) => {
        this.bonusConfigurations = configs.filter(c => c.isActive);
        if (this.bonusConfigurations.length > 0) {
          this.selectedConfiguration = this.bonusConfigurations[0];
        }
      },
      error: (error) => {
        console.error('Error loading bonus configurations:', error);
      }
    });
  }

  onStaffSelect(event: Event): void {
    const staffId = (event.target as HTMLSelectElement).value;
    this.selectedStaff =
      this.staff.find((s) => (s.id || s._id) === staffId) || null;
    this.calculationResult = null;
    this.bonusBreakdown = [];

    if (this.selectedStaff) {
      this.tempSalary = this.selectedStaff.salary;
      // Reset calculated metrics before loading
      this.resetCalculatedMetrics();
      this.loadPerformanceData();
    }
  }

  loadPerformanceData(): void {
    if (!this.selectedStaff || !this.startDate || !this.endDate) return;

    this.isLoading = true;
    const token = localStorage.getItem('token');
    const headers = new HttpHeaders({
      'Authorization': `Bearer ${token}`
    });

    const staffId = this.selectedStaff.id || this.selectedStaff._id;
    if (!staffId) {
      this.errorMessage = 'Staff ID not found';
      this.isLoading = false;
      return;
    }

    // Clear previous error messages
    this.errorMessage = '';

    this.http.get<DailyPerformanceEntry[]>(
      `${environment.apiUrl}/dailyperformance/staff/${staffId}?startDate=${this.startDate}&endDate=${this.endDate}`,
      { headers }
    ).subscribe({
      next: (response) => {
        // Backend returns array directly
        this.performanceData = Array.isArray(response) ? response : [];
        this.autoPopulateMetrics();
        this.isLoading = false;

        if (this.performanceData.length === 0) {
          this.errorMessage = 'No performance data found for the selected period';
        }
      },
      error: (error) => {
        console.error('Error loading performance data:', error);
        this.errorMessage = error.status === 404
          ? 'No performance data found for this staff member'
          : `Failed to load performance data: ${error.error?.message || error.message}`;
        this.performanceData = [];
        this.resetCalculatedMetrics();
        this.isLoading = false;
      }
    });
  }

  autoPopulateMetrics(): void {
    if (this.performanceData.length === 0) {
      this.resetCalculatedMetrics();
      return;
    }

    // Calculate expected hours based on staff's configured shifts
    const expectedHours = this.calculateExpectedHoursForPeriod();

    // Calculate totals from performance data
    let workedHours = 0;       // Actual worked hours
    let totalOrdersPrepared = 0;
    let goodOrdersCount = 0;
    let badOrdersCount = 0;
    let totalRefundAmount = 0;
    let totalLeaveHours = 0;

    this.performanceData.forEach(entry => {
      // Accumulate leave hours
      totalLeaveHours += entry.leaveHours || 0;

      // Sum up shift data if available, otherwise use legacy fields
      if (entry.shifts && entry.shifts.length > 0) {
        entry.shifts.forEach(shift => {
          // Actual worked hours
          workedHours += shift.workingHours || 0;

          totalOrdersPrepared += shift.totalOrdersPrepared || 0;
          goodOrdersCount += shift.goodOrdersCount || 0;
          badOrdersCount += shift.badOrdersCount || 0;
          totalRefundAmount += shift.refundAmountRecovery || 0;
        });
      } else {
        workedHours += entry.workingHours || 0;
        totalOrdersPrepared += entry.totalOrdersPrepared || 0;
        goodOrdersCount += entry.goodOrdersCount || 0;
        badOrdersCount += entry.badOrdersCount || 0;
        totalRefundAmount += entry.refundAmountRecovery || 0;
      }
    });

    // Calculate adjusted expected hours (expected - leave hours)
    const adjustedExpectedHours = Math.max(0, expectedHours - totalLeaveHours);

    // Calculate overtime and undertime based on worked vs adjusted expected hours
    // If worked > adjustedExpected: overtime (positive difference)
    // If worked < adjustedExpected: undertime (positive difference)
    const hoursDifference = workedHours - adjustedExpectedHours;
    // Subtract buffer hours from overtime - only hours beyond buffer are eligible for bonus
    const overtimeHours = Math.max(0, hoursDifference - this.bufferHours);
    const undertimeHours = Math.max(0, -hoursDifference);

    this.calculatedMetrics = {
      expectedHours: Math.round(expectedHours * 100) / 100,
      workedHours: Math.round(workedHours * 100) / 100,
      overtimeHours: Math.round(overtimeHours * 100) / 100,
      undertimeHours: Math.round(undertimeHours * 100) / 100,
      totalLeaveHours: Math.round(totalLeaveHours * 100) / 100,
      adjustedExpectedHours: Math.round(adjustedExpectedHours * 100) / 100,
      totalOrdersPrepared,
      goodOrdersCount,
      badOrdersCount,
      totalRefundAmount: Math.round(totalRefundAmount * 100) / 100,
      averageRating: goodOrdersCount > 0 ? (goodOrdersCount / totalOrdersPrepared) * 5 : 0
    };

    // Update old metrics for backward compatibility
    this.metrics.availableTime = workedHours;
    this.metrics.deliveredOrderCount = totalOrdersPrepared;
    this.metrics.complaintCount = badOrdersCount;
    this.metrics.refundGiven = totalRefundAmount;
    this.metrics.ratingsReceived = this.calculatedMetrics.averageRating;

    this.successMessage = 'Performance data loaded and metrics auto-populated!';
    setTimeout(() => this.successMessage = '', 3000);
  }

  /**
   * Handle buffer hours change - recalculate overtime and bonus
   */
  onBufferHoursChange(): void {
    // Recalculate metrics with new buffer hours
    this.autoPopulateMetrics();

    // If there's already a calculation, recalculate the bonus
    if (this.calculationResult && this.selectedConfiguration) {
      this.calculateBonus();
    }
  }

  /**
   * Calculate expected hours for the entire period based on staff's configured shifts
   * @returns Total expected hours for the date range
   */
  calculateExpectedHoursForPeriod(): number {
    if (!this.selectedStaff || !this.startDate || !this.endDate) return 0;
    return this.bonusEngine.calculateExpectedHoursForPeriod(
      this.selectedStaff.shifts || [], this.startDate, this.endDate
    );
  }

  /**
   * Get day of week name from date
   * @param date - Date object
   * @returns Day name (Monday, Tuesday, etc.)
   */
  getDayOfWeek(date: Date): string {
    return this.bonusEngine.getDayOfWeek(date);
  }

  /**
   * Calculate expected hours for a shift based on inTime and outTime
   * @param inTime - Format: HH:mm
   * @param outTime - Format: HH:mm
   * @returns Hours as decimal (e.g., 8.5 for 8 hours 30 minutes)
   */
  calculateShiftExpectedHours(inTime: string, outTime: string): number {
    return this.bonusEngine.calculateShiftExpectedHours(inTime, outTime);
  }

  resetCalculatedMetrics(): void {
    this.calculatedMetrics = {
      expectedHours: 0,
      workedHours: 0,
      overtimeHours: 0,
      undertimeHours: 0,
      totalLeaveHours: 0,
      adjustedExpectedHours: 0,
      totalOrdersPrepared: 0,
      goodOrdersCount: 0,
      badOrdersCount: 0,
      totalRefundAmount: 0,
      averageRating: 0
    };
  }

  onDateChange(): void {
    if (this.selectedStaff && this.startDate && this.endDate) {
      this.loadPerformanceData();
    }
  }

  // Salary editing
  startEditingSalary(): void {
    this.editingSalary = true;
    this.tempSalary = this.selectedStaff?.salary || 0;
  }

  cancelEditingSalary(): void {
    this.editingSalary = false;
    this.tempSalary = this.selectedStaff?.salary || 0;
  }

  saveSalary(): void {
    if (!this.selectedStaff || !this.tempSalary || this.tempSalary <= 0) {
      this.errorMessage = 'Please enter a valid salary amount';
      return;
    }

    // Update salary locally for calculation purposes only (not saved to database)
    if (this.selectedStaff) {
      this.selectedStaff.salary = this.tempSalary;
    }
    this.editingSalary = false;
    this.successMessage = 'Salary updated for calculation';
    setTimeout(() => this.successMessage = '', 2000);
  }

  calculateBonus(): void {
    if (!this.selectedStaff) {
      this.errorMessage = 'Please select a staff member';
      return;
    }

    if (!this.startDate || !this.endDate) {
      this.errorMessage = 'Please select calculation period';
      return;
    }

    if (!this.selectedConfiguration) {
      this.errorMessage = 'No active bonus configuration found';
      return;
    }

    // Calculate bonus based on configuration rules
    this.bonusBreakdown = [];
    let totalBonusAmount = 0;
    let totalDeductionAmount = 0;

    this.selectedConfiguration.rules.forEach(rule => {
      const result = this.calculateRuleAmount(rule);
      if (result.amount !== 0) {
        this.bonusBreakdown.push({
          ruleName: this.getRuleTypeLabel(rule.ruleType),
          ruleType: rule.ruleType,
          calculation: result.calculation,
          amount: Math.abs(result.amount),
          isBonus: rule.isBonus
        });

        if (rule.isBonus) {
          totalBonusAmount += result.amount;
        } else {
          totalDeductionAmount += Math.abs(result.amount);
        }
      }
    });

    const netBonusAmount = totalBonusAmount - totalDeductionAmount;

    // Calculate traditional scores for backward compatibility
    const scores = {
      availableTimeScore: this.calculateAvailableTimeScore(this.metrics.availableTime),
      kptScore: this.calculateKPTScore(this.metrics.kpt),
      orderDeliveryScore: this.calculateOrderDeliveryScore(this.metrics.deliveredOrderCount),
      complaintScore: this.calculateComplaintScore(this.metrics.complaintCount),
      refundScore: this.calculateRefundScore(this.metrics.refundGiven),
      ratingScore: this.calculateRatingScore(this.metrics.ratingsReceived),
      stockMaintenanceScore: this.metrics.stockMaintenance,
      promptActionScore: this.metrics.promptAction,
      wastageScore: this.calculateWastageScore(this.metrics.wastageReduced),
      cleanlinessScore: this.metrics.cleanliness,
    };

    const totalScore = this.calculateWeightedScore(scores);
    const bonusTier = this.getBonusTier(totalScore);

    // Calculate total pay amount
    const totalPayAmount = this.selectedStaff.salary + netBonusAmount;

    this.calculationResult = {
      staffId: this.selectedStaff.id || this.selectedStaff._id || '',
      staffName: `${this.selectedStaff.firstName} ${this.selectedStaff.lastName}`,
      employeeId: this.selectedStaff.employeeId,
      position: this.selectedStaff.position,
      calculationPeriod: {
        startDate: this.startDate,
        endDate: this.endDate,
      },
      metrics: { ...this.metrics },
      scores,
      weights: { ...this.weights },
      totalScore: Math.round(totalScore * 100) / 100,
      bonusPercentage: bonusTier.bonusPercentage,
      bonusAmount: Math.round(netBonusAmount * 100) / 100,
      baseSalary: this.selectedStaff.salary,
      totalPayAmount: Math.round(totalPayAmount * 100) / 100,
      status: 'pending',
      calculatedAt: new Date().toISOString(),
    };

    this.showCalculationDetails = true;
    this.successMessage = 'Bonus calculated successfully!';
    setTimeout(() => (this.successMessage = ''), 3000);
  }

  calculateRuleAmount(rule: BonusRule): { amount: number; calculation: string } {
    let amount = 0;
    let calculation = '';
    const staff = this.selectedStaff!;

    switch (rule.ruleType) {
      case 'OvertimeHours':
        const overtimeHours = this.calculatedMetrics.overtimeHours;
        if (overtimeHours > 0) {
          const hourlyRate = this.getHourlyRate(staff);
          let rate = 0;

          // Check if using dynamic rate
          if (rule.useDynamicRate) {
            const multiplier = rule.rateMultiplier || 1.5;

            // Check for staff-specific override
            const override = rule.staffRateOverrides?.find(o => o.staffId === staff.id);
            rate = override ? override.customRate : (hourlyRate * multiplier);
          } else if (rule.rateAmount > 0) {
            // Use fixed rate from configuration
            rate = rule.rateAmount;
          } else {
            // Fallback: use hourly rate with multiplier if rateAmount is 0
            const multiplier = rule.rateMultiplier || 1.5;
            rate = hourlyRate * multiplier;
          }

          amount = overtimeHours * rate;
          const rawOvertime = this.calculatedMetrics.workedHours - this.calculatedMetrics.adjustedExpectedHours;
          calculation = `(${rawOvertime.toFixed(2)} - ${this.bufferHours} buffer) = ${overtimeHours.toFixed(2)} hrs × ₹${rate.toFixed(2)}`;


          if (rule.maxAmount && amount > rule.maxAmount) {
            amount = rule.maxAmount;
            calculation += ` (capped at ₹${rule.maxAmount})`;
          }
        }
        break;

      case 'UndertimeHours':
        const undertimeHours = this.calculatedMetrics.undertimeHours;
        if (undertimeHours > 0) {
          const hourlyRate = this.getHourlyRate(staff);
          let deductionRate = rule.rateAmount;

          if (rule.calculationType === 'PerHour') {
            // If rateAmount is meant as multiplier, apply to hourly rate
            // Otherwise use it as fixed rate
            if (rule.rateAmount <= 5) { // Likely a multiplier
              deductionRate = hourlyRate * rule.rateAmount;
            }
            amount = -(undertimeHours * deductionRate);
            calculation = `${undertimeHours.toFixed(2)} hrs × ₹${deductionRate.toFixed(2)}`;
          }
        }
        break;

      case 'BadOrders':
        const badOrders = this.calculatedMetrics.badOrdersCount;
        if (badOrders > 0) {
          if (rule.calculationType === 'PerUnit') {
            amount = -(badOrders * rule.rateAmount);
            calculation = `${badOrders} orders × ₹${rule.rateAmount}`;
          } else if (rule.calculationType === 'Percentage' && rule.percentageValue) {
            amount = -(staff.salary * rule.percentageValue / 100);
            calculation = `${rule.percentageValue}% of salary`;
          }
        }
        break;

      case 'GoodRatings':
        const goodOrders = this.calculatedMetrics.goodOrdersCount;
        if (goodOrders > 0 && rule.calculationType === 'PerUnit') {
          amount = goodOrders * rule.rateAmount;
          calculation = `${goodOrders} good orders × ₹${rule.rateAmount}`;

          if (rule.maxAmount && amount > rule.maxAmount) {
            amount = rule.maxAmount;
            calculation += ` (capped at ₹${rule.maxAmount})`;
          }
        }
        break;

      case 'RefundDeduction':
        const refundAmount = this.calculatedMetrics.totalRefundAmount;
        if (refundAmount > 0) {
          if (rule.calculationType === 'Percentage' && rule.percentageValue) {
            amount = -(refundAmount * rule.percentageValue / 100);
            calculation = `₹${refundAmount.toFixed(2)} × ${rule.percentageValue}%`;
          } else if (rule.calculationType === 'Fixed') {
            amount = -rule.rateAmount;
            calculation = `Fixed deduction`;
          }
        }
        break;

      case 'SnacksPreparation':
        // This would need additional data source
        calculation = 'N/A - No data available';
        break;
    }

    return { amount, calculation };
  }

  getHourlyRate(staff: Staff): number {
    return this.bonusEngine.getHourlyRate(staff.salary, staff.salaryType, staff.shifts || []);
  }

  /**
   * Calculate average daily working hours from staff's configured shifts
   * @param staff - Staff object with shifts configuration
   * @returns Average hours per working day
   */
  getAverageDailyWorkingHours(staff: Staff): number {
    return this.bonusEngine.getAverageDailyWorkingHours(staff.shifts || []);
  }

  getRuleTypeLabel(ruleType: string): string {
    return this.bonusEngine.getRuleTypeLabel(ruleType);
  }

  // Scoring functions
  calculateAvailableTimeScore(hours: number): number {
    return this.bonusEngine.calculateAvailableTimeScore(hours);
  }

  calculateKPTScore(kpt: number): number {
    return this.bonusEngine.calculateKPTScore(kpt);
  }

  calculateOrderDeliveryScore(orders: number): number {
    return this.bonusEngine.calculateOrderDeliveryScore(orders);
  }

  calculateComplaintScore(complaints: number): number {
    return this.bonusEngine.calculateComplaintScore(complaints);
  }

  calculateRefundScore(refundAmount: number): number {
    return this.bonusEngine.calculateRefundScore(refundAmount);
  }

  calculateRatingScore(rating: number): number {
    return this.bonusEngine.calculateRatingScore(rating);
  }

  calculateWastageScore(wastageReduction: number): number {
    return this.bonusEngine.calculateWastageScore(wastageReduction);
  }

  calculateWeightedScore(scores: any): number {
    return this.bonusEngine.calculateWeightedScore(scores, this.weights);
  }

  getBonusTier(score: number): BonusTier {
    const tier = this.bonusTiers.find(
      (t) => score >= t.minScore && score <= t.maxScore
    );
    return tier || this.bonusTiers[0];
  }

  getTierClass(score: number): string {
    if (score >= 96) return 'tier-outstanding';
    if (score >= 86) return 'tier-excellent';
    if (score >= 76) return 'tier-good';
    if (score >= 61) return 'tier-average';
    if (score >= 41) return 'tier-below-average';
    return 'tier-poor';
  }

  saveCalculation(): void {
    if (!this.calculationResult) return;

    this.calculatedBonuses.unshift({ ...this.calculationResult });
    this.updateTotalBonusAmount();
  }
  resetForm(): void {
    this.selectedStaff = null;
    this.metrics = {
      availableTime: 0,
      kpt: 0,
      deliveredOrderCount: 0,
      complaintCount: 0,
      refundGiven: 0,
      ratingsReceived: 0,
      stockMaintenance: 0,
      promptAction: 0,
      wastageReduced: 0,
      cleanliness: 0,
    };
    this.calculationResult = null;
    this.showCalculationDetails = false;
  }

  resetWeights(): void {
    this.weights = { ...DEFAULT_BONUS_WEIGHTS };
    this.successMessage = 'Weights reset to default values';
    setTimeout(() => (this.successMessage = ''), 3000);
  }

  exportCalculation(): void {
    if (!this.calculationResult) return;

    const csvData = [
      ['Staff Member', this.calculationResult.staffName],
      ['Employee ID', this.calculationResult.employeeId],
      ['Position', this.calculationResult.position],
      [
        'Period',
        `${this.calculationResult.calculationPeriod.startDate} to ${this.calculationResult.calculationPeriod.endDate}`,
      ],
      ['Base Salary', this.calculationResult.baseSalary.toString()],
      [''],
      ['Performance Metrics', 'Score'],
      [
        'Available Time',
        this.calculationResult.scores.availableTimeScore.toFixed(2),
      ],
      ['KPT Performance', this.calculationResult.scores.kptScore.toFixed(2)],
      [
        'Order Delivery',
        this.calculationResult.scores.orderDeliveryScore.toFixed(2),
      ],
      [
        'Complaint Score',
        this.calculationResult.scores.complaintScore.toFixed(2),
      ],
      ['Refund Score', this.calculationResult.scores.refundScore.toFixed(2)],
      ['Rating Score', this.calculationResult.scores.ratingScore.toFixed(2)],
      [
        'Stock Maintenance',
        this.calculationResult.scores.stockMaintenanceScore.toFixed(2),
      ],
      [
        'Prompt Action',
        this.calculationResult.scores.promptActionScore.toFixed(2),
      ],
      [
        'Wastage Reduction',
        this.calculationResult.scores.wastageScore.toFixed(2),
      ],
      [
        'Cleanliness',
        this.calculationResult.scores.cleanlinessScore.toFixed(2),
      ],
      [''],
      ['Total Score', this.calculationResult.totalScore.toFixed(2)],
      ['Bonus Percentage', `${this.calculationResult.bonusPercentage}%`],
      ['Bonus Amount', this.calculationResult.bonusAmount.toFixed(2)],
    ];

    const csv = csvData.map((row) => row.join(',')).join('\n');
    downloadFile(csv, `bonus-calculation-${
      this.calculationResult.employeeId
    }-${Date.now()}.csv`);
  }

  exportAllCalculations(): void {
    if (this.calculatedBonuses.length === 0) return;

    const headers = [
      'Employee ID',
      'Name',
      'Position',
      'Period',
      'Total Score',
      'Bonus %',
      'Bonus Amount',
      'Status',
    ];
    const rows = this.calculatedBonuses.map((calc) => [
      calc.employeeId,
      calc.staffName,
      calc.position,
      `${calc.calculationPeriod.startDate} to ${calc.calculationPeriod.endDate}`,
      calc.totalScore.toFixed(2),
      `${calc.bonusPercentage}%`,
      calc.bonusAmount.toFixed(2),
      calc.status,
    ]);

    const csv = [headers, ...rows].map((row) => row.join(',')).join('\n');
    downloadFile(csv, `bonus-calculations-${Date.now()}.csv`);
  }

  deleteCalculation(index: number): void {
    if (confirm('Are you sure you want to delete this calculation?')) {
      this.calculatedBonuses.splice(index, 1);
      this.updateTotalBonusAmount();
      this.successMessage = 'Calculation deleted';
      setTimeout(() => (this.successMessage = ''), 3000);
    }
  }

  exportPayslip(): void {
    if (!this.calculationResult) return;

    const payslipData = {
      Employee: this.calculationResult.staffName,
      'Employee ID': this.calculationResult.employeeId,
      Position: this.calculationResult.position,
      Period: `${this.calculationResult.calculationPeriod.startDate} to ${this.calculationResult.calculationPeriod.endDate}`,
      'Base Salary': `₹${this.calculationResult.baseSalary}`,
      'Bonus Amount': `₹${this.calculationResult.bonusAmount}`,
      'Total Pay': `₹${this.calculationResult.totalPayAmount || (this.calculationResult.baseSalary + this.calculationResult.bonusAmount)}`,
      'Generated': new Date().toLocaleString()
    };

    // Add bonus breakdown
    let breakdownText = '\n\nBonus/Deduction Breakdown:\n';
    this.bonusBreakdown.forEach(item => {
      breakdownText += `${item.ruleName}: ${item.isBonus ? '+' : '-'}₹${item.amount.toFixed(2)} (${item.calculation})\n`;
    });

    const content = Object.entries(payslipData).map(([key, value]) => `${key}: ${value}`).join('\n') + breakdownText;

    downloadFile(content, `payslip-${this.calculationResult.employeeId}-${Date.now()}.txt`, 'text/plain');
  }

  private updateTotalBonusAmount(): void {
    const total = this.calculatedBonuses.reduce(
      (sum, calc) => sum + calc.bonusAmount,
      0
    );
    this.totalBonusAmount = total.toLocaleString('en-IN', {
      maximumFractionDigits: 0,
    });
  }

  generatePayslipPDF(): void {
    if (!this.calculationResult) return;

    // Create a simple HTML-based payslip and print it
    const printWindow = window.open('', '_blank', 'width=800,height=600');
    if (!printWindow) {
      alert('Please allow popups for this website');
      return;
    }

    const netPay = this.calculationResult.baseSalary + this.calculationResult.bonusAmount;
    const filteredBreakdown = this.filteredBonusBreakdown;

    const htmlContent = `
    <!DOCTYPE html>
    <html>
    <head>
      <title>Payslip - ${this.calculationResult.employeeId}</title>
      <style>
        body {
          font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
          max-width: 800px;
          margin: 0 auto;
          padding: 40px 20px;
          color: #333;
        }
        .payslip-header {
          text-align: center;
          border-bottom: 3px solid #2c3e50;
          padding-bottom: 20px;
          margin-bottom: 30px;
        }
        .company-name {
          font-size: 28px;
          font-weight: bold;
          color: #2c3e50;
          margin-bottom: 5px;
        }
        .document-title {
          font-size: 18px;
          color: #7f8c8d;
          margin-top: 5px;
        }
        .staff-info {
          background: #ecf0f1;
          padding: 20px;
          border-radius: 8px;
          margin-bottom: 30px;
        }
        .info-row {
          display: flex;
          justify-content: space-between;
          padding: 8px 0;
          border-bottom: 1px solid #bdc3c7;
        }
        .info-row:last-child {
          border-bottom: none;
        }
        .info-label {
          font-weight: 600;
          color: #34495e;
        }
        .info-value {
          color: #2c3e50;
        }
        .section {
          margin: 30px 0;
        }
        .section-title {
          font-size: 18px;
          font-weight: bold;
          color: #2c3e50;
          margin-bottom: 15px;
          padding-bottom: 10px;
          border-bottom: 2px solid #3498db;
        }
        .breakdown-table {
          width: 100%;
          border-collapse: collapse;
        }
        .breakdown-row {
          display: flex;
          justify-content: space-between;
          padding: 12px 10px;
          border-bottom: 1px solid #ecf0f1;
        }
        .breakdown-row:hover {
          background: #f8f9fa;
        }
        .breakdown-label {
          flex: 1;
        }
        .breakdown-calc {
          color: #7f8c8d;
          font-size: 12px;
          display: block;
          margin-top: 4px;
        }
        .breakdown-amount {
          font-weight: 600;
          min-width: 120px;
          text-align: right;
        }
        .amount-positive {
          color: #27ae60;
        }
        .amount-negative {
          color: #e74c3c;
        }
        .total-section {
          background: #2c3e50;
          color: white;
          padding: 20px;
          border-radius: 8px;
          margin-top: 30px;
        }
        .total-row {
          display: flex;
          justify-content: space-between;
          align-items: center;
        }
        .total-label {
          font-size: 20px;
          font-weight: bold;
        }
        .total-amount {
          font-size: 28px;
          font-weight: bold;
        }
        .summary-grid {
          display: grid;
          grid-template-columns: repeat(2, 1fr);
          gap: 15px;
          margin-top: 30px;
        }
        .summary-item {
          background: #f8f9fa;
          padding: 15px;
          border-radius: 6px;
          border-left: 4px solid #3498db;
        }
        .summary-label {
          font-size: 12px;
          color: #7f8c8d;
          text-transform: uppercase;
          margin-bottom: 5px;
        }
        .summary-value {
          font-size: 18px;
          font-weight: 600;
          color: #2c3e50;
        }
        .footer {
          margin-top: 50px;
          padding-top: 20px;
          border-top: 2px solid #ecf0f1;
          text-align: center;
          color: #7f8c8d;
          font-size: 12px;
        }
        @media print {
          body {
            padding: 0;
          }
          .no-print {
            display: none;
          }
        }
      </style>
    </head>
    <body>
      <div class="payslip-header">
        <div class="company-name">Maa Tara Cafe</div>
        <div class="document-title">PAYSLIP</div>
      </div>

      <div class="staff-info">
        <div class="info-row">
          <span class="info-label">Employee Name:</span>
          <span class="info-value">${this.calculationResult.staffName}</span>
        </div>
        <div class="info-row">
          <span class="info-label">Employee ID:</span>
          <span class="info-value">${this.calculationResult.employeeId}</span>
        </div>
        <div class="info-row">
          <span class="info-label">Position:</span>
          <span class="info-value">${this.calculationResult.position}</span>
        </div>
        <div class="info-row">
          <span class="info-label">Pay Period:</span>
          <span class="info-value">${new Date(this.calculationResult.calculationPeriod.startDate).toLocaleDateString()} - ${new Date(this.calculationResult.calculationPeriod.endDate).toLocaleDateString()}</span>
        </div>
      </div>

      <div class="section">
        <div class="section-title">💰 Earnings</div>
        <div class="breakdown-row">
          <div class="breakdown-label">Base Salary</div>
          <div class="breakdown-amount">₹${this.calculationResult.baseSalary.toLocaleString()}</div>
        </div>
      </div>

      ${filteredBreakdown.length > 0 ? `
      <div class="section">
        <div class="section-title">✨ Bonus & Deductions</div>
        ${filteredBreakdown.map(item => `
        <div class="breakdown-row">
          <div>
            <div class="breakdown-label">${item.ruleName}</div>
            <div class="breakdown-calc">${item.calculation}</div>
          </div>
          <div class="breakdown-amount ${item.isBonus ? 'amount-positive' : 'amount-negative'}">
            ${item.isBonus ? '+' : '-'}₹${item.amount.toFixed(2)}
          </div>
        </div>
        `).join('')}
      </div>
      ` : ''}

      <div class="total-section">
        <div class="total-row">
          <span class="total-label">Net Pay</span>
          <span class="total-amount">₹${netPay.toLocaleString()}</span>
        </div>
      </div>

      <div class="summary-grid">
        <div class="summary-item">
          <div class="summary-label">Expected Hours</div>
          <div class="summary-value">${this.calculatedMetrics.expectedHours.toFixed(1)} hrs</div>
        </div>
        <div class="summary-item">
          <div class="summary-label">Worked Hours</div>
          <div class="summary-value">${this.calculatedMetrics.workedHours.toFixed(1)} hrs</div>
        </div>
        <div class="summary-item">
          <div class="summary-label">Total Orders</div>
          <div class="summary-value">${this.calculatedMetrics.totalOrdersPrepared}</div>
        </div>
        <div class="summary-item">
          <div class="summary-label">Good Orders</div>
          <div class="summary-value">${this.calculatedMetrics.goodOrdersCount}</div>
        </div>
      </div>

      <div class="footer">
        <p>Generated on ${new Date().toLocaleString()}</p>
        <p>This is a computer-generated document. No signature is required.</p>
      </div>

      <div class="no-print" style="margin-top: 30px; text-align: center;">
        <button onclick="window.print()" style="padding: 10px 30px; background: #3498db; color: white; border: none; border-radius: 5px; cursor: pointer; font-size: 16px;">
          🖨️ Print Payslip
        </button>
        <button onclick="window.close()" style="padding: 10px 30px; background: #95a5a6; color: white; border: none; border-radius: 5px; cursor: pointer; font-size: 16px; margin-left: 10px;">
          Close
        </button>
      </div>
    </body>
    </html>
    `;

    printWindow.document.write(htmlContent);
    printWindow.document.close();
  }

  /**
   * Get average bonus amount across all calculated bonuses
   */
  getAverageBonusAmount(): string {
    if (this.calculatedBonuses.length === 0) return '0';
    const total = this.calculatedBonuses.reduce((sum, calc) => sum + calc.bonusAmount, 0);
    const average = total / this.calculatedBonuses.length;
    return average.toLocaleString('en-IN', { maximumFractionDigits: 0 });
  }

  /**
   * Get the name of the top performer (highest bonus amount)
   */
  getTopPerformerName(): string {
    if (this.calculatedBonuses.length === 0) return 'N/A';
    const topPerformer = this.calculatedBonuses.reduce((prev, current) =>
      (current.bonusAmount > prev.bonusAmount) ? current : prev
    );
    return topPerformer.staffName;
  }

  trackByIndex(index: number): number { return index; }
  trackByObjId(index: number, item: any): string { return item.id; }
}
