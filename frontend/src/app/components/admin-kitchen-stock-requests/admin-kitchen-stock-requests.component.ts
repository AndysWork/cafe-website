import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { KitchenVoiceStockRequest, KitchenVoiceStockRequestService } from '../../services/kitchen-voice-stock-request.service';
import { UIStore } from '../../store/ui.store';

@Component({
  selector: 'app-admin-kitchen-stock-requests',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './admin-kitchen-stock-requests.component.html',
  styleUrls: ['./admin-kitchen-stock-requests.component.scss']
})
export class AdminKitchenStockRequestsComponent implements OnInit {
  private requestService = inject(KitchenVoiceStockRequestService);
  private uiStore = inject(UIStore);

  loading = true;
  statusFilter: 'pending' | 'approved' | 'rejected' | '' = 'pending';
  reviewNotes: Record<string, string> = {};
  requests: KitchenVoiceStockRequest[] = [];

  ngOnInit(): void {
    this.loadRequests();
  }

  loadRequests(): void {
    this.loading = true;
    this.requestService.getAdminRequests(this.statusFilter || undefined).subscribe({
      next: (res) => {
        this.requests = res.items || [];
        this.loading = false;
      },
      error: () => {
        this.loading = false;
        this.uiStore.error('Unable to load kitchen stock requests');
      }
    });
  }

  review(request: KitchenVoiceStockRequest, decision: 'approved' | 'rejected'): void {
    if (!request.id) {
      return;
    }

    const note = (this.reviewNotes[request.id] || '').trim();
    this.requestService.reviewRequest(request.id, decision, note || undefined).subscribe({
      next: () => {
        this.uiStore.success(`Request ${decision}`);
        this.reviewNotes[request.id!] = '';
        this.loadRequests();
      },
      error: () => this.uiStore.error(`Failed to ${decision} request`)
    });
  }

  trackById(_: number, item: KitchenVoiceStockRequest): string {
    return item.id || `${item.requestedByUserId}-${item.createdAt}`;
  }
}
