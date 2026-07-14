import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { Subscription } from 'rxjs';
import { filter } from 'rxjs/operators';
import { downloadFile } from '../../utils/file-download';
import { environment } from '../../../environments/environment';
import { OutletService } from '../../services/outlet.service';
import { getIstInputDate } from '../../utils/date-utils';
import { Outlet } from '../../models/outlet.model';

interface OnlineSale {
  id: string;
  orderId: string;
  platform: string;
  customerName: string;
  orderAt: string;
  distance?: number;
  orderedItems: Array<{ itemName: string; quantity: number; menuItemId?: string }>;
  billSubTotal: number;
  packagingCharges: number;
  discountAmount: number;
  platformDeduction: number;
  payout: number;
  investment: number;
  miscCharges?: number;
  freebies?: number | string;
  rating?: number;
  discountCoupon?: string;
  complain?: string;
}

interface OrderProfit {
  sale: OnlineSale;
  revenue: number;
  cogs: number;
  miscCharges: number;
  freebies: number;
  trueCost: number;
  profit: number;
  margin: number;
  hasCost: boolean;
  costCoverage: number;
  deductionRate: number;
  discountRate: number;
}

interface ItemProfit {
  itemName: string;
  quantity: number;
  revenue: number;
  cost: number;
  profit: number;
  margin: number;
  hasCost: boolean;
  quadrant: 'Star' | 'Volume Trap' | 'Hidden Gem' | 'Dead Weight';
}

interface PeriodSummary {
  totalOrders: number;
  totalBillValue: number;
  totalPayout: number;
  totalInvestment: number;
  totalMiscCharges: number;
  totalFreebies: number;
  totalTrueCost: number;
  totalProfit: number;
  profitMargin: number;
  totalPlatformDeduction: number;
  totalDiscount: number;
  totalPackaging: number;
  zomatoOrders: number;
  swiggyOrders: number;
  webSalesOrders: number;
  zomatoPayout: number;
  swiggyPayout: number;
  webSalesPayout: number;
  ordersWithCost: number;
  costCoveragePercent: number;
  negativeMarginOrders: number;
  breakEvenOrders: number;
  deductionRate: number;
  discountRate: number;
  freebieRate: number;
  miscRate: number;
  avgPayoutPerOrder: number;
  avgTrueProfitPerOrder: number;
}

interface PlatformInsight {
  platform: string;
  orders: number;
  payout: number;
  cogs: number;
  miscCharges: number;
  freebies: number;
  trueProfit: number;
  margin: number;
  deductionRate: number;
  discountRate: number;
  avgPayout: number;
  negativeOrders: number;
}

interface DaypartInsight {
  daypart: string;
  orders: number;
  payout: number;
  trueProfit: number;
  margin: number;
  avgPayout: number;
}

interface DistanceInsight {
  band: string;
  orders: number;
  payout: number;
  trueProfit: number;
  margin: number;
  avgPayout: number;
}

interface CouponInsight {
  coupon: string;
  orders: number;
  totalDiscount: number;
  payout: number;
  trueProfit: number;
  avgProfitPerOrder: number;
  discountShare: number;
  profitPerDiscount: number;
}

interface LossOrderInsight {
  orderId: string;
  platform: string;
  customerName: string;
  profit: number;
  margin: number;
  reasons: string;
}

interface PayoutVarianceSummary {
  totalOrders: number;
  expectedPayout: number;
  actualPayout: number;
  totalVariance: number;
  variancePercent: number;
  overpaidOrders: number;
  underpaidOrders: number;
  perfectMatchOrders: number;
}

interface PayoutVarianceOrder {
  orderId: string;
  platform: string;
  orderAt: string;
  expectedPayout: number;
  actualPayout: number;
  variance: number;
}

interface OutletBenchmarkRow {
  outletId: string;
  outletName: string;
  totalOrders: number;
  totalBillValue: number;
  totalPayout: number;
  expectedPayout: number;
  payoutVariance: number;
  payoutVariancePercent: number;
  totalDeduction: number;
  totalDiscount: number;
  totalPackaging: number;
  averageOrderValue: number;
}

interface AlertThresholds {
  minCostCoveragePercent: number;
  maxDeductionRate: number;
  maxDiscountRate: number;
  longDistanceKm: number;
  maxPayoutVariancePercent: number;
  minCouponProfitPerDiscount: number;
}

@Component({
  selector: 'app-online-profit-tracker',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './online-profit-tracker.component.html',
  styleUrls: ['./online-profit-tracker.component.scss']
})
export class OnlineProfitTrackerComponent implements OnInit, OnDestroy {
  private outletService = inject(OutletService);
  private outletSubscription?: Subscription;

  Math = Math;

  dateRange = 'month';
  startDate = '';
  endDate = '';
  platform = 'all';

  isLoading = false;
  errorMessage = '';
  isComparisonLoading = false;

  showThresholds = false;
  compareAcrossOutlets = true;

  alertThresholds: AlertThresholds = {
    minCostCoveragePercent: 95,
    maxDeductionRate: 22,
    maxDiscountRate: 12,
    longDistanceKm: 5,
    maxPayoutVariancePercent: 3,
    minCouponProfitPerDiscount: 1
  };

  viewMode: 'summary' | 'orders' | 'items' | 'insights' = 'summary';

  orderSearch = '';
  sortField = 'orderAt';
  sortDir: 'asc' | 'desc' = 'desc';

  allSales: OnlineSale[] = [];
  orderProfits: OrderProfit[] = [];
  itemProfits: ItemProfit[] = [];
  summary: PeriodSummary = this.emptySummary();

  platformInsights: PlatformInsight[] = [];
  daypartInsights: DaypartInsight[] = [];
  distanceInsights: DistanceInsight[] = [];
  couponInsights: CouponInsight[] = [];
  topLossOrders: LossOrderInsight[] = [];

  payoutVarianceSummary: PayoutVarianceSummary | null = null;
  payoutVarianceOrders: PayoutVarianceOrder[] = [];
  topPayoutVarianceOrders: PayoutVarianceOrder[] = [];

  availableOutlets: Outlet[] = [];
  selectedComparisonOutletIds: string[] = [];
  outletBenchmarkRows: OutletBenchmarkRow[] = [];

  private recipeCostByMenuId = new Map<string, number>();
  private recipeCostByName = new Map<string, number>();

  constructor(private http: HttpClient) {}

  ngOnInit(): void {
    this.setDefaultDateRange();
    this.loadAvailableOutlets();

    this.outletSubscription = this.outletService.selectedOutlet$
      .pipe(filter(outlet => outlet !== null))
      .subscribe(() => this.loadData());

    if (this.outletService.getSelectedOutlet()) {
      this.loadData();
    }
  }

  ngOnDestroy(): void {
    this.outletSubscription?.unsubscribe();
  }

  private emptySummary(): PeriodSummary {
    return {
      totalOrders: 0,
      totalBillValue: 0,
      totalPayout: 0,
      totalInvestment: 0,
      totalMiscCharges: 0,
      totalFreebies: 0,
      totalTrueCost: 0,
      totalProfit: 0,
      profitMargin: 0,
      totalPlatformDeduction: 0,
      totalDiscount: 0,
      totalPackaging: 0,
      zomatoOrders: 0,
      swiggyOrders: 0,
      webSalesOrders: 0,
      zomatoPayout: 0,
      swiggyPayout: 0,
      webSalesPayout: 0,
      ordersWithCost: 0,
      costCoveragePercent: 0,
      negativeMarginOrders: 0,
      breakEvenOrders: 0,
      deductionRate: 0,
      discountRate: 0,
      freebieRate: 0,
      miscRate: 0,
      avgPayoutPerOrder: 0,
      avgTrueProfitPerOrder: 0
    };
  }

  private loadAvailableOutlets(): void {
    this.outletService.getAllOutlets().subscribe(outlets => {
      this.availableOutlets = outlets || [];
      this.selectedComparisonOutletIds = this.availableOutlets
        .map(o => o.id || o._id || '')
        .filter(id => !!id);
    });
  }

  setDefaultDateRange(): void {
    const now = new Date();
    this.endDate = getIstInputDate(now);
    this.startDate = getIstInputDate(new Date(now.getFullYear(), now.getMonth(), 1));
  }

  onDateRangeChange(): void {
    const now = new Date();
    this.endDate = getIstInputDate(now);

    switch (this.dateRange) {
      case 'today':
        this.startDate = getIstInputDate(now);
        break;
      case 'week':
        this.startDate = getIstInputDate(new Date(now.getTime() - 7 * 24 * 60 * 60 * 1000));
        break;
      case 'month':
        this.startDate = getIstInputDate(new Date(now.getFullYear(), now.getMonth(), 1));
        break;
      case 'year':
        this.startDate = getIstInputDate(new Date(now.getFullYear(), 0, 1));
        break;
    }

    if (this.dateRange !== 'custom') {
      this.loadData();
    }
  }

  async loadData(): Promise<void> {
    this.isLoading = true;
    this.errorMessage = '';

    try {
      const params: any = { startDate: this.startDate, endDate: this.endDate, includeWebSales: true };
      if (this.platform !== 'all') params.platform = this.platform;

      const [salesResult, recipesResult, varianceResult] = await Promise.all([
        this.http.get(`${environment.apiUrl}/online-sales/date-range`, { params }).toPromise(),
        this.http.get(`${environment.apiUrl}/recipes`).toPromise().catch(() => []),
        this.http.get(`${environment.apiUrl}/online-sales/payout-variance`, { params }).toPromise().catch(() => null)
      ]);

      const result: any = salesResult;
      const recipes: any[] = Array.isArray(recipesResult)
        ? recipesResult
        : Array.isArray((recipesResult as any)?.data)
          ? (recipesResult as any).data
          : [];

      this.buildRecipeCostLookup(recipes);
      this.allSales = (Array.isArray(result) ? result : result?.data || []) as OnlineSale[];
      this.compute();

      const varianceData = (varianceResult as any)?.data;
      this.payoutVarianceSummary = varianceData?.summary || null;
      this.payoutVarianceOrders = varianceData?.orders || [];
      this.topPayoutVarianceOrders = this.payoutVarianceOrders.slice(0, 20);

      if (this.compareAcrossOutlets) {
        await this.loadOutletBenchmark();
      } else {
        this.outletBenchmarkRows = [];
      }
    } catch (err: any) {
      this.errorMessage = err?.error?.error || err?.message || 'Failed to load online sales data';
    } finally {
      this.isLoading = false;
    }
  }

  async loadOutletBenchmark(): Promise<void> {
    this.isComparisonLoading = true;
    try {
      const params: any = { startDate: this.startDate, endDate: this.endDate, includeWebSales: true };
      if (this.platform !== 'all') params.platform = this.platform;
      if (this.selectedComparisonOutletIds.length > 0) {
        params.outletIds = this.selectedComparisonOutletIds.join(',');
      }

      const response: any = await this.http
        .get(`${environment.apiUrl}/online-sales/benchmark`, { params })
        .toPromise();

      this.outletBenchmarkRows = response?.data || [];
    } catch (error) {
      console.error('Failed to load outlet benchmark', error);
      this.outletBenchmarkRows = [];
    } finally {
      this.isComparisonLoading = false;
    }
  }

  toggleOutletSelection(outletId: string): void {
    if (!outletId) return;
    const exists = this.selectedComparisonOutletIds.includes(outletId);
    if (exists) {
      this.selectedComparisonOutletIds = this.selectedComparisonOutletIds.filter(id => id !== outletId);
    } else {
      this.selectedComparisonOutletIds = [...this.selectedComparisonOutletIds, outletId];
    }
  }

  private buildRecipeCostLookup(recipes: any[]): void {
    this.recipeCostByMenuId.clear();
    this.recipeCostByName.clear();

    for (const recipe of recipes || []) {
      const makingCost = Number(recipe?.totalMakingCost || 0);
      if (makingCost <= 0) continue;

      const menuItemId = recipe?.menuItemId ? String(recipe.menuItemId) : '';
      const menuItemName = this.normalizeItemName(String(recipe?.menuItemName || ''));

      if (menuItemId) this.recipeCostByMenuId.set(menuItemId, makingCost);
      if (menuItemName) this.recipeCostByName.set(menuItemName, makingCost);
    }
  }

  private normalizeItemName(name: string): string {
    return (name || '').trim().toLowerCase();
  }

  private toNumber(value: unknown): number {
    if (typeof value === 'number') return Number.isFinite(value) ? value : 0;
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : 0;
  }

  private getDaypart(orderAt: string): string {
    const hour = new Date(orderAt).getHours();
    if (hour >= 6 && hour < 11) return 'Breakfast';
    if (hour >= 11 && hour < 15) return 'Lunch';
    if (hour >= 15 && hour < 18) return 'Snacks';
    if (hour >= 18 && hour < 23) return 'Dinner';
    return 'Late Night';
  }

  private getDistanceBand(distance: number): string {
    if (distance < 2) return '0-2 km';
    if (distance < 4) return '2-4 km';
    if (distance < 6) return '4-6 km';
    return '6+ km';
  }

  private deriveOrderCost(sale: OnlineSale): { cost: number; hasCost: boolean; costCoverage: number } {
    const items = sale.orderedItems || [];
    if (!items.length) return { cost: 0, hasCost: false, costCoverage: 0 };

    let totalQty = 0;
    let coveredQty = 0;
    let cost = 0;

    for (const item of items) {
      const qty = Number(item.quantity || 1);
      totalQty += qty;

      const byId = item.menuItemId ? this.recipeCostByMenuId.get(String(item.menuItemId)) : undefined;
      const byName = this.recipeCostByName.get(this.normalizeItemName(item.itemName));
      const makingCost = byId ?? byName;

      if (makingCost && makingCost > 0) {
        cost += makingCost * qty;
        coveredQty += qty;
      }
    }

    const costCoverage = totalQty > 0 ? (coveredQty / totalQty) * 100 : 0;
    return { cost, hasCost: coveredQty > 0, costCoverage };
  }

  private getLossReasons(op: OrderProfit): string {
    const reasons: string[] = [];
    if (op.deductionRate > this.alertThresholds.maxDeductionRate) reasons.push('High platform deduction');
    if (op.discountRate > this.alertThresholds.maxDiscountRate) reasons.push('High discounting');
    if ((op.sale.distance || 0) > this.alertThresholds.longDistanceKm) reasons.push('Long-distance order');
    if ((op.sale.complain || '').trim()) reasons.push('Customer complaint flagged');
    if (op.costCoverage < this.alertThresholds.minCostCoveragePercent) {
      reasons.push(`Partial cost mapping (${op.costCoverage.toFixed(0)}%)`);
    }
    return reasons.length ? reasons.join(', ') : 'COGS + charges exceeded payout';
  }

  private median(values: number[]): number {
    if (!values.length) return 0;
    const sorted = [...values].sort((a, b) => a - b);
    const mid = Math.floor(sorted.length / 2);
    return sorted.length % 2 === 0 ? (sorted[mid - 1] + sorted[mid]) / 2 : sorted[mid];
  }

  private compute(): void {
    const s = this.emptySummary();
    const orderProfits: OrderProfit[] = [];

    const itemMap = new Map<string, { qty: number; rev: number; cost: number; hasCost: boolean }>();
    const platformMap = new Map<string, { orders: number; payout: number; cogs: number; misc: number; freebies: number; bill: number; deduct: number; discount: number; negative: number }>();
    const daypartMap = new Map<string, { orders: number; payout: number; profit: number }>();
    const distanceMap = new Map<string, { orders: number; payout: number; profit: number }>();
    const couponMap = new Map<string, { orders: number; discount: number; payout: number; profit: number }>();

    let totalQty = 0;
    let coveredQty = 0;

    for (const sale of this.allSales) {
      const revenue = this.toNumber(sale.payout);
      const billValue = this.toNumber(sale.billSubTotal);
      const platformDeduction = this.toNumber(sale.platformDeduction);
      const discountAmount = this.toNumber(sale.discountAmount);
      const packaging = this.toNumber(sale.packagingCharges);
      const miscCharges = this.toNumber(sale.miscCharges);
      const freebies = this.toNumber(sale.freebies);

      const derived = this.deriveOrderCost(sale);
      const investmentFallback = this.toNumber(sale.investment);
      const hasAnyCost = derived.hasCost || investmentFallback > 0;
      const cogs = derived.hasCost ? derived.cost : investmentFallback;
      const costCoverage = derived.hasCost ? derived.costCoverage : (investmentFallback > 0 ? 100 : 0);
      const trueCost = cogs + miscCharges + freebies;
      const profit = revenue - trueCost;
      const margin = revenue > 0 ? (profit / revenue) * 100 : 0;

      const orderedQty = (sale.orderedItems || []).reduce((sum, item) => sum + Number(item.quantity || 1), 0);
      totalQty += orderedQty;
      coveredQty += orderedQty * (derived.costCoverage / 100);

      const op: OrderProfit = {
        sale,
        revenue,
        cogs,
        miscCharges,
        freebies,
        trueCost,
        profit,
        margin,
        hasCost: hasAnyCost,
        costCoverage,
        deductionRate: billValue > 0 ? (platformDeduction / billValue) * 100 : 0,
        discountRate: billValue > 0 ? (discountAmount / billValue) * 100 : 0
      };
      orderProfits.push(op);

      s.totalOrders += 1;
      s.totalBillValue += billValue;
      s.totalPayout += revenue;
      s.totalInvestment += cogs;
      s.totalMiscCharges += miscCharges;
      s.totalFreebies += freebies;
      s.totalPlatformDeduction += platformDeduction;
      s.totalDiscount += discountAmount;
      s.totalPackaging += packaging;
      if (hasAnyCost) s.ordersWithCost += 1;
      if (hasAnyCost && profit < 0) s.negativeMarginOrders += 1;
      if (hasAnyCost && Math.abs(profit) < 0.01) s.breakEvenOrders += 1;

      const platformName = (sale.platform || 'Unknown').trim();
      const p = platformMap.get(platformName) || { orders: 0, payout: 0, cogs: 0, misc: 0, freebies: 0, bill: 0, deduct: 0, discount: 0, negative: 0 };
      p.orders += 1;
      p.payout += revenue;
      p.cogs += cogs;
      p.misc += miscCharges;
      p.freebies += freebies;
      p.bill += billValue;
      p.deduct += platformDeduction;
      p.discount += discountAmount;
      if (hasAnyCost && profit < 0) p.negative += 1;
      platformMap.set(platformName, p);

      if (platformName.toLowerCase() === 'zomato') {
        s.zomatoOrders += 1;
        s.zomatoPayout += revenue;
      } else if (platformName.toLowerCase() === 'swiggy') {
        s.swiggyOrders += 1;
        s.swiggyPayout += revenue;
      } else if (platformName.toLowerCase() === 'web sales') {
        s.webSalesOrders += 1;
        s.webSalesPayout += revenue;
      }

      const daypart = this.getDaypart(sale.orderAt);
      const day = daypartMap.get(daypart) || { orders: 0, payout: 0, profit: 0 };
      day.orders += 1;
      day.payout += revenue;
      day.profit += profit;
      daypartMap.set(daypart, day);

      const distanceBand = this.getDistanceBand(this.toNumber(sale.distance));
      const dist = distanceMap.get(distanceBand) || { orders: 0, payout: 0, profit: 0 };
      dist.orders += 1;
      dist.payout += revenue;
      dist.profit += profit;
      distanceMap.set(distanceBand, dist);

      const couponCode = (sale.discountCoupon || '').trim();
      if (couponCode) {
        const c = couponMap.get(couponCode) || { orders: 0, discount: 0, payout: 0, profit: 0 };
        c.orders += 1;
        c.discount += discountAmount;
        c.payout += revenue;
        c.profit += profit;
        couponMap.set(couponCode, c);
      }

      for (const item of sale.orderedItems || []) {
        const key = item.itemName;
        const existing = itemMap.get(key) || { qty: 0, rev: 0, cost: 0, hasCost: false };
        const share = orderedQty > 0 ? Number(item.quantity || 1) / orderedQty : 0;
        itemMap.set(key, {
          qty: existing.qty + Number(item.quantity || 1),
          rev: existing.rev + revenue * share,
          cost: existing.cost + trueCost * share,
          hasCost: existing.hasCost || derived.hasCost
        });
      }
    }

    s.totalTrueCost = s.totalInvestment + s.totalMiscCharges + s.totalFreebies;
    s.totalProfit = s.totalPayout - s.totalTrueCost;
    s.profitMargin = s.totalPayout > 0 ? (s.totalProfit / s.totalPayout) * 100 : 0;
    s.costCoveragePercent = totalQty > 0 ? (coveredQty / totalQty) * 100 : 0;
    s.deductionRate = s.totalBillValue > 0 ? (s.totalPlatformDeduction / s.totalBillValue) * 100 : 0;
    s.discountRate = s.totalBillValue > 0 ? (s.totalDiscount / s.totalBillValue) * 100 : 0;
    s.freebieRate = s.totalPayout > 0 ? (s.totalFreebies / s.totalPayout) * 100 : 0;
    s.miscRate = s.totalPayout > 0 ? (s.totalMiscCharges / s.totalPayout) * 100 : 0;
    s.avgPayoutPerOrder = s.totalOrders > 0 ? s.totalPayout / s.totalOrders : 0;
    s.avgTrueProfitPerOrder = s.totalOrders > 0 ? s.totalProfit / s.totalOrders : 0;

    const unsortedItems: ItemProfit[] = Array.from(itemMap.entries()).map(([itemName, data]) => ({
      itemName,
      quantity: data.qty,
      revenue: data.rev,
      cost: data.cost,
      profit: data.rev - data.cost,
      margin: data.rev > 0 ? ((data.rev - data.cost) / data.rev) * 100 : 0,
      hasCost: data.hasCost,
      quadrant: 'Dead Weight'
    }));

    const qtyMedian = this.median(unsortedItems.map(item => item.quantity));
    const profitMedian = this.median(unsortedItems.map(item => item.profit));

    this.itemProfits = unsortedItems.map(item => {
      const highVolume = item.quantity >= qtyMedian;
      const highProfit = item.profit >= profitMedian;
      let quadrant: ItemProfit['quadrant'];
      if (highVolume && highProfit) quadrant = 'Star';
      else if (highVolume && !highProfit) quadrant = 'Volume Trap';
      else if (!highVolume && highProfit) quadrant = 'Hidden Gem';
      else quadrant = 'Dead Weight';
      return { ...item, quadrant };
    }).sort((a, b) => b.profit - a.profit);

    this.platformInsights = Array.from(platformMap.entries())
      .map(([platformName, data]) => {
        const trueProfit = data.payout - (data.cogs + data.misc + data.freebies);
        return {
          platform: platformName,
          orders: data.orders,
          payout: data.payout,
          cogs: data.cogs,
          miscCharges: data.misc,
          freebies: data.freebies,
          trueProfit,
          margin: data.payout > 0 ? (trueProfit / data.payout) * 100 : 0,
          deductionRate: data.bill > 0 ? (data.deduct / data.bill) * 100 : 0,
          discountRate: data.bill > 0 ? (data.discount / data.bill) * 100 : 0,
          avgPayout: data.orders > 0 ? data.payout / data.orders : 0,
          negativeOrders: data.negative
        };
      })
      .sort((a, b) => b.trueProfit - a.trueProfit);

    const daypartOrder = ['Breakfast', 'Lunch', 'Snacks', 'Dinner', 'Late Night'];
    this.daypartInsights = Array.from(daypartMap.entries())
      .map(([daypart, data]) => ({
        daypart,
        orders: data.orders,
        payout: data.payout,
        trueProfit: data.profit,
        margin: data.payout > 0 ? (data.profit / data.payout) * 100 : 0,
        avgPayout: data.orders > 0 ? data.payout / data.orders : 0
      }))
      .sort((a, b) => daypartOrder.indexOf(a.daypart) - daypartOrder.indexOf(b.daypart));

    const distanceOrder = ['0-2 km', '2-4 km', '4-6 km', '6+ km'];
    this.distanceInsights = Array.from(distanceMap.entries())
      .map(([band, data]) => ({
        band,
        orders: data.orders,
        payout: data.payout,
        trueProfit: data.profit,
        margin: data.payout > 0 ? (data.profit / data.payout) * 100 : 0,
        avgPayout: data.orders > 0 ? data.payout / data.orders : 0
      }))
      .sort((a, b) => distanceOrder.indexOf(a.band) - distanceOrder.indexOf(b.band));

    this.couponInsights = Array.from(couponMap.entries())
      .map(([coupon, data]) => ({
        coupon,
        orders: data.orders,
        totalDiscount: data.discount,
        payout: data.payout,
        trueProfit: data.profit,
        avgProfitPerOrder: data.orders > 0 ? data.profit / data.orders : 0,
        discountShare: s.totalDiscount > 0 ? (data.discount / s.totalDiscount) * 100 : 0,
        profitPerDiscount: data.discount > 0 ? data.profit / data.discount : 0
      }))
      .sort((a, b) => b.totalDiscount - a.totalDiscount)
      .slice(0, 15);

    this.topLossOrders = orderProfits
      .filter(op => op.hasCost)
      .sort((a, b) => a.profit - b.profit)
      .slice(0, 20)
      .map(op => ({
        orderId: op.sale.orderId,
        platform: op.sale.platform,
        customerName: op.sale.customerName || '-',
        profit: op.profit,
        margin: op.margin,
        reasons: this.getLossReasons(op)
      }));

    this.summary = s;
    this.orderProfits = orderProfits;
  }

  get filteredOrders(): OrderProfit[] {
    let list = this.orderProfits;
    if (this.orderSearch.trim()) {
      const query = this.orderSearch.toLowerCase();
      list = list.filter(op =>
        op.sale.orderId?.toLowerCase().includes(query) ||
        op.sale.customerName?.toLowerCase().includes(query) ||
        op.sale.orderedItems?.some(item => item.itemName?.toLowerCase().includes(query))
      );
    }

    return list.sort((a, b) => {
      let aVal: any;
      let bVal: any;
      switch (this.sortField) {
        case 'orderAt':
          aVal = a.sale.orderAt;
          bVal = b.sale.orderAt;
          break;
        case 'payout':
          aVal = a.revenue;
          bVal = b.revenue;
          break;
        case 'trueCost':
          aVal = a.trueCost;
          bVal = b.trueCost;
          break;
        case 'profit':
          aVal = a.profit;
          bVal = b.profit;
          break;
        case 'margin':
          aVal = a.margin;
          bVal = b.margin;
          break;
        default:
          aVal = a.sale.orderAt;
          bVal = b.sale.orderAt;
      }
      const cmp = aVal < bVal ? -1 : aVal > bVal ? 1 : 0;
      return this.sortDir === 'asc' ? cmp : -cmp;
    });
  }

  get menuStars(): ItemProfit[] {
    return this.itemProfits.filter(item => item.quadrant === 'Star').slice(0, 10);
  }

  get menuVolumeTraps(): ItemProfit[] {
    return this.itemProfits.filter(item => item.quadrant === 'Volume Trap').slice(0, 10);
  }

  get menuHiddenGems(): ItemProfit[] {
    return this.itemProfits.filter(item => item.quadrant === 'Hidden Gem').slice(0, 10);
  }

  get configuredAlerts(): string[] {
    const alerts: string[] = [];

    if (this.summary.totalOrders > 0 && this.summary.costCoveragePercent < this.alertThresholds.minCostCoveragePercent) {
      alerts.push(`Cost coverage is ${this.summary.costCoveragePercent.toFixed(1)}%. Threshold: ${this.alertThresholds.minCostCoveragePercent}%`);
    }
    if (this.summary.deductionRate > this.alertThresholds.maxDeductionRate) {
      alerts.push(`Deduction rate is ${this.summary.deductionRate.toFixed(1)}%. Threshold: ${this.alertThresholds.maxDeductionRate}%`);
    }
    if (this.summary.discountRate > this.alertThresholds.maxDiscountRate) {
      alerts.push(`Discount rate is ${this.summary.discountRate.toFixed(1)}%. Threshold: ${this.alertThresholds.maxDiscountRate}%`);
    }
    if (this.payoutVarianceSummary && Math.abs(this.payoutVarianceSummary.variancePercent) > this.alertThresholds.maxPayoutVariancePercent) {
      alerts.push(`Payout variance is ${this.payoutVarianceSummary.variancePercent.toFixed(2)}%. Threshold: ±${this.alertThresholds.maxPayoutVariancePercent}%`);
    }

    const weakCoupons = this.couponInsights.filter(c => c.profitPerDiscount < this.alertThresholds.minCouponProfitPerDiscount).length;
    if (weakCoupons > 0) {
      alerts.push(`${weakCoupons} coupon(s) have profit/discount below ${this.alertThresholds.minCouponProfitPerDiscount.toFixed(2)}`);
    }

    return alerts;
  }

  sort(field: string): void {
    if (this.sortField === field) {
      this.sortDir = this.sortDir === 'asc' ? 'desc' : 'asc';
    } else {
      this.sortField = field;
      this.sortDir = 'desc';
    }
  }

  sortIcon(field: string): string {
    if (this.sortField !== field) return '↕';
    return this.sortDir === 'asc' ? '↑' : '↓';
  }

  profitClass(profit: number): string {
    if (profit > 0) return 'profit-pos';
    if (profit < 0) return 'profit-neg';
    return 'profit-zero';
  }

  marginClass(margin: number): string {
    if (margin >= 30) return 'margin-excellent';
    if (margin >= 20) return 'margin-good';
    if (margin >= 10) return 'margin-fair';
    return 'margin-poor';
  }

  itemsLabel(sale: OnlineSale): string {
    return (sale.orderedItems || [])
      .map(item => `${item.itemName}${item.quantity > 1 ? ' ×' + item.quantity : ''}`)
      .join(', ') || '—';
  }

  exportReport(): void {
    const s = this.summary;
    const lines: string[] = [
      'Online Profit Report',
      `Period: ${this.startDate} to ${this.endDate}   Platform: ${this.platform}`,
      `Generated: ${new Date().toLocaleString('en-IN', { timeZone: 'Asia/Kolkata' })}`,
      '',
      'THRESHOLDS',
      '----------',
      `Min Cost Coverage %      : ${this.alertThresholds.minCostCoveragePercent}`,
      `Max Deduction Rate %     : ${this.alertThresholds.maxDeductionRate}`,
      `Max Discount Rate %      : ${this.alertThresholds.maxDiscountRate}`,
      `Long Distance KM         : ${this.alertThresholds.longDistanceKm}`,
      `Max Payout Variance %    : ${this.alertThresholds.maxPayoutVariancePercent}`,
      `Min Coupon Profit/Disc   : ${this.alertThresholds.minCouponProfitPerDiscount}`,
      '',
      'EXECUTIVE SUMMARY',
      '-----------------',
      `Total Orders          : ${s.totalOrders}`,
      `Total Bill Value      : ₹${s.totalBillValue.toFixed(2)}`,
      `Total Payout          : ₹${s.totalPayout.toFixed(2)}`,
      `True Profit           : ₹${s.totalProfit.toFixed(2)}`,
      `True Margin           : ${s.profitMargin.toFixed(2)}%`,
      `Cost Coverage         : ${s.costCoveragePercent.toFixed(1)}%`,
      `Deduction Rate        : ${s.deductionRate.toFixed(1)}%`,
      `Discount Rate         : ${s.discountRate.toFixed(1)}%`,
      '',
      'PAYOUT VARIANCE',
      '--------------',
      `Expected Payout       : ₹${(this.payoutVarianceSummary?.expectedPayout || 0).toFixed(2)}`,
      `Actual Payout         : ₹${(this.payoutVarianceSummary?.actualPayout || 0).toFixed(2)}`,
      `Total Variance        : ₹${(this.payoutVarianceSummary?.totalVariance || 0).toFixed(2)}`,
      `Variance %            : ${(this.payoutVarianceSummary?.variancePercent || 0).toFixed(2)}%`,
      '',
      'OUTLET BENCHMARK',
      '---------------',
      ...this.outletBenchmarkRows.map(row =>
        `${row.outletName}: Orders ${row.totalOrders}, Payout ₹${row.totalPayout.toFixed(2)}, Expected ₹${row.expectedPayout.toFixed(2)}, Variance ${row.payoutVariancePercent.toFixed(2)}%`
      ),
      '',
      'TOP LOSS ORDERS',
      '---------------',
      ...this.topLossOrders.map(order =>
        `${order.platform} #${order.orderId} Profit ₹${order.profit.toFixed(2)} (${order.margin.toFixed(1)}%) Reasons: ${order.reasons}`
      )
    ];

    downloadFile(lines.join('\n'), `online-profit-${this.startDate}-to-${this.endDate}.txt`, 'text/plain');
  }

  trackById(index: number, op: OrderProfit): string {
    return op.sale.id || String(index);
  }

  trackByName(index: number, item: ItemProfit): string {
    return item.itemName;
  }

  trackByPlatform(index: number, item: PlatformInsight): string {
    return item.platform;
  }

  trackByDaypart(index: number, item: DaypartInsight): string {
    return item.daypart;
  }

  trackByDistance(index: number, item: DistanceInsight): string {
    return item.band;
  }

  trackByCoupon(index: number, item: CouponInsight): string {
    return item.coupon;
  }

  trackByOutlet(index: number, row: OutletBenchmarkRow): string {
    return row.outletId;
  }
}
