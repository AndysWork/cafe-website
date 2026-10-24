import { DestroyRef, Injectable, inject } from '@angular/core';
import { DOCUMENT } from '@angular/common';
import { NavigationEnd, NavigationError, Router } from '@angular/router';
import { SwUpdate } from '@angular/service-worker';
import { filter } from 'rxjs/operators';

export const UPDATE_CHECK_INTERVAL_MS = 10 * 60 * 1000;

/**
 * Keeps installed PWA/tab sessions on the latest deployment.
 * A downloaded version is applied only after the user navigates to another page,
 * never while a page is open or the tab is in the background, so an unfinished
 * order on the current screen is not discarded by an automatic reload.
 */
@Injectable({ providedIn: 'root' })
export class AppUpdateService {
  private readonly swUpdate = inject(SwUpdate);
  private readonly router = inject(Router);
  private readonly document = inject(DOCUMENT);
  private readonly destroyRef = inject(DestroyRef);

  private started = false;
  private updateReady = false;
  private reloading = false;

  start(): void {
    if (this.started || !this.swUpdate.isEnabled) {
      return;
    }
    this.started = true;

    const versionSub = this.swUpdate.versionUpdates
      .pipe(filter(event => event.type === 'VERSION_READY'))
      .subscribe(() => (this.updateReady = true));

    // The current page is already loaded and usable; defer recovery to the next navigation too.
    const unrecoverableSub = this.swUpdate.unrecoverable.subscribe(() => (this.updateReady = true));

    const navigationSub = this.router.events.subscribe(event => {
      if (!this.updateReady) {
        return;
      }
      if (event instanceof NavigationEnd) {
        this.reload();
      } else if (event instanceof NavigationError) {
        // Old lazy-loaded chunks may no longer exist after a deployment; load the target page fresh.
        this.reload(event.url);
      }
    });

    const onVisibilityChange = () => {
      if (this.document.visibilityState === 'visible') {
        this.checkForUpdate();
      }
    };
    this.document.addEventListener('visibilitychange', onVisibilityChange);

    const view = this.document.defaultView;
    const intervalId = view?.setInterval(() => this.checkForUpdate(), UPDATE_CHECK_INTERVAL_MS);

    this.destroyRef.onDestroy(() => {
      versionSub.unsubscribe();
      unrecoverableSub.unsubscribe();
      navigationSub.unsubscribe();
      this.document.removeEventListener('visibilitychange', onVisibilityChange);
      if (intervalId !== undefined) {
        view?.clearInterval(intervalId);
      }
    });
  }

  private checkForUpdate(): void {
    if (this.updateReady || this.reloading) {
      return;
    }
    this.swUpdate.checkForUpdate().catch(() => undefined);
  }

  private reload(targetUrl?: string): void {
    if (this.reloading) {
      return;
    }
    this.reloading = true;
    if (targetUrl) {
      this.document.location.assign(targetUrl);
    } else {
      this.document.location.reload();
    }
  }
}
