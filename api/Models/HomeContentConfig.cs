using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Cafe.Api.Services;

namespace Cafe.Api.Models;

public class HomeContentConfig
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("outletId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? OutletId { get; set; }

    [BsonElement("announcementTitle")]
    public string? AnnouncementTitle { get; set; }

    [BsonElement("announcementMessage")]
    public string? AnnouncementMessage { get; set; }

    [BsonElement("announcementEnabled")]
    public bool AnnouncementEnabled { get; set; } = false;

    [BsonElement("featuredMenuItemIds")]
    [BsonRepresentation(BsonType.ObjectId)]
    public List<string> FeaturedMenuItemIds { get; set; } = new();

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = MongoService.GetIstNow();

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = MongoService.GetIstNow();

    [BsonElement("updatedBy")]
    public string UpdatedBy { get; set; } = "System";
}

public class UpdateHomeContentConfigRequest
{
    public string? AnnouncementTitle { get; set; }
    public string? AnnouncementMessage { get; set; }
    public bool AnnouncementEnabled { get; set; }
    public List<string> FeaturedMenuItemIds { get; set; } = new();
}
