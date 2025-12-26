using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace Cafe.Api.Models;

// Online Sale Record (Zomato/Swiggy)
public class OnlineSale
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("platform")]
    public string Platform { get; set; } = string.Empty; // "Zomato" or "Swiggy"

    [BsonElement("orderId")]
    public string OrderId { get; set; } = string.Empty; // ZomatoOrderId or SwiggyOrderId

    [BsonElement("customerName")]
    public string? CustomerName { get; set; }

    [BsonElement("orderAt")]
    public DateTime OrderAt { get; set; }

    [BsonElement("distance")]
    public decimal Distance { get; set; }

    [BsonElement("orderedItems")]
    public List<OrderedItem> OrderedItems { get; set; } = new();

    [BsonElement("instructions")]
    public string? Instructions { get; set; }

    [BsonElement("discountCoupon")]
    public string? DiscountCoupon { get; set; }

    [BsonElement("billSubTotal")]
    public decimal BillSubTotal { get; set; }

    [BsonElement("packagingCharges")]
    public decimal PackagingCharges { get; set; }

    [BsonElement("discountAmount")]
    public decimal DiscountAmount { get; set; }

    [BsonElement("gst")]
    public decimal GST { get; set; }

    [BsonElement("totalCommissionable")]
    public decimal TotalCommissionable { get; set; }

    [BsonElement("payout")]
    public decimal Payout { get; set; } // Key field for income calculation

    [BsonElement("platformDeduction")]
    public decimal PlatformDeduction { get; set; } // ZomatoDeduction or SwiggyDeduction

    [BsonElement("investment")]
    public decimal Investment { get; set; }

    [BsonElement("miscCharges")]
    public decimal MiscCharges { get; set; }

    [BsonElement("rating")]
    public decimal? Rating { get; set; }

    [BsonElement("review")]
    public string? Review { get; set; }

    [BsonElement("kpt")]
    public decimal? KPT { get; set; }

    [BsonElement("rwt")]
    public decimal? RWT { get; set; }

    [BsonElement("orderMarking")]
    public string? OrderMarking { get; set; }

    [BsonElement("complain")]
    public string? Complain { get; set; }

    [BsonElement("uploadedBy")]
    public string UploadedBy { get; set; } = string.Empty;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; }
}

// Ordered Item with parsed quantity and name
public class OrderedItem
{
    [BsonElement("quantity")]
    public int Quantity { get; set; }

    [BsonElement("itemName")]
    public string ItemName { get; set; } = string.Empty;

    [BsonElement("menuItemId")]
    public string? MenuItemId { get; set; } // Matched from CafeMenuItem
}

// Request DTOs
public class CreateOnlineSaleRequest
{
    [Required(ErrorMessage = "Platform is required")]
    [RegularExpression("^(Zomato|Swiggy)$", ErrorMessage = "Platform must be either 'Zomato' or 'Swiggy'")]
    public string Platform { get; set; } = string.Empty;

    [Required(ErrorMessage = "Order ID is required")]
    public string OrderId { get; set; } = string.Empty;

    public string? CustomerName { get; set; }

    [Required(ErrorMessage = "Order date is required")]
    public DateTime OrderAt { get; set; }

    [Range(0, 1000, ErrorMessage = "Distance must be between 0 and 1000 km")]
    public decimal Distance { get; set; }

    [Required(ErrorMessage = "Ordered items are required")]
    [MinLength(1, ErrorMessage = "At least one item is required")]
    public List<OrderedItemRequest> OrderedItems { get; set; } = new();

    public string? Instructions { get; set; }
    public string? DiscountCoupon { get; set; }

    [Range(0, 10000000, ErrorMessage = "Bill sub-total must be between 0 and 10,000,000")]
    public decimal BillSubTotal { get; set; }

    [Range(0, 10000, ErrorMessage = "Packaging charges must be between 0 and 10,000")]
    public decimal PackagingCharges { get; set; }

    [Range(0, 10000000, ErrorMessage = "Discount amount must be between 0 and 10,000,000")]
    public decimal DiscountAmount { get; set; }

    [Range(0, 10000000, ErrorMessage = "Total commissionable must be between 0 and 10,000,000")]
    public decimal TotalCommissionable { get; set; }

    [Range(0, 10000000, ErrorMessage = "Payout must be between 0 and 10,000,000")]
    public decimal Payout { get; set; }

    [Range(0, 10000000, ErrorMessage = "Platform deduction must be between 0 and 10,000,000")]
    public decimal PlatformDeduction { get; set; }

    [Range(0, 10000000, ErrorMessage = "Investment must be between 0 and 10,000,000")]
    public decimal Investment { get; set; }

    [Range(0, 10000000, ErrorMessage = "Misc charges must be between 0 and 10,000,000")]
    public decimal MiscCharges { get; set; }

    [Range(0, 5, ErrorMessage = "Rating must be between 0 and 5")]
    public decimal? Rating { get; set; }

    public string? Review { get; set; }
    public decimal? KPT { get; set; }
    public decimal? RWT { get; set; }
    public string? OrderMarking { get; set; }
    public string? Complain { get; set; }
}

public class OrderedItemRequest
{
    [Range(1, 1000, ErrorMessage = "Quantity must be between 1 and 1000")]
    public int Quantity { get; set; }

    [Required(ErrorMessage = "Item name is required")]
    public string ItemName { get; set; } = string.Empty;

    public string? MenuItemId { get; set; }
}

public class UpdateOnlineSaleRequest
{
    public string? CustomerName { get; set; }
    public List<OrderedItemRequest>? OrderedItems { get; set; }
    public string? Instructions { get; set; }
    public string? Review { get; set; }
    public string? OrderMarking { get; set; }
    public string? Complain { get; set; }
}

public class OnlineSaleResponse
{
    public string Id { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public DateTime OrderAt { get; set; }
    public decimal Distance { get; set; }
    public List<OrderedItem> OrderedItems { get; set; } = new();
    public string? Instructions { get; set; }
    public string? DiscountCoupon { get; set; }
    public decimal BillSubTotal { get; set; }
    public decimal PackagingCharges { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalCommissionable { get; set; }
    public decimal Payout { get; set; }
    public decimal PlatformDeduction { get; set; }
    public decimal Investment { get; set; }
    public decimal MiscCharges { get; set; }
    public decimal? Rating { get; set; }
    public string? Review { get; set; }
    public decimal? KPT { get; set; }
    public decimal? RWT { get; set; }
    public string? OrderMarking { get; set; }
    public string? Complain { get; set; }
    public string UploadedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class DailyOnlineIncomeResponse
{
    public DateTime Date { get; set; }
    public string Platform { get; set; } = string.Empty;
    public decimal TotalPayout { get; set; }
    public decimal TotalOrders { get; set; }
    public decimal TotalDeduction { get; set; }
    public decimal TotalDiscount { get; set; }
    public decimal TotalPackaging { get; set; }
    public decimal AverageRating { get; set; }
}

public class DiscountCouponResponse
{
    public string? Id { get; set; }
    public string CouponCode { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public int UsageCount { get; set; }
    public decimal TotalDiscountAmount { get; set; }
    public decimal AverageDiscountAmount { get; set; }
    public DateTime FirstUsed { get; set; }
    public DateTime LastUsed { get; set; }
    public bool IsActive { get; set; }
    public decimal? MaxValue { get; set; }
    public decimal? DiscountPercentage { get; set; }
}

public class BulkInsertResult
{
    public bool Success { get; set; }
    public int InsertedCount { get; set; }
    public int SkippedCount { get; set; }
    public List<string> Duplicates { get; set; } = new();
    public string? ErrorMessage { get; set; }
}

