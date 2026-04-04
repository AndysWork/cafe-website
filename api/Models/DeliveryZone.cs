using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Cafe.Api.Services;
using System.ComponentModel.DataAnnotations;

namespace Cafe.Api.Models;

public class DeliveryZone : ISoftDeletable
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("outletId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string OutletId { get; set; } = string.Empty;

    [BsonElement("zoneName")]
    [Required(ErrorMessage = "Zone name is required")]
    [StringLength(100, MinimumLength = 2)]
    public string ZoneName { get; set; } = string.Empty;

    [BsonElement("minDistance")]
    [Range(0, 1000)]
    public double MinDistance { get; set; }

    [BsonElement("maxDistance")]
    [Range(0.1, 1000)]
    public double MaxDistance { get; set; }

    [BsonElement("deliveryFee")]
    [Range(0, 10000)]
    public decimal DeliveryFee { get; set; }

    [BsonElement("freeDeliveryAbove")]
    public decimal? FreeDeliveryAbove { get; set; }

    [BsonElement("estimatedMinutes")]
    [Range(5, 300)]
    public int EstimatedMinutes { get; set; } = 30;

    [BsonElement("isActive")]
    public bool IsActive { get; set; } = true;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = MongoService.GetIstNow();

    // Soft-delete support
    [BsonElement("isDeleted")] public bool IsDeleted { get; set; }
    [BsonElement("deletedAt")] public DateTime? DeletedAt { get; set; }
    [BsonElement("deletedBy")] public string? DeletedBy { get; set; }
}

// DTOs
public class CreateDeliveryZoneRequest
{
    [Required(ErrorMessage = "Zone name is required")]
    [StringLength(100, MinimumLength = 2)]
    public string ZoneName { get; set; } = string.Empty;

    [Range(0, 1000)]
    public double MinDistance { get; set; }

    [Range(0.1, 1000)]
    public double MaxDistance { get; set; }

    [Range(0, 10000)]
    public decimal DeliveryFee { get; set; }

    public decimal? FreeDeliveryAbove { get; set; }

    [Range(5, 300)]
    public int EstimatedMinutes { get; set; } = 30;

    public bool IsActive { get; set; } = true;
}
