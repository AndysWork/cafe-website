import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { CustomerSegmentService, CustomerSegment, SegmentSummary } from '../../services/customer-segment.service';
import { OutletService } from '../../services/outlet.service';
import { UIStore } from '../../store/ui.store';
import { Subscription } from 'rxjs';
import { filter } from 'rxjs/operators';

@Component({
  selector: 'app-admin-customer-segments',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './admin-customer-segments.component.html',
  styleUrls: ['./admin-customer-segments.component.scss']
})
export class AdminCustomerSegmentsComponent implements OnInit, OnDestroy {
  private outletService = inject(OutletService);
  private uiStore = inject(UIStore);
  private outletSub?: Subscription;

  segments: CustomerSegment[] = [];
  summary: SegmentSummary[] = [];
  loading = true;
  refreshing = false;
  filterSegment = '';

  constructor(private segmentService: CustomerSegmentService) {}

  ngOnInit() {
    this.outletSub = this.outletService.selectedOutlet$
      .pipe(filter(o => o !== null))
      .subscribe(() => this.loadData());
    if (this.outletService.getSelectedOutlet()) this.loadData();
  }

  ngOnDestroy() { this.outletSub?.unsubscribe(); }

  loadData() {
    this.loading = true;
    this.segmentService.getSegmentSummary().subscribe({
      next: s => this.summary = s,
      error: () => {}
    });
    this.segmentService.getCustomerSegments(this.filterSegment || undefined).subscribe({
      next: s => { this.segments = s; this.loading = false; },
      error: () => { this.uiStore.error('Failed to load segments'); this.loading = false; }
    });
  }

  refreshSegments() {
    this.refreshing = true;
    this.segmentService.refreshSegments().subscribe({
      next: (res) => { this.uiStore.success(`Refreshed ${res.customersProcessed || res.segmentsUpdated} segments`); this.refreshing = false; this.loadData(); },
      error: () => { this.uiStore.error('Failed to refresh'); this.refreshing = false; }
    });
  }

  getSegmentIcon(segment: string): string {
    const icons: Record<string, string> = { new: '🆕', regular: '🔄', vip: '👑', dormant: '😴', 'at-risk': '⚠️' };
    return icons[segment] || '👤';
  }

  getSegmentColor(segment: string): string {
    const colors: Record<string, string> = { new: '#3b82f6', regular: '#10b981', vip: '#f59e0b', dormant: '#6b7280', 'at-risk': '#ef4444' };
    return colors[segment] || '#6b7280';
  }

  trackById(_: number, item: CustomerSegment) { return item.id; }
}
