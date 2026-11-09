import { Injectable } from '@angular/core';
import { Subject } from 'rxjs';

export type HomePanel = 'story' | 'offers' | 'rewards' | 'contact' | 'reserve';

@Injectable({ providedIn: 'root' })
export class HomePanelService {
  private readonly panelRequests = new Subject<HomePanel>();
  readonly requests = this.panelRequests.asObservable();

  open(panel: HomePanel): void {
    this.panelRequests.next(panel);
  }
}
