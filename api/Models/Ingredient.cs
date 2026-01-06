using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Cafe.Api.Models
{
    public class Ingredient
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        [BsonElement("name")]
        public required string Name { get; set; }

        [BsonElement("category")]
        public required string Category { get; set; } // vegetables, spices, dairy, meat, grains, oils, beverages, others

        [BsonElement("marketPrice")]
        public decimal MarketPrice { get; set; }

        [BsonElement("unit")]
        public required string Unit { get; set; } // kg, gm, ml, pc, ltr

        [BsonElement("lastUpdated")]
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        [BsonElement("isActive")]
        public bool IsActive { get; set; } = true;

        // Price tracking fields
        [BsonElement("priceSource")]
        public string PriceSource { get; set; } = "manual"; // manual, agmarknet, scraped, api

        [BsonElement("lastPriceFetch")]
        public DateTime? LastPriceFetch { get; set; }

        [BsonElement("priceChangePercentage")]
        public decimal? PriceChangePercentage { get; set; }

        [BsonElement("previousPrice")]
        public decimal? PreviousPrice { get; set; }

        [BsonElement("autoUpdateEnabled")]
        public bool AutoUpdateEnabled { get; set; } = false;

        [BsonElement("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [BsonElement("updatedAt")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class IngredientUsage
    {
        [BsonElement("ingredientId")]
        public required string IngredientId { get; set; }

        [BsonElement("ingredientName")]
        public required string IngredientName { get; set; }

        [BsonElement("quantity")]
        public decimal Quantity { get; set; }

        [BsonElement("unit")]
        public required string Unit { get; set; }

        [BsonElement("unitPrice")]
        public decimal UnitPrice { get; set; }

        [BsonElement("totalCost")]
        public decimal TotalCost { get; set; }
    }

    public class OverheadCosts
    {
        [BsonElement("labourCharge")]
        public decimal LabourCharge { get; set; } = 10;

        [BsonElement("rentAllocation")]
        public decimal RentAllocation { get; set; } = 5;

        [BsonElement("electricityCharge")]
        public decimal ElectricityCharge { get; set; } = 3;

        [BsonElement("wastagePercentage")]
        public decimal WastagePercentage { get; set; } = 5;

        [BsonElement("miscellaneous")]
        public decimal Miscellaneous { get; set; } = 2;
    }

    public class OilUsage
    {
        [BsonElement("fryingTimeMinutes")]
        public decimal FryingTimeMinutes { get; set; }

        [BsonElement("oilCapacityLiters")]
        public decimal OilCapacityLiters { get; set; }

        [BsonElement("oilPricePer750ml")]
        public decimal OilPricePer750ml { get; set; }

        [BsonElement("oilUsageDays")]
        public decimal OilUsageDays { get; set; }

        [BsonElement("oilUsageHoursPerDay")]
        public decimal OilUsageHoursPerDay { get; set; }

        [BsonElement("calculatedOilCost")]
        public decimal CalculatedOilCost { get; set; }
    }

    public class PriceForecastData
    {
        [BsonElement("packagingCost")]
        public decimal PackagingCost { get; set; }

        [BsonElement("onlineDeduction")]
        public decimal OnlineDeduction { get; set; }

        [BsonElement("onlineDiscount")]
        public decimal OnlineDiscount { get; set; }

        [BsonElement("shopPrice")]
        public decimal ShopPrice { get; set; }

        [BsonElement("shopDeliveryPrice")]
        public decimal ShopDeliveryPrice { get; set; }

        [BsonElement("onlinePrice")]
        public decimal OnlinePrice { get; set; }

        [BsonElement("onlinePayout")]
        public decimal OnlinePayout { get; set; }

        [BsonElement("onlineProfit")]
        public decimal OnlineProfit { get; set; }

        [BsonElement("offlineProfit")]
        public decimal OfflineProfit { get; set; }

        [BsonElement("takeawayProfit")]
        public decimal TakeawayProfit { get; set; }
    }

    public class KptAnalysis
    {
        [BsonElement("avgPreparationTime")]
        public decimal AvgPreparationTime { get; set; }

        [BsonElement("minPreparationTime")]
        public decimal MinPreparationTime { get; set; }

        [BsonElement("maxPreparationTime")]
        public decimal MaxPreparationTime { get; set; }

        [BsonElement("medianPreparationTime")]
        public decimal MedianPreparationTime { get; set; }

        [BsonElement("stdDeviation")]
        public decimal StdDeviation { get; set; }

        [BsonElement("orderCount")]
        public decimal OrderCount { get; set; }
    }

    public class MenuItemRecipe
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        [BsonElement("menuItemId")]
        public string? MenuItemId { get; set; }

        [BsonElement("menuItemName")]
        public required string MenuItemName { get; set; }

        [BsonElement("ingredients")]
        public List<IngredientUsage> Ingredients { get; set; } = new List<IngredientUsage>();

        [BsonElement("overheadCosts")]
        public OverheadCosts OverheadCosts { get; set; } = new OverheadCosts();

        [BsonElement("totalIngredientCost")]
        public decimal TotalIngredientCost { get; set; }

        [BsonElement("totalOverheadCost")]
        public decimal TotalOverheadCost { get; set; }

        [BsonElement("totalMakingCost")]
        public decimal TotalMakingCost { get; set; }

        [BsonElement("profitMargin")]
        public decimal ProfitMargin { get; set; } = 30;

        [BsonElement("suggestedSellingPrice")]
        public decimal SuggestedSellingPrice { get; set; }

        [BsonElement("actualSellingPrice")]
        public decimal? ActualSellingPrice { get; set; }

        [BsonElement("notes")]
        public string? Notes { get; set; }

        [BsonElement("oilUsage")]
        public OilUsage? OilUsage { get; set; }

        [BsonElement("priceForecast")]
        public PriceForecastData? PriceForecast { get; set; }

        [BsonElement("kptAnalysis")]
        public KptAnalysis? KptAnalysis { get; set; }

        [BsonElement("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [BsonElement("updatedAt")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
