using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Cafe.Api.Models;

// Daily Sales Record
public class Sales
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

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
    public DateTime Date { get; set; }
    public List<SalesItemRequest> Items { get; set; } = new();
    public string PaymentMethod { get; set; } = "Cash";
    public string? Notes { get; set; }
}

public class SalesItemRequest
{
    public string? MenuItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public int Quantity { get; set; }
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
