using System.ComponentModel.DataAnnotations;
using MongoDB.Bson.Serialization.Attributes;

namespace Cafe.Api.Models;

public sealed class JourneySession
{
    [BsonId] public string Id { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public string VisitorId { get; set; } = string.Empty;
    public string OutletId { get; set; } = string.Empty;
    public string? AccountId { get; set; }
    public string? AccountLabel { get; set; }
    public bool IsLoggedIn { get; set; }
    public bool IsStaff { get; set; }
    public string UserRole { get; set; } = "anonymous";
    public bool IsReturning { get; set; }
    public bool Revoked { get; set; }
    public string Device { get; set; } = "desktop";
    public string ConsentVersion { get; set; } = "journey-v1";
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)] public DateTime StartedAt { get; set; }
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)] public DateTime LastSeenAt { get; set; }
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)] public DateTime ExpiresAt { get; set; }
}

public sealed class JourneyEvent
{
    [BsonId] public string Id { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string OutletId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Page { get; set; } = "/";
    public string? MenuItemId { get; set; }
    public string? ItemName { get; set; }
    public string? Category { get; set; }
    public int Quantity { get; set; }
    public int CartCount { get; set; }
    public decimal CartValue { get; set; }
    public decimal? DeliveryFee { get; set; }
    public int ActiveSeconds { get; set; }
    public string? OrderId { get; set; }
    public List<JourneyOrderedItem> Items { get; set; } = new();
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)] public DateTime OccurredAt { get; set; }
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)] public DateTime ExpiresAt { get; set; }
    [BsonIgnore] public bool Cancelled { get; set; }
}

public sealed record JourneyOrderedItem(string MenuItemId, string Name, int Quantity, decimal Revenue);

/// <summary>Explicit, versioned opt-in; identity is resolved only from the caller's validated JWT.</summary>
public sealed record CreateJourneySessionRequest
{
    public bool AnalyticsConsent { get; init; }
    public bool LinkAccount { get; init; }
    public DateTimeOffset ConsentExpiresAt { get; init; }
    [Required, StringLength(40)] public string ConsentVersion { get; init; } = string.Empty;
    [Required, StringLength(36)] public string VisitorId { get; init; } = string.Empty;
    [Required, StringLength(24)] public string OutletId { get; init; } = string.Empty;
    [Required, RegularExpression("^(mobile|tablet|desktop)$")] public string Device { get; init; } = "desktop";
}

/// <summary>Capability for one consented session. Never returned by reporting endpoints or stored on orders.</summary>
public sealed record JourneyContext
{
    [Required, StringLength(36)] public string SessionId { get; init; } = string.Empty;
    [Required, StringLength(64)] public string Token { get; init; } = string.Empty;
}

/// <summary>Issued session capability and fixed retention deadline.</summary>
public sealed record JourneySessionResponse(JourneyContext Context, DateTimeOffset ExpiresAt);

/// <summary>Allowlisted browser event, without arbitrary metadata, URLs, searches, or personal fields.</summary>
public sealed record JourneyEventRequest
{
    public string Id { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public DateTimeOffset OccurredAt { get; init; }
    public string Page { get; init; } = "/";
    public string? MenuItemId { get; init; }
    public string? CategoryId { get; init; }
    public int Quantity { get; init; }
    public int CartCount { get; init; }
    public decimal CartValue { get; init; }
    public decimal? DeliveryFee { get; init; }
    public int ActiveSeconds { get; init; }
}

/// <summary>Idempotent event batch, authenticated by a session capability rather than a user-supplied identity.</summary>
public sealed record TrackJourneyBatchRequest
{
    [Required] public JourneyContext Context { get; init; } = new();
    [Required, MinLength(1), MaxLength(50)] public List<JourneyEventRequest> Events { get; init; } = new();
}

/// <summary>Revokes tracking and erases the current session's detailed history.</summary>
public sealed record RevokeJourneyRequest
{
    [Required] public JourneyContext Context { get; init; } = new();
}

/// <summary>Backend order confirmation retry; the order is reloaded and ownership checked server-side.</summary>
public sealed record ConfirmJourneyOrderRequest
{
    [Required] public JourneyContext Context { get; init; } = new();
    [Required, StringLength(24)] public string OrderId { get; init; } = string.Empty;
}

/// <summary>Consented customer traffic. Staff are reported separately and excluded from these metrics.</summary>
public sealed record JourneyOverview(
    int Visitors, int Sessions, int LoggedInVisitors, int ReturningVisitors, int PageViews,
    int ActiveSeconds, double AverageActiveSeconds, int Orders, int CancelledOrders,
    decimal Revenue, double ConversionRate, double CartAbandonmentRate, int LiveVisitors, int StaffSessions);

/// <summary>Daily IST traffic and confirmed orders.</summary>
public sealed record JourneyDailyStat(string Date, int Visitors, int Sessions, int Orders);

/// <summary>Ordered, session-based conversion funnel.</summary>
public sealed record JourneyFunnelStep(string Name, int Sessions, double DropOffRate);

/// <summary>Aggregates for a page, visitor segment, navigation path, or abandonment dimension.</summary>
public sealed record JourneyBreakdown(
    string Name, int Sessions, int Views, int Orders, int AbandonedCarts,
    int ActiveSeconds, decimal Revenue, double ConversionRate);

/// <summary>Attention and net ordered quantities for one menu item.</summary>
public sealed record JourneyMenuStat(
    string ItemId, string Name, string Category, int Views, int Clicks, int Viewers,
    int CartAdds, int CartSessions, int QuantityOrdered, int Orders, decimal Revenue,
    double AddToCartRate, double OrderRate, bool HighInterestLowConversion);

/// <summary>Privacy-filtered summary; no capability hashes or raw visitor IDs are exposed.</summary>
public sealed record JourneySessionSummary(
    string Id, string VisitorLabel, string? AccountId, string? AccountLabel,
    bool IsLoggedIn, bool IsStaff, bool IsReturning, string Device,
    DateTimeOffset StartedAt, DateTimeOffset LastSeenAt, string LastPage,
    int ActiveSeconds, decimal CartValue, decimal? DeliveryFee, string Outcome, int Orders, int Pages);

/// <summary>One event in a retained session timeline.</summary>
public sealed record JourneyTimelineEvent(
    string Id, string Type, DateTimeOffset OccurredAt, string Page, string? ItemName,
    string? Category, int Quantity, int CartCount, decimal CartValue, decimal? DeliveryFee,
    int ActiveSeconds, string? OrderId, bool Cancelled);

/// <summary>Paginated explorer results.</summary>
public sealed record JourneySessionListResponse(IReadOnlyList<JourneySessionSummary> Sessions, int Total, int Page, int PageSize);

/// <summary>Paginated chronological timeline; engagement is actively measured, not tab-open duration.</summary>
public sealed record JourneyTimelineResponse(
    JourneySessionSummary Session, IReadOnlyList<JourneyTimelineEvent> Events, int Total, int Page, int PageSize,
    IReadOnlyList<JourneyPageEngagement> Pages);

/// <summary>Per-page engagement for the whole selected journey, independently of event pagination.</summary>
public sealed record JourneyPageEngagement(string Page, int Views, int ActiveSeconds);

/// <summary>Outlet-scoped, retained, consented analytics and transparent coverage.</summary>
public sealed record JourneyDashboardResponse(
    DateTimeOffset From, DateTimeOffset ToExclusive, DateTimeOffset RetainedSince,
    DateTimeOffset GeneratedAt, JourneyOverview Overview, JourneyOverview? Previous,
    IReadOnlyList<JourneyDailyStat> Daily, IReadOnlyList<JourneyFunnelStep> Funnel,
    IReadOnlyList<JourneyBreakdown> Pages, IReadOnlyList<JourneyBreakdown> Categories,
    IReadOnlyList<JourneyBreakdown> VisitorTypes, IReadOnlyList<JourneyBreakdown> Devices,
    IReadOnlyList<JourneyBreakdown> CartValues, IReadOnlyList<JourneyBreakdown> DeliveryCharges,
    IReadOnlyList<JourneyBreakdown> Paths, IReadOnlyList<JourneyBreakdown> UserSegments,
    IReadOnlyList<JourneyMenuStat> Menu, IReadOnlyList<JourneySessionSummary> RecentSessions,
    int RegisteredAccountsAllOutlets);

/// <summary>All active sessions and recent events for the selected outlet, independently of report dates.</summary>
public sealed record JourneyLiveResponse(
    DateTimeOffset GeneratedAt, IReadOnlyList<JourneySessionSummary> Sessions,
    IReadOnlyList<JourneyTimelineEvent> RecentEvents);

public sealed record JourneyReportRange(DateTime From, DateTime ToExclusive);
