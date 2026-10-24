import { of } from 'rxjs';
import { TestBed } from '@angular/core/testing';
import { AdminTablesComponent } from './admin-tables.component';
import { AuthService } from '../../services/auth.service';
import { CancelledSessionCleanupResult, CafeTableService } from '../../services/cafe-table.service';
import { UIStore } from '../../store/ui.store';

describe('AdminTablesComponent cancelled-session cleanup', () => {
  let component: AdminTablesComponent;
  let tables: jasmine.SpyObj<CafeTableService>;
  let uiStore: jasmine.SpyObj<UIStore>;
  let isAdmin: boolean;
  const preview: CancelledSessionCleanupResult = {
    dryRun: true,
    candidateCount: 2,
    closedCount: 0,
    skippedCount: 0,
    candidates: [
      { sessionId: 's1', tableNumber: 'T1', orderCount: 1 },
      { sessionId: 's2', tableNumber: 'T2', orderCount: 2 }
    ],
    closed: []
  };

  beforeEach(() => {
    isAdmin = true;
    tables = jasmine.createSpyObj<CafeTableService>('CafeTableService', ['cleanupCancelledSessions', 'getFloor']);
    tables.cleanupCancelledSessions.and.returnValue(of(preview));
    tables.getFloor.and.returnValue(of({
      tables: [],
      summary: { total: 0, free: 0, occupied: 0, billRequested: 0, cleaning: 0, outOfService: 0 }
    }));
    uiStore = jasmine.createSpyObj<UIStore>('UIStore', ['success', 'error']);
    TestBed.configureTestingModule({
      providers: [
        { provide: CafeTableService, useValue: tables },
        { provide: UIStore, useValue: uiStore },
        { provide: AuthService, useValue: { isAdmin: () => isAdmin } }
      ]
    });
    component = TestBed.runInInjectionContext(() => new AdminTablesComponent());
  });

  afterEach(() => component.ngOnDestroy());

  it('previews candidates without applying the cleanup', () => {
    component.previewCancelledSessionCleanup();

    expect(tables.cleanupCancelledSessions).toHaveBeenCalledOnceWith(true);
    expect(component.cleanupReport).toEqual(preview);
    expect(uiStore.success).not.toHaveBeenCalled();
  });

  it('requires admin access before starting a preview', () => {
    isAdmin = false;
    component = TestBed.runInInjectionContext(() => new AdminTablesComponent());

    component.previewCancelledSessionCleanup();

    expect(tables.cleanupCancelledSessions).not.toHaveBeenCalled();
  });

  it('requires confirmation, then applies cleanup and refreshes the floor', () => {
    const closed: CancelledSessionCleanupResult = {
      ...preview, dryRun: false, candidateCount: 2, closedCount: 2, closed: preview.candidates
    };
    tables.cleanupCancelledSessions.and.returnValues(of(preview), of(closed));
    spyOn(window, 'confirm').and.returnValue(true);
    component.previewCancelledSessionCleanup();

    component.applyCancelledSessionCleanup();

    expect(window.confirm).toHaveBeenCalledWith(jasmine.stringMatching(/T1, T2/));
    expect(tables.cleanupCancelledSessions).toHaveBeenCalledTimes(2);
    expect(tables.cleanupCancelledSessions.calls.argsFor(1)).toEqual([false]);
    expect(component.cleanupReport).toBeNull();
    expect(tables.getFloor).toHaveBeenCalled();
    expect(uiStore.success).toHaveBeenCalledWith('2 stale table session(s) closed');
  });

  it('does not apply cleanup if the admin declines confirmation', () => {
    spyOn(window, 'confirm').and.returnValue(false);
    component.previewCancelledSessionCleanup();

    component.applyCancelledSessionCleanup();

    expect(tables.cleanupCancelledSessions).toHaveBeenCalledOnceWith(true);
    expect(component.cleanupReport).toEqual(preview);
  });

  it('reports an empty preview without offering sessions to close', () => {
    tables.cleanupCancelledSessions.and.returnValue(of({
      ...preview, candidateCount: 0, candidates: []
    }));

    component.previewCancelledSessionCleanup();

    expect(component.cleanupReport?.candidateCount).toBe(0);
    expect(uiStore.success).toHaveBeenCalledWith('No stale cancelled table sessions need cleanup.');
  });
});
