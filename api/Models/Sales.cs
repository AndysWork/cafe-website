using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;
using Cafe.Api.Helpers;

namespace Cafe.Api.Models;

// Daily Sales Record
public class Sales
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("outletId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? OutletId { get; set; }

    [BsonElement("date")]
    public DateTime Date { get; set; }

    [BsonElement("items")]
    public List<SalesItem> Items { get; set; } = new();

    [BsonElement("totalAmount")]
    public decimal TotalAmount { get; set; }

    [BsonElement("paymentMethod")]
    public string PaymentMethod { get; set; } = string.Empty; // Cash, Card, UPI, Online

    [BsonElement("notes")]
    public string? Notes { get; set; }

    [BsonElement("recordedBy")]
    public string RecordedBy { get; set; } = string.Empty;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; }
}

// Individual item in a sales record
public class SalesItem
{
    [BsonElement("menuItemId")]
    public string? MenuItemId { get; set; }

    [BsonElement("itemName")]
    public string ItemName { get; set; } = string.Empty;

    [BsonElement("quantity")]
    public int Quantity { get; set; }

    [BsonElement("unitPrice")]
    public decimal UnitPrice { get; set; }

    [BsonElement("totalPrice")]
    public decimal TotalPrice { get; set; }
}

// Request/Response DTOs
public class CreateSalesRequest
{
    [Required(ErrorMessage = "Date is required")]
    public DateTime Date { get; set; }
    
    [Required(ErrorMessage = "Sales must contain at least one item")]
    [MinLength(1, ErrorMessage = "Sales must contain at least one item")]
    public List<SalesItemRequest> Items { get; set; } = new();
    
    [Required(ErrorMessage = "Payment method is required")]
    [AllowedValuesList("Cash", "Card", "UPI", "Online")]
    public string PaymentMethod { get; set; } = "Cash";
    
    [StringLength(500, ErrorMessage = "Notes cannot exceed 500 characters")]
    public string? Notes { get; set; }
}

public class SalesItemRequest
{
    public string? MenuItemId { get; set; }
    
    [Required(ErrorMessage = "Item name is required")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Item name must be between 2 and 100 characters")]
    public string ItemName { get; set; } = string.Empty;
    
    [Range(1, 1000, ErrorMessage = "Quantity must be between 1 and 1000")]
    public int Quantity { get; set; }
    
    [Range(0.01, 100000, ErrorMessage = "Unit price must be between 0.01 and 100,000")]
    public decimal UnitPrice { get; set; }
}

public class SalesResponse
{
    public string Id { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public List<SalesItem> Items { get; set; } = new();
    public decimal TotalAmount { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class SalesSummary
{
    public DateTime Date { get; set; }
    public decimal TotalSales { get; set; }
    public int TotalTransactions { get; set; }
    public Dictionary<string, decimal> PaymentMethodBreakdown { get; set; } = new();
}
