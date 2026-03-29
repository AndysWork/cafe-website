using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Cafe.Api.Services;

namespace Cafe.Api.Models;

public class CustomerSegment
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("userId")]
    public string UserId { get; set; } = string.Empty;

    [BsonElement("username")]
    public string Username { get; set; } = string.Empty;

    [BsonElement("email")]
    public string? Email { get; set; }

    [BsonElement("phone")]
    public string? Phone { get; set; }

    [BsonElement("segment")]
    public string Segment { get; set; } = "new"; // new, regular, vip, dormant, at-risk

    [BsonElement("totalOrders")]
    public int TotalOrders { get; set; }

    [BsonElement("totalSpent")]
    public decimal TotalSpent { get; set; }

    [BsonElement("averageOrderValue")]
    public decimal AverageOrderValue { get; set; }

    [BsonElement("lastOrderDate")]
    public DateTime? LastOrderDate { get; set; }

    [BsonElement("firstOrderDate")]
    public DateTime? FirstOrderDate { get; set; }

    [BsonElement("favoriteItems")]
    public List<string> FavoriteItems { get; set; } = new();

    [BsonElement("preferredPaymentMethod")]
    public string? PreferredPaymentMethod { get; set; }

    [BsonElement("tags")]
    public List<string> Tags { get; set; } = new();

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = MongoService.GetIstNow();
}

public class SegmentSummary
{
    public string Segment { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal AverageOrderValue { get; set; }
}
