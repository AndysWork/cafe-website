using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Cafe.Api.Helpers;

namespace Cafe.Api.Models;

public class DineInSession
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("outletId")]
    [BsonSerializer(typeof(StringOrObjectIdSerializer))]
    [Required]
    public string OutletId { get; set; } = string.Empty;

    [BsonElement("outletName")]
    public string? OutletName { get; set; }

    [BsonElement("tableNumber")]
    [Required]
    public string TableNumber { get; set; } = string.Empty;

    [BsonElement("userId")]
    public string? UserId { get; set; }

    [BsonElement("waiterUserId")]
    public string? WaiterUserId { get; set; }

    [BsonElement("waiterName")]
    public string? WaiterName { get; set; }

    [BsonElement("customerName")]
    public string? CustomerName { get; set; }

    [BsonElement("customerPhone")]
    public string? CustomerPhone { get; set; }

    [BsonElement("accessTokenHash")]
    [BsonIgnoreIfNull]
    [JsonIgnore]
    public string? AccessTokenHash { get; set; }

    [BsonElement("qrAccessTokenHash")]
    [BsonIgnoreIfNull]
    [JsonIgnore]
    public string? QrAccessTokenHash { get; set; }

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
    public string PaymentStatus { get; set; } = "unpaid"; // unpaid, pending_verification, pending_cash, processing, partially_paid, paid

    [BsonElement("paymentMethod")]
    public string? PaymentMethod { get; set; } // "upi-qr", "cash_at_counter"

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

    [BsonElement("amountPaid")]
    public decimal AmountPaid { get; set; }

    [BsonElement("lastPaymentRequestedAt")]
    public DateTime? LastPaymentRequestedAt { get; set; }

    [BsonElement("paymentEntries")]
    public List<DineInPaymentEntry> PaymentEntries { get; set; } = new();

    [BsonElement("voidRequests")]
    public List<DineInVoidRequest> VoidRequests { get; set; } = new();

    [BsonElement("timeline")]
    public List<DineInSessionEvent> Timeline { get; set; } = new();

    [BsonElement("mergedIntoSessionId")]
    public string? MergedIntoSessionId { get; set; }

    [BsonElement("mergeNote")]
    public string? MergeNote { get; set; }

    [BsonElement("reopenedByUserId")]
    public string? ReopenedByUserId { get; set; }

    [BsonElement("reopenedReason")]
    public string? ReopenedReason { get; set; }
}

public class DineInPaymentEntry
{
    [BsonElement("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [BsonElement("amount")]
    public decimal Amount { get; set; }

    [BsonElement("paymentMethod")]
    public string PaymentMethod { get; set; } = "upi-qr";

    [BsonElement("upiReference")]
    public string? UpiReference { get; set; }

    [BsonElement("status")]
    public string Status { get; set; } = "pending"; // pending, confirmed, rejected

    [BsonElement("notes")]
    public string? Notes { get; set; }

    [BsonElement("requestedAt")]
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("confirmedAt")]
    public DateTime? ConfirmedAt { get; set; }

    [BsonElement("confirmedBy")]
    public string? ConfirmedBy { get; set; }
}

public class DineInVoidRequest
{
    [BsonElement("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [BsonElement("orderId")]
    public string OrderId { get; set; } = string.Empty;

    [BsonElement("menuItemId")]
    public string MenuItemId { get; set; } = string.Empty;

    [BsonElement("itemName")]
    public string ItemName { get; set; } = string.Empty;

    [BsonElement("quantity")]
    public int Quantity { get; set; }

    [BsonElement("reason")]
    public string Reason { get; set; } = string.Empty;

    [BsonElement("status")]
    public string Status { get; set; } = "pending"; // pending, approved, rejected

    [BsonElement("requestedBy")]
    public string? RequestedBy { get; set; }

    [BsonElement("requestedAt")]
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("reviewedBy")]
    public string? ReviewedBy { get; set; }

    [BsonElement("reviewedAt")]
    public DateTime? ReviewedAt { get; set; }

    [BsonElement("reviewNotes")]
    public string? ReviewNotes { get; set; }
}

public class DineInSessionEvent
{
    [BsonElement("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [BsonElement("eventType")]
    public string EventType { get; set; } = string.Empty;

    [BsonElement("message")]
    public string Message { get; set; } = string.Empty;

    [BsonElement("actor")]
    public string? Actor { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("metadata")]
    public Dictionary<string, string>? Metadata { get; set; }
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
    public string Status { get; set; } = "confirmed"; // pending, confirmed, preparing, ready, served, delivered
    public DateTime CreatedAt { get; set; }
    public decimal RoundSubtotal { get; set; }
    public string? CustomerName { get; set; }
    public string? WaiterUserId { get; set; }
    public string? WaiterName { get; set; }
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
    public string? WaiterUserId { get; set; }
    public string? WaiterName { get; set; }
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
    public string? InvoiceNumber { get; set; }
    public int EstimatedPointsToEarn { get; set; }
    public bool CanApplyCoupon { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal AmountDue { get; set; }
    public List<DineInPaymentEntry> PaymentEntries { get; set; } = new();
    public List<DineInVoidRequest> VoidRequests { get; set; } = new();
    public List<DineInSessionEvent> Timeline { get; set; } = new();
}

public class StartDineInSessionRequest
{
    [Required]
    public string TableNumber { get; set; } = string.Empty;
    public string? OutletId { get; set; }
    [StringLength(100)]
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
}

public class SettleDineInBillRequest
{
    [Required]
    [AllowedValuesList("upi-qr", "cash_at_counter")]
    public string PaymentMethod { get; set; } = "upi-qr";

    [Range(0.01, 1000000)]
    public decimal? Amount { get; set; }

    public string? UpiReference { get; set; }
    public string? Notes { get; set; }
}

public class ConfirmDineInPaymentRequest
{
    [Range(0.01, 1000000)]
    public decimal? ConfirmedAmount { get; set; }

    public string? Notes { get; set; }
}

public class DineInPaymentReconciliationResponse
{
    public string SessionId { get; set; } = string.Empty;
    public decimal GrandTotal { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal AmountDue { get; set; }
    public bool IsFullySettled { get; set; }
    public List<DineInPaymentEntry> PaymentEntries { get; set; } = new();
}

public class DineInReopenOrderingRequest
{
    [Required]
    [StringLength(300)]
    public string Reason { get; set; } = string.Empty;
}

public class DineInVoidItemRequest
{
    [Required]
    public string OrderId { get; set; } = string.Empty;

    [Required]
    public string MenuItemId { get; set; } = string.Empty;

    [Range(1, 1000)]
    public int Quantity { get; set; }

    [Required]
    [StringLength(300)]
    public string Reason { get; set; } = string.Empty;
}

public class DineInReviewVoidRequest
{
    [Required]
    [AllowedValuesList("approved", "rejected")]
    public string Decision { get; set; } = "approved";

    [StringLength(300)]
    public string? Notes { get; set; }
}

public class DineInReseatRequest
{
    [Required]
    public string NewTableNumber { get; set; } = string.Empty;

    public bool MergeIfOccupied { get; set; }

    [StringLength(300)]
    public string? Reason { get; set; }
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
