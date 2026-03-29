import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { BranchComparisonService, BranchComparison, BranchMetrics } from '../../services/branch-comparison.service';
import { OutletService } from '../../services/outlet.service';
import { UIStore } from '../../store/ui.store';

@Component({
  selector: 'app-admin-branch-comparison',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './admin-branch-comparison.component.html',
  styleUrls: ['./admin-branch-comparison.component.scss']
})
export class AdminBranchComparisonComponent implements OnInit {
  private branchService = inject(BranchComparisonService);
  private outletService = inject(OutletService);
  private uiStore = inject(UIStore);

  outlets: any[] = [];
  selectedOutletIds: string[] = [];
  startDate = '';
  endDate = '';
  comparison: BranchComparison | null = null;
  loading = false;

  bestBranch: BranchMetrics | null = null;
  barColors = ['#38BDF8', '#84CC16', '#f59e0b', '#8b5cf6', '#ef4444', '#10b981', '#f97316', '#ec4899'];

  ngOnInit() {
    const now = new Date();
    this.endDate = now.toISOString().split('T')[0];
    const start = new Date(now);
    start.setDate(start.getDate() - 30);
    this.startDate = start.toISOString().split('T')[0];

    this.outletService.getAllOutlets().subscribe({
      next: (outlets: any[]) => {
        this.outlets = outlets;
        if (outlets.length >= 2) {
          this.selectedOutletIds = outlets.slice(0, 4).map((o: any) => o.id || o._id);
          this.loadComparison();
        }
      }
    });
  }

  toggleOutlet(outletId: string) {
    const idx = this.selectedOutletIds.indexOf(outletId);
    if (idx >= 0) {
      this.selectedOutletIds.splice(idx, 1);
    } else {
      this.selectedOutletIds.push(outletId);
    }
  }

  loadComparison() {
    if (!this.startDate || !this.endDate) {
      this.uiStore.error('Please select both start and end dates');
      return;
    }
    if (this.selectedOutletIds.length < 2) {
      this.uiStore.error('Select at least 2 outlets to compare');
      return;
    }
    this.loading = true;
    this.branchService.compareBranches(this.startDate, this.endDate, this.selectedOutletIds).subscribe({
      next: (data) => {
        this.comparison = data;
        this.bestBranch = data.branches?.reduce((a, b) => a.totalSales > b.totalSales ? a : b, data.branches[0]) || null;
        this.loading = false;
      },
      error: () => {
        this.uiStore.error('Failed to load branch comparison');
        this.loading = false;
      }
    });
  }

  getMaxSales(): number {
    if (!this.comparison) return 1;
    return Math.max(...this.comparison.branches.map(b => b.totalSales), 1);
  }

  getMaxOrders(): number {
    if (!this.comparison) return 1;
    return Math.max(...this.comparison.branches.map(b => b.totalOrders), 1);
  }

  getBarWidth(value: number, max: number): number {
    return Math.max((value / max) * 100, 2);
  }

  getColor(index: number): string {
    return this.barColors[index % this.barColors.length];
  }

  formatCurrency(val: number): string {
    return '₹' + (val || 0).toLocaleString('en-IN', { minimumFractionDigits: 0 });
  }
}
