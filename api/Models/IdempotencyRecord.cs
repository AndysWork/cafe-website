using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Cafe.Api.Models;

public class IdempotencyRecord
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("key")]
    public string Key { get; set; } = string.Empty;

    [BsonElement("action")]
    public string Action { get; set; } = string.Empty;

    [BsonElement("userId")]
    public string UserId { get; set; } = string.Empty;

    [BsonElement("requestHash")]
    public string RequestHash { get; set; } = string.Empty;

    [BsonElement("status")]
    public string Status { get; set; } = "in-progress"; // in-progress, completed, failed

    [BsonElement("responseStatusCode")]
    public int? ResponseStatusCode { get; set; }

    [BsonElement("responseBody")]
    public string? ResponseBody { get; set; }

    [BsonElement("error")]
    public string? Error { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    [BsonElement("expiresAt")]
    public DateTime ExpiresAt { get; set; }
}
