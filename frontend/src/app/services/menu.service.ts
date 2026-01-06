import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, BehaviorSubject } from 'rxjs';
import { tap } from 'rxjs/operators';
import { environment } from '../../environments/environment';

export interface MenuSubCategory {
  id: string;
  name: string;
  categoryId: string;
}

export interface MenuCategory {
  id: string;
  name: string;
  description?: string;
  imageUrl?: string;
  subCategories?: MenuSubCategory[];
}

export interface MenuItem {
  id: string;
  name: string;
  description?: string;
  categoryId: string;
  categoryName?: string;
  subCategoryId?: string;
  subCategoryName?: string;
  onlinePrice: number;
  dineInPrice?: number;
  imageUrl?: string;
  isAvailable?: boolean;
  makingPrice?: number;
  packagingCharge?: number;
  shopSellingPrice?: number;
}

@Injectable({
  providedIn: 'root'
})
export class MenuService {
  private http = inject(HttpClient);
  private apiUrl = environment.apiUrl;

  // Subject to notify components when menu items are updated
  private menuItemsUpdated$ = new BehaviorSubject<boolean>(false);

  // Observable that components can subscribe to
  get menuItemsRefresh$(): Observable<boolean> {
    return this.menuItemsUpdated$.asObservable();
  }

  // Trigger refresh notification
  notifyMenuItemsUpdated(): void {
    this.menuItemsUpdated$.next(true);
  }

  // Get all categories
  getCategories(): Observable<MenuCategory[]> {
    return this.http.get<MenuCategory[]>(`${this.apiUrl}/categories`);
  }

  // Get category by ID
  getCategory(id: string): Observable<MenuCategory> {
    return this.http.get<MenuCategory>(`${this.apiUrl}/categories/${id}`);
  }

  // Get all menu items
  getMenuItems(): Observable<MenuItem[]> {
    return this.http.get<MenuItem[]>(`${this.apiUrl}/menu`);
  }

  // Get menu item by ID
  getMenuItem(id: string): Observable<MenuItem> {
    return this.http.get<MenuItem>(`${this.apiUrl}/menu/${id}`);
  }

  // Get menu items by category
  getMenuItemsByCategory(categoryId: string): Observable<MenuItem[]> {
    return this.http.get<MenuItem[]>(`${this.apiUrl}/menu/category/${categoryId}`);
  }

  // Update menu item
  updateMenuItem(id: string, menuItem: Partial<MenuItem>): Observable<MenuItem> {
    return this.http.put<MenuItem>(`${this.apiUrl}/menu/${id}`, menuItem)
      .pipe(
        tap(() => this.notifyMenuItemsUpdated())
      );
  }

  // Create menu item
  createMenuItem(menuItem: Partial<MenuItem>): Observable<MenuItem> {
    return this.http.post<MenuItem>(`${this.apiUrl}/menu`, menuItem)
      .pipe(
        tap(() => this.notifyMenuItemsUpdated())
      );
  }
}
