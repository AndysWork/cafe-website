using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Cafe.Api.Services;
using System.ComponentModel.DataAnnotations;

namespace Cafe.Api.Models;

public class WastageRecord
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("outletId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string OutletId { get; set; } = string.Empty;

    [BsonElement("date")]
    public DateTime Date { get; set; }

    [BsonElement("items")]
    public List<WastageItem> Items { get; set; } = new();

    [BsonElement("totalValue")]
    public decimal TotalValue { get; set; }

    [BsonElement("reason")]
    public string Reason { get; set; } = string.Empty; // expired, damaged, overproduction, returned, other

    [BsonElement("notes")]
    public string? Notes { get; set; }

    [BsonElement("recordedBy")]
    public string RecordedBy { get; set; } = string.Empty;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = MongoService.GetIstNow();
}

public class WastageItem
{
    [BsonElement("itemName")]
    public string ItemName { get; set; } = string.Empty;

    [BsonElement("menuItemId")]
    public string? MenuItemId { get; set; }

    [BsonElement("ingredientId")]
    public string? IngredientId { get; set; }

    [BsonElement("quantity")]
    public decimal Quantity { get; set; }

    [BsonElement("unit")]
    public string Unit { get; set; } = string.Empty;

    [BsonElement("costPerUnit")]
    public decimal CostPerUnit { get; set; }

    [BsonElement("totalCost")]
    public decimal TotalCost { get; set; }
}

public class CreateWastageRequest
{
    [Required]
    public DateTime Date { get; set; }

    [Required] [MinLength(1)]
    public List<WastageItemRequest> Items { get; set; } = new();

    [Required]
    public string Reason { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Notes { get; set; }
}

public class WastageItemRequest
{
    [Required] [StringLength(200)]
    public string ItemName { get; set; } = string.Empty;
    public string? MenuItemId { get; set; }
    public string? IngredientId { get; set; }

    [Range(0.01, 100000)]
    public decimal Quantity { get; set; }

    [Required]
    public string Unit { get; set; } = string.Empty;

    [Range(0, 100000)]
    public decimal CostPerUnit { get; set; }
}

public class WastageSummary
{
    public decimal TotalWastageValue { get; set; }
    public int TotalRecords { get; set; }
    public Dictionary<string, decimal> ByReason { get; set; } = new();
    public List<WastageRecord> Records { get; set; } = new();
}
