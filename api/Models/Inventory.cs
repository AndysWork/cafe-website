using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Cafe.Api.Services;

namespace Cafe.Api.Models;

[BsonIgnoreExtraElements]
public class Inventory
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("outletId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? OutletId { get; set; }

    [BsonRepresentation(BsonType.ObjectId)]
    public string? IngredientId { get; set; }

    public string IngredientName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;

    // Stock Information
    public decimal CurrentStock { get; set; } = 0;
    public decimal MinimumStock { get; set; } = 0; // Reorder point
    public decimal MaximumStock { get; set; } = 0;
    public decimal ReorderQuantity { get; set; } = 0; // Suggested order quantity

    // Supplier Information
    public string? SupplierName { get; set; }
    public string? SupplierContact { get; set; }
    public decimal? LastPurchasePrice { get; set; }
    public DateTime? LastPurchaseDate { get; set; }

    // Valuation
    public decimal CostPerUnit { get; set; } = 0;
    public decimal TotalValue { get; set; } = 0; // CurrentStock * CostPerUnit

    // Status
    public StockStatus Status { get; set; } = StockStatus.InStock;
    public bool IsActive { get; set; } = true;

    // Tracking
    public DateTime? LastRestockDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public string? StorageLocation { get; set; }
    public string? Notes { get; set; }

    // Batches with distinct expiry date, purchase price, cost per unit, and remaining quantity
    [BsonElement("batches")]
    public List<StockBatch> Batches { get; set; } = new();

    // Audit
    public DateTime CreatedAt { get; set; } = MongoService.GetIstNow();
    public DateTime UpdatedAt { get; set; } = MongoService.GetIstNow();
    public string? CreatedBy { get; set; }
    public string? LastUpdatedBy { get; set; }
}

[BsonIgnoreExtraElements]
public class StockBatch
{
    [BsonElement("id")]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    [BsonElement("batchNumber")]
    public string? BatchNumber { get; set; }

    [BsonElement("initialQuantity")]
    public decimal InitialQuantity { get; set; }

    [BsonElement("remainingQuantity")]
    public decimal RemainingQuantity { get; set; }

    [BsonElement("costPerUnit")]
    public decimal CostPerUnit { get; set; }

    [BsonElement("purchasePrice")]
    public decimal? PurchasePrice { get; set; }

    [BsonElement("supplierName")]
    public string? SupplierName { get; set; }

    [BsonElement("referenceNumber")]
    public string? ReferenceNumber { get; set; }

    [BsonElement("expiryDate")]
    public DateTime? ExpiryDate { get; set; }

    [BsonElement("receivedDate")]
    public DateTime ReceivedDate { get; set; } = MongoService.GetIstNow();

    [BsonIgnore]
    public bool IsDepleted => RemainingQuantity <= 0;
}

public enum StockStatus
{
    InStock,        // CurrentStock > MinimumStock
    LowStock,       // CurrentStock <= MinimumStock
    OutOfStock,     // CurrentStock = 0
    Overstock,      // CurrentStock > MaximumStock
    Expiring        // ExpiryDate within warning period
}

public class InventoryTransaction
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("outletId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? OutletId { get; set; }

    [BsonRepresentation(BsonType.ObjectId)]
    public string InventoryId { get; set; } = string.Empty;

    public string IngredientName { get; set; } = string.Empty;

    // Transaction Details
    public TransactionType Type { get; set; }
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = string.Empty;

    // Financial
    public decimal? CostPerUnit { get; set; }
    public decimal? TotalCost { get; set; }

    // Stock Levels
    public decimal StockBefore { get; set; }
    public decimal StockAfter { get; set; }

    // Reference
    public string? ReferenceNumber { get; set; } // PO number, invoice, etc.
    public string? SupplierName { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public string? BatchId { get; set; }

    // Metadata
    public string Reason { get; set; } = string.Empty; // Purchase, Sale, Wastage, etc.
    public string? Notes { get; set; }
    public DateTime TransactionDate { get; set; } = MongoService.GetIstNow();
    public string PerformedBy { get; set; } = "admin";
}

public enum TransactionType
{
    StockIn,        // Purchase, receiving stock
    StockOut,       // Usage, sale
    Adjustment,     // Manual correction
    Transfer,       // Between locations
    Wastage,        // Spoilage, damage
    Return          // Return to supplier
}

public class StockAlert
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonRepresentation(BsonType.ObjectId)]
    public string InventoryId { get; set; } = string.Empty;

    public string IngredientName { get; set; } = string.Empty;

    public AlertType Type { get; set; }
    public AlertSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;

    public decimal CurrentStock { get; set; }
    public decimal? ThresholdValue { get; set; }

    public bool IsResolved { get; set; } = false;
    public DateTime? ResolvedAt { get; set; }
    public string? ResolvedBy { get; set; }

    public DateTime CreatedAt { get; set; } = MongoService.GetIstNow();
}

public enum AlertType
{
    LowStock,
    OutOfStock,
    Overstock,
    ExpiringStock,
    ExpiredStock
}

public enum AlertSeverity
{
    Info,       // FYI
    Warning,    // Attention needed
    Critical    // Immediate action required
}

public class InventoryReport
{
    public int TotalItems { get; set; }
    public int ActiveItems { get; set; }
    public int InStockItems { get; set; }
    public int LowStockItems { get; set; }
    public int OutOfStockItems { get; set; }
    public int ExpiringItems { get; set; }
    public int TotalBatchesCount { get; set; }
    public int ActiveBatchesCount { get; set; }

    public decimal TotalInventoryValue { get; set; }
    public decimal AverageCostPerItem { get; set; }
    public decimal MonthlyWastageLoss { get; set; }
    public DateTime LastUpdated { get; set; } = MongoService.GetIstNow();

    public List<InventoryItem> TopValueItems { get; set; } = new();
    public List<InventoryItem> CriticalItems { get; set; } = new();
    public List<ExpiringBatchItem> ExpiringBatches { get; set; } = new();
    public List<CategoryInventorySummary> CategorySummaries { get; set; } = new();
    public List<InventoryTransaction> RecentTransactions { get; set; } = new();
}

public class CategoryInventorySummary
{
    public string Category { get; set; } = string.Empty;
    public int ItemCount { get; set; }
    public decimal TotalValue { get; set; }
    public decimal PercentageOfTotal { get; set; }
    public int LowStockCount { get; set; }
    public int OutOfStockCount { get; set; }
}

public class ExpiringBatchItem
{
    public string InventoryId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string BatchId { get; set; } = string.Empty;
    public string? BatchNumber { get; set; }
    public decimal RemainingQuantity { get; set; }
    public string Unit { get; set; } = string.Empty;
    public decimal CostPerUnit { get; set; }
    public decimal BatchValue { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public int DaysRemaining { get; set; }
    public int ShelfLifeThresholdDays { get; set; }
    public string Urgency { get; set; } = "warning"; // "expired", "critical", "warning", "good"
}

public class InventoryItem
{
    public string? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal CurrentStock { get; set; }
    public decimal MinimumStock { get; set; }
    public string Unit { get; set; } = string.Empty;
    public decimal Value { get; set; }
    public decimal CostPerUnit { get; set; }
    public DateTime? EarliestExpiryDate { get; set; }
    public int? DaysUntilExpiry { get; set; }
    public StockStatus Status { get; set; }
}

public class LogBatchWastageRequest
{
    public string? BatchId { get; set; }
    public decimal Quantity { get; set; }
    public string Reason { get; set; } = "Expired"; // Expired, Spoiled, Damaged, Quality Issue
    public string? Notes { get; set; }
    public string? PerformedBy { get; set; }
}

public class LogBatchWastageResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public decimal QuantityWasted { get; set; }
    public string Unit { get; set; } = string.Empty;
    public decimal FinancialLoss { get; set; }
    public decimal RemainingStock { get; set; }
    public string? WastageRecordId { get; set; }
}

public class InventoryItemUpload
{
    public string ItemName { get; set; } = string.Empty;
    public string Category { get; set; } = "Other";
    public string Unit { get; set; } = "kg";
    public decimal MinimumStock { get; set; } = 5;
    public decimal MaximumStock { get; set; } = 50;
    public decimal ReorderQuantity { get; set; } = 10;
    public string? StorageLocation { get; set; }
    public decimal InitialStock { get; set; } = 0;
    public decimal CostPerUnit { get; set; } = 0;
    public string? SupplierName { get; set; }
    public DateTime? ExpiryDate { get; set; }
}

public class BulkUploadInventoryResult
{
    public int Success { get; set; }
    public int Failed { get; set; }
    public int Total { get; set; }
    public List<string> Errors { get; set; } = new();
    public string Message { get; set; } = string.Empty;
}

