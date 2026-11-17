using System.ComponentModel.DataAnnotations;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Cafe.Api.Models;

public sealed class SubscriptionMenuSet
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }
    [BsonElement("outletId")]
    public string OutletId { get; set; } = "default";
    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;
    [BsonElement("items")]
    public List<SubscriptionItem> Items { get; set; } = new();
    [BsonElement("createdAtUtc")]
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Stores a reusable menu selection, including quantities and serving days.</summary>
public sealed record CreateSubscriptionMenuSetRequest
{
    [Required, StringLength(100)]
    public string Name { get; init; } = string.Empty;
    [Required, MinLength(1)]
    public List<SubscriptionItem> Items { get; init; } = new();
}

/// <summary>Live paid-subscription metrics for the selected outlet, using IST calendar months.</summary>
public sealed record SubscriptionOverviewResponse(
    int TotalSubscriptions, int NewSubscriptionsThisMonth, int PreviousMonthSubscriptions,
    decimal MonthlyRevenue, decimal PreviousMonthRevenue,
    IReadOnlyDictionary<string, int> PlanSubscribers);
