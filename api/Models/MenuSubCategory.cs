using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace Cafe.Api.Models;

[BsonIgnoreExtraElements]
public class MenuSubCategory : ISoftDeletable
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    [BsonElement("outletId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string OutletId { get; set; } = string.Empty;

    [BsonElement("categoryId")]
    [BsonRepresentation(BsonType.ObjectId)]
    [Required(ErrorMessage = "Category ID is required")]
    public string CategoryId { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "Sub-category name is required")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Sub-category name must be between 2 and 100 characters")]
    public string Name { get; set; } = string.Empty;

    // Temporary property for file upload processing (not saved to DB)
    [BsonIgnore]
    public string? CategoryName { get; set; }

    // Soft-delete support
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
}
