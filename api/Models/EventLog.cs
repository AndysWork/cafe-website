using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Cafe.Api.Models;

/// <summary>
/// Immutable event log entry for critical state transitions.
/// Tracks changes to Orders, Payments, Inventory, and Loyalty operations
/// providing a full audit trail for debugging and compliance.
/// Complements AuditLogger (security events) with domain event sourcing.
/// </summary>
public class EventLog
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("entityType")]
    public string EntityType { get; set; } = string.Empty;  // "Order", "Payment", "Inventory", "Loyalty"

    [BsonElement("entityId")]
    public string EntityId { get; set; } = string.Empty;

    [BsonElement("eventType")]
    public string EventType { get; set; } = string.Empty;  // "Created", "StatusChanged", "PointsAwarded", etc.

    [BsonElement("actorId")]
    public string? ActorId { get; set; }

    [BsonElement("actorRole")]
    public string? ActorRole { get; set; }

    [BsonElement("oldState")]
    public string? OldState { get; set; }  // JSON snapshot of previous state

    [BsonElement("newState")]
    public string? NewState { get; set; }  // JSON snapshot of new state

    [BsonElement("metadata")]
    public Dictionary<string, string>? Metadata { get; set; }

    [BsonElement("outletId")]
    public string? OutletId { get; set; }

    [BsonElement("timestamp")]
    public DateTime Timestamp { get; set; }
}
