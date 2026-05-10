import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { downloadFile } from '../../utils/file-download';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { OutletService } from '../../services/outlet.service';
import { Subscription } from 'rxjs';
import { filter } from 'rxjs/operators';
import { environment } from '../../../environments/environment';
import { getIstInputDate, getIstStartOfDay, getIstEndOfDay } from '../../utils/date-utils';

interface OnlineSale {
  id: string;
  orderId: string;
  platform: string;
  customerName: string;
  orderAt: string;
  orderedItems: Array<{ itemName: string; quantity: number; menuItemId?: string }>;
  billSubTotal: number;
  packagingCharges: number;
  discountAmount: number;
  platformDeduction: number;
  payout: number;
  investment: number; // cost of goods
  rating?: number;
  discountCoupon?: string;
  freebies?: string;
}

interface OrderProfit {
  sale: OnlineSale;
  revenue: number;      // payout (what restaurant receives)
  cost: number;         // derived order making cost from recipe items
  profit: number;       // payout - investment
  margin: number;       // profit / payout * 100
  hasCost: boolean;     // derived cost available for at least one ordered item
  costCoverage: number; // percentage of ordered quantity covered by recipe making cost
}

interface ItemProfit {
  itemName: string;
  quantity: number;
  revenue: number;
  cost: number;
  profit: number;
  margin: number;
  hasCost: boolean;
}

interface PeriodSummary {
  totalOrders: number;
  totalBillValue: number;     // sum of billSubTotal
  totalPayout: number;        // sum of payout (revenue received)
  totalInvestment: number;    // sum of investment
  totalProfit: number;        // totalPayout - totalInvestment
  profitMargin: number;
  totalPlatformDeduction: number;
  totalDiscount: number;
  totalPackaging: number;
  zomatoOrders: number;
  swiggyOrders: number;
  zomatoPayout: number;
  swiggyPayout: number;
  ordersWithCost: number;     // orders where investment > 0
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

  // Filters
  dateRange = 'month';
  startDate = '';
  endDate = '';
  platform = 'all'; // all | Zomato | Swiggy

  isLoading = false;
  errorMessage = '';

  viewMode: 'summary' | 'orders' | 'items' = 'summary';

  // Search/sort for order table
  orderSearch = '';
  sortField = 'orderAt';
  sortDir: 'asc' | 'desc' = 'desc';

  // Data
  allSales: OnlineSale[] = [];
  orderProfits: OrderProfit[] = [];
  itemProfits: ItemProfit[] = [];
  summary: PeriodSummary = this.emptyS();
  private recipeCostByMenuId = new Map<string, number>();
  private recipeCostByName = new Map<string, number>();

  constructor(private http: HttpClient) {}

  ngOnInit(): void {
    this.setDefaultDateRange();
    this.outletSubscription = this.outletService.selectedOutlet$
      .pipe(filter(o => o !== null))
      .subscribe(() => this.loadData());

    if (this.outletService.getSelectedOutlet()) {
      this.loadData();
    }
  }

  ngOnDestroy(): void {
    this.outletSubscription?.unsubscribe();
  }

  private emptyS(): PeriodSummary {
    return {
      totalOrders: 0, totalBillValue: 0, totalPayout: 0,
      totalInvestment: 0, totalProfit: 0, profitMargin: 0,
      totalPlatformDeduction: 0, totalDiscount: 0, totalPackaging: 0,
      zomatoOrders: 0, swiggyOrders: 0, zomatoPayout: 0, swiggyPayout: 0,
      ordersWithCost: 0
    };
  }

  setDefaultDateRange(): void {
    const now = new Date();
    this.endDate = getIstInputDate(now);
    const startOfMonth = new Date(now.getFullYear(), now.getMonth(), 1);
    this.startDate = getIstInputDate(startOfMonth);
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
      const params: any = { startDate: this.startDate, endDate: this.endDate };
      if (this.platform !== 'all') params.platform = this.platform;

      const [salesResult, recipesResult] = await Promise.all([
        this.http.get(`${environment.apiUrl}/online-sales/date-range`, { params }).toPromise(),
        this.http.get(`${environment.apiUrl}/recipes`).toPromise().catch(() => [])
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
    } catch (err: any) {
      this.errorMessage = err?.error?.error || err?.message || 'Failed to load online sales data';
    } finally {
      this.isLoading = false;
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

    const coverage = totalQty > 0 ? (coveredQty / totalQty) * 100 : 0;
    return { cost, hasCost: coveredQty > 0, costCoverage: coverage };
  }

  private compute(): void {
    const s = this.emptyS();
    const orderProfits: OrderProfit[] = [];
    const itemMap = new Map<string, { qty: number; rev: number; cost: number; hasCost: boolean }>();

    for (const sale of this.allSales) {
      const revenue = sale.payout || 0;
      const derived = this.deriveOrderCost(sale);
      const cost = derived.cost;
      const profit = revenue - cost;
      const margin = revenue > 0 ? (profit / revenue) * 100 : 0;
      const hasCost = derived.hasCost;
      const costCoverage = derived.costCoverage;

      orderProfits.push({ sale, revenue, cost, profit, margin, hasCost, costCoverage });

      s.totalOrders++;
      s.totalBillValue += sale.billSubTotal || 0;
      s.totalPayout += revenue;
      s.totalInvestment += cost;
      s.totalPlatformDeduction += sale.platformDeduction || 0;
      s.totalDiscount += sale.discountAmount || 0;
      s.totalPackaging += sale.packagingCharges || 0;
      if (hasCost) s.ordersWithCost++;

      if (sale.platform?.toLowerCase() === 'zomato') {
        s.zomatoOrders++; s.zomatoPayout += revenue;
      } else if (sale.platform?.toLowerCase() === 'swiggy') {
        s.swiggyOrders++; s.swiggyPayout += revenue;
      }

      // Aggregate per item. Revenue/cost are distributed by ordered quantity share.
      for (const item of (sale.orderedItems || [])) {
        const key = item.itemName;
        const existing = itemMap.get(key) || { qty: 0, rev: 0, cost: 0, hasCost: false };
        const itemCount = (sale.orderedItems || []).reduce((t, i) => t + (i.quantity || 1), 0);
        const share = itemCount > 0 ? ((item.quantity || 1) / itemCount) : 0;
        const itemRev = itemCount > 0 ? revenue * share : 0;
        const itemCost = hasCost && itemCount > 0 ? cost * share : 0;
        itemMap.set(key, {
          qty: existing.qty + (item.quantity || 1),
          rev: existing.rev + itemRev,
          cost: existing.cost + itemCost,
          hasCost: existing.hasCost || hasCost
        });
      }
    }

    s.totalProfit = s.totalPayout - s.totalInvestment;
    s.profitMargin = s.totalPayout > 0 ? (s.totalProfit / s.totalPayout) * 100 : 0;

    this.summary = s;
    this.orderProfits = orderProfits;
    this.itemProfits = Array.from(itemMap.entries()).map(([itemName, d]) => ({
      itemName,
      quantity: d.qty,
      revenue: d.rev,
      cost: d.cost,
      profit: d.rev - d.cost,
      margin: d.rev > 0 ? ((d.rev - d.cost) / d.rev) * 100 : 0,
      hasCost: d.hasCost
    })).sort((a, b) => b.revenue - a.revenue);
  }

  // Filtered & sorted order list for the table
  get filteredOrders(): OrderProfit[] {
    let list = this.orderProfits;
    if (this.orderSearch.trim()) {
      const q = this.orderSearch.toLowerCase();
      list = list.filter(op =>
        op.sale.orderId?.toLowerCase().includes(q) ||
        op.sale.customerName?.toLowerCase().includes(q) ||
        op.sale.orderedItems?.some(i => i.itemName?.toLowerCase().includes(q))
      );
    }
    return list.sort((a, b) => {
      let av: any, bv: any;
      switch (this.sortField) {
        case 'orderAt': av = a.sale.orderAt; bv = b.sale.orderAt; break;
        case 'payout': av = a.revenue; bv = b.revenue; break;
        case 'profit': av = a.profit; bv = b.profit; break;
        case 'margin': av = a.margin; bv = b.margin; break;
        default: av = a.sale.orderAt; bv = b.sale.orderAt;
      }
      const cmp = av < bv ? -1 : av > bv ? 1 : 0;
      return this.sortDir === 'asc' ? cmp : -cmp;
    });
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
    return profit > 0 ? 'profit-pos' : profit < 0 ? 'profit-neg' : 'profit-zero';
  }

  marginClass(margin: number): string {
    if (margin >= 30) return 'margin-excellent';
    if (margin >= 20) return 'margin-good';
    if (margin >= 10) return 'margin-fair';
    return 'margin-poor';
  }

  itemsLabel(sale: OnlineSale): string {
    return (sale.orderedItems || []).map(i => `${i.itemName}${i.quantity > 1 ? ' ×' + i.quantity : ''}`).join(', ') || '—';
  }

  exportReport(): void {
    const s = this.summary;
    const lines: string[] = [
      `Online Profit Report`,
      `Period: ${this.startDate} to ${this.endDate}   Platform: ${this.platform}`,
      `Generated: ${new Date().toLocaleString('en-IN')}`,
      ``,
      `SUMMARY`,
      `-------`,
      `Total Orders       : ${s.totalOrders}`,
      `Total Bill Value   : ₹${s.totalBillValue.toFixed(2)}`,
      `Total Payout       : ₹${s.totalPayout.toFixed(2)}`,
      `Platform Deductions: ₹${s.totalPlatformDeduction.toFixed(2)}`,
      `Discounts          : ₹${s.totalDiscount.toFixed(2)}`,
      `Packaging          : ₹${s.totalPackaging.toFixed(2)}`,
      `Total Investment   : ₹${s.totalInvestment.toFixed(2)}  (${s.ordersWithCost}/${s.totalOrders} orders)`,
      `Total Profit       : ₹${s.totalProfit.toFixed(2)}`,
      `Profit Margin      : ${s.profitMargin.toFixed(2)}%`,
      ``,
      `PLATFORM BREAKDOWN`,
      `------------------`,
      `Zomato : ${s.zomatoOrders} orders  ₹${s.zomatoPayout.toFixed(2)} payout`,
      `Swiggy : ${s.swiggyOrders} orders  ₹${s.swiggyPayout.toFixed(2)} payout`,
      ``,
      `ORDER-WISE DETAIL`,
      `-----------------`,
      ...this.filteredOrders.map(op =>
        `${op.sale.orderAt?.slice(0,10)}  ${op.sale.platform?.padEnd(8)}  #${op.sale.orderId}  ` +
        `Payout ₹${op.revenue.toFixed(2)}  Cost ₹${op.cost.toFixed(2)}  ` +
        `Profit ₹${op.profit.toFixed(2)}  (${op.margin.toFixed(1)}%)`
      )
    ];
    downloadFile(lines.join('\n'), `online-profit-${this.startDate}-to-${this.endDate}.txt`, 'text/plain');
  }

  trackById(index: number, op: OrderProfit): string { return op.sale.id || String(index); }
  trackByName(index: number, item: ItemProfit): string { return item.itemName; }
}
