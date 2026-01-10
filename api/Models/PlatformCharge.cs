using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace Cafe.Api.Models;

// Monthly charges for Zomato/Swiggy platforms
public class PlatformCharge
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("outletId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? OutletId { get; set; }

    [BsonElement("platform")]
    [Required]
    public string Platform { get; set; } = string.Empty; // "Zomato" or "Swiggy"

    [BsonElement("month")]
    [Required]
    [Range(1, 12)]
    public int Month { get; set; } // 1-12

    [BsonElement("year")]
    [Required]
    [Range(2020, 2100)]
    public int Year { get; set; }

    [BsonElement("charges")]
    [Required]
    public decimal Charges { get; set; } // Monthly charges amount

    [BsonElement("chargeType")]
    public string? ChargeType { get; set; } // e.g., "Commission", "Subscription", "Other"

    [BsonElement("notes")]
    public string? Notes { get; set; }

    [BsonElement("recordedBy")]
    public string RecordedBy { get; set; } = string.Empty;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; }
}

// Request DTOs
public class CreatePlatformChargeRequest
{
    [Required(ErrorMessage = "Platform is required")]
    [RegularExpression("^(Zomato|Swiggy)$", ErrorMessage = "Platform must be either 'Zomato' or 'Swiggy'")]
    public string Platform { get; set; } = string.Empty;

    [Required(ErrorMessage = "Month is required")]
    [Range(1, 12, ErrorMessage = "Month must be between 1 and 12")]
    public int Month { get; set; }

    [Required(ErrorMessage = "Year is required")]
    [Range(2020, 2100, ErrorMessage = "Year must be between 2020 and 2100")]
    public int Year { get; set; }

    [Required(ErrorMessage = "Charges amount is required")]
    [Range(0, 10000000, ErrorMessage = "Charges must be between 0 and 10,000,000")]
    public decimal Charges { get; set; }

    public string? ChargeType { get; set; }
    public string? Notes { get; set; }
}

public class UpdatePlatformChargeRequest
{
    [Range(0, 10000000, ErrorMessage = "Charges must be between 0 and 10,000,000")]
    public decimal? Charges { get; set; }

    public string? ChargeType { get; set; }
    public string? Notes { get; set; }
}

public class PlatformChargeResponse
{
    public string Id { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public int Month { get; set; }
    public int Year { get; set; }
    public decimal Charges { get; set; }
    public string? ChargeType { get; set; }
    public string? Notes { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
