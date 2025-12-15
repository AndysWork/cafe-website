using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Cafe.Api.Services;
using System.ComponentModel.DataAnnotations;
using Cafe.Api.Helpers;

namespace Cafe.Api.Models;

public class Offer
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("title")]
    [Required(ErrorMessage = "Title is required")]
    [StringLength(100, MinimumLength = 3, ErrorMessage = "Title must be between 3 and 100 characters")]
    public required string Title { get; set; }

    [BsonElement("description")]
    [Required(ErrorMessage = "Description is required")]
    [StringLength(500, MinimumLength = 10, ErrorMessage = "Description must be between 10 and 500 characters")]
    public required string Description { get; set; }

    [BsonElement("discountType")]
    [Required(ErrorMessage = "Discount type is required")]
    [AllowedValuesList("percentage", "flat", "bogo")]
    public required string DiscountType { get; set; } // "percentage", "flat", "bogo"

    [BsonElement("discountValue")]
    [Range(0.01, 100000, ErrorMessage = "Discount value must be between 0.01 and 100,000")]
    public decimal DiscountValue { get; set; } // Percentage or flat amount

    [BsonElement("code")]
    [Required(ErrorMessage = "Code is required")]
    [StringLength(20, MinimumLength = 3, ErrorMessage = "Code must be between 3 and 20 characters")]
    [RegularExpression(@"^[A-Z0-9]+$", ErrorMessage = "Code must contain only uppercase letters and numbers")]
    public required string Code { get; set; }

    [BsonElement("icon")]
    [StringLength(10, ErrorMessage = "Icon cannot exceed 10 characters")]
    public string Icon { get; set; } = "🎁";

    [BsonElement("minOrderAmount")]
    [Range(0, 100000, ErrorMessage = "Minimum order amount must be between 0 and 100,000")]
    public decimal? MinOrderAmount { get; set; }

    [BsonElement("maxDiscount")]
    [Range(0, 100000, ErrorMessage = "Maximum discount must be between 0 and 100,000")]
    public decimal? MaxDiscount { get; set; }

    [BsonElement("validFrom")]
    public DateTime ValidFrom { get; set; }

    [BsonElement("validTill")]
    public DateTime ValidTill { get; set; }

    [BsonElement("isActive")]
    public bool IsActive { get; set; } = true;

    [BsonElement("usageLimit")]
    [Range(0, int.MaxValue, ErrorMessage = "Usage limit must be a positive number")]
    public int? UsageLimit { get; set; }

    [BsonElement("usageCount")]
    public int UsageCount { get; set; } = 0;

    [BsonElement("applicableCategories")]
    public List<string>? ApplicableCategories { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = MongoService.GetIstNow();

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = MongoService.GetIstNow();
}

public class OfferValidationRequest
{
    [Required(ErrorMessage = "Offer code is required")]
    [StringLength(20, MinimumLength = 3, ErrorMessage = "Code must be between 3 and 20 characters")]
    public required string Code { get; set; }
    
    [Range(0.01, 1000000, ErrorMessage = "Order amount must be between 0.01 and 1,000,000")]
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
