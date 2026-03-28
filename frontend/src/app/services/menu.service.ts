import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { tap, shareReplay, catchError } from 'rxjs/operators';
import { toObservable } from '@angular/core/rxjs-interop';
import { environment } from '../../environments/environment';
import { handleServiceError } from '../utils/error-handler';

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
  // Future pricing
  futureShopPrice?: number;
  futureOnlinePrice?: number;
}

@Injectable({
  providedIn: 'root'
})
export class MenuService {
  private http = inject(HttpClient);
  private apiUrl = environment.apiUrl;

  // Signal-based refresh notification
  private menuItemsUpdatedSignal = signal(false);

  // Observable bridge for backward compatibility
  get menuItemsRefresh$(): Observable<boolean> {
    return toObservable(this.menuItemsUpdatedSignal);
  }

  // Trigger refresh notification
  notifyMenuItemsUpdated(): void {
    this.menuItemsUpdatedSignal.set(true);
  }

  getCategories(): Observable<MenuCategory[]> {
    return this.http.get<MenuCategory[]>(`${this.apiUrl}/categories`).pipe(
      shareReplay({ bufferSize: 1, refCount: true }),
      catchError(handleServiceError('MenuService.getCategories'))
    );
  }

  // Get category by ID
  getCategory(id: string): Observable<MenuCategory> {
    return this.http.get<MenuCategory>(`${this.apiUrl}/categories/${id}`).pipe(
      catchError(handleServiceError('MenuService.getCategory'))
    );
  }

  getMenuItems(): Observable<MenuItem[]> {
    return this.http.get<MenuItem[]>(`${this.apiUrl}/menu`).pipe(
      shareReplay({ bufferSize: 1, refCount: true }),
      catchError(handleServiceError('MenuService.getMenuItems'))
    );
  }

  // Get menu item by ID
  getMenuItem(id: string): Observable<MenuItem> {
    return this.http.get<MenuItem>(`${this.apiUrl}/menu/${id}`).pipe(
      catchError(handleServiceError('MenuService.getMenuItem'))
    );
  }

  // Get menu items by category
  getMenuItemsByCategory(categoryId: string): Observable<MenuItem[]> {
    return this.http.get<MenuItem[]>(`${this.apiUrl}/menu/category/${categoryId}`).pipe(
      catchError(handleServiceError('MenuService.getMenuItemsByCategory'))
    );
  }

  // Update menu item
  updateMenuItem(id: string, menuItem: Partial<MenuItem>): Observable<MenuItem> {
    return this.http.put<MenuItem>(`${this.apiUrl}/menu/${id}`, menuItem)
      .pipe(
        tap(() => this.notifyMenuItemsUpdated()),
        catchError(handleServiceError('MenuService.updateMenuItem'))
      );
  }

  // Create menu item
  createMenuItem(menuItem: Partial<MenuItem>): Observable<MenuItem> {
    return this.http.post<MenuItem>(`${this.apiUrl}/menu`, menuItem)
      .pipe(
        tap(() => this.notifyMenuItemsUpdated()),
        catchError(handleServiceError('MenuService.createMenuItem'))
      );
  }

  // Toggle menu item availability (in stock/out of stock)
  toggleAvailability(id: string): Observable<any> {
    return this.http.patch(`${this.apiUrl}/menu/${id}/toggle-availability`, {})
      .pipe(
        tap(() => this.notifyMenuItemsUpdated()),
        catchError(handleServiceError('MenuService.toggleAvailability'))
      );
  }

  // Copy menu item data from one outlet to another
  copyMenuItemFromOutlet(menuItemName: string, sourceOutletId: string, targetOutletId: string): Observable<CopyMenuItemResponse> {
    return this.http.post<CopyMenuItemResponse>(`${this.apiUrl}/menu/copy-from-outlet`, {
      menuItemName,
      sourceOutletId,
      targetOutletId
    }).pipe(
      tap(() => this.notifyMenuItemsUpdated()),
      catchError(handleServiceError('MenuService.copyMenuItemFromOutlet'))
    );
  }
}

export interface CopyMenuItemResponse {
  success: boolean;
  message: string;
  data: {
    menuItemName: string;
    sourceOutletId: string;
    targetOutletId: string;
    recipeCopied: boolean;
    copiedRecipeId?: string;
    priceForecastCopied: boolean;
    copiedForecastId?: string;
    futurePricesUpdated: boolean;
  };
}
