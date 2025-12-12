using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Cafe.Api.Models;

public class CafeMenuItem
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    
    // Foreign key references
    [BsonRepresentation(BsonType.ObjectId)]
    public string? CategoryId { get; set; }
    
    [BsonRepresentation(BsonType.ObjectId)]
    public string? SubCategoryId { get; set; }
    
    public int Quantity { get; set; }
    public decimal MakingPrice { get; set; }
    public decimal PackagingCharge { get; set; }
    public decimal ShopSellingPrice { get; set; }
    public decimal OnlinePrice { get; set; }
    
    // Variants for menu items (e.g., different sizes, quantities)
    public List<MenuItemVariant> Variants { get; set; } = new List<MenuItemVariant>();
    
    public string CreatedBy { get; set; } = "System";
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public string LastUpdatedBy { get; set; } = "System";
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

public class MenuItemVariant
{
    public string VariantName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int? Quantity { get; set; } // If variant name contains numeric value
}
