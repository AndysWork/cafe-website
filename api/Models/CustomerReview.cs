using System.ComponentModel.DataAnnotations;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Cafe.Api.Models;

public class CustomerReview
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("outletId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? OutletId { get; set; }

    [BsonElement("orderId")]
    [Required]
    public string OrderId { get; set; } = string.Empty;

    [BsonElement("userId")]
    [Required]
    public string UserId { get; set; } = string.Empty;

    [BsonElement("username")]
    public string Username { get; set; } = string.Empty;

    [BsonElement("rating")]
    [Required]
    [Range(1, 5)]
    public int Rating { get; set; }

    [BsonElement("comment")]
    [StringLength(1000)]
    public string? Comment { get; set; }

    [BsonElement("itemRatings")]
    public List<ItemRating> ItemRatings { get; set; } = new();

    [BsonElement("loyaltyBonusAwarded")]
    public bool LoyaltyBonusAwarded { get; set; }

    [BsonElement("loyaltyBonusPoints")]
    public int LoyaltyBonusPoints { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class CreateReviewRequest
{
    [Required]
    public string OrderId { get; set; } = string.Empty;

    [Required]
    [Range(1, 5)]
    public int Rating { get; set; }

    [StringLength(1000)]
    public string? Comment { get; set; }

    public List<CreateItemRatingRequest>? ItemRatings { get; set; }
}

public class ItemRating
{
    [BsonElement("menuItemId")]
    [Required]
    public string MenuItemId { get; set; } = string.Empty;

    [BsonElement("itemName")]
    public string ItemName { get; set; } = string.Empty;

    [BsonElement("rating")]
    [Range(1, 5)]
    public int Rating { get; set; }
}

public class CreateItemRatingRequest
{
    [Required]
    public string MenuItemId { get; set; } = string.Empty;

    [Range(1, 5)]
    public int Rating { get; set; }
}
