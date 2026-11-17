using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Cafe.Api.Services;
using Cafe.Api.Helpers;
using System.ComponentModel.DataAnnotations;

namespace Cafe.Api.Models;

public class SubscriptionPlan : ISoftDeletable
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("outletId")]
    [BsonSerializer(typeof(StringOrObjectIdSerializer))]
    public string OutletId { get; set; } = string.Empty;

    [BsonElement("name")]
    [Required] [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [BsonElement("description")]
    [StringLength(500)]
    public string Description { get; set; } = string.Empty;

    [BsonElement("category")]
    public string Category { get; set; } = "all-day"; // breakfast, lunch, snacks, dinner, all-day

    [BsonElement("price")]
    [Range(1, 100000)]
    public decimal Price { get; set; }

    [BsonElement("pricingMode")]
    public string PricingMode { get; set; } = "fixed";

    [BsonElement("isDraft")]
    public bool IsDraft { get; set; }

    [BsonElement("durationDays")]
    [AllowedValues(7, 14, 30)]
    public int DurationDays { get; set; } = 30;

    [BsonElement("benefits")]
    public List<string> Benefits { get; set; } = new();

    [BsonElement("freeDelivery")]
    public bool FreeDelivery { get; set; } = true;

    [BsonElement("discountPercent")]
    public decimal DiscountPercent { get; set; } = 15;

    [BsonElement("dailyItemLimit")]
    public int? DailyItemLimit { get; set; }

    [BsonElement("includedItems")]
    public List<SubscriptionItem>? IncludedItems { get; set; }

    [BsonElement("badgeText")]
    public string? BadgeText { get; set; } // e.g. "Most Popular", "Best Value", "Chef's Choice"

    [BsonElement("imageUrl")]
    public string? ImageUrl { get; set; }

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

    [BsonElement("unitPrice")]
    public decimal UnitPrice { get; set; }

    [BsonElement("dailyQuantity")]
    public int DailyQuantity { get; set; } = 1;

    [BsonElement("categoryName")]
    public string? CategoryName { get; set; }

    [BsonElement("imageUrl")]
    public string? ImageUrl { get; set; }

    [BsonElement("isVeg")]
    public bool? IsVeg { get; set; }

    [BsonElement("servingDays")]
    public List<string> ServingDays { get; set; } = new() { "Everyday" };
}

public class CustomerSubscription
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("userId")]
    public string UserId { get; set; } = string.Empty;

    [BsonElement("customerName")]
    public string? CustomerName { get; set; }

    [BsonElement("customerPhone")]
    public string? CustomerPhone { get; set; }

    [BsonElement("customerEmail")]
    public string? CustomerEmail { get; set; }

    [BsonElement("subscriptionType")]
    public string SubscriptionType { get; set; } = "curated_plan"; // "curated_plan", "custom_combo"

    [BsonElement("planId")]
    [BsonSerializer(typeof(StringOrObjectIdSerializer))]
    public string? PlanId { get; set; }

    [BsonElement("planName")]
    public string PlanName { get; set; } = string.Empty;

    [BsonElement("outletId")]
    [BsonSerializer(typeof(StringOrObjectIdSerializer))]
    public string OutletId { get; set; } = string.Empty;

    [BsonElement("items")]
    public List<SubscriptionItem> Items { get; set; } = new();

    [BsonElement("deliveryTimeSlot")]
    public string DeliveryTimeSlot { get; set; } = string.Empty; // e.g. "12:30 PM - 2:00 PM"

    [BsonElement("deliveryDays")]
    public List<string> DeliveryDays { get; set; } = new(); // e.g. ["Mon", "Tue", "Wed", "Thu", "Fri"] or ["Everyday"]

    [BsonElement("deliveryAddress")]
    public string? DeliveryAddress { get; set; }

    [BsonElement("specialInstructions")]
    public string? SpecialInstructions { get; set; }

    [BsonElement("startDate")]
    public DateTime StartDate { get; set; }

    [BsonElement("endDate")]
    public DateTime EndDate { get; set; }

    [BsonElement("durationDays")]
    public int DurationDays { get; set; } = 30;

    [BsonElement("status")]
    public string Status { get; set; } = "pending_payment"; // pending_payment, active, paused, cancelled, expired, completed

    [BsonElement("pausedAt")]
    public DateTime? PausedAt { get; set; }

    [BsonElement("totalDaysPaused")]
    public int TotalDaysPaused { get; set; }

    [BsonElement("pauseEndsAtUtc")]
    public DateTime? PauseEndsAtUtc { get; set; }

    [BsonElement("pauseStartsUtc")]
    public List<DateTime> PauseStartsUtc { get; set; } = new();

    [BsonElement("revision")]
    public int Revision { get; set; }

    [BsonIgnore]
    public int PauseLimit => SubscriptionTerms.ForExistingSubscription(DurationDays).PauseLimit;

    [BsonIgnore]
    public int MaxPauseDays => SubscriptionTerms.ForExistingSubscription(DurationDays).MaxPauseDays;

    [BsonIgnore]
    public int PausesRemaining => Math.Max(0, PauseLimit - SubscriptionPausePolicy.PausesUsed(this));

    [BsonElement("amountPaid")]
    public decimal AmountPaid { get; set; }

    [BsonElement("amountDue")]
    public decimal AmountDue { get; set; }

    [BsonElement("dailySubtotal")]
    public decimal DailySubtotal { get; set; }

    [BsonElement("discountPercent")]
    public decimal DiscountPercent { get; set; }

    [BsonElement("discountAmount")]
    public decimal DiscountAmount { get; set; }

    [BsonElement("freeDelivery")]
    public bool FreeDelivery { get; set; } = true;

    [BsonElement("paymentMethod")]
    public string? PaymentMethod { get; set; } = "upi-qr";

    [BsonElement("paymentStatus")]
    public string PaymentStatus { get; set; } = "pending";

    [BsonElement("paymentReference")]
    public string? PaymentReference { get; set; }

    [BsonElement("paymentConfirmedBy")]
    public string? PaymentConfirmedBy { get; set; }

    [BsonElement("paymentConfirmedAt")]
    public DateTime? PaymentConfirmedAt { get; set; }

    [BsonElement("razorpayPaymentId")]
    public string? RazorpayPaymentId { get; set; }

    [BsonElement("usageCount")]
    public int UsageCount { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = MongoService.GetIstNow();

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = MongoService.GetIstNow();
}

public class CreateSubscriptionPlanRequest
{
    [Required] [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = "all-day";

    [Range(1, 100000)]
    public decimal Price { get; set; }

    [AllowedValuesList("fixed", "per_day")]
    public string PricingMode { get; set; } = "fixed";
    public bool IsActive { get; set; } = true;
    public bool IsDraft { get; set; }

    [AllowedValues(7, 14, 30)]
    public int DurationDays { get; set; } = 30;

    public List<string> Benefits { get; set; } = new();
    public bool FreeDelivery { get; set; } = true;
    public decimal DiscountPercent { get; set; }
    public int? DailyItemLimit { get; set; }
    public List<SubscriptionItem>? IncludedItems { get; set; }
    public string? BadgeText { get; set; }
    public string? ImageUrl { get; set; }
}

public class SubscribeRequest
{
    [Required]
    public string PlanId { get; set; } = string.Empty;

    public string? DeliveryTimeSlot { get; set; }
    public List<string>? DeliveryDays { get; set; }
    public string? DeliveryAddress { get; set; }
    public string? CustomerPhone { get; set; }
    public string? CustomerName { get; set; }
    public string? SpecialInstructions { get; set; }
    public int? DurationDays { get; set; }
    [AllowedValuesList("upi-qr", "cash_at_counter")]
    public string? PaymentMethod { get; set; } = "upi-qr";
    public string? OutletId { get; set; }

}

public class CreateCustomComboSubscriptionRequest
{
    [Required]
    [StringLength(100, MinimumLength = 2)]
    public string ComboName { get; set; } = "My Daily Meal Combo";

    [Required]
    public List<CustomComboItemRequest> Items { get; set; } = new();

    [Required]
    public string DeliveryTimeSlot { get; set; } = "12:30 PM - 2:00 PM";

    public List<string> DeliveryDays { get; set; } = new() { "Everyday" };

    [AllowedValues(7, 14, 30)]
    public int DurationDays { get; set; } = 30;

    [Required]
    public string DeliveryAddress { get; set; } = string.Empty;

    [Required]
    public string CustomerPhone { get; set; } = string.Empty;

    public string? CustomerName { get; set; }
    public string? SpecialInstructions { get; set; }
    [AllowedValuesList("upi-qr", "cash_at_counter")]
    public string? PaymentMethod { get; set; } = "upi-qr";
    public string? OutletId { get; set; }
}

public class CustomComboItemRequest
{
    [Required]
    public string MenuItemId { get; set; } = string.Empty;

    [Range(1, 20)]
    public int Quantity { get; set; } = 1;
}

public class ConfirmSubscriptionPaymentRequest
{
    [StringLength(100)]
    public string? PaymentReference { get; set; }
}

/// <summary>Pauses deliveries for one to seven days, with early resumption allowed.</summary>
public sealed record PauseSubscriptionRequest
{
    [Range(1, SubscriptionPausePolicy.MaximumDays)]
    public int Days { get; init; }
}
