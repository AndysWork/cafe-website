using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Cafe.Api.Models;

public class Inventory
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

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

    // Audit
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public string? LastUpdatedBy { get; set; }
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

    // Metadata
    public string Reason { get; set; } = string.Empty; // Purchase, Sale, Wastage, etc.
    public string? Notes { get; set; }
    public DateTime TransactionDate { get; set; } = DateTime.UtcNow;
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

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
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
    public int InStockItems { get; set; }
    public int LowStockItems { get; set; }
    public int OutOfStockItems { get; set; }
    public int ExpiringItems { get; set; }

    public decimal TotalInventoryValue { get; set; }
    public decimal AverageCostPerItem { get; set; }

    public List<InventoryItem> TopValueItems { get; set; } = new();
    public List<InventoryItem> CriticalItems { get; set; } = new();
    public List<InventoryTransaction> RecentTransactions { get; set; } = new();
}

public class InventoryItem
{
    public string? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal CurrentStock { get; set; }
    public string Unit { get; set; } = string.Empty;
    public decimal Value { get; set; }
    public StockStatus Status { get; set; }
}
