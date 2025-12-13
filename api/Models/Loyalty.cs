using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Cafe.Api.Models;

public class LoyaltyAccount
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("userId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string UserId { get; set; } = string.Empty;

    [BsonElement("username")]
    public string Username { get; set; } = string.Empty;

    [BsonElement("currentPoints")]
    public int CurrentPoints { get; set; } = 0;

    [BsonElement("totalPointsEarned")]
    public int TotalPointsEarned { get; set; } = 0;

    [BsonElement("totalPointsRedeemed")]
    public int TotalPointsRedeemed { get; set; } = 0;

    [BsonElement("tier")]
    public string Tier { get; set; } = "Bronze"; // Bronze, Silver, Gold, Platinum

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class Reward
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("description")]
    public string Description { get; set; } = string.Empty;

    [BsonElement("pointsCost")]
    public int PointsCost { get; set; }

    [BsonElement("icon")]
    public string Icon { get; set; } = "🎁";

    [BsonElement("isActive")]
    public bool IsActive { get; set; } = true;

    [BsonElement("expiresAt")]
    public DateTime? ExpiresAt { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class PointsTransaction
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("userId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string UserId { get; set; } = string.Empty;

    [BsonElement("points")]
    public int Points { get; set; }

    [BsonElement("type")]
    public string Type { get; set; } = string.Empty; // "earned", "redeemed", "expired"

    [BsonElement("description")]
    public string Description { get; set; } = string.Empty;

    [BsonElement("orderId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? OrderId { get; set; }

    [BsonElement("rewardId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? RewardId { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// DTOs for API responses
public class LoyaltyAccountResponse
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public int CurrentPoints { get; set; }
    public int TotalPointsEarned { get; set; }
    public int TotalPointsRedeemed { get; set; }
    public string Tier { get; set; } = string.Empty;
    public string? NextTier { get; set; }
    public int? PointsToNextTier { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class RewardResponse
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int PointsCost { get; set; }
    public string Icon { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool CanRedeem { get; set; } // Based on user's current points
}

public class PointsTransactionResponse
{
    public string Id { get; set; } = string.Empty;
    public int Points { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? OrderId { get; set; }
    public string? RewardId { get; set; }
    public DateTime CreatedAt { get; set; }
}
