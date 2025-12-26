using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Cafe.Api.Models;

public class IngredientPriceHistory
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;
    
    [BsonRepresentation(BsonType.ObjectId)]
    public string IngredientId { get; set; } = string.Empty;
    
    public string IngredientName { get; set; } = string.Empty;
    
    public decimal Price { get; set; }
    
    public string Unit { get; set; } = string.Empty;
    
    // Source of the price: 'manual', 'agmarknet', 'scraped', 'api'
    public string Source { get; set; } = "manual";
    
    // Market/location name if available
    public string? MarketName { get; set; }
    
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
    
    // Percentage change from previous price
    public decimal? ChangePercentage { get; set; }
    
    // Additional metadata
    public string? Notes { get; set; }
}

public class PriceSource
{
    public const string Manual = "manual";
    public const string AgriMarket = "agmarknet";
    public const string WebScraping = "scraped";
    public const string ExternalApi = "api";
    public const string Supplier = "supplier";
}

public class PriceUpdateSettings
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;
    
    // Enable automatic price updates
    public bool AutoUpdateEnabled { get; set; } = false;
    
    // Update frequency in hours (default: 24 hours = daily)
    public int UpdateFrequencyHours { get; set; } = 24;
    
    // Minimum price change percentage to record (avoid noise)
    public decimal MinChangePercentageToRecord { get; set; } = 2.0m;
    
    // Alert threshold for significant price changes
    public decimal AlertThresholdPercentage { get; set; } = 15.0m;
    
    // Which categories to auto-update
    public List<string> EnabledCategories { get; set; } = new();
    
    // Last update timestamp
    public DateTime? LastUpdateRun { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
