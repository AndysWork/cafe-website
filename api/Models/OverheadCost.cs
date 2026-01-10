using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Cafe.Api.Models;

public class OverheadCost
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonRepresentation(BsonType.ObjectId)]
    public string? OutletId { get; set; } // Null means shared across all outlets

    public string CostType { get; set; } = string.Empty; // Rent, Labour, Electricity, etc.
    public decimal MonthlyCost { get; set; } = 0;
    public int OperationalHoursPerDay { get; set; } = 11; // Default 11 hours
    public int WorkingDaysPerMonth { get; set; } = 30; // Default 30 days
    
    // Calculated fields (can be computed on the fly)
    public decimal CostPerDay => MonthlyCost / WorkingDaysPerMonth;
    public decimal CostPerHour => CostPerDay / OperationalHoursPerDay;
    public decimal CostPerMinute => CostPerHour / 60;

    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }
    
    // Audit fields
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public string? LastUpdatedBy { get; set; }
}

public class OverheadCostCalculation
{
    public string CostType { get; set; } = string.Empty;
    public decimal MonthlyCost { get; set; }
    public decimal CostPerMinute { get; set; }
    public decimal AllocatedCost { get; set; } // Cost for specific preparation time
}

public class OverheadAllocation
{
    public int PreparationTimeMinutes { get; set; }
    public List<OverheadCostCalculation> Costs { get; set; } = new();
    public decimal TotalOverheadCost { get; set; }
}
