import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, Subject, throwError } from 'rxjs';
import { ZomatoSalesPayoutParserComponent } from './zomato-sales-payout-parser.component';
import {
  SavedInsightSnapshot,
  ZomatoSalesPayoutInsights,
  ZomatoSalesPayoutInsightsService
} from '../../services/zomato-sales-payout-insights.service';
import { UIStore } from '../../store/ui.store';

function report(): ZomatoSalesPayoutInsights {
  return {
    period: { startDate: '2026-10-26', endDate: '2026-11-01' },
    orderHistorySummary: {
      totalOrders: 2, deliveredOrders: 2, cancelledOrRejectedOrders: 0,
      grossSales: 200, averageOrderValue: 100, totalDiscount: 20,
      promoDiscount: 20, otherRestaurantDiscount: 0, goldDiscount: 0, brandPackDiscount: 0,
      packagingCharges: 10, averageDistanceKm: 1, averageKptMinutes: 10,
      averageRiderWaitMinutes: 0, ratedOrders: 0, averageRating: 0,
      peakOrderHour: null, peakOrderHourOrders: 0, topSalesDay: null,
      topSalesDayGrossSales: 0, orderMarkingIssues: 0, customerComplaintCount: 0
    },
    settlementSummary: {
      totalOrders: 2, settledOrders: 2, unsettledOrders: 0, netOrderValue: 200,
      totalPayout: 160, settledPayout: 160, netDeductions: 40, netAdditions: 0,
      serviceFeeAndPmFee: 20, taxesOnServiceFee: 0, governmentCharges: 0,
      otherOrderLevelDeductions: 0, totalUnsettledAmount: 0, tds194OTotal: 0,
      additionalDeductionsAdjusted: 10, additionalAdditionsAdjusted: 0,
      actualPayoutCredited: 150, averagePayoutPerOrder: 80, topPayoutDay: null,
      topPayoutDayAmount: 0
    },
    reconciliation: {
      matchedDeliveredOrders: 2, missingInSettlementCount: 0, missingInOrderHistoryCount: 0,
      missingInSettlementOrderIds: [], missingInOrderHistoryOrderIds: [],
      csvGrossSales: 200, settlementPayout: 160, grossToPayoutDifference: 40,
      payoutToGrossPercent: 80, deliveredOrderCoveragePercent: 100
    },
    dailySales: [], dailySettlement: [], hourlyOrderInsights: [], itemSalesInsights: [],
    topDiscountConstructs: [], detailedDiscountInsights: [], additionalDeductionDetails: [],
    additionalDeductionTotals: [], topLossOrders: []
  };
}

function missingSourceError() {
  return {
    status: 409,
    message: '[ZomatoSalesPayoutInsightsService.getDateRangeInsights] Conflict: missing source coverage',
    originalError: {
      error: { error: 'No order-level source coverage from 2026-11-02. Upload reports covering the selected dates.' }
    }
  };
}

describe('Zomato parser custom-date reports', () => {
  let fixture: ComponentFixture<ZomatoSalesPayoutParserComponent>;
  let component: ZomatoSalesPayoutParserComponent;
  let service: jasmine.SpyObj<ZomatoSalesPayoutInsightsService>;
  let ui: jasmine.SpyObj<UIStore>;
  let snapshot: SavedInsightSnapshot;

  beforeEach(async () => {
    snapshot = {
      id: 'saved-week', periodStartDate: '2026-10-26', periodEndDate: '2026-11-01',
      updatedAt: '2026-11-02T10:00:00Z', parsedBy: 'admin', data: report()
    };
    service = jasmine.createSpyObj<ZomatoSalesPayoutInsightsService>(
      'insights', ['listSavedSnapshots', 'getDateRangeInsights', 'parseReports', 'deleteSavedInsights']
    );
    service.listSavedSnapshots.and.returnValue(of([snapshot]));
    ui = jasmine.createSpyObj<UIStore>('UIStore', ['warning', 'error', 'success']);
    await TestBed.configureTestingModule({
      imports: [ZomatoSalesPayoutParserComponent],
      providers: [
        { provide: ZomatoSalesPayoutInsightsService, useValue: service },
        { provide: UIStore, useValue: ui }
      ]
    }).compileComponents();
    fixture = TestBed.createComponent(ZomatoSalesPayoutParserComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('keeps date controls collapsed until the toolbar button is clicked', () => {
    const root = fixture.nativeElement as HTMLElement;
    const trigger = root.querySelector<HTMLButtonElement>('.zp-toolbar .zp-range-trigger')!;
    expect(root.querySelector('.zp-range-panel')).toBeNull();
    expect(trigger.getAttribute('aria-expanded')).toBe('false');
    trigger.click();
    fixture.detectChanges();
    expect(root.querySelector('#desktop-date-range input[type="date"]')).not.toBeNull();
    expect(trigger.getAttribute('aria-expanded')).toBe('true');
    expect(service.getDateRangeInsights).not.toHaveBeenCalled();
  });

  it('applies the picker dates, populates the report and collapses the picker', async () => {
    const root = fixture.nativeElement as HTMLElement;
    const trigger = root.querySelector<HTMLButtonElement>('.zp-toolbar .zp-range-trigger')!;
    const data = report();
    data.period = { startDate: '2026-10-28', endDate: '2026-10-30' };
    data.orderHistorySummary.grossSales = 80;
    service.getDateRangeInsights.and.returnValue(of(data));
    trigger.click();
    fixture.detectChanges();
    await fixture.whenStable();
    const panel = root.querySelector<HTMLElement>('#desktop-date-range')!;
    const from = panel.querySelector<HTMLInputElement>('input[name="reportStartDate"]')!;
    const to = panel.querySelector<HTMLInputElement>('input[name="reportEndDate"]')!;
    from.value = '2026-10-28';
    from.dispatchEvent(new Event('input', { bubbles: true }));
    to.value = '2026-10-30';
    to.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    panel.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
    fixture.detectChanges();

    expect(service.getDateRangeInsights).toHaveBeenCalledOnceWith('2026-10-28', '2026-10-30');
    expect(component.kpis.find(k => k.key === 'gross')?.value).toBe(component.formatCurrency(80));
    expect(root.querySelector('.zp-range-panel')).toBeNull();
    expect(trigger.textContent).toContain(component.customRangeLabel);
  });

  it('dismisses the picker without applying and restores the applied dates on reopening', () => {
    const root = fixture.nativeElement as HTMLElement;
    const trigger = root.querySelector<HTMLButtonElement>('.zp-toolbar .zp-range-trigger')!;
    trigger.click();
    fixture.detectChanges();
    component.reportStartDate = '2026-10-29';
    root.querySelector<HTMLButtonElement>('#desktop-date-range .zp-btn.outline')!.click();
    fixture.detectChanges();
    expect(component.dateRangeOpen).toBeFalse();
    trigger.click();
    fixture.detectChanges();
    expect(component.reportStartDate).toBe(snapshot.periodStartDate);
    document.body.click();
    fixture.detectChanges();
    expect(root.querySelector('.zp-range-panel')).toBeNull();
    trigger.click();
    fixture.detectChanges();
    trigger.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();
    expect(component.dateRangeOpen).toBeFalse();
    expect(service.getDateRangeInsights).not.toHaveBeenCalled();
  });

  it('loads exact dates and hides unknown bank credit rather than displaying zero', () => {
    const data = report();
    data.period = { startDate: '2026-10-28', endDate: '2026-10-28' };
    data.settlementSummary.additionalDeductionsAdjusted = null;
    data.settlementSummary.additionalAdditionsAdjusted = null;
    data.settlementSummary.actualPayoutCredited = null;
    data.notices = ['Weekly adjustments cannot be assigned exactly to these dates.'];
    service.getDateRangeInsights.and.returnValue(of(data));
    component.reportStartDate = '2026-10-28';
    component.reportEndDate = '2026-10-28';
    component.applyDateRange();
    fixture.detectChanges();

    expect(service.getDateRangeInsights).toHaveBeenCalledOnceWith('2026-10-28', '2026-10-28');
    expect(component.isCustomRange).toBeTrue();
    expect(component.selectedSnapshotId).toBe('');
    expect(component.kpis.find(k => k.key === 'credited')?.value).toBe('—');
    expect(component.waterfallBars.some(b => b.kind === 'credit')).toBeFalse();
    expect(component.previousSnapshot).toBeNull();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain(data.notices[0]);
    expect(component.snapshots[0].data.settlementSummary.actualPayoutCredited).toBe(150);
  });

  it('rejects invalid date ranges without making a request', () => {
    component.reportStartDate = '2026-10-29';
    component.reportEndDate = '2026-10-28';
    component.applyDateRange();
    expect(service.getDateRangeInsights).not.toHaveBeenCalled();
    expect(ui.warning).toHaveBeenCalled();
    expect(component.insights).toBe(snapshot.data);
  });

  it('keeps the previous report and displays a source-coverage error', () => {
    service.getDateRangeInsights.and.returnValue(throwError(() => ({ message: 'Re-upload source reports.' })));
    component.applyDateRange();
    fixture.detectChanges();
    expect(component.insights).toBe(snapshot.data);
    expect(component.isLoadingRange).toBeFalse();
    expect(component.isCustomRange).toBeFalse();
    expect(component.dateRangeOpen).toBeTrue();
    expect((fixture.nativeElement as HTMLElement).querySelector('[role="alert"]')?.textContent)
      .toContain('Re-upload source reports.');
  });

  it('prevents a pending range response from competing with snapshot loading or parsing', () => {
    const response = new Subject<ZomatoSalesPayoutInsights>();
    service.getDateRangeInsights.and.returnValue(response);
    component.applyDateRange();
    component.loadSnapshot({ ...snapshot, id: 'other' });
    component.parseReports();
    expect(component.isLoadingRange).toBeTrue();
    expect(component.dateRangeOpen).toBeTrue();
    expect(component.selectedSnapshotId).toBe('saved-week');
    expect(service.parseReports).not.toHaveBeenCalled();
    response.next(report());
    response.complete();
    expect(component.isLoadingRange).toBeFalse();
    expect(component.dateRangeOpen).toBeFalse();
  });

  it('does not save or delete a custom view and can return to the original snapshot', () => {
    service.getDateRangeInsights.and.returnValue(of(report()));
    component.applyDateRange();
    component.saveSnapshot();
    component.deleteCurrentPeriod();
    expect(service.parseReports).not.toHaveBeenCalled();
    expect(service.deleteSavedInsights).not.toHaveBeenCalled();
    component.resetDateRange();
    expect(component.insights).toBe(snapshot.data);
    expect(component.selectedSnapshotId).toBe('saved-week');
    expect(component.isCustomRange).toBeFalse();
  });

  it('lazily recovers legacy timings without replacing finances and compares exact prior dates', () => {
    const rebuilt = { ...report(), kitchenOrders: [] };
    rebuilt.settlementSummary = { ...rebuilt.settlementSummary, actualPayoutCredited: null };
    service.getDateRangeInsights.and.returnValue(of(rebuilt));
    component.selectTab('kitchen');
    fixture.detectChanges();
    expect(service.getDateRangeInsights.calls.allArgs()).toEqual([
      ['2026-10-26', '2026-11-01'], ['2026-10-19', '2026-10-25']
    ]);
    expect(component.insights).toBe(snapshot.data);
    expect(component.insights?.settlementSummary.actualPayoutCredited).toBe(150);
    expect(component.kitchenOrders).toEqual([]);
    expect(component.kitchenNotice).toContain('retained uploads');
    expect((fixture.nativeElement as HTMLElement).querySelector('app-kitchen-performance')).not.toBeNull();
  });

  it('cancels old kitchen requests when switching snapshots', () => {
    const response = new Subject<ZomatoSalesPayoutInsights>();
    service.getDateRangeInsights.and.returnValue(response);
    component.selectTab('kitchen');
    expect(response.observed).toBeTrue();
    const next = { ...snapshot, id: 'next', data: { ...report(), kitchenOrders: [] } };
    component.loadSnapshot(next);
    expect(response.observed).toBeFalse();
    response.next({ ...report(), kitchenOrders: [] });
    expect(component.insights).toBe(next.data);
    expect(component.kitchenNotice).toBe('');
    expect(component.kitchenLoading).toBeFalse();
  });

  it('exposes previous-period coverage failure while keeping current timing data available', () => {
    snapshot.data.kitchenOrders = [];
    component.loadSnapshot(snapshot);
    service.getDateRangeInsights.and.returnValue(throwError(() => new Error('Missing source coverage.')));
    component.selectTab('kitchen');
    fixture.detectChanges();
    expect(component.kitchenComparisonError).toContain('Missing source coverage');
    expect(component.kitchenError).toBe('');
    expect(component.kitchenOrders).toEqual([]);
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Comparison unavailable');
    component.toggleKitchenComparison(false);
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('Comparison unavailable');
  });

  it('uses an exact previous saved period without a request and cancels comparison on destroy', () => {
    snapshot.data.kitchenOrders = [];
    component.loadSnapshot(snapshot);
    component.snapshots.push({
      ...snapshot, id: 'previous', periodStartDate: '2026-10-19', periodEndDate: '2026-10-25',
      data: { ...report(), kitchenOrders: [], period: { startDate: '2026-10-19', endDate: '2026-10-25' } }
    });
    component.selectTab('kitchen');
    expect(service.getDateRangeInsights).not.toHaveBeenCalled();
    expect(component.kitchenPreviousOrders).toEqual([]);
    component.snapshots.pop();
    const pending = new Subject<ZomatoSalesPayoutInsights>();
    service.getDateRangeInsights.and.returnValue(pending);
    component.loadKitchenData(true);
    expect(pending.observed).toBeTrue();
    fixture.destroy();
    expect(pending.observed).toBeFalse();
  });

  it('explains missing kitchen source data without a technical prefix or a futile retry', () => {
    service.getDateRangeInsights.and.returnValue(throwError(missingSourceError));
    component.selectTab('kitchen');
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const error = root.querySelector('app-kitchen-performance [role="alert"]')!;
    expect(error.textContent).toContain('Order-level report data needed');
    expect(error.textContent).toContain('2026-11-02');
    expect(error.textContent).not.toContain('ZomatoSalesPayoutInsightsService');
    expect(error.textContent).not.toContain('Retry');
    expect(component.kitchenNeedsUpload).toBeTrue();
    expect(component.insights).toBe(snapshot.data);
    expect(component.insights?.settlementSummary.actualPayoutCredited).toBe(150);
    const openUploads = spyOn(component, 'openReportUploads');
    error.querySelector<HTMLButtonElement>('button')!.click();
    expect(openUploads).toHaveBeenCalledOnceWith();
  });

  it('treats missing comparison uploads separately while retaining current-period analytics', () => {
    snapshot.data.kitchenOrders = [];
    component.loadSnapshot(snapshot);
    service.getDateRangeInsights.and.returnValue(throwError(missingSourceError));
    component.selectTab('kitchen');
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    expect(component.kitchenComparisonNeedsUpload).toBeTrue();
    expect(component.kitchenError).toBe('');
    expect(root.textContent).toContain('Current-period analytics remain available');
    expect(root.textContent).toContain('Upload Comparison Reports');
    expect(root.querySelector('.kp-stats')).not.toBeNull();
  });

  it('offers source uploads for an uncovered custom range without replacing the loaded report', () => {
    service.getDateRangeInsights.and.returnValue(throwError(missingSourceError));
    component.applyDateRange();
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    expect(component.rangeNeedsUpload).toBeTrue();
    expect(component.rangeError).toBe(missingSourceError().originalError.error.error);
    expect(ui.warning).toHaveBeenCalledWith(component.rangeError);
    expect(ui.error).not.toHaveBeenCalled();
    expect(root.textContent).toContain('Upload Missing Reports');
    expect(component.insights).toBe(snapshot.data);
  });

  it('opens the existing mobile upload sheet when desktop uploads are hidden', () => {
    const uploads = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('[aria-label^="Upload order history"]')!;
    uploads.style.display = 'none';
    component.openReportUploads();
    fixture.detectChanges();
    const sheet = (fixture.nativeElement as HTMLElement).querySelector('.zp-sheet')!;
    expect(sheet.querySelector('input[accept=".csv"]')).not.toBeNull();
    expect(sheet.querySelector('input[accept=".xlsx"]')).not.toBeNull();
    expect(sheet.textContent).toContain('Parse Reports');
    expect(service.parseReports).not.toHaveBeenCalled();
  });
});
