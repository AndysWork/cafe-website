import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { CdkDragDrop, DragDropModule, moveItemInArray } from '@angular/cdk/drag-drop';
import { environment } from '../../../environments/environment';
import { HomeContentConfigService, HomeContentConfig } from '../../services/home-content-config.service';

interface AdminMenuItem {
  id: string;
  name: string;
  category?: string;
  onlinePrice?: number;
}

@Component({
  selector: 'app-admin-announcements',
  standalone: true,
  imports: [CommonModule, FormsModule, DragDropModule],
  templateUrl: './admin-announcements.component.html',
  styleUrls: ['./admin-announcements.component.scss']
})
export class AdminAnnouncementsComponent implements OnInit {
  isLoading = false;
  isSaving = false;
  saveMessage = '';
  saveError = '';

  announcementEnabled = false;
  announcementTitle = '';
  announcementMessage = '';

  menuItems: AdminMenuItem[] = [];
  selectedItemIds: string[] = [];
  maxFeaturedItems = 6;

  constructor(
    private homeContentConfigService: HomeContentConfigService,
    private http: HttpClient
  ) {}

  ngOnInit(): void {
    this.loadData();
  }

  loadData(): void {
    this.isLoading = true;
    this.saveError = '';

    this.http.get<any>(`${environment.apiUrl}/menu`).subscribe({
      next: (menuResponse) => {
        const rawItems = Array.isArray(menuResponse) ? menuResponse : (menuResponse?.data || []);
        this.menuItems = rawItems.map((item: any) => ({
          id: this.normalizeId(item.id ?? item._id),
          name: item.name || item.catalogueName || 'Menu Item',
          category: item.category || item.categoryName,
          onlinePrice: item.onlinePrice ?? item.shopSellingPrice
        })).filter((item: AdminMenuItem) => !!item.id);

        this.homeContentConfigService.getAdminConfig().subscribe({
          next: (response) => {
            const config = response?.data;
            this.applyConfig(config);
            this.isLoading = false;
          },
          error: () => {
            this.saveError = 'Failed to load home content configuration.';
            this.isLoading = false;
          }
        });
      },
      error: () => {
        this.saveError = 'Failed to load menu items.';
        this.isLoading = false;
      }
    });
  }

  isSelected(itemId: string): boolean {
    return this.selectedItemIds.includes(itemId);
  }

  get selectedCount(): number {
    return this.selectedItemIds.length;
  }

  get selectedMenuItems(): AdminMenuItem[] {
    return this.selectedItemIds
      .map(id => this.menuItems.find(item => item.id === id))
      .filter((item): item is AdminMenuItem => !!item);
  }

  toggleFeatured(itemId: string): void {
    const existingIndex = this.selectedItemIds.indexOf(itemId);
    if (existingIndex !== -1) {
      this.selectedItemIds.splice(existingIndex, 1);
      return;
    }

    if (this.selectedItemIds.length >= this.maxFeaturedItems) {
      this.saveError = `You can select up to ${this.maxFeaturedItems} items for Fresh Additions.`;
      return;
    }

    this.selectedItemIds.push(itemId);
    this.saveError = '';
  }

  removeSelected(itemId: string): void {
    const index = this.selectedItemIds.indexOf(itemId);
    if (index === -1) return;
    this.selectedItemIds.splice(index, 1);
  }

  moveSelectedItem(itemId: string, direction: 'up' | 'down'): void {
    const index = this.selectedItemIds.indexOf(itemId);
    if (index === -1) return;

    const targetIndex = direction === 'up' ? index - 1 : index + 1;
    if (targetIndex < 0 || targetIndex >= this.selectedItemIds.length) return;

    const [item] = this.selectedItemIds.splice(index, 1);
    this.selectedItemIds.splice(targetIndex, 0, item);
  }

  dropSelectedItems(event: CdkDragDrop<string[]>): void {
    if (event.previousIndex === event.currentIndex) return;
    moveItemInArray(this.selectedItemIds, event.previousIndex, event.currentIndex);
  }

  save(): void {
    this.isSaving = true;
    this.saveMessage = '';
    this.saveError = '';

    const payload: Partial<HomeContentConfig> = {
      announcementEnabled: this.announcementEnabled,
      announcementTitle: this.announcementTitle.trim(),
      announcementMessage: this.announcementMessage.trim(),
      featuredMenuItemIds: [...this.selectedItemIds]
    };

    this.homeContentConfigService.updateConfig(payload).subscribe({
      next: () => {
        this.isSaving = false;
        this.saveMessage = 'Home content updated successfully.';
      },
      error: () => {
        this.isSaving = false;
        this.saveError = 'Failed to save home content. Please try again.';
      }
    });
  }

  trackById = (_index: number, item: AdminMenuItem): string => item.id;

  private applyConfig(config?: HomeContentConfig): void {
    this.announcementEnabled = !!config?.announcementEnabled;
    this.announcementTitle = config?.announcementTitle || '';
    this.announcementMessage = config?.announcementMessage || '';
    this.selectedItemIds = (config?.featuredMenuItemIds || [])
      .map(id => this.normalizeId(id))
      .filter(id => !!id);

    // Remove unknown IDs and dedupe while preserving order.
    const knownIds = new Set(this.menuItems.map(item => item.id));
    const seen = new Set<string>();
    this.selectedItemIds = this.selectedItemIds.filter((id) => {
      if (!knownIds.has(id) || seen.has(id)) return false;
      seen.add(id);
      return true;
    });
  }

  private normalizeId(value: any): string {
    if (!value) return '';
    if (typeof value === 'string') return value;
    if (typeof value === 'object') {
      if (typeof value.$oid === 'string') return value.$oid;
      if (typeof value.id === 'string') return value.id;
      if (typeof value._id === 'string') return value._id;
    }
    return String(value);
  }
}
