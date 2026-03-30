using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Cafe.Api.Services;
using System.ComponentModel.DataAnnotations;

namespace Cafe.Api.Models;

public class SubscriptionPlan : ISoftDeletable
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("outletId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string OutletId { get; set; } = string.Empty;

    [BsonElement("name")]
    [Required] [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [BsonElement("description")]
    [StringLength(500)]
    public string Description { get; set; } = string.Empty;

    [BsonElement("price")]
    [Range(1, 100000)]
    public decimal Price { get; set; }

    [BsonElement("durationDays")]
    [Range(1, 365)]
    public int DurationDays { get; set; }

    [BsonElement("benefits")]
    public List<string> Benefits { get; set; } = new();

    [BsonElement("freeDelivery")]
    public bool FreeDelivery { get; set; }

    [BsonElement("discountPercent")]
    public decimal DiscountPercent { get; set; }

    [BsonElement("dailyItemLimit")]
    public int? DailyItemLimit { get; set; }

    [BsonElement("includedItems")]
    public List<SubscriptionItem>? IncludedItems { get; set; }

    [BsonElement("isActive")]
    public bool IsActive { get; set; } = true;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = MongoService.GetIstNow();

    // Soft-delete support
    [BsonElement("isDeleted")] public bool IsDeleted { get; set; }
    [BsonElement("deletedAt")] public DateTime? DeletedAt { get; set; }
    [BsonElement("deletedBy")] public string? DeletedBy { get; set; }
}

public class SubscriptionItem
{
    [BsonElement("menuItemId")]
    public string MenuItemId { get; set; } = string.Empty;

    [BsonElement("menuItemName")]
    public string MenuItemName { get; set; } = string.Empty;

    [BsonElement("dailyQuantity")]
    public int DailyQuantity { get; set; } = 1;
}

public class CustomerSubscription
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("userId")]
    public string UserId { get; set; } = string.Empty;

    [BsonElement("planId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string PlanId { get; set; } = string.Empty;

    [BsonElement("planName")]
    public string PlanName { get; set; } = string.Empty;

    [BsonElement("outletId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string OutletId { get; set; } = string.Empty;

    [BsonElement("startDate")]
    public DateTime StartDate { get; set; }

    [BsonElement("endDate")]
    public DateTime EndDate { get; set; }

    [BsonElement("status")]
    public string Status { get; set; } = "active"; // active, expired, cancelled

    [BsonElement("amountPaid")]
    public decimal AmountPaid { get; set; }

    [BsonElement("razorpayPaymentId")]
    public string? RazorpayPaymentId { get; set; }

    [BsonElement("usageCount")]
    public int UsageCount { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = MongoService.GetIstNow();
}

public class CreateSubscriptionPlanRequest
{
    [Required] [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string Description { get; set; } = string.Empty;

    [Range(1, 100000)]
    public decimal Price { get; set; }

    [Range(1, 365)]
    public int DurationDays { get; set; }

    public List<string> Benefits { get; set; } = new();
    public bool FreeDelivery { get; set; }
    public decimal DiscountPercent { get; set; }
    public int? DailyItemLimit { get; set; }
    public List<SubscriptionItem>? IncludedItems { get; set; }
}

public class SubscribeRequest
{
    [Required]
    public string PlanId { get; set; } = string.Empty;

    public string? RazorpayPaymentId { get; set; }
    public string? RazorpayOrderId { get; set; }
    public string? RazorpaySignature { get; set; }
}
