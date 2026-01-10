using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Cafe.Api.Services;
using System.ComponentModel.DataAnnotations;

namespace Cafe.Api.Models;

public class CafeMenuItem
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "Menu item name is required")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Name must be between 2 and 100 characters")]
    public string Name { get; set; } = string.Empty;
    
    [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters")]
    public string Description { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "Category is required")]
    [StringLength(100, ErrorMessage = "Category cannot exceed 100 characters")]
    public string Category { get; set; } = string.Empty;
    
    // Foreign key references
    [BsonRepresentation(BsonType.ObjectId)]
    public string? CategoryId { get; set; }
    
    [BsonRepresentation(BsonType.ObjectId)]
    public string? SubCategoryId { get; set; }
    
    [BsonElement("outletId")]
    [BsonRepresentation(BsonType.ObjectId)]
    [Required(ErrorMessage = "Outlet ID is required")]
    public string OutletId { get; set; } = string.Empty; // Multi-outlet support
    
    [Range(0, int.MaxValue, ErrorMessage = "Quantity must be a positive number")]
    public int Quantity { get; set; }
    
    [Range(0.01, 100000, ErrorMessage = "Making price must be between 0.01 and 100,000")]
    public decimal MakingPrice { get; set; }
    
    [Range(0, 10000, ErrorMessage = "Packaging charge must be between 0 and 10,000")]
    public decimal PackagingCharge { get; set; }
    
    [Range(0.01, 100000, ErrorMessage = "Shop selling price must be between 0.01 and 100,000")]
    public decimal ShopSellingPrice { get; set; }
    
    [Range(0.01, 100000, ErrorMessage = "Online price must be between 0.01 and 100,000")]
    public decimal OnlinePrice { get; set; }
    
    [Range(0.01, 100000, ErrorMessage = "Dine-in price must be between 0.01 and 100,000")]
    public decimal DineInPrice { get; set; }
    
    // Future pricing for planning
    [BsonElement("futureShopPrice")]
    public decimal? FutureShopPrice { get; set; }
    
    [BsonElement("futureOnlinePrice")]
    public decimal? FutureOnlinePrice { get; set; }
    
    [StringLength(500, ErrorMessage = "Image URL cannot exceed 500 characters")]
    public string ImageUrl { get; set; } = string.Empty;
    
    public bool IsAvailable { get; set; } = true;
    
    // Variants for menu items (e.g., different sizes, quantities)
    public List<MenuItemVariant> Variants { get; set; } = new List<MenuItemVariant>();
    
    public string CreatedBy { get; set; } = "System";
    public DateTime CreatedDate { get; set; } = MongoService.GetIstNow();
    public string LastUpdatedBy { get; set; } = "System";
    public DateTime LastUpdated { get; set; } = MongoService.GetIstNow();
}

public class MenuItemVariant
{
    [Required(ErrorMessage = "Variant name is required")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Variant name must be between 2 and 100 characters")]
    public string VariantName { get; set; } = string.Empty;
    
    [Range(0.01, 100000, ErrorMessage = "Price must be between 0.01 and 100,000")]
    public decimal Price { get; set; }
    
    [Range(0, int.MaxValue, ErrorMessage = "Quantity must be a positive number")]
    public int? Quantity { get; set; } // If variant name contains numeric value
}
