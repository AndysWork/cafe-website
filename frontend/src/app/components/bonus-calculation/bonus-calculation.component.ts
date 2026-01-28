import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { StaffService } from '../../services/staff.service';
import { Staff } from '../../models/staff.model';
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

  staff: Staff[] = [];
  selectedStaff: Staff | null = null;
  isLoading = false;
  errorMessage = '';
  successMessage = '';

  // Date range for calculation period
  startDate: string = '';
  endDate: string = '';

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

  // Weights (customizable)
  weights: BonusWeights = { ...DEFAULT_BONUS_WEIGHTS };
  bonusTiers: BonusTier[] = [...DEFAULT_BONUS_TIERS];

  // Calculation result
  calculationResult: BonusCalculation | null = null;

  // UI state
  showWeightSettings = false;
  showCalculationDetails = false;
  calculatedBonuses: BonusCalculation[] = [];
  totalBonusAmount = '0';

  ngOnInit(): void {
    this.loadStaff();
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

  onStaffSelect(event: Event): void {
    const staffId = (event.target as HTMLSelectElement).value;
    this.selectedStaff =
      this.staff.find((s) => (s.id || s._id) === staffId) || null;
    this.calculationResult = null;
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

    // Calculate individual scores
    const scores = {
      availableTimeScore: this.calculateAvailableTimeScore(
        this.metrics.availableTime
      ),
      kptScore: this.calculateKPTScore(this.metrics.kpt),
      orderDeliveryScore: this.calculateOrderDeliveryScore(
        this.metrics.deliveredOrderCount
      ),
      complaintScore: this.calculateComplaintScore(this.metrics.complaintCount),
      refundScore: this.calculateRefundScore(this.metrics.refundGiven),
      ratingScore: this.calculateRatingScore(this.metrics.ratingsReceived),
      stockMaintenanceScore: this.metrics.stockMaintenance,
      promptActionScore: this.metrics.promptAction,
      wastageScore: this.calculateWastageScore(this.metrics.wastageReduced),
      cleanlinessScore: this.metrics.cleanliness,
    };

    // Calculate weighted total score
    const totalScore = this.calculateWeightedScore(scores);

    // Determine bonus percentage based on tier
    const bonusTier = this.getBonusTier(totalScore);
    const bonusPercentage = bonusTier.bonusPercentage;
    const bonusAmount = (this.selectedStaff.salary * bonusPercentage) / 100;

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
      bonusPercentage,
      bonusAmount: Math.round(bonusAmount * 100) / 100,
      baseSalary: this.selectedStaff.salary,
      status: 'pending',
      calculatedAt: new Date().toISOString(),
    };

    this.showCalculationDetails = true;
    this.successMessage = 'Bonus calculated successfully!';
    setTimeout(() => (this.successMessage = ''), 3000);
  }

  // Scoring functions
  calculateAvailableTimeScore(hours: number): number {
    // Assuming 160 hours per month is 100%
    const maxHours = 160;
    return Math.min((hours / maxHours) * 100, 100);
  }

  calculateKPTScore(kpt: number): number {
    // Lower KPT is better. Assuming 15 mins is ideal, 30+ mins is poor
    if (kpt <= 15) return 100;
    if (kpt >= 30) return 0;
    return 100 - ((kpt - 15) / 15) * 100;
  }

  calculateOrderDeliveryScore(orders: number): number {
    // Assuming 100 orders per month is excellent performance
    const targetOrders = 100;
    return Math.min((orders / targetOrders) * 100, 100);
  }

  calculateComplaintScore(complaints: number): number {
    // 0 complaints = 100, 5+ complaints = 0
    if (complaints === 0) return 100;
    if (complaints >= 5) return 0;
    return 100 - complaints * 20;
  }

  calculateRefundScore(refundAmount: number): number {
    // 0 refunds = 100, 5000+ refunds = 0
    if (refundAmount === 0) return 100;
    if (refundAmount >= 5000) return 0;
    return 100 - (refundAmount / 5000) * 100;
  }

  calculateRatingScore(rating: number): number {
    // Rating out of 5, converted to 100
    return (rating / 5) * 100;
  }

  calculateWastageScore(wastageReduction: number): number {
    // Direct percentage score
    return Math.min(wastageReduction, 100);
  }

  calculateWeightedScore(scores: any): number {
    const weightedSum =
      scores.availableTimeScore * this.weights.availableTime +
      scores.kptScore * this.weights.kpt +
      scores.orderDeliveryScore * this.weights.orderDelivery +
      scores.complaintScore * this.weights.complaint +
      scores.refundScore * this.weights.refund +
      scores.ratingScore * this.weights.rating +
      scores.stockMaintenanceScore * this.weights.stockMaintenance +
      scores.promptActionScore * this.weights.promptAction +
      scores.wastageScore * this.weights.wastage +
      scores.cleanlinessScore * this.weights.cleanliness;

    const totalWeight = Object.values(this.weights).reduce(
      (sum, weight) => sum + weight,
      0
    );
    return weightedSum / totalWeight;
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
    const blob = new Blob([csv], { type: 'text/csv' });
    const url = window.URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `bonus-calculation-${
      this.calculationResult.employeeId
    }-${Date.now()}.csv`;
    link.click();
    window.URL.revokeObjectURL(url);
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
    const blob = new Blob([csv], { type: 'text/csv' });
    const url = window.URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `bonus-calculations-${Date.now()}.csv`;
    link.click();
    window.URL.revokeObjectURL(url);
  }

  deleteCalculation(index: number): void {
    if (confirm('Are you sure you want to delete this calculation?')) {
      this.calculatedBonuses.splice(index, 1);
      this.updateTotalBonusAmount();
      this.successMessage = 'Calculation deleted';
      setTimeout(() => (this.successMessage = ''), 3000);
    }
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
}
