using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Cafe.Api.Services;

namespace Cafe.Api.Models;

public class ZomatoSalesPayoutInsightRecord
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("outletId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string OutletId { get; set; } = string.Empty;

    [BsonElement("periodStartDate")]
    public DateTime PeriodStartDate { get; set; }

    [BsonElement("periodEndDate")]
    public DateTime PeriodEndDate { get; set; }

    [BsonElement("insightsPayload")]
    public BsonDocument InsightsPayload { get; set; } = new();

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = MongoService.GetIstNow();

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = MongoService.GetIstNow();

    [BsonElement("parsedBy")]
    public string ParsedBy { get; set; } = string.Empty;
}
