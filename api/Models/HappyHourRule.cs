using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Cafe.Api.Services;
using System.ComponentModel.DataAnnotations;

namespace Cafe.Api.Models;

public class HappyHourRule
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("outletId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string OutletId { get; set; } = string.Empty;

    [BsonElement("name")]
    [Required] [StringLength(200, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [BsonElement("description")]
    [StringLength(500)]
    public string? Description { get; set; }

    [BsonElement("startTime")]
    public string StartTime { get; set; } = string.Empty; // "14:00"

    [BsonElement("endTime")]
    public string EndTime { get; set; } = string.Empty; // "17:00"

    [BsonElement("daysOfWeek")]
    public List<int> DaysOfWeek { get; set; } = new(); // 0=Sun, 1=Mon, ... 6=Sat

    [BsonElement("discountType")]
    public string DiscountType { get; set; } = "percentage"; // percentage, flat

    [BsonElement("discountValue")]
    public decimal DiscountValue { get; set; }

    [BsonElement("maxDiscount")]
    public decimal? MaxDiscount { get; set; }

    [BsonElement("applicableCategories")]
    public List<string>? ApplicableCategories { get; set; }

    [BsonElement("applicableItems")]
    public List<string>? ApplicableItems { get; set; }

    [BsonElement("isActive")]
    public bool IsActive { get; set; } = true;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = MongoService.GetIstNow();

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = MongoService.GetIstNow();
}

public class CreateHappyHourRequest
{
    [Required] [StringLength(200, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    [Required]
    public string StartTime { get; set; } = string.Empty;

    [Required]
    public string EndTime { get; set; } = string.Empty;

    [Required] [MinLength(1)]
    public List<int> DaysOfWeek { get; set; } = new();

    [Required]
    public string DiscountType { get; set; } = "percentage";

    [Range(0.01, 10000)]
    public decimal DiscountValue { get; set; }

    public decimal? MaxDiscount { get; set; }

    public List<string>? ApplicableCategories { get; set; }
    public List<string>? ApplicableItems { get; set; }
}

public class ActiveHappyHourResponse
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string DiscountType { get; set; } = string.Empty;
    public decimal DiscountValue { get; set; }
    public decimal? MaxDiscount { get; set; }
    public string EndTime { get; set; } = string.Empty;
    public List<string>? ApplicableCategories { get; set; }
    public List<string>? ApplicableItems { get; set; }
}
