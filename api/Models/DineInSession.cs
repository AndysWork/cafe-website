using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;
using Cafe.Api.Helpers;

namespace Cafe.Api.Models;

public class DineInSession
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("outletId")]
    [Required]
    public string OutletId { get; set; } = string.Empty;

    [BsonElement("outletName")]
    public string? OutletName { get; set; }

    [BsonElement("tableNumber")]
    [Required]
    public string TableNumber { get; set; } = string.Empty;

    [BsonElement("userId")]
    public string? UserId { get; set; }

    [BsonElement("customerName")]
    public string? CustomerName { get; set; }

    [BsonElement("customerPhone")]
    public string? CustomerPhone { get; set; }

    [BsonElement("status")]
    public string Status { get; set; } = "active"; // "active", "bill_requested", "paid", "cancelled"

    [BsonElement("orderIds")]
    public List<string> OrderIds { get; set; } = new();

    [BsonElement("subtotal")]
    public decimal Subtotal { get; set; }

    [BsonElement("discountAmount")]
    public decimal DiscountAmount { get; set; }

    [BsonElement("couponCode")]
    public string? CouponCode { get; set; }

    [BsonElement("loyaltyPointsUsed")]
    public int LoyaltyPointsUsed { get; set; }

    [BsonElement("loyaltyDiscountAmount")]
    public decimal LoyaltyDiscountAmount { get; set; }

    [BsonElement("taxAmount")]
    public decimal TaxAmount { get; set; }

    [BsonElement("grandTotal")]
    public decimal GrandTotal { get; set; }

    [BsonElement("paymentStatus")]
    public string PaymentStatus { get; set; } = "unpaid"; // "unpaid", "pending", "paid"

    [BsonElement("paymentMethod")]
    public string? PaymentMethod { get; set; } // "upi-qr", "razorpay", "cash_at_counter"

    [BsonElement("upiReference")]
    public string? UpiReference { get; set; }

    [BsonElement("razorpayOrderId")]
    public string? RazorpayOrderId { get; set; }

    [BsonElement("razorpayPaymentId")]
    public string? RazorpayPaymentId { get; set; }

    [BsonElement("razorpaySignature")]
    public string? RazorpaySignature { get; set; }

    [BsonElement("notes")]
    public string? Notes { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("billRequestedAt")]
    public DateTime? BillRequestedAt { get; set; }

    [BsonElement("settledAt")]
    public DateTime? SettledAt { get; set; }
}

public class DineInRoundItemDto
{
    public string MenuItemId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalPrice { get; set; }
    public string? SelectedVariantName { get; set; }
    public List<string>? SelectedAddOnNames { get; set; }
    public string? PreparationNotes { get; set; }
}

public class DineInRoundDto
{
    public int RoundNumber { get; set; }
    public string OrderId { get; set; } = string.Empty;
    public string Status { get; set; } = "confirmed"; // confirmed, preparing, ready, delivered
    public DateTime CreatedAt { get; set; }
    public decimal RoundSubtotal { get; set; }
    public List<DineInRoundItemDto> Items { get; set; } = new();
}

public class DineInBillResponse
{
    public string SessionId { get; set; } = string.Empty;
    public string OutletId { get; set; } = string.Empty;
    public string OutletName { get; set; } = string.Empty;
    public string TableNumber { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public string Status { get; set; } = "active"; // active, bill_requested, paid, cancelled
    public string PaymentStatus { get; set; } = "unpaid";
    public string? PaymentMethod { get; set; }
    public List<DineInRoundDto> Rounds { get; set; } = new();
    public int TotalItemsCount { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public string? CouponCode { get; set; }
    public int LoyaltyPointsUsed { get; set; }
    public decimal LoyaltyDiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal GrandTotal { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? BillRequestedAt { get; set; }
    public DateTime? SettledAt { get; set; }
    public bool CanAddItems { get; set; }
    public bool CanRequestBill { get; set; }
    public string? UpiQrString { get; set; }
    public string? UpiId { get; set; }
    public string? PayeeName { get; set; }
    public bool RazorpayEnabled { get; set; }
    public string? InvoiceNumber { get; set; }
    public int EstimatedPointsToEarn { get; set; }
    public bool CanApplyCoupon { get; set; }
}

public class StartDineInSessionRequest
{
    [Required]
    public string TableNumber { get; set; } = string.Empty;
    public string? OutletId { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
}

public class SettleDineInBillRequest
{
    [Required]
    [AllowedValuesList("upi-qr", "razorpay", "cash_at_counter")]
    public string PaymentMethod { get; set; } = "upi-qr";

    public string? UpiReference { get; set; }
    public string? RazorpayOrderId { get; set; }
    public string? RazorpayPaymentId { get; set; }
    public string? RazorpaySignature { get; set; }
    public string? Notes { get; set; }
}

public class ApplyDineInCouponRequest
{
    [Required]
    public string CouponCode { get; set; } = string.Empty;
}

public class RedeemDineInLoyaltyRequest
{
    [Range(1, 100000)]
    public int Points { get; set; }
}
