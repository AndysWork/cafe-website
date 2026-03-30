using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Cafe.Api.Models;

/// <summary>
/// Transactional outbox message for reliable delivery of side effects.
/// Written alongside the primary operation, then processed asynchronously
/// by OutboxProcessorFunction. Ensures eventual consistency for cross-cutting
/// concerns (notifications, emails, loyalty points, etc.).
/// </summary>
public class OutboxMessage
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("eventType")]
    public string EventType { get; set; } = string.Empty;

    [BsonElement("aggregateType")]
    public string AggregateType { get; set; } = string.Empty;  // "Order", "Payment"

    [BsonElement("aggregateId")]
    public string AggregateId { get; set; } = string.Empty;

    [BsonElement("payload")]
    public string Payload { get; set; } = string.Empty;  // JSON payload

    [BsonElement("status")]
    public string Status { get; set; } = "pending";  // pending, processing, completed, failed

    [BsonElement("retryCount")]
    public int RetryCount { get; set; }

    [BsonElement("maxRetries")]
    public int MaxRetries { get; set; } = 5;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }

    [BsonElement("processedAt")]
    public DateTime? ProcessedAt { get; set; }

    [BsonElement("nextRetryAt")]
    public DateTime? NextRetryAt { get; set; }

    [BsonElement("error")]
    public string? Error { get; set; }
}
