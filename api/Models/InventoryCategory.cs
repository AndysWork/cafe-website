using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;
using Cafe.Api.Services;

namespace Cafe.Api.Models;

[BsonIgnoreExtraElements]
public class InventoryCategory : ISoftDeletable
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("outletId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string OutletId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Category name is required")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Category name must be between 1 and 100 characters")]
    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("description")]
    public string? Description { get; set; }

    [BsonElement("shelfLifeDays")]
    public int ShelfLifeDays { get; set; } = 7; // Configurable shelf-life expiry warning threshold in days

    [BsonElement("displayOrder")]
    public int DisplayOrder { get; set; } = 0;

    [BsonElement("isActive")]
    public bool IsActive { get; set; } = true;

    // Audit
    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = MongoService.GetIstNow();

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = MongoService.GetIstNow();

    [BsonElement("createdBy")]
    public string? CreatedBy { get; set; }

    [BsonElement("lastUpdatedBy")]
    public string? LastUpdatedBy { get; set; }

    // Soft delete
    [BsonElement("isDeleted")]
    public bool IsDeleted { get; set; } = false;

    [BsonElement("deletedAt")]
    public DateTime? DeletedAt { get; set; }

    [BsonElement("deletedBy")]
    public string? DeletedBy { get; set; }
}
