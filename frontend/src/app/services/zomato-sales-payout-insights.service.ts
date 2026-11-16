import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { catchError, map } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { handleServiceError } from '../utils/error-handler';

export interface InsightPeriod {
  startDate: string | null;
  endDate: string | null;
}

export interface OrderHistorySummary {
  totalOrders: number;
  deliveredOrders: number;
  cancelledOrRejectedOrders: number;
  grossSales: number;
  averageOrderValue: number;
  totalDiscount: number;
  promoDiscount: number;
  otherRestaurantDiscount: number;
  goldDiscount: number;
  brandPackDiscount: number;
  packagingCharges: number;
  averageDistanceKm: number;
  averageKptMinutes: number;
  averageRiderWaitMinutes: number;
  ratedOrders: number;
  averageRating: number;
  peakOrderHour: string | null;
  peakOrderHourOrders: number;
  topSalesDay: string | null;
  topSalesDayGrossSales: number;
  orderMarkingIssues: number;
  customerComplaintCount: number;
}

export interface SettlementSummary {
  totalOrders: number;
  settledOrders: number;
  unsettledOrders: number;
  netOrderValue: number;
  totalPayout: number;
  settledPayout: number;
  netDeductions: number;
  netAdditions: number;
  serviceFeeAndPmFee: number;
  taxesOnServiceFee: number;
  governmentCharges: number;
  otherOrderLevelDeductions: number;
  totalUnsettledAmount: number;
  tds194OTotal: number;
  additionalDeductionsAdjusted: number | null;
  additionalAdditionsAdjusted: number | null;
  actualPayoutCredited: number | null;
  averagePayoutPerOrder: number;
  topPayoutDay: string | null;
  topPayoutDayAmount: number;
}

export interface ReconciliationSummary {
  matchedDeliveredOrders: number;
  missingInSettlementCount: number;
  missingInOrderHistoryCount: number;
  missingInSettlementOrderIds: string[];
  missingInOrderHistoryOrderIds: string[];
  csvGrossSales: number;
  settlementPayout: number;
  grossToPayoutDifference: number;
  payoutToGrossPercent: number;
  deliveredOrderCoveragePercent: number;
}

export interface DailyInsight {
  date: string;
  orders: number;
  grossSales: number;
  discounts: number;
}

export interface DailySettlementInsight {
  date: string;
  orders: number;
  netOrderValue: number;
  payout: number;
  netDeductions: number;
}

export interface DiscountConstructInsight {
  discountConstruct: string;
  orders: number;
  grossSales: number;
  discountGiven: number;
}

export interface LossOrderInsight {
  orderId: string;
  orderDate: string | null;
  netOrderValue: number;
  netDeductions: number;
  payout: number;
  deductionRatePercent: number;
}

export interface HourlyOrderInsight {
  hourLabel: string;
  orders: number;
  grossSales: number;
  averageOrderValue: number;
}

export interface ItemSalesInsight {
  itemName: string;
  quantitySold: number;
  ordersContainingItem: number;
  estimatedGrossSales: number;
}

export interface DetailedDiscountInsight {
  discountConstruct: string;
  orders: number;
  grossSales: number;
  discountGiven: number;
  averageDiscountPerOrder: number;
  effectiveDiscountPercent: number;
}

export interface AdditionalDeductionRow {
  section: string;
  isAddition: boolean;
  type: string;
  reference: string;
  period: string;
  settlementStatus: string;
  totalAmount: number;
  adjustedAmount: number;
  outstandingAmount: number;
}

export interface AdditionalDeductionTotal {
  label: string;
  totalAmount: number;
  adjustedAmount: number;
  outstandingAmount: number;
}

export interface ZomatoSalesPayoutInsights {
  kitchenOrders?: KitchenOrderTiming[] | null;
  notices?: string[];
  period: InsightPeriod;
  orderHistorySummary: OrderHistorySummary;
  settlementSummary: SettlementSummary;
  reconciliation: ReconciliationSummary;
  dailySales: DailyInsight[];
  dailySettlement: DailySettlementInsight[];
  hourlyOrderInsights: HourlyOrderInsight[];
  itemSalesInsights: ItemSalesInsight[];
  topDiscountConstructs: DiscountConstructInsight[];
  detailedDiscountInsights: DetailedDiscountInsight[];
  additionalDeductionDetails: AdditionalDeductionRow[];
  additionalDeductionTotals: AdditionalDeductionTotal[];
  topLossOrders: LossOrderInsight[];
  reviewRatingInsights?: ReviewRatingInsights;
}

export interface KitchenOrderTiming {
  orderId: string;
  placedAt: string | null;
  kptMinutes: number | null;
  riderWaitMinutes: number | null;
  kptStatus: 'valid' | 'missing' | 'invalid';
  riderWaitStatus: 'valid' | 'missing' | 'invalid';
  timingsValidated: boolean;
  items: { name: string; quantity: number }[];
  itemsText: string;
  quantity: number;
  distinctItems: number;
  deliveryType: string;
  rating: number | null;
  review: string;
  complaint: string;
  readyMarked: string;
  orderValue: number;
}

export interface ReviewRatingInsights {
  ratedOrders: number;
  ratingCoveragePercent: number;
  averageRating: number;
  positiveRatings: number;
  neutralRatings: number;
  negativeRatings: number;
  positivePercent: number;
  negativePercent: number;
  writtenReviews: number;
  complaintOrders: number;
  averageKptLowRated: number | null;
  averageKptHighRated: number | null;
  ratingDistribution: { stars: number; count: number; percent: number }[];
  complaintTags: { tag: string; count: number }[];
  itemRatings: { itemName: string; ratedOrders: number; averageRating: number; lowRatings: number }[];
  dailyRatings: { date: string; ratedOrders: number; averageRating: number; lowRatings: number }[];
  orderFeedback: {
    orderId: string;
    orderDate: string | null;
    rating: number | null;
    review: string;
    complaintTag: string;
    items: string;
    orderValue: number;
    kptMinutes: number | null;
    orderReadyMarked: string;
  }[];
}

export interface PersistedInsightInfo {
  id: string;
  outletId: string;
  periodStartDate: string;
  periodEndDate: string;
  updatedAt: string;
}

export interface ParseResponse {
  success: boolean;
  data: ZomatoSalesPayoutInsights;
  persisted: PersistedInsightInfo;
}

interface DeleteResponse {
  success: boolean;
  deletedCount: number;
}

export interface SavedInsightSnapshot {
  id: string;
  periodStartDate: string;
  periodEndDate: string;
  updatedAt: string;
  parsedBy: string;
  data: ZomatoSalesPayoutInsights;
}

interface SnapshotListResponse {
  success: boolean;
  data: SavedInsightSnapshot[];
}

@Injectable({ providedIn: 'root' })
export class ZomatoSalesPayoutInsightsService {
  private http = inject(HttpClient);
  private apiUrl = environment.apiUrl;

  parseReports(orderHistoryFile: File, settlementFile: File): Observable<ParseResponse> {
    const formData = new FormData();
    formData.append('orderHistoryFile', orderHistoryFile);
    formData.append('settlementFile', settlementFile);

    return this.http.post<ParseResponse>(`${this.apiUrl}/reports/zomato-sales-payout/insights`, formData).pipe(
      map((response) => response),
      catchError(handleServiceError('ZomatoSalesPayoutInsightsService.parseReports'))
    );
  }

  listSavedSnapshots(): Observable<SavedInsightSnapshot[]> {
    return this.http.get<SnapshotListResponse>(`${this.apiUrl}/reports/zomato-sales-payout/insights`).pipe(
      map((response) => response.data ?? []),
      catchError(handleServiceError('ZomatoSalesPayoutInsightsService.listSavedSnapshots'))
    );
  }

  getDateRangeInsights(startDate: string, endDate: string): Observable<ZomatoSalesPayoutInsights> {
    return this.http.get<{ success: boolean; data: ZomatoSalesPayoutInsights }>(
      `${this.apiUrl}/reports/zomato-sales-payout/insights/range`,
      { params: { startDate, endDate } }
    ).pipe(
      map((response) => response.data),
      catchError(handleServiceError('ZomatoSalesPayoutInsightsService.getDateRangeInsights'))
    );
  }

  deleteSavedInsights(startDate: string, endDate: string): Observable<number> {
    return this.http
      .delete<DeleteResponse>(
        `${this.apiUrl}/reports/zomato-sales-payout/insights?startDate=${encodeURIComponent(startDate)}&endDate=${encodeURIComponent(endDate)}`
      )
      .pipe(
        map((response) => response.deletedCount ?? 0),
        catchError(handleServiceError('ZomatoSalesPayoutInsightsService.deleteSavedInsights'))
      );
  }
}
