using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Cafe.Api.Services;
using System.ComponentModel.DataAnnotations;
using Cafe.Api.Helpers;

namespace Cafe.Api.Models;

public class Order : ISoftDeletable
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("outletId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? OutletId { get; set; }

    [BsonElement("userId")]
    public string UserId { get; set; } = string.Empty;

    [BsonElement("username")]
    public string Username { get; set; } = string.Empty;

    [BsonElement("customerName")]
    public string? CustomerName { get; set; }

    [BsonElement("orderSource")]
    public string? OrderSource { get; set; }

    [BsonElement("deliveryLocationUrl")]
    public string? DeliveryLocationUrl { get; set; }

    [BsonElement("recordedByUserId")]
    public string? RecordedByUserId { get; set; }

    [BsonElement("recordedByName")]
    public string? RecordedByName { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    [BsonElement("phoneTrackingToken")]
    public string? PhoneTrackingToken { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    [BsonElement("phoneRequestHash")]
    public string? PhoneRequestHash { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    [BsonElement("phoneEffectsQueued")]
    public bool PhoneEffectsQueued { get; set; }

    [BsonElement("waiterUserId")]
    public string? WaiterUserId { get; set; }

    [BsonElement("waiterName")]
    public string? WaiterName { get; set; }

    [BsonElement("userEmail")]
    public string? UserEmail { get; set; }

    [BsonElement("items")]
    public List<OrderItem> Items { get; set; } = new();

    [BsonElement("subtotal")]
    public decimal Subtotal { get; set; }

    [BsonElement("tax")]
    public decimal Tax { get; set; }

    [BsonElement("platformCharge")]
    public decimal PlatformCharge { get; set; }

    [BsonElement("total")]
    public decimal Total { get; set; }

    [BsonElement("status")]
    public string Status { get; set; } = "pending"; // scheduled/pending, confirmed, preparing, ready, served, out-for-delivery, delivered, cancelled

    [BsonElement("inventoryDeducted")]
    public bool InventoryDeducted { get; set; } = false;

    [BsonElement("paymentStatus")]
    public string PaymentStatus { get; set; } = "pending"; // pending, paid, refunded

    [BsonElement("paymentMethod")]
    public string PaymentMethod { get; set; } = "cod"; // cod, upi-qr, dine_in_tab, cash_at_counter

    [BsonElement("razorpayOrderId")]
    public string? RazorpayOrderId { get; set; }

    [BsonElement("razorpayPaymentId")]
    public string? RazorpayPaymentId { get; set; }

    [BsonElement("razorpaySignature")]
    public string? RazorpaySignature { get; set; }

    [BsonElement("razorpayRefundId")]
    public string? RazorpayRefundId { get; set; }

    [BsonElement("upiReference")]
    public string? UpiReference { get; set; }

    [BsonElement("upiConfirmedBy")]
    public string? UpiConfirmedBy { get; set; }

    [BsonElement("upiConfirmedAt")]
    public DateTime? UpiConfirmedAt { get; set; }

    [BsonElement("upiProofUrl")]
    public string? UpiProofUrl { get; set; }

    [BsonElement("deliveryAddress")]
    public string? DeliveryAddress { get; set; }

    [BsonElement("phoneNumber")]
    public string? PhoneNumber { get; set; }

    [BsonElement("notes")]
    public string? Notes { get; set; }

    [BsonElement("preparationNotes")]
    public string? PreparationNotes { get; set; }

    [BsonElement("couponCode")]
    public string? CouponCode { get; set; }

    [BsonElement("discountAmount")]
    public decimal DiscountAmount { get; set; }

    [BsonElement("loyaltyPointsUsed")]
    public int LoyaltyPointsUsed { get; set; }

    [BsonElement("loyaltyDiscountAmount")]
    public decimal LoyaltyDiscountAmount { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = MongoService.GetIstNow();

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = MongoService.GetIstNow();

    [BsonElement("completedAt")]
    public DateTime? CompletedAt { get; set; }

    [BsonElement("receiptImageUrl")]
    public string? ReceiptImageUrl { get; set; }

    [BsonElement("deliveryFee")]
    public decimal DeliveryFee { get; set; }

    [BsonElement("orderType")]
    public string OrderType { get; set; } = "delivery"; // delivery, pickup, dine-in

    [BsonElement("channel")]
    public string Channel { get; set; } = "web"; // web, shop, partner

    [BsonElement("scheduledFor")]
    public DateTime? ScheduledFor { get; set; }

    [BsonElement("isScheduled")]
    public bool IsScheduled { get; set; }

    [BsonElement("walletAmountUsed")]
    public decimal WalletAmountUsed { get; set; }

    [BsonElement("deliveryPartnerId")]
    public string? DeliveryPartnerId { get; set; }

    [BsonElement("deliveryPartnerName")]
    public string? DeliveryPartnerName { get; set; }

    [BsonElement("deliveryRouteUrl")]
    public string? DeliveryRouteUrl { get; set; }

    [BsonElement("deliveryRouteShortCode")]
    public string? DeliveryRouteShortCode { get; set; }

    [BsonElement("deliveryRouteShortUrl")]
    public string? DeliveryRouteShortUrl { get; set; }

    [BsonElement("deliveryDistanceKm")]
    public double? DeliveryDistanceKm { get; set; }

    [BsonElement("deliveryEtaMinutes")]
    public int? DeliveryEtaMinutes { get; set; }

    [BsonElement("manualDeliveryFeeOverride")]
    public decimal? ManualDeliveryFeeOverride { get; set; }

    [BsonElement("manualDeliveryDistanceKmOverride")]
    public double? ManualDeliveryDistanceKmOverride { get; set; }

    [BsonElement("deliveryRouteUpdatedAt")]
    public DateTime? DeliveryRouteUpdatedAt { get; set; }

    [BsonElement("notifiedAt")]
    public DateTime? NotifiedAt { get; set; }

    [BsonElement("notifiedTargetsCount")]
    public int NotifiedTargetsCount { get; set; }

    [BsonElement("lastNotificationChannel")]
    public string? LastNotificationChannel { get; set; }

    [BsonElement("tableNumber")]
    public string? TableNumber { get; set; }

    [BsonElement("dineInSessionId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? DineInSessionId { get; set; }

    [BsonElement("dineInOrderSource")]
    public string? DineInOrderSource { get; set; }

    [BsonElement("takeawaySource")]
    public string? TakeawaySource { get; set; } // waiter

    [BsonElement("takeawayPaymentTiming")]
    public string? TakeawayPaymentTiming { get; set; } // upfront, handover

    [BsonElement("packagingCharge")]
    public decimal PackagingCharge { get; set; }

    [BsonElement("paymentRecordedByUserId")]
    public string? PaymentRecordedByUserId { get; set; }

    [BsonElement("paymentRecordedAt")]
    public DateTime? PaymentRecordedAt { get; set; }

    [BsonElement("handedOverAt")]
    public DateTime? HandedOverAt { get; set; }

    [BsonElement("handedOverByUserId")]
    public string? HandedOverByUserId { get; set; }

    [BsonElement("handedOverByName")]
    public string? HandedOverByName { get; set; }

    [BsonElement("kotNumber")]
    public string? KotNumber { get; set; }

    [BsonElement("kotReprintCount")]
    public int KotReprintCount { get; set; }

    [BsonElement("lastKotReprintedAt")]
    public DateTime? LastKotReprintedAt { get; set; }

    [BsonElement("lastKotReprintedByUserId")]
    public string? LastKotReprintedByUserId { get; set; }

    [BsonElement("kotCancelledAt")]
    public DateTime? KotCancelledAt { get; set; }

    [BsonElement("kotCancelledByUserId")]
    public string? KotCancelledByUserId { get; set; }

    [BsonElement("kotCancellationReason")]
    public string? KotCancellationReason { get; set; }

    [BsonElement("approvalStatus")]
    public string? ApprovalStatus { get; set; }

    [BsonElement("approvedByUserId")]
    public string? ApprovedByUserId { get; set; }

    [BsonElement("approvedAt")]
    public DateTime? ApprovedAt { get; set; }

    [BsonElement("rejectedByUserId")]
    public string? RejectedByUserId { get; set; }

    [BsonElement("rejectedAt")]
    public DateTime? RejectedAt { get; set; }

    [BsonElement("rejectionReason")]
    public string? RejectionReason { get; set; }

    [BsonElement("roundNumber")]
    public int RoundNumber { get; set; } = 1;

    [BsonElement("kitchenChecklist")]
    public List<KitchenChecklistItem> KitchenChecklist { get; set; } = new();

    [BsonElement("kitchenPrepStartedAt")]
    public DateTime? KitchenPrepStartedAt { get; set; }

    [BsonElement("kitchenReadyAt")]
    public DateTime? KitchenReadyAt { get; set; }

    [BsonElement("kptMinutes")]
    public decimal? KptMinutes { get; set; }

    [BsonElement("kitchenHandledByUserId")]
    public string? KitchenHandledByUserId { get; set; }

    [BsonElement("kitchenHandledByRole")]
    public string? KitchenHandledByRole { get; set; }

    [BsonElement("kitchenAssignedStaffId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? KitchenAssignedStaffId { get; set; }

    [BsonElement("kitchenAssignedStaffName")]
    public string? KitchenAssignedStaffName { get; set; }

    [BsonElement("kitchenAssignedRole")]
    public string? KitchenAssignedRole { get; set; }

    [BsonElement("kitchenAssignedAt")]
    public DateTime? KitchenAssignedAt { get; set; }

    [BsonElement("isUrgent")]
    public bool IsUrgent { get; set; }

    [BsonElement("urgentReason")]
    public string? UrgentReason { get; set; }

    [BsonElement("urgentMarkedAt")]
    public DateTime? UrgentMarkedAt { get; set; }

    [BsonElement("loyaltyPointsAwarded")]
    public bool LoyaltyPointsAwarded { get; set; }

    [BsonElement("loyaltyPointsAwardedValue")]
    public int LoyaltyPointsAwardedValue { get; set; }

    // Soft-delete support
    [BsonElement("isDeleted")] public bool IsDeleted { get; set; }
    [BsonElement("deletedAt")] public DateTime? DeletedAt { get; set; }
    [BsonElement("deletedBy")] public string? DeletedBy { get; set; }
}

public class KitchenChecklistItem
{
    [BsonElement("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [BsonElement("label")]
    public string Label { get; set; } = string.Empty;

    [BsonElement("isCompleted")]
    public bool IsCompleted { get; set; }

    [BsonElement("completedAt")]
    public DateTime? CompletedAt { get; set; }

    [BsonElement("completedBy")]
    public string? CompletedBy { get; set; }
}

public class OrderItem
{
    [BsonElement("kitchenStatus")]
    public string KitchenStatus { get; set; } = "pending";

    [BsonElement("kitchenReadyAt")]
    public DateTime? KitchenReadyAt { get; set; }

    [BsonElement("kitchenReadyByUserId")]
    public string? KitchenReadyByUserId { get; set; }

    [BsonElement("servedAt")]
    public DateTime? ServedAt { get; set; }

    [BsonElement("servedByUserId")]
    public string? ServedByUserId { get; set; }

    [BsonElement("menuItemId")]
    public string MenuItemId { get; set; } = string.Empty;

    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("description")]
    public string? Description { get; set; }

    [BsonElement("categoryId")]
    public string? CategoryId { get; set; }

    [BsonElement("categoryName")]
    public string? CategoryName { get; set; }

    [BsonElement("quantity")]
    public int Quantity { get; set; }

    [BsonElement("price")]
    public decimal Price { get; set; }

    [BsonElement("baseUnitPrice")]
    public decimal BaseUnitPrice { get; set; }

    [BsonElement("selectedVariantName")]
    public string? SelectedVariantName { get; set; }

    [BsonElement("selectedVariantPrice")]
    public decimal? SelectedVariantPrice { get; set; }

    [BsonElement("selectedAddOns")]
    public List<OrderItemAddOn> SelectedAddOns { get; set; } = new();

    [BsonElement("addOnTotal")]
    public decimal AddOnTotal { get; set; }

    [BsonElement("total")]
    public decimal Total { get; set; }
}

public class OrderItemAddOn
{
    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("price")]
    public decimal Price { get; set; }
}

// Request/Response DTOs
public class CreateOrderRequest
{
    [Required(ErrorMessage = "Order must contain at least one item")]
    [MinLength(1, ErrorMessage = "Order must contain at least one item")]
    public List<OrderItemRequest> Items { get; set; } = new();
    
    [StringLength(500, ErrorMessage = "Delivery address cannot exceed 500 characters")]
    public string? DeliveryAddress { get; set; }

    [AllowedValuesList("cod", "upi-qr", "dine_in_tab")]
    public string PaymentMethod { get; set; } = "cod";

    [StringLength(100, ErrorMessage = "UPI reference cannot exceed 100 characters")]
    public string? UpiReference { get; set; }
    
    [IndianPhoneNumber]
    public string? PhoneNumber { get; set; }

    [StringLength(100, ErrorMessage = "Customer name cannot exceed 100 characters")]
    public string? CustomerName { get; set; }
    
    [StringLength(500, ErrorMessage = "Notes cannot exceed 500 characters")]
    public string? Notes { get; set; }

    [StringLength(500, ErrorMessage = "Preparation notes cannot exceed 500 characters")]
    public string? PreparationNotes { get; set; }

    [StringLength(50, ErrorMessage = "Coupon code cannot exceed 50 characters")]
    public string? CouponCode { get; set; }

    public int LoyaltyPointsUsed { get; set; }

    [AllowedValuesList("delivery", "pickup", "dine-in")]
    public string OrderType { get; set; } = "delivery";

    [AllowedValuesList("web", "shop", "partner")]
    public string? Channel { get; set; }

    public DateTime? ScheduledFor { get; set; }

    [Range(0, 10000, ErrorMessage = "Delivery fee must be between 0 and 10,000")]
    public decimal DeliveryFee { get; set; }

    [StringLength(20, ErrorMessage = "Table number cannot exceed 20 characters")]
    public string? TableNumber { get; set; }

    public string? DineInSessionId { get; set; }

    public int? RoundNumber { get; set; }

    public string? OutletId { get; set; }
}

public class OrderItemRequest
{
    [Required(ErrorMessage = "Menu item ID is required")]
    public string MenuItemId { get; set; } = string.Empty;
    
    [Range(1, 1000, ErrorMessage = "Quantity must be between 1 and 1000")]
    public int Quantity { get; set; }

    [StringLength(100, ErrorMessage = "Selected variant name cannot exceed 100 characters")]
    public string? SelectedVariantName { get; set; }

    public List<string>? SelectedAddOnNames { get; set; }
}

public class UpdateOrderStatusRequest
{
    [Required(ErrorMessage = "Status is required")]
    [AllowedValuesList("pending", "confirmed", "preparing", "ready", "out-for-delivery", "delivered", "cancelled")]
    public string Status { get; set; } = string.Empty;
}

public class AdminConfirmPaymentRequest
{
    [StringLength(100, ErrorMessage = "Payment reference cannot exceed 100 characters")]
    public string? PaymentReference { get; set; }

    [StringLength(300, ErrorMessage = "Admin note cannot exceed 300 characters")]
    public string? AdminNote { get; set; }
}

public class DeleteOrderHistoryRequest
{
    [Required(ErrorMessage = "Outlet code is required")]
    [StringLength(10, MinimumLength = 2, ErrorMessage = "Outlet code must be between 2 and 10 characters")]
    public string OutletCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "Keep-from date is required")]
    [StringLength(40, ErrorMessage = "Keep-from date cannot exceed 40 characters")]
    public string KeepFromDate { get; set; } = string.Empty;
}

public class OrderResponse
{
    public string? OrderSource { get; set; }
    public string? DeliveryLocationUrl { get; set; }
    public string? RecordedByName { get; set; }
    public string? TrackingPath { get; set; }
    public string? KotNumber { get; set; }
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public string? WaiterUserId { get; set; }
    public string? WaiterName { get; set; }
    public string? UserEmail { get; set; }
    public List<OrderItem> Items { get; set; } = new();
    public decimal Subtotal { get; set; }
    public decimal Tax { get; set; }
    public decimal PlatformCharge { get; set; }
    public decimal Total { get; set; }
    public string? CouponCode { get; set; }
    public decimal DiscountAmount { get; set; }
    public int LoyaltyPointsUsed { get; set; }
    public decimal LoyaltyDiscountAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public string? RazorpayOrderId { get; set; }
    public string? RazorpayPaymentId { get; set; }
    public string? UpiReference { get; set; }
    public string? UpiConfirmedBy { get; set; }
    public DateTime? UpiConfirmedAt { get; set; }
    public string? UpiProofUrl { get; set; }
    public string? DeliveryAddress { get; set; }
    public string? PhoneNumber { get; set; }
    public string? PreparationNotes { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ReceiptImageUrl { get; set; }
    public decimal DeliveryFee { get; set; }
    public string? OutletId { get; set; }
    public string OrderType { get; set; } = "delivery";
    public string Channel { get; set; } = "web";
    public DateTime? ScheduledFor { get; set; }
    public bool IsScheduled { get; set; }
    public string? DeliveryPartnerId { get; set; }
    public string? DeliveryPartnerName { get; set; }
    public string? DeliveryRouteUrl { get; set; }
    public string? DeliveryRouteShortCode { get; set; }
    public string? DeliveryRouteShortUrl { get; set; }
    public double? DeliveryDistanceKm { get; set; }
    public int? DeliveryEtaMinutes { get; set; }
    public decimal? ManualDeliveryFeeOverride { get; set; }
    public double? ManualDeliveryDistanceKmOverride { get; set; }
    public DateTime? DeliveryRouteUpdatedAt { get; set; }
    public string? TableNumber { get; set; }
    public string? DineInSessionId { get; set; }
    public int RoundNumber { get; set; } = 1;
    public bool LoyaltyPointsAwarded { get; set; }
    public int LoyaltyPointsAwardedValue { get; set; }
}

public class OutletSuggestionResponse
{
    public string OutletId { get; set; } = string.Empty;
    public string OutletName { get; set; } = string.Empty;
    public string OutletCode { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public double Rating { get; set; }
    public int EstimatedEtaMinutes { get; set; }
    public double EstimatedDistanceKm { get; set; }
    public decimal EstimatedDeliveryFee { get; set; }
    public double Score { get; set; }
    public List<string> Reasons { get; set; } = new();
}
