using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Cafe.Api.Models;

public class Offer
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("title")]
    public required string Title { get; set; }

    [BsonElement("description")]
    public required string Description { get; set; }

    [BsonElement("discountType")]
    public required string DiscountType { get; set; } // "percentage", "flat", "bogo"

    [BsonElement("discountValue")]
    public decimal DiscountValue { get; set; } // Percentage or flat amount

    [BsonElement("code")]
    public required string Code { get; set; }

    [BsonElement("icon")]
    public string Icon { get; set; } = "🎁";

    [BsonElement("minOrderAmount")]
    public decimal? MinOrderAmount { get; set; }

    [BsonElement("maxDiscount")]
    public decimal? MaxDiscount { get; set; }

    [BsonElement("validFrom")]
    public DateTime ValidFrom { get; set; }

    [BsonElement("validTill")]
    public DateTime ValidTill { get; set; }

    [BsonElement("isActive")]
    public bool IsActive { get; set; } = true;

    [BsonElement("usageLimit")]
    public int? UsageLimit { get; set; }

    [BsonElement("usageCount")]
    public int UsageCount { get; set; } = 0;

    [BsonElement("applicableCategories")]
    public List<string>? ApplicableCategories { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class OfferValidationRequest
{
    public required string Code { get; set; }
    public decimal OrderAmount { get; set; }
    public List<string>? Categories { get; set; }
}

public class OfferValidationResponse
{
    public bool IsValid { get; set; }
    public string? Message { get; set; }
    public Offer? Offer { get; set; }
    public decimal DiscountAmount { get; set; }
}
