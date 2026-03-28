import { Injectable } from '@angular/core';

export interface CalculatedMetrics {
  expectedHours: number;
  workedHours: number;
  overtimeHours: number;
  undertimeHours: number;
  totalLeaveHours: number;
  adjustedExpectedHours: number;
  totalOrdersPrepared: number;
  goodOrdersCount: number;
  badOrdersCount: number;
  totalRefundAmount: number;
  averageRating: number;
}

export interface BonusWeightsConfig {
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

@Injectable({ providedIn: 'root' })
export class BonusCalculationEngineService {

  // --- Performance Scoring ---

  calculateAvailableTimeScore(hours: number): number {
    const maxHours = 160;
    return Math.min((hours / maxHours) * 100, 100);
  }

  calculateKPTScore(kpt: number): number {
    if (kpt <= 15) return 100;
    if (kpt >= 30) return 0;
    return 100 - ((kpt - 15) / 15) * 100;
  }

  calculateOrderDeliveryScore(orders: number): number {
    const targetOrders = 100;
    return Math.min((orders / targetOrders) * 100, 100);
  }

  calculateComplaintScore(complaints: number): number {
    if (complaints === 0) return 100;
    if (complaints >= 5) return 0;
    return 100 - complaints * 20;
  }

  calculateRefundScore(refundAmount: number): number {
    if (refundAmount === 0) return 100;
    if (refundAmount >= 5000) return 0;
    return 100 - (refundAmount / 5000) * 100;
  }

  calculateRatingScore(rating: number): number {
    return (rating / 5) * 100;
  }

  calculateWastageScore(wastageReduction: number): number {
    return Math.min(wastageReduction, 100);
  }

  calculateWeightedScore(scores: any, weights: BonusWeightsConfig): number {
    const weightedSum =
      scores.availableTimeScore * weights.availableTime +
      scores.kptScore * weights.kpt +
      scores.orderDeliveryScore * weights.orderDelivery +
      scores.complaintScore * weights.complaint +
      scores.refundScore * weights.refund +
      scores.ratingScore * weights.rating +
      scores.stockMaintenanceScore * weights.stockMaintenance +
      scores.promptActionScore * weights.promptAction +
      scores.wastageScore * weights.wastage +
      scores.cleanlinessScore * weights.cleanliness;

    const totalWeight = Object.values(weights).reduce((sum, weight) => sum + weight, 0);
    return weightedSum / totalWeight;
  }

  // --- Work Hours Computation ---

  getDayOfWeek(date: Date): string {
    const days = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
    return days[date.getDay()];
  }

  calculateShiftExpectedHours(inTime: string, outTime: string): number {
    if (!inTime || !outTime) return 0;
    try {
      const [inHour, inMin] = inTime.split(':').map(Number);
      const [outHour, outMin] = outTime.split(':').map(Number);
      if (isNaN(inHour) || isNaN(inMin) || isNaN(outHour) || isNaN(outMin)) return 0;

      const inTimeMinutes = inHour * 60 + inMin;
      let outTimeMinutes = outHour * 60 + outMin;
      if (outTimeMinutes < inTimeMinutes) {
        outTimeMinutes += 24 * 60;
      }
      return (outTimeMinutes - inTimeMinutes) / 60;
    } catch {
      return 0;
    }
  }

  getAverageDailyWorkingHours(shifts: any[]): number {
    if (!shifts || shifts.length === 0) return 0;

    const dayMap = new Map<string, number>();
    shifts.forEach((shift: any) => {
      if (shift.isActive) {
        const shiftHours = this.calculateShiftExpectedHours(shift.startTime, shift.endTime);
        const effectiveHours = shiftHours - (shift.breakDuration / 60);
        if (dayMap.has(shift.dayOfWeek)) {
          dayMap.set(shift.dayOfWeek, dayMap.get(shift.dayOfWeek)! + effectiveHours);
        } else {
          dayMap.set(shift.dayOfWeek, effectiveHours);
        }
      }
    });

    let totalHours = 0;
    let totalDays = 0;
    dayMap.forEach((hours) => {
      totalHours += hours;
      totalDays++;
    });
    return totalDays > 0 ? totalHours / totalDays : 0;
  }

  getHourlyRate(salary: number, salaryType: string, shifts: any[]): number {
    let hourlyRate = 0;
    const dailyHours = salaryType !== 'Hourly' ? this.getAverageDailyWorkingHours(shifts) : 0;

    if (salaryType === 'Hourly') {
      hourlyRate = salary;
    } else if (salaryType === 'Daily') {
      hourlyRate = dailyHours > 0 ? salary / dailyHours : salary / 8;
    } else if (salaryType === 'Monthly') {
      const workingDaysPerMonth = 30;
      hourlyRate = dailyHours > 0 ? salary / (workingDaysPerMonth * dailyHours) : salary / (workingDaysPerMonth * 8);
    }
    return hourlyRate;
  }

  calculateExpectedHoursForPeriod(shifts: any[], startDate: string, endDate: string): number {
    if (!shifts || shifts.length === 0 || !startDate || !endDate) return 0;

    const start = new Date(startDate);
    const end = new Date(endDate);
    let totalExpectedHours = 0;

    const shiftsByDay = new Map<string, any[]>();
    shifts.forEach((shift: any) => {
      if (shift.isActive) {
        if (!shiftsByDay.has(shift.dayOfWeek)) {
          shiftsByDay.set(shift.dayOfWeek, []);
        }
        shiftsByDay.get(shift.dayOfWeek)!.push(shift);
      }
    });

    const currentDate = new Date(start);
    while (currentDate <= end) {
      const dayOfWeek = this.getDayOfWeek(currentDate);
      const shiftsForDay = shiftsByDay.get(dayOfWeek);
      if (shiftsForDay && shiftsForDay.length > 0) {
        shiftsForDay.forEach((shift: any) => {
          const shiftHours = this.calculateShiftExpectedHours(shift.startTime, shift.endTime);
          const effectiveHours = shiftHours - (shift.breakDuration / 60);
          totalExpectedHours += Math.max(0, effectiveHours);
        });
      }
      currentDate.setDate(currentDate.getDate() + 1);
    }
    return totalExpectedHours;
  }

  // --- Label Helpers ---

  getRuleTypeLabel(ruleType: string): string {
    const labels: { [key: string]: string } = {
      'OvertimeHours': 'Overtime Hours',
      'UndertimeHours': 'Undertime Hours',
      'SnacksPreparation': 'Snacks Preparation',
      'BadOrders': 'Bad Orders',
      'GoodRatings': 'Good Ratings',
      'RefundDeduction': 'Refund Deduction'
    };
    return labels[ruleType] || ruleType;
  }
}
