using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Cafe.Api.Models;

/// <summary>
/// Tracks individual user activity events (login, page view, feature usage, cart actions, API calls)
/// </summary>
public class UserActivityEvent
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    /// <summary>User ID (null for anonymous visitors)</summary>
    public string? UserId { get; set; }

    /// <summary>Username for quick reference</summary>
    public string? Username { get; set; }

    /// <summary>Event type: Login, Logout, PageView, FeatureUsage, CartView, CartAdd, CartRemove, ApiCall</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>Specific feature or page name (e.g., "Menu", "Orders", "Price Calculator")</summary>
    public string? FeatureName { get; set; }

    /// <summary>Additional detail (e.g. API endpoint, menu item ID added to cart)</summary>
    public string? Detail { get; set; }

    /// <summary>API response time in milliseconds (for ApiCall events)</summary>
    public long? ResponseTimeMs { get; set; }

    /// <summary>HTTP method (for ApiCall events)</summary>
    public string? HttpMethod { get; set; }

    /// <summary>HTTP status code (for ApiCall events)</summary>
    public int? StatusCode { get; set; }

    /// <summary>User's session ID to group activity within a session</summary>
    public string? SessionId { get; set; }

    /// <summary>User role at the time of the event</summary>
    public string? UserRole { get; set; }

    /// <summary>Outlet ID context</summary>
    public string? OutletId { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Tracks active user sessions
/// </summary>
public class UserSession
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    public string UserId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string UserRole { get; set; } = string.Empty;

    public string SessionId { get; set; } = string.Empty;

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime LoginTime { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? LogoutTime { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime LastActiveTime { get; set; }

    /// <summary>Whether the session is still considered active</summary>
    public bool IsActive { get; set; } = true;
}

// ─── DTOs for analytics responses ───

public class AnalyticsDashboardResponse
{
    public UserMetrics UserMetrics { get; set; } = new();
    public List<FeatureUsageStat> TopFeatures { get; set; } = new();
    public List<ApiPerformanceStat> ApiPerformance { get; set; } = new();
    public CartAnalytics CartAnalytics { get; set; } = new();
    public List<DailyActiveUserStat> DailyActiveUsers { get; set; } = new();
    public List<HourlyActivityStat> HourlyActivity { get; set; } = new();
    public List<RecentSessionInfo> RecentSessions { get; set; } = new();
}

public class UserMetrics
{
    public int TotalRegisteredUsers { get; set; }
    public int CurrentlyActiveUsers { get; set; }
    public int TodayLogins { get; set; }
    public int WeekLogins { get; set; }
    public int MonthLogins { get; set; }
    public int TotalLoginEvents { get; set; }
    public int UniqueUsersToday { get; set; }
    public int UniqueUsersThisWeek { get; set; }
    public int UniqueUsersThisMonth { get; set; }
}

public class FeatureUsageStat
{
    public string FeatureName { get; set; } = string.Empty;
    public long UsageCount { get; set; }
    public long UniqueUsers { get; set; }
}

public class ApiPerformanceStat
{
    public string Endpoint { get; set; } = string.Empty;
    public long TotalCalls { get; set; }
    public double AvgResponseTimeMs { get; set; }
    public double MaxResponseTimeMs { get; set; }
    public double MinResponseTimeMs { get; set; }
    public double P95ResponseTimeMs { get; set; }
    public long ErrorCount { get; set; }
}

public class CartAnalytics
{
    public long TotalCartViews { get; set; }
    public long TotalAddToCart { get; set; }
    public long TotalCartRemovals { get; set; }
    public long UniqueUsersWhoCarted { get; set; }
    public long UniqueUsersWhoBrowsed { get; set; }
    public List<CartItemStat> TopCartedItems { get; set; } = new();
}

public class CartItemStat
{
    public string ItemName { get; set; } = string.Empty;
    public long AddCount { get; set; }
}

public class DailyActiveUserStat
{
    public string Date { get; set; } = string.Empty;
    public int ActiveUsers { get; set; }
    public int LoginCount { get; set; }
}

public class HourlyActivityStat
{
    public int Hour { get; set; }
    public long EventCount { get; set; }
}

public class RecentSessionInfo
{
    public string Username { get; set; } = string.Empty;
    public string UserRole { get; set; } = string.Empty;
    public DateTime LoginTime { get; set; }
    public DateTime? LogoutTime { get; set; }
    public DateTime LastActiveTime { get; set; }
    public bool IsActive { get; set; }
}

// ─── Request DTOs ───

public class TrackEventRequest
{
    public string EventType { get; set; } = string.Empty;
    public string? FeatureName { get; set; }
    public string? Detail { get; set; }
    public long? ResponseTimeMs { get; set; }
    public string? HttpMethod { get; set; }
    public int? StatusCode { get; set; }
    public string? SessionId { get; set; }
}

public class TrackBatchRequest
{
    public List<TrackEventRequest> Events { get; set; } = new();
}
