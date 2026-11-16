import { ComponentFixture, TestBed } from '@angular/core/testing';
import { KitchenPerformanceComponent } from './kitchen-performance.component';
import { KitchenOrderTiming } from '../../services/zomato-sales-payout-insights.service';

describe('Kitchen performance view', () => {
  let fixture: ComponentFixture<KitchenPerformanceComponent>;
  let component: KitchenPerformanceComponent;
  const order: KitchenOrderTiming = {
    orderId: 'delayed', placedAt: '2026-10-27T18:02:00', kptMinutes: 25, riderWaitMinutes: 6,
    kptStatus: 'valid', riderWaitStatus: 'valid', timingsValidated: true,
    items: [{ name: 'Tea', quantity: 2 }], itemsText: '2 x Tea', quantity: 2, distinctItems: 1,
    deliveryType: 'Delivery', rating: 2, review: 'Late', complaint: 'Cold', readyMarked: 'Missed', orderValue: 200
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [KitchenPerformanceComponent] }).compileComponents();
    fixture = TestBed.createComponent(KitchenPerformanceComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('orders', [order, { ...order, orderId: 'on-time', kptMinutes: 0, riderWaitMinutes: 0 }]);
    fixture.componentRef.setInput('period', { startDate: '2026-10-27', endDate: '2026-10-28' });
    fixture.detectChanges();
  });

  it('renders charts and exceptions, then recalculates both periods with editable targets', () => {
    fixture.componentRef.setInput('previousOrders', [{ ...order, kptMinutes: 30, riderWaitMinutes: 10 }]);
    fixture.componentRef.setInput('previousPeriod', { startDate: '2026-10-25', endDate: '2026-10-26' });
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelectorAll('.kp-scatter circle').length).toBe(2);
    expect(root.querySelector('.kp-exceptions')?.textContent).toContain('#delayed');
    expect(component.exceptionRows.map(o => o.orderId)).toEqual(['delayed']);
    component.draftKpt = 26;
    component.draftWait = 7;
    component.applyTargets();
    fixture.detectChanges();
    expect(component.analysis?.kpt.breaches).toBe(0);
    expect(component.previous?.kpt.breaches).toBe(1);
    expect(root.querySelector('.kp-exceptions')?.textContent).toContain('No orders exceed');
  });

  it('rejects invalid targets without changing the displayed analysis', () => {
    const original = component.analysis;
    component.draftKpt = -1;
    component.applyTargets();
    fixture.detectChanges();
    expect(component.analysis).toBe(original);
    expect((fixture.nativeElement as HTMLElement).querySelector('[role="alert"]')?.textContent).toContain('non-negative');
    component.draftKpt = null;
    component.applyTargets();
    expect(component.targets.kpt).toBe(20);
  });

  it('filters delayed orders by search, metric and marking, and displays excess wait only', () => {
    component.exceptionSearch = 'Tea';
    component.exceptionKind = 'both';
    component.exceptionMarking = 'Missed';
    expect(component.exceptionRows.length).toBe(1);
    expect(component.excessWait(order)).toBe(1);
    component.exceptionMarking = 'Correct';
    expect(component.exceptionRows.length).toBe(0);
    component.exceptionMarking = 'All';
    component.exceptionSearch = 'absent item';
    expect(component.exceptionRows.length).toBe(0);
  });

  it('renders no-data metrics rather than zero performance for an empty selection', () => {
    fixture.componentRef.setInput('orders', []);
    fixture.detectChanges();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('No delivered orders in this selection');
    expect(component.minutes(component.analysis?.kpt.average)).toBe('—');
    expect(component.percent(component.analysis?.kpt.breachPercent)).toBe('—');
  });

  it('applies target inputs through the form and keeps the mobile layout within its container', async () => {
    const root = fixture.nativeElement as HTMLElement;
    root.style.width = '360px';
    const kpt = root.querySelector<HTMLInputElement>('input[name="kptTarget"]')!;
    const wait = root.querySelector<HTMLInputElement>('input[name="waitTarget"]')!;
    kpt.value = '30';
    wait.value = '10';
    kpt.dispatchEvent(new Event('input'));
    wait.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    root.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
    fixture.detectChanges();
    await fixture.whenStable();
    expect(component.targets).toEqual({ kpt: 30, riderWait: 10 });
    expect(component.exceptionRows.length).toBe(0);
    expect(root.scrollWidth).toBeLessThanOrEqual(Math.ceil(root.getBoundingClientRect().width));
  });

  it('paginates exceptions and sorts/filter cohorts without mixing order and item quantity counts', () => {
    fixture.componentRef.setInput('orders', Array.from({ length: 30 }, (_, i) => ({
      ...order, orderId: `${i}`, items: [{ name: i < 20 ? 'Tea' : 'Coffee', quantity: 2 }],
      kptMinutes: i < 20 ? 25 : 35
    })));
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelectorAll('.kp-exceptions tbody tr').length).toBe(25);
    component.exceptionPage = 2;
    component.cohortSort = 'kpt';
    fixture.detectChanges();
    expect(root.querySelectorAll('.kp-exceptions tbody tr').length).toBe(5);
    expect(component.cohortRows.map(c => c.label)).toEqual(['Coffee', 'Tea']);
    expect(component.cohortRows[0].orders).toBe(10);
    component.minimumSample = 20;
    expect(component.cohortRows.map(c => c.label)).toEqual(['Tea']);
  });

  it('offers upload and saved-report actions for missing sources, but retry for a temporary failure', () => {
    fixture.componentRef.setInput('orders', null);
    fixture.componentRef.setInput('error', 'No order-level source coverage from 2026-11-02.');
    fixture.componentRef.setInput('needsUpload', true);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const upload = spyOn(component.uploadReports, 'emit');
    const saved = spyOn(component.viewSavedReports, 'emit');
    const buttons = root.querySelectorAll<HTMLButtonElement>('[role="alert"] button');
    expect(buttons.length).toBe(2);
    buttons[0].click();
    buttons[1].click();
    expect(upload).toHaveBeenCalledOnceWith();
    expect(saved).toHaveBeenCalledOnceWith();
    fixture.componentRef.setInput('needsUpload', false);
    fixture.componentRef.setInput('error', 'Temporary server error.');
    fixture.detectChanges();
    const retry = spyOn(component.retry, 'emit');
    const retryButton = root.querySelector<HTMLButtonElement>('[role="alert"] button')!;
    expect(retryButton.textContent).toBe('Retry');
    retryButton.click();
    expect(retry).toHaveBeenCalledOnceWith();
  });
});
