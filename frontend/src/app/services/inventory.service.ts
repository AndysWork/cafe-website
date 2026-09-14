import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export interface StockBatch {
  id?: string;
  batchNumber?: string;
  initialQuantity: number;
  remainingQuantity: number;
  costPerUnit: number;
  purchasePrice?: number;
  supplierName?: string;
  referenceNumber?: string;
  expiryDate?: Date;
  receivedDate: Date;
  isDepleted?: boolean;
}

export interface InventoryCategory {
  id?: string;
  outletId?: string;
  name: string;
  description?: string;
  shelfLifeDays: number;
  displayOrder?: number;
  isActive?: boolean;
  createdAt?: Date;
  updatedAt?: Date;
}

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
  batches?: StockBatch[];
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

export interface CategoryInventorySummary {
  category: string;
  itemCount: number;
  totalValue: number;
  percentageOfTotal: number;
  lowStockCount: number;
  outOfStockCount: number;
}

export interface ExpiringBatchItem {
  inventoryId: string;
  itemName: string;
  category: string;
  batchId: string;
  batchNumber?: string;
  remainingQuantity: number;
  unit: string;
  costPerUnit: number;
  batchValue: number;
  expiryDate?: Date;
  daysRemaining: number;
  shelfLifeThresholdDays: number;
  urgency: 'expired' | 'critical' | 'warning' | 'good';
}

export interface InventoryReport {
  totalItems: number;
  activeItems: number;
  inStockItems?: number;
  lowStockItems: number;
  outOfStockItems: number;
  expiringItems: number;
  totalBatchesCount?: number;
  activeBatchesCount?: number;
  totalValue: number;
  totalInventoryValue?: number;
  averageCostPerItem: number;
  monthlyWastageLoss?: number;
  lastUpdated?: Date;
  topValueItems: InventoryItem[];
  criticalItems: InventoryItem[];
  expiringBatches?: ExpiringBatchItem[];
  categorySummaries?: CategoryInventorySummary[];
  recentTransactions: InventoryTransaction[];
}

export interface InventoryItem {
  id?: string;
  name: string;
  category: string;
  currentStock: number;
  minimumStock?: number;
  unit: string;
  value: number;
  costPerUnit?: number;
  earliestExpiryDate?: Date;
  daysUntilExpiry?: number;
  status: string;
}

export interface StockInRequest {
  quantity: number;
  costPerUnit?: number;
  purchasePrice?: number;
  supplierName?: string;
  referenceNumber?: string;
  expiryDate?: Date | string;
  performedBy?: string;
}

export interface StockOutRequest {
  quantity: number;
  reason?: string;
  batchId?: string;
  performedBy?: string;
}

export interface StockAdjustmentRequest {
  quantityChange: number;
  reason?: string;
  referenceNumber?: string;
  performedBy?: string;
}

export interface LogBatchWastageRequest {
  batchId?: string;
  quantity: number;
  reason: string;
  notes?: string;
  performedBy?: string;
}

export interface LogBatchWastageResult {
  success: boolean;
  message: string;
  quantityWasted: number;
  unit: string;
  financialLoss: number;
  remainingStock: number;
  wastageRecordId?: string;
}

export interface BulkUploadResult {
  success: number;
  failed: number;
  total: number;
  errors: string[];
  message: string;
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

  logBatchWastage(id: string, request: LogBatchWastageRequest): Observable<LogBatchWastageResult> {
    return this.http.post<LogBatchWastageResult>(`${this.apiUrl}/item/${id}/wastage`, request);
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

  // Inventory Categories (Outlet-Scoped)
  getInventoryCategories(): Observable<InventoryCategory[]> {
    return this.http.get<InventoryCategory[]>(`${this.apiUrl}/categories`);
  }

  createInventoryCategory(category: Partial<InventoryCategory>): Observable<InventoryCategory> {
    return this.http.post<InventoryCategory>(`${this.apiUrl}/categories`, category);
  }

  updateInventoryCategory(id: string, category: Partial<InventoryCategory>): Observable<any> {
    return this.http.put(`${this.apiUrl}/categories/${id}`, category);
  }

  deleteInventoryCategory(id: string): Observable<any> {
    return this.http.delete(`${this.apiUrl}/categories/${id}`);
  }

  // Excel Bulk Upload & Template
  downloadTemplate(): Observable<Blob> {
    return this.http.get(`${this.apiUrl}/template`, { responseType: 'blob' });
  }

  uploadInventoryExcel(file: File): Observable<BulkUploadResult> {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post<BulkUploadResult>(`${this.apiUrl}/upload`, formData);
  }

  // Reports
  getInventoryReport(): Observable<InventoryReport> {
    return this.http.get<InventoryReport>(`${this.apiUrl}/report`);
  }
}
