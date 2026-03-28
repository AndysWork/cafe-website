using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Cafe.Api.Services;

namespace Cafe.Api.Models;

public class AppNotification
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("userId")]
    public string UserId { get; set; } = string.Empty;

    [BsonElement("type")]
    public string Type { get; set; } = "system";  // order_status, loyalty_points, offer, system, stock_alert

    [BsonElement("title")]
    public string Title { get; set; } = string.Empty;

    [BsonElement("message")]
    public string Message { get; set; } = string.Empty;

    [BsonElement("data")]
    public Dictionary<string, string>? Data { get; set; }

    [BsonElement("actionUrl")]
    public string? ActionUrl { get; set; }

    [BsonElement("imageUrl")]
    public string? ImageUrl { get; set; }

    [BsonElement("isRead")]
    public bool IsRead { get; set; } = false;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = MongoService.GetIstNow();
}

public class NotificationPreferences
{
    [BsonElement("orderUpdates")]
    public bool OrderUpdates { get; set; } = true;

    [BsonElement("loyaltyPoints")]
    public bool LoyaltyPoints { get; set; } = true;

    [BsonElement("offers")]
    public bool Offers { get; set; } = true;

    [BsonElement("systemNotifications")]
    public bool SystemNotifications { get; set; } = true;

    [BsonElement("emailNotifications")]
    public bool EmailNotifications { get; set; } = true;

    [BsonElement("pushNotifications")]
    public bool PushNotifications { get; set; } = true;
}

// --- DTOs ---

public class NotificationListResponse
{
    public List<AppNotification> Notifications { get; set; } = new();
    public int UnreadCount { get; set; }
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class UpdateNotificationPreferencesRequest
{
    public bool? OrderUpdates { get; set; }
    public bool? LoyaltyPoints { get; set; }
    public bool? Offers { get; set; }
    public bool? SystemNotifications { get; set; }
    public bool? EmailNotifications { get; set; }
    public bool? PushNotifications { get; set; }
}
