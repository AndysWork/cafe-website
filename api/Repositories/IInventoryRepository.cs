using Cafe.Api.Models;

namespace Cafe.Api.Repositories;

public interface IInventoryRepository
{
    // Inventory CRUD
    Task<List<Inventory>> GetAllInventoryAsync(string? outletId = null, int? page = null, int? pageSize = null);
    Task<long> GetAllInventoryCountAsync(string? outletId = null);
    Task<List<Inventory>> GetActiveInventoryAsync(string? outletId = null, int? page = null, int? pageSize = null);
    Task<Inventory?> GetInventoryByIdAsync(string id);
    Task<Inventory?> GetInventoryByIngredientIdAsync(string ingredientId);
    Task<List<Inventory>> GetInventoryByCategoryAsync(string category);
    Task<List<Inventory>> GetInventoryByStatusAsync(StockStatus status);
    Task<List<Inventory>> GetLowStockItemsAsync(string? outletId = null);
    Task<List<Inventory>> GetOutOfStockItemsAsync(string? outletId = null);
    Task<List<Inventory>> GetExpiringItemsAsync(int daysThreshold = 7, string? outletId = null);
    Task<Inventory> CreateInventoryAsync(Inventory inventory);
    Task<bool> UpdateInventoryAsync(string id, Inventory inventory);
    Task<bool> DeleteInventoryAsync(string id);
    Task<bool> AdjustStockAsync(string inventoryId, decimal quantityChange, TransactionType type, string reason, string performedBy, string? referenceNumber = null);
    Task<bool> StockInAsync(string inventoryId, decimal quantity, decimal? costPerUnit, string? supplierName, string? referenceNumber, string performedBy);
    Task<bool> StockOutAsync(string inventoryId, decimal quantity, string reason, string performedBy);

    // Transactions
    Task<List<InventoryTransaction>> GetAllInventoryTransactionsAsync(string? outletId = null);
    Task<List<InventoryTransaction>> GetTransactionsByInventoryIdAsync(string inventoryId, int limit = 50);
    Task<List<InventoryTransaction>> GetTransactionsByDateRangeAsync(DateTime startDate, DateTime endDate);
    Task<List<InventoryTransaction>> GetRecentTransactionsAsync(int limit = 20);
    Task<List<InventoryTransaction>> GetRecentTransactionsAsync(int limit, string? outletId);

    // Alerts
    Task<List<StockAlert>> GetAllAlertsAsync();
    Task<List<StockAlert>> GetAllAlertsAsync(string? outletId);
    Task<List<StockAlert>> GetAlertsByTypeAsync(AlertType type);
    Task<List<StockAlert>> GetCriticalAlertsAsync();
    Task<bool> ResolveAlertAsync(string alertId, string resolvedBy);

    // Reports
    Task<InventoryReport> GetInventoryReportAsync();
    Task<object> GetInventoryReportAsync(string? outletId);

    // Frozen Items
    Task<List<FrozenItem>> GetAllFrozenItemsAsync(string? outletId = null);
    Task<List<FrozenItem>> GetActiveFrozenItemsAsync(string? outletId = null);
    Task<FrozenItem?> GetFrozenItemByIdAsync(string id, string? outletId = null);
    Task<FrozenItem> CreateFrozenItemAsync(FrozenItem frozenItem);
    Task<bool> UpdateFrozenItemAsync(string id, FrozenItem frozenItem, string? outletId = null);
    Task<bool> DeleteFrozenItemAsync(string id, string? outletId = null);
    Task<(int success, int failed, List<string> errors)> BulkUploadFrozenItemsAsync(List<FrozenItemUpload> items, string outletId);
    Task<int> SyncAllFrozenItemsToInventoryAsync();

    // Purchase Orders
    Task<PurchaseOrder> CreatePurchaseOrderAsync(PurchaseOrder po);
    Task<List<PurchaseOrder>> GetPurchaseOrdersAsync(string outletId, string? status = null, int page = 1, int pageSize = 50);
    Task<bool> UpdatePurchaseOrderStatusAsync(string id, string status);
    Task<List<PurchaseOrder>> GenerateAutoReorderPurchaseOrdersAsync(string outletId);

    // Wastage
    Task<WastageRecord> CreateWastageRecordAsync(WastageRecord record);
    Task<List<WastageRecord>> GetWastageRecordsAsync(string outletId, DateTime? startDate = null, DateTime? endDate = null, int page = 1, int pageSize = 50);
    Task<WastageSummary> GetWastageSummaryAsync(string outletId, DateTime startDate, DateTime endDate);
}
