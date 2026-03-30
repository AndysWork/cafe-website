using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Cafe.Api.Services;

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

    [BsonElement("referralCode")]
    public string ReferralCode { get; set; } = string.Empty;

    [BsonElement("referredBy")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? ReferredBy { get; set; }

    [BsonElement("totalReferrals")]
    public int TotalReferrals { get; set; } = 0;

    [BsonElement("loyaltyCardNumber")]
    public string LoyaltyCardNumber { get; set; } = string.Empty;

    [BsonElement("dateOfBirth")]
    public DateTime? DateOfBirth { get; set; }

    [BsonElement("lastBirthdayRewardYear")]
    public int? LastBirthdayRewardYear { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = MongoService.GetIstNow();

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = MongoService.GetIstNow();
}

public class Reward : ISoftDeletable
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
    public DateTime CreatedAt { get; set; } = MongoService.GetIstNow();

    // Soft-delete support
    [BsonElement("isDeleted")] public bool IsDeleted { get; set; }
    [BsonElement("deletedAt")] public DateTime? DeletedAt { get; set; }
    [BsonElement("deletedBy")] public string? DeletedBy { get; set; }
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

    [BsonElement("expiresAt")]
    public DateTime? ExpiresAt { get; set; }

    [BsonElement("isExpired")]
    public bool IsExpired { get; set; } = false;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = MongoService.GetIstNow();
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
    public string ReferralCode { get; set; } = string.Empty;
    public int TotalReferrals { get; set; }
    public string LoyaltyCardNumber { get; set; } = string.Empty;
    public DateTime? DateOfBirth { get; set; }
    public double TierMultiplier { get; set; } = 1.0;
    public string[] TierBenefits { get; set; } = Array.Empty<string>();
    public int ExpiringPoints { get; set; }
    public DateTime? ExpiringDate { get; set; }
    public bool BirthdayBonusAvailable { get; set; }
    public int BirthdayBonusPoints { get; set; }
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
    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

// Request DTOs
public class TransferPointsRequest
{
    public string RecipientUsername { get; set; } = string.Empty;
    public int Points { get; set; }
}

public class SetBirthdayRequest
{
    public DateTime DateOfBirth { get; set; }
}

public class ApplyReferralRequest
{
    public string ReferralCode { get; set; } = string.Empty;
}
