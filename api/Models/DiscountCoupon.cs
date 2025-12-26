using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace Cafe.Api.Models;

// Discount Coupon Management
public class DiscountCoupon
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("couponCode")]
    [Required]
    public string CouponCode { get; set; } = string.Empty;

    [BsonElement("platform")]
    [Required]
    public string Platform { get; set; } = string.Empty; // "Zomato" or "Swiggy"

    [BsonElement("isActive")]
    public bool IsActive { get; set; } = true;

    [BsonElement("maxValue")]
    public decimal? MaxValue { get; set; }

    [BsonElement("discountPercentage")]
    public decimal? DiscountPercentage { get; set; }

    [BsonElement("createdBy")]
    public string CreatedBy { get; set; } = string.Empty;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; }
}

public class DiscountCouponRequest
{
    [Required]
    public string CouponCode { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^(Zomato|Swiggy)$", ErrorMessage = "Platform must be either 'Zomato' or 'Swiggy'")]
    public string Platform { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    [Range(0, 10000, ErrorMessage = "Max value must be between 0 and 10000")]
    public decimal? MaxValue { get; set; }

    [Range(0, 100, ErrorMessage = "Discount percentage must be between 0 and 100")]
    public decimal? DiscountPercentage { get; set; }
}

public class UpdateCouponStatusRequest
{
    [Required]
    public bool IsActive { get; set; }
}

public class UpdateCouponMaxValueRequest
{
    [Range(0, 10000, ErrorMessage = "Max value must be between 0 and 10000")]
    public decimal? MaxValue { get; set; }
}

public class UpdateCouponDiscountPercentageRequest
{
    [Range(0, 100, ErrorMessage = "Discount percentage must be between 0 and 100")]
    public decimal? DiscountPercentage { get; set; }
}
