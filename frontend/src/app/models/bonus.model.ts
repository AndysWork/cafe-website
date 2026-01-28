export interface BonusCalculation {
  staffId: string;
  staffName: string;
  employeeId: string;
  position: string;
  calculationPeriod: {
    startDate: Date | string;
    endDate: Date | string;
  };

  // Performance Metrics
  metrics: {
    availableTime: number; // Hours worked in the period
    kpt: number; // Kitchen Preparation Time average (minutes)
    deliveredOrderCount: number;
    complaintCount: number;
    refundGiven: number; // Amount in currency
    ratingsReceived: number; // Average rating (0-5)
    stockMaintenance: number; // Score (0-100)
    promptAction: number; // Score (0-100)
    wastageReduced: number; // Percentage reduction
    cleanliness: number; // Score (0-100)
  };

  // Scoring Details
  scores: {
    availableTimeScore: number;
    kptScore: number;
    orderDeliveryScore: number;
    complaintScore: number;
    refundScore: number;
    ratingScore: number;
    stockMaintenanceScore: number;
    promptActionScore: number;
    wastageScore: number;
    cleanlinessScore: number;
  };

  // Weights for each metric
  weights: {
    availableTime: number;
    kpt: number;
    orderDelivery: number;
    complaint: number;
    refund: number;
    rating: number;
    stockMaintenance: number;
    promptAction: number;
    wastage: number;
    cleanliness: number;
  };

  // Final Calculation
  totalScore: number; // Out of 100
  bonusPercentage: number; // Percentage of base salary
  bonusAmount: number; // Actual bonus amount
  baseSalary: number;

  // Status
  status: 'pending' | 'approved' | 'rejected' | 'paid';
  calculatedBy?: string;
  calculatedAt?: Date | string;
  approvedBy?: string;
  approvedAt?: Date | string;
  notes?: string;
}

export interface BonusMetricInput {
  availableTime: number;
  kpt: number;
  deliveredOrderCount: number;
  complaintCount: number;
  refundGiven: number;
  ratingsReceived: number;
  stockMaintenance: number;
  promptAction: number;
  wastageReduced: number;
  cleanliness: number;
}

export interface BonusWeights {
  availableTime: number;
  kpt: number;
  orderDelivery: number;
  complaint: number;
  refund: number;
  rating: number;
  stockMaintenance: number;
  promptAction: number;
  wastage: number;
  cleanliness: number;
}

export const DEFAULT_BONUS_WEIGHTS: BonusWeights = {
  availableTime: 10,
  kpt: 12,
  orderDelivery: 15,
  complaint: 10,
  refund: 8,
  rating: 15,
  stockMaintenance: 8,
  promptAction: 7,
  wastage: 8,
  cleanliness: 7
};

export interface BonusSettings {
  minScoreForBonus: number; // Minimum total score to qualify for bonus
  maxBonusPercentage: number; // Maximum bonus as % of salary
  bonusTiers: BonusTier[];
}

export interface BonusTier {
  minScore: number;
  maxScore: number;
  bonusPercentage: number;
  label: string;
}

export const DEFAULT_BONUS_TIERS: BonusTier[] = [
  { minScore: 0, maxScore: 40, bonusPercentage: 0, label: 'No Bonus' },
  { minScore: 41, maxScore: 60, bonusPercentage: 5, label: 'Basic' },
  { minScore: 61, maxScore: 75, bonusPercentage: 10, label: 'Good' },
  { minScore: 76, maxScore: 85, bonusPercentage: 15, label: 'Very Good' },
  { minScore: 86, maxScore: 95, bonusPercentage: 20, label: 'Excellent' },
  { minScore: 96, maxScore: 100, bonusPercentage: 25, label: 'Outstanding' }
];
