import { TestBed, fakeAsync, tick, discardPeriodicTasks } from '@angular/core/testing';
import { DOCUMENT } from '@angular/common';
import { NavigationEnd, NavigationError, Router, Event as RouterEvent } from '@angular/router';
import { SwUpdate, VersionEvent, UnrecoverableStateEvent } from '@angular/service-worker';
import { Subject } from 'rxjs';
import { AppUpdateService, UPDATE_CHECK_INTERVAL_MS } from './app-update.service';

describe('AppUpdateService', () => {
  let versionUpdates: Subject<VersionEvent>;
  let unrecoverable: Subject<UnrecoverableStateEvent>;
  let routerEvents: Subject<RouterEvent>;
  let swUpdate: jasmine.SpyObj<SwUpdate> & { isEnabled: boolean };
  let reload: jasmine.Spy;
  let assign: jasmine.Spy;
  let visibility: DocumentVisibilityState;
  let fakeDocument: any;
  let listeners: Record<string, () => void>;

  function setup(isEnabled = true): AppUpdateService {
    versionUpdates = new Subject();
    unrecoverable = new Subject();
    routerEvents = new Subject();
    swUpdate = Object.assign(jasmine.createSpyObj<SwUpdate>('SwUpdate', ['checkForUpdate']), {
      isEnabled,
      versionUpdates: versionUpdates.asObservable(),
      unrecoverable: unrecoverable.asObservable()
    }) as any;
    swUpdate.checkForUpdate.and.resolveTo(false);
    reload = jasmine.createSpy('reload');
    assign = jasmine.createSpy('assign');
    visibility = 'visible';
    listeners = {};
    fakeDocument = {
      get visibilityState() { return visibility; },
      location: { reload, assign },
      defaultView: window,
      addEventListener: (name: string, fn: () => void) => { listeners[name] = fn; },
      removeEventListener: (name: string) => { delete listeners[name]; }
    };

    TestBed.configureTestingModule({
      providers: [
        AppUpdateService,
        { provide: SwUpdate, useValue: swUpdate },
        { provide: Router, useValue: { events: routerEvents.asObservable() } },
        { provide: DOCUMENT, useValue: fakeDocument }
      ]
    });
    return TestBed.inject(AppUpdateService);
  }

  const versionReady = () => versionUpdates.next({
    type: 'VERSION_READY',
    currentVersion: { hash: 'old' },
    latestVersion: { hash: 'new' }
  });
  const navigate = () => routerEvents.next(new NavigationEnd(1, '/menu', '/menu'));

  afterEach(() => TestBed.resetTestingModule());

  it('does nothing when the service worker is disabled', () => {
    const service = setup(false);
    service.start();
    versionReady();
    navigate();
    expect(reload).not.toHaveBeenCalled();
    expect(listeners['visibilitychange']).toBeUndefined();
  });

  it('waits for the next navigation before applying a ready update', () => {
    setup().start();
    versionReady();
    expect(reload).not.toHaveBeenCalled();

    navigate();
    expect(reload).toHaveBeenCalledTimes(1);

    navigate();
    expect(reload).toHaveBeenCalledTimes(1);
  });

  it('does not reload on navigation when no update is ready', () => {
    setup().start();
    navigate();
    expect(reload).not.toHaveBeenCalled();
  });

  it('never reloads when the tab is hidden, even with an update ready', () => {
    setup().start();
    versionReady();
    visibility = 'hidden';
    listeners['visibilitychange']();
    expect(reload).not.toHaveBeenCalled();
    expect(swUpdate.checkForUpdate).not.toHaveBeenCalled();
  });

  it('does not reload if the update arrives while the tab is hidden', () => {
    setup().start();
    visibility = 'hidden';
    versionReady();
    expect(reload).not.toHaveBeenCalled();

    visibility = 'visible';
    navigate();
    expect(reload).toHaveBeenCalledTimes(1);
  });

  it('checks for updates when the tab becomes visible', () => {
    setup().start();
    listeners['visibilitychange']();
    expect(swUpdate.checkForUpdate).toHaveBeenCalledTimes(1);
  });

  it('checks for updates periodically', fakeAsync(() => {
    setup().start();
    tick(UPDATE_CHECK_INTERVAL_MS);
    expect(swUpdate.checkForUpdate).toHaveBeenCalledTimes(1);
    tick(UPDATE_CHECK_INTERVAL_MS);
    expect(swUpdate.checkForUpdate).toHaveBeenCalledTimes(2);
    discardPeriodicTasks();
  }));

  it('defers unrecoverable-state recovery to the next navigation', () => {
    setup().start();
    unrecoverable.next({ type: 'UNRECOVERABLE_STATE', reason: 'missing hashed asset' });
    expect(reload).not.toHaveBeenCalled();

    navigate();
    expect(reload).toHaveBeenCalledTimes(1);
  });

  it('loads the target page fresh when a navigation fails with an update pending', () => {
    setup().start();
    unrecoverable.next({ type: 'UNRECOVERABLE_STATE', reason: 'missing hashed asset' });
    routerEvents.next(new NavigationError(2, '/admin/tables', new Error('ChunkLoadError')));
    expect(assign).toHaveBeenCalledOnceWith('/admin/tables');
    expect(reload).not.toHaveBeenCalled();
  });

  it('ignores navigation errors when no update is pending', () => {
    setup().start();
    routerEvents.next(new NavigationError(2, '/admin/tables', new Error('boom')));
    expect(assign).not.toHaveBeenCalled();
  });

  it('only starts once', () => {
    const service = setup();
    service.start();
    service.start();
    versionReady();
    navigate();
    expect(reload).toHaveBeenCalledTimes(1);
  });
});
