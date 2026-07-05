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
    public string Status { get; set; } = "pending"; // scheduled/pending, confirmed, preparing, ready, out-for-delivery, delivered, cancelled

    [BsonElement("paymentStatus")]
    public string PaymentStatus { get; set; } = "pending"; // pending, paid, refunded

    [BsonElement("paymentMethod")]
    public string PaymentMethod { get; set; } = "cod"; // cod, razorpay, upi-qr

    [BsonElement("razorpayOrderId")]
    public string? RazorpayOrderId { get; set; }

    [BsonElement("razorpayPaymentId")]
    public string? RazorpayPaymentId { get; set; }

    [BsonElement("razorpaySignature")]
    public string? RazorpaySignature { get; set; }

    [BsonElement("razorpayRefundId")]
    public string? RazorpayRefundId { get; set; }

    [BsonElement("deliveryAddress")]
    public string? DeliveryAddress { get; set; }

    [BsonElement("phoneNumber")]
    public string? PhoneNumber { get; set; }

    [BsonElement("notes")]
    public string? Notes { get; set; }

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

    [BsonElement("tableNumber")]
    public string? TableNumber { get; set; }

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

    [BsonElement("total")]
    public decimal Total { get; set; }
}

// Request/Response DTOs
public class CreateOrderRequest
{
    [Required(ErrorMessage = "Order must contain at least one item")]
    [MinLength(1, ErrorMessage = "Order must contain at least one item")]
    public List<OrderItemRequest> Items { get; set; } = new();
    
    [StringLength(500, ErrorMessage = "Delivery address cannot exceed 500 characters")]
    public string? DeliveryAddress { get; set; }

    [AllowedValuesList("cod", "razorpay", "upi-qr")]
    public string PaymentMethod { get; set; } = "cod";

    public string? RazorpayPaymentId { get; set; }
    public string? RazorpayOrderId { get; set; }
    public string? RazorpaySignature { get; set; }
    
    [IndianPhoneNumber]
    public string? PhoneNumber { get; set; }
    
    [StringLength(500, ErrorMessage = "Notes cannot exceed 500 characters")]
    public string? Notes { get; set; }

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

    public decimal WalletAmountUsed { get; set; }

    [StringLength(20, ErrorMessage = "Table number cannot exceed 20 characters")]
    public string? TableNumber { get; set; }

    public string? OutletId { get; set; }
}

public class OrderItemRequest
{
    [Required(ErrorMessage = "Menu item ID is required")]
    public string MenuItemId { get; set; } = string.Empty;
    
    [Range(1, 1000, ErrorMessage = "Quantity must be between 1 and 1000")]
    public int Quantity { get; set; }
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

public class OrderResponse
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
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
    public string? DeliveryAddress { get; set; }
    public string? PhoneNumber { get; set; }
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
    public decimal WalletAmountUsed { get; set; }
    public string? DeliveryPartnerId { get; set; }
    public string? DeliveryPartnerName { get; set; }
    public string? TableNumber { get; set; }
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
