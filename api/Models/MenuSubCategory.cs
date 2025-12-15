using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace Cafe.Api.Models;

public class MenuSubCategory
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    [BsonRepresentation(BsonType.ObjectId)]
    [Required(ErrorMessage = "Category ID is required")]
    public string CategoryId { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "Sub-category name is required")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Sub-category name must be between 2 and 100 characters")]
    public string Name { get; set; } = string.Empty;

    // Temporary property for file upload processing (not saved to DB)
    [BsonIgnore]
    public string? CategoryName { get; set; }
}
