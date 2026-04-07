using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Cafe.Api.Services;
using System.ComponentModel.DataAnnotations;

namespace Cafe.Api.Models;

public class ComboMeal : ISoftDeletable
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("outletId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string OutletId { get; set; } = string.Empty;

    [BsonElement("name")]
    [Required] [StringLength(200, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [BsonElement("description")]
    [StringLength(500)]
    public string Description { get; set; } = string.Empty;

    [BsonElement("imageUrl")]
    public string? ImageUrl { get; set; }

    [BsonElement("items")]
    public List<ComboItem> Items { get; set; } = new();

    [BsonElement("originalPrice")]
    public decimal OriginalPrice { get; set; }

    [BsonElement("comboPrice")]
    [Range(0, 100000)]
    public decimal ComboPrice { get; set; }

    [BsonElement("savingsAmount")]
    public decimal SavingsAmount { get; set; }

    [BsonElement("savingsPercent")]
    public decimal SavingsPercent { get; set; }

    [BsonElement("isActive")]
    public bool IsActive { get; set; } = true;

    [BsonElement("validFrom")]
    public DateTime? ValidFrom { get; set; }

    [BsonElement("validTill")]
    public DateTime? ValidTill { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = MongoService.GetIstNow();

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = MongoService.GetIstNow();

    // Soft-delete support
    [BsonElement("isDeleted")] public bool IsDeleted { get; set; }
    [BsonElement("deletedAt")] public DateTime? DeletedAt { get; set; }
    [BsonElement("deletedBy")] public string? DeletedBy { get; set; }
}

public class ComboItem
{
    [BsonElement("menuItemId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string MenuItemId { get; set; } = string.Empty;

    [BsonElement("menuItemName")]
    public string MenuItemName { get; set; } = string.Empty;

    [BsonElement("quantity")]
    public int Quantity { get; set; } = 1;

    [BsonElement("originalPrice")]
    public decimal OriginalPrice { get; set; }
}

public class CreateComboMealRequest
{
    [Required] [StringLength(200, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string Description { get; set; } = string.Empty;

    public string? ImageUrl { get; set; }

    [Required] [MinLength(2, ErrorMessage = "Combo must have at least 2 items")]
    public List<ComboItemRequest> Items { get; set; } = new();

    [Range(0, 100000)]
    public decimal ComboPrice { get; set; }

    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTill { get; set; }
}

public class ComboItemRequest
{
    [Required]
    public string MenuItemId { get; set; } = string.Empty;

    [Range(1, 10)]
    public int Quantity { get; set; } = 1;
}
