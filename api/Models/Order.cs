using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Cafe.Api.Services;

namespace Cafe.Api.Models;

public class Order
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

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

    [BsonElement("total")]
    public decimal Total { get; set; }

    [BsonElement("status")]
    public string Status { get; set; } = "pending"; // pending, confirmed, preparing, ready, delivered, cancelled

    [BsonElement("paymentStatus")]
    public string PaymentStatus { get; set; } = "pending"; // pending, paid, refunded

    [BsonElement("deliveryAddress")]
    public string? DeliveryAddress { get; set; }

    [BsonElement("phoneNumber")]
    public string? PhoneNumber { get; set; }

    [BsonElement("notes")]
    public string? Notes { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = MongoService.GetIstNow();

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = MongoService.GetIstNow();

    [BsonElement("completedAt")]
    public DateTime? CompletedAt { get; set; }
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
    public List<OrderItemRequest> Items { get; set; } = new();
    public string? DeliveryAddress { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Notes { get; set; }
}

public class OrderItemRequest
{
    public string MenuItemId { get; set; } = string.Empty;
    public int Quantity { get; set; }
}

public class UpdateOrderStatusRequest
{
    public string Status { get; set; } = string.Empty;
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
    public decimal Total { get; set; }
    public string Status { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string? DeliveryAddress { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
