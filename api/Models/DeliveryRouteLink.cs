using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Cafe.Api.Services;

namespace Cafe.Api.Models;

public class DeliveryRouteLink
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("code")]
    public string Code { get; set; } = string.Empty;

    [BsonElement("fullMapUrl")]
    public string FullMapUrl { get; set; } = string.Empty;

    [BsonElement("shortUrl")]
    public string ShortUrl { get; set; } = string.Empty;

    [BsonElement("orderId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? OrderId { get; set; }

    [BsonElement("outletId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string OutletId { get; set; } = string.Empty;

    [BsonElement("destinationAddress")]
    public string DestinationAddress { get; set; } = string.Empty;

    [BsonElement("distanceKm")]
    public double? DistanceKm { get; set; }

    [BsonElement("etaMinutes")]
    public int? EtaMinutes { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = MongoService.GetIstNow();

    [BsonElement("expiresAt")]
    public DateTime ExpiresAt { get; set; } = MongoService.GetIstNow().AddDays(14);
}

public class DeliveryRouteQuoteResponse
{
    public string MapUrl { get; set; } = string.Empty;
    public string? ShortCode { get; set; }
    public string? ShortUrl { get; set; }
    public string OriginAddress { get; set; } = string.Empty;
    public string DestinationAddress { get; set; } = string.Empty;
    public double? DistanceKm { get; set; }
    public int? EtaMinutes { get; set; }
    public string Provider { get; set; } = "google";
}