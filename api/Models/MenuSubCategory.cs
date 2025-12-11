using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Cafe.Api.Models;

public class MenuSubCategory
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    [BsonRepresentation(BsonType.ObjectId)]
    public string CategoryId { get; set; } = string.Empty;
    
    public string Name { get; set; } = string.Empty;

    // Temporary property for file upload processing (not saved to DB)
    [BsonIgnore]
    public string? CategoryName { get; set; }
}
