using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Cafe.Api.Services;
using System.ComponentModel.DataAnnotations;

namespace Cafe.Api.Models;

public class PriceForecast
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    [Required(ErrorMessage = "Menu item ID is required")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string MenuItemId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Menu item name is required")]
    public string MenuItemName { get; set; } = string.Empty;

    [Range(0, 100000, ErrorMessage = "Make price must be between 0 and 100,000")]
    public decimal MakePrice { get; set; }

    [Range(0, 10000, ErrorMessage = "Packaging cost must be between 0 and 10,000")]
    public decimal PackagingCost { get; set; }

    [Range(0, 100000, ErrorMessage = "Shop price must be between 0 and 100,000")]
    public decimal ShopPrice { get; set; }

    [Range(0, 100000, ErrorMessage = "Shop delivery price must be between 0 and 100,000")]
    public decimal ShopDeliveryPrice { get; set; }

    [Range(0, 100000, ErrorMessage = "Online price must be between 0 and 100,000")]
    public decimal OnlinePrice { get; set; }

    [Range(0, 100000, ErrorMessage = "Updated shop price must be between 0 and 100,000")]
    public decimal UpdatedShopPrice { get; set; }

    [Range(0, 100000, ErrorMessage = "Updated online price must be between 0 and 100,000")]
    public decimal UpdatedOnlinePrice { get; set; }

    [Range(0, 10000, ErrorMessage = "Online deduction must be between 0 and 10,000")]
    public decimal OnlineDeduction { get; set; }

    [Range(0, 100, ErrorMessage = "Online discount must be between 0 and 100")]
    public decimal OnlineDiscount { get; set; }

    public decimal PayoutCalculation { get; set; }

    public decimal OnlinePayout { get; set; }

    public decimal OnlineProfit { get; set; }

    public decimal OfflineProfit { get; set; }

    public decimal TakeawayProfit { get; set; }

    public bool IsFinalized { get; set; } = false;

    public DateTime? FinalizedDate { get; set; }

    public string FinalizedBy { get; set; } = string.Empty;

    public List<PriceHistory> History { get; set; } = new List<PriceHistory>();

    public string CreatedBy { get; set; } = "System";
    public DateTime CreatedDate { get; set; } = MongoService.GetIstNow();
    public string LastUpdatedBy { get; set; } = "System";
    public DateTime LastUpdated { get; set; } = MongoService.GetIstNow();
}

public class PriceHistory
{
    public DateTime ChangeDate { get; set; } = MongoService.GetIstNow();
    
    public string ChangedBy { get; set; } = string.Empty;

    public decimal MakePrice { get; set; }

    public decimal PackagingCost { get; set; }

    public decimal ShopPrice { get; set; }

    public decimal ShopDeliveryPrice { get; set; }

    public decimal OnlinePrice { get; set; }

    public decimal UpdatedShopPrice { get; set; }

    public decimal UpdatedOnlinePrice { get; set; }

    public decimal OnlineDeduction { get; set; }

    public decimal OnlineDiscount { get; set; }

    public decimal PayoutCalculation { get; set; }

    public decimal OnlinePayout { get; set; }

    public decimal OnlineProfit { get; set; }

    public decimal OfflineProfit { get; set; }

    public decimal TakeawayProfit { get; set; }

    public string ChangeReason { get; set; } = string.Empty;
}
