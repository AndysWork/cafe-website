using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;
using Cafe.Api.Services;

namespace Cafe.Api.Models;

public class OrderIssue
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("orderId")]
    [BsonRepresentation(BsonType.ObjectId)]
    [Required]
    public string OrderId { get; set; } = string.Empty;

    [BsonElement("outletId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? OutletId { get; set; }

    [BsonElement("userId")]
    [Required]
    public string UserId { get; set; } = string.Empty;

    [BsonElement("username")]
    public string Username { get; set; } = string.Empty;

    [BsonElement("category")]
    public string Category { get; set; } = "other"; // missing-item, wrong-item, damaged-item, delay, quality, other

    [BsonElement("description")]
    public string Description { get; set; } = string.Empty;

    [BsonElement("status")]
    public string Status { get; set; } = "open"; // open, in-progress, resolved, closed

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = MongoService.GetIstNow();

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = MongoService.GetIstNow();
}

public class CreateOrderIssueRequest
{
    [Required]
    [StringLength(40)]
    public string Category { get; set; } = "other";

    [Required]
    [StringLength(1000, MinimumLength = 5)]
    public string Description { get; set; } = string.Empty;
}

public class CancelOrderItemRequest
{
    [Range(1, 1000)]
    public int Quantity { get; set; } = 1;
}

public class DeliveryTrackingPartnerInfo
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public string? VehicleType { get; set; }
    public string? VehicleNumber { get; set; }
    public string? Status { get; set; }
    public double? CurrentLatitude { get; set; }
    public double? CurrentLongitude { get; set; }
    public DateTime? LastLocationUpdatedAt { get; set; }
}

public class OrderTrackingResponse
{
    public string OrderId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string OrderType { get; set; } = "delivery";
    public bool IsScheduled { get; set; }
    public DateTime? ScheduledFor { get; set; }
    public DateTime? EstimatedDeliveryAt { get; set; }
    public int? EtaMinutes { get; set; }
    public string EtaLabel { get; set; } = "";
    public bool LiveLocationAvailable { get; set; }
    public string? LiveLocationMapUrl { get; set; }
    public DeliveryTrackingPartnerInfo? DeliveryPartner { get; set; }
    public string SupportPhone { get; set; } = "+91-9876543210";
    public string SupportEmail { get; set; } = "support@cafemanagement.com";
}
