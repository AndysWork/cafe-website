import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export interface Inventory {
  id?: string;
  ingredientId?: string;
  ingredientName: string;
  category: string;
  unit: string;
  currentStock: number;
  minimumStock: number;
  maximumStock: number;
  reorderQuantity: number;
  supplierName?: string;
  supplierContact?: string;
  lastPurchasePrice?: number;
  lastPurchaseDate?: Date;
  costPerUnit: number;
  totalValue: number;
  status: 'InStock' | 'LowStock' | 'OutOfStock' | 'Overstock' | 'Expiring';
  lastRestockDate?: Date;
  expiryDate?: Date;
  storageLocation?: string;
  notes?: string;
  isActive: boolean;
  createdAt?: Date;
  updatedAt?: Date;
  createdBy?: string;
  lastUpdatedBy?: string;
}

export interface InventoryTransaction {
  id?: string;
  inventoryId: string;
  ingredientName: string;
  type: 'StockIn' | 'StockOut' | 'Adjustment' | 'Transfer' | 'Wastage' | 'Return';
  quantity: number;
  unit: string;
  costPerUnit?: number;
  totalCost?: number;
  stockBefore: number;
  stockAfter: number;
  referenceNumber?: string;
  supplierName?: string;
  reason?: string;
  transactionDate: Date;
  performedBy: string;
}

export interface StockAlert {
  id?: string;
  inventoryId: string;
  ingredientName: string;
  type: 'LowStock' | 'OutOfStock' | 'Overstock' | 'ExpiringStock' | 'ExpiredStock';
  severity: 'Info' | 'Warning' | 'Critical';
  message: string;
  currentStock?: number;
  thresholdValue?: number;
  isResolved: boolean;
  resolvedAt?: Date;
  resolvedBy?: string;
  createdAt: Date;
}

export interface InventoryReport {
  totalItems: number;
  activeItems: number;
  lowStockItems: number;
  outOfStockItems: number;
  expiringItems: number;
  totalValue: number;
  averageCostPerItem: number;
  topValueItems: InventoryItem[];
  criticalItems: InventoryItem[];
  recentTransactions: InventoryTransaction[];
}

export interface InventoryItem {
  id?: string;
  name: string;
  category: string;
  currentStock: number;
  unit: string;
  value: number;
  status: string;
}

export interface StockInRequest {
  quantity: number;
  costPerUnit?: number;
  supplierName?: string;
  referenceNumber?: string;
  performedBy?: string;
}

export interface StockOutRequest {
  quantity: number;
  reason?: string;
  performedBy?: string;
}

export interface StockAdjustmentRequest {
  quantityChange: number;
  reason?: string;
  referenceNumber?: string;
  performedBy?: string;
}

@Injectable({
  providedIn: 'root'
})
export class InventoryService {
  private apiUrl = `${environment.apiUrl}/inventory`;

  constructor(private http: HttpClient) { }

  // Inventory CRUD
  getAllInventory(): Observable<Inventory[]> {
    return this.http.get<Inventory[]>(this.apiUrl);
  }

  getActiveInventory(): Observable<Inventory[]> {
    return this.http.get<Inventory[]>(`${this.apiUrl}/active`);
  }

  getInventoryById(id: string): Observable<Inventory> {
    return this.http.get<Inventory>(`${this.apiUrl}/item/${id}`);
  }

  getInventoryByIngredientId(ingredientId: string): Observable<Inventory> {
    return this.http.get<Inventory>(`${this.apiUrl}/ingredient/${ingredientId}`);
  }

  getLowStockItems(): Observable<Inventory[]> {
    return this.http.get<Inventory[]>(`${this.apiUrl}/low-stock`);
  }

  getOutOfStockItems(): Observable<Inventory[]> {
    return this.http.get<Inventory[]>(`${this.apiUrl}/out-of-stock`);
  }

  getExpiringItems(days: number = 7): Observable<Inventory[]> {
    const params = new HttpParams().set('days', days.toString());
    return this.http.get<Inventory[]>(`${this.apiUrl}/expiring`, { params });
  }

  createInventory(inventory: Inventory): Observable<Inventory> {
    return this.http.post<Inventory>(this.apiUrl, inventory);
  }

  updateInventory(id: string, inventory: Inventory): Observable<any> {
    return this.http.put(`${this.apiUrl}/item/${id}`, inventory);
  }

  deleteInventory(id: string): Observable<any> {
    return this.http.delete(`${this.apiUrl}/item/${id}`);
  }

  // Stock operations
  stockIn(id: string, request: StockInRequest): Observable<any> {
    return this.http.post(`${this.apiUrl}/item/${id}/stock-in`, request);
  }

  stockOut(id: string, request: StockOutRequest): Observable<any> {
    return this.http.post(`${this.apiUrl}/item/${id}/stock-out`, request);
  }

  adjustStock(id: string, request: StockAdjustmentRequest): Observable<any> {
    return this.http.post(`${this.apiUrl}/item/${id}/adjust`, request);
  }

  // Transactions
  getInventoryTransactions(id: string, limit: number = 50): Observable<InventoryTransaction[]> {
    const params = new HttpParams().set('limit', limit.toString());
    return this.http.get<InventoryTransaction[]>(`${this.apiUrl}/item/${id}/transactions`, { params });
  }

  getRecentTransactions(limit: number = 20): Observable<InventoryTransaction[]> {
    const params = new HttpParams().set('limit', limit.toString());
    return this.http.get<InventoryTransaction[]>(`${this.apiUrl}/transactions/recent`, { params });
  }

  // Alerts
  getStockAlerts(): Observable<StockAlert[]> {
    return this.http.get<StockAlert[]>(`${this.apiUrl}/alerts`);
  }

  getCriticalAlerts(): Observable<StockAlert[]> {
    return this.http.get<StockAlert[]>(`${this.apiUrl}/alerts/critical`);
  }

  resolveAlert(alertId: string, resolvedBy: string): Observable<any> {
    return this.http.post(`${this.apiUrl}/alerts/${alertId}/resolve`, { resolvedBy });
  }

  // Reports
  getInventoryReport(): Observable<InventoryReport> {
    return this.http.get<InventoryReport>(`${this.apiUrl}/report`);
  }
}
