import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { OutletService } from '../../services/outlet.service';
import { Subscription } from 'rxjs';
import { filter } from 'rxjs/operators';
import { environment } from '../../../environments/environment';
import { getIstDateString, getIstInputDate, getIstNow } from '../../utils/date-utils';

interface MenuItemKptStats {
  itemName: string;
  menuItemId?: string;
  orderCount: number;
  totalQuantity: number;
  avgPreparationTime: number;
  minPreparationTime: number;
  maxPreparationTime: number;
  medianPreparationTime: number;
  stdDeviation: number;
  preparationTimeRange: string;
}

interface KptAnalysisSummary {
  totalOrdersAnalyzed: number;
  totalMenuItems: number;
  dateRange: {
    start: string;
    end: string;
  };
  platform: string;
  averageKptAllOrders: number;
  minKptAllOrders: number;
  maxKptAllOrders: number;
}

@Component({
  selector: 'app-kpt-analysis',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './kpt-analysis.component.html',
  styleUrls: ['./kpt-analysis.component.scss']
})
export class KptAnalysisComponent implements OnInit, OnDestroy {
  private outletService = inject(OutletService);
  private outletSubscription?: Subscription;

  isLoading = false;
  errorMessage = '';

  // Filters
  selectedPlatform: 'All' | 'Zomato' | 'Swiggy' = 'All';
  startDate = getIstInputDate(new Date(getIstNow().getFullYear(), 0, 1));
  endDate = getIstDateString();

  // Data
  summary: KptAnalysisSummary | null = null;
  menuItems: MenuItemKptStats[] = [];
  filteredMenuItems: MenuItemKptStats[] = [];

  // Search and filtering
  searchTerm = '';
  sortBy: 'name' | 'avgTime' | 'orders' | 'quantity' = 'orders';
  sortDirection: 'asc' | 'desc' = 'desc';

  // View options
  viewMode: 'table' | 'cards' = 'table';
  itemsPerPage = 20;
  currentPage = 1;

  // Expose Math for template
  Math = Math;

  constructor(private http: HttpClient) {}

  ngOnInit(): void {
    // Subscribe to outlet changes
    this.outletSubscription = this.outletService.selectedOutlet$
      .pipe(filter(outlet => outlet !== null))
      .subscribe(() => {
        this.loadKptAnalysis();
      });

    // Load immediately if outlet is already selected
    if (this.outletService.getSelectedOutlet()) {
      this.loadKptAnalysis();
    }
  }

  ngOnDestroy(): void {
    this.outletSubscription?.unsubscribe();
  }

  async loadKptAnalysis(): Promise<void> {
    this.isLoading = true;
    this.errorMessage = '';

    try {
      const params = new URLSearchParams();

      if (this.selectedPlatform !== 'All') {
        params.append('platform', this.selectedPlatform);
      }
      params.append('startDate', this.startDate);
      params.append('endDate', this.endDate);

      const url = `${environment.apiUrl}/online-sales/kpt-analysis?${params.toString()}`;
      console.log('Loading KPT analysis from:', url);

      const response: any = await this.http.get(url).toPromise();

      if (response.success) {
        this.summary = response.summary;
        this.menuItems = response.menuItems;
        this.applyFiltersAndSort();
      } else {
        this.errorMessage = response.message || 'Failed to load KPT analysis';
      }
    } catch (error: any) {
      this.errorMessage = error.error?.message || 'Error loading KPT analysis';
      console.error('Error loading KPT analysis:', error);
    } finally {
      this.isLoading = false;
    }
  }

  onFilterChange(): void {
    this.loadKptAnalysis();
  }

  applyFiltersAndSort(): void {
    // Filter by search term
    let filtered = this.menuItems;

    if (this.searchTerm.trim()) {
      const term = this.searchTerm.toLowerCase();
      filtered = filtered.filter(item =>
        item.itemName.toLowerCase().includes(term)
      );
    }

    // Sort
    filtered.sort((a, b) => {
      let comparison = 0;

      switch (this.sortBy) {
        case 'name':
          comparison = a.itemName.localeCompare(b.itemName);
          break;
        case 'avgTime':
          comparison = a.avgPreparationTime - b.avgPreparationTime;
          break;
        case 'orders':
          comparison = a.orderCount - b.orderCount;
          break;
        case 'quantity':
          comparison = a.totalQuantity - b.totalQuantity;
          break;
      }

      return this.sortDirection === 'asc' ? comparison : -comparison;
    });

    this.filteredMenuItems = filtered;
    this.currentPage = 1;
  }

  onSearchChange(): void {
    this.applyFiltersAndSort();
  }

  changeSort(field: 'name' | 'avgTime' | 'orders' | 'quantity'): void {
    if (this.sortBy === field) {
      this.sortDirection = this.sortDirection === 'asc' ? 'desc' : 'asc';
    } else {
      this.sortBy = field;
      this.sortDirection = 'desc';
    }
    this.applyFiltersAndSort();
  }

  get paginatedItems(): MenuItemKptStats[] {
    const start = (this.currentPage - 1) * this.itemsPerPage;
    const end = start + this.itemsPerPage;
    return this.filteredMenuItems.slice(start, end);
  }

  get totalPages(): number {
    return Math.ceil(this.filteredMenuItems.length / this.itemsPerPage);
  }

  goToPage(page: number): void {
    if (page >= 1 && page <= this.totalPages) {
      this.currentPage = page;
    }
  }

  getTimeCategory(time: number): string {
    if (time < 15) return 'fast';
    if (time < 25) return 'medium';
    return 'slow';
  }

  getTimeCategoryColor(time: number): string {
    if (time < 15) return '#4caf50'; // green
    if (time < 25) return '#ff9800'; // orange
    return '#f44336'; // red
  }

  exportToCSV(): void {
    const headers = ['Item Name', 'Order Count', 'Total Quantity', 'Avg Time (min)', 'Min Time (min)', 'Max Time (min)', 'Median Time (min)', 'Std Deviation', 'Time Range'];
    const rows = this.filteredMenuItems.map(item => [
      item.itemName,
      item.orderCount.toString(),
      item.totalQuantity.toString(),
      item.avgPreparationTime.toFixed(2),
      item.minPreparationTime.toFixed(2),
      item.maxPreparationTime.toFixed(2),
      item.medianPreparationTime.toFixed(2),
      item.stdDeviation.toFixed(2),
      item.preparationTimeRange
    ]);

    const csvContent = [headers, ...rows]
      .map(row => row.map(cell => `"${cell}"`).join(','))
      .join('\n');

    const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' });
    const link = document.createElement('a');
    const url = URL.createObjectURL(blob);

    link.setAttribute('href', url);
    link.setAttribute('download', `kpt-analysis-${new Date().toISOString().split('T')[0]}.csv`);
    link.style.visibility = 'hidden';

    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
  }
}
