using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace Cafe.Api.Models;

[BsonIgnoreExtraElements]
public class BonusConfiguration : ISoftDeletable
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("outletId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? OutletId { get; set; }

    [BsonElement("configurationName")]
    [Required]
    public string ConfigurationName { get; set; } = string.Empty;

    [BsonElement("description")]
    public string Description { get; set; } = string.Empty;

    [BsonElement("applicableToAllStaff")]
    public bool ApplicableToAllStaff { get; set; } = true;

    [BsonElement("applicablePositions")]
    public List<string> ApplicablePositions { get; set; } = new(); // Positions this applies to

    [BsonElement("calculationPeriod")]
    public string CalculationPeriod { get; set; } = "Monthly"; // Monthly, Weekly, Daily

    // Bonus/Deduction Rules
    [BsonElement("rules")]
    public List<BonusRule> Rules { get; set; } = new();

    [BsonElement("isActive")]
    public bool IsActive { get; set; } = true;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updatedAt")]
    public DateTime? UpdatedAt { get; set; }

    [BsonElement("createdBy")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? CreatedBy { get; set; }

    // Soft-delete support
    [BsonElement("isDeleted")] public bool IsDeleted { get; set; }
    [BsonElement("deletedAt")] public DateTime? DeletedAt { get; set; }
    [BsonElement("deletedBy")] public string? DeletedBy { get; set; }
}

// Individual bonus or deduction rule
public class BonusRule
{
    [BsonElement("ruleType")]
    [Required]
    public string RuleType { get; set; } = string.Empty; 
    // Types: "OvertimeHours", "UndertimeHours", "SnacksPreparation", "BadOrders", "GoodRatings", "RefundDeduction"

    [BsonElement("ruleName")]
    public string RuleName { get; set; } = string.Empty;

    [BsonElement("description")]
    public string Description { get; set; } = string.Empty;

    [BsonElement("isBonus")]
    public bool IsBonus { get; set; } = true; // true = bonus, false = deduction

    [BsonElement("calculationType")]
    public string CalculationType { get; set; } = "PerUnit"; 
    // Types: "PerUnit", "PerHour", "Percentage", "FixedAmount", "PerOrder", "PerRating"

    [BsonElement("rateAmount")]
    public decimal RateAmount { get; set; } = 0; // Amount per unit/hour/order

    [BsonElement("percentageValue")]
    public decimal? PercentageValue { get; set; } // For percentage-based calculations

    [BsonElement("threshold")]
    public decimal? Threshold { get; set; } // Minimum/Maximum threshold to trigger rule

    [BsonElement("maxAmount")]
    public decimal? MaxAmount { get; set; } // Maximum bonus/deduction cap

    [BsonElement("useDynamicRate")]
    public bool UseDynamicRate { get; set; } = false; // If true, calculates rate based on staff hourly rate x multiplier

    [BsonElement("rateMultiplier")]
    public decimal RateMultiplier { get; set; } = 1.5m; // Default multiplier for overtime (1.5x hourly rate)

    [BsonElement("staffRateOverrides")]
    public List<StaffRateOverride> StaffRateOverrides { get; set; } = new(); // Staff-specific rate customization

    [BsonElement("isActive")]
    public bool IsActive { get; set; } = true;
}

// Staff-specific rate override for bonus rules
public class StaffRateOverride
{
    [BsonElement("staffId")]
    [BsonRepresentation(BsonType.ObjectId)]
    [Required]
    public string StaffId { get; set; } = string.Empty;

    [BsonElement("customRate")]
    public decimal CustomRate { get; set; } = 0; // Custom rate for this specific staff member

    [BsonElement("notes")]
    public string? Notes { get; set; }
}

public class CreateBonusConfigurationRequest
{
    [Required]
    public string ConfigurationName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool ApplicableToAllStaff { get; set; } = true;

    public List<string> ApplicablePositions { get; set; } = new();

    public string CalculationPeriod { get; set; } = "Monthly";

    [Required]
    public List<BonusRuleRequest> Rules { get; set; } = new();
}

public class BonusRuleRequest
{
    [Required]
    public string RuleType { get; set; } = string.Empty;

    public string RuleName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool IsBonus { get; set; } = true;

    public string CalculationType { get; set; } = "PerUnit";

    public decimal RateAmount { get; set; } = 0;

    public decimal? PercentageValue { get; set; }

    public decimal? Threshold { get; set; }

    public decimal? MaxAmount { get; set; }

    public bool UseDynamicRate { get; set; } = false;

    public decimal RateMultiplier { get; set; } = 1.5m;

    public List<StaffRateOverrideRequest>? StaffRateOverrides { get; set; }

    public bool IsActive { get; set; } = true;
}

public class StaffRateOverrideRequest
{
    [Required]
    public string StaffId { get; set; } = string.Empty;

    public decimal CustomRate { get; set; } = 0;

    public string? Notes { get; set; }
}

public class UpdateBonusConfigurationRequest
{
    public string? ConfigurationName { get; set; }
    public string? Description { get; set; }
    public bool? ApplicableToAllStaff { get; set; }
    public List<string>? ApplicablePositions { get; set; }
    public string? CalculationPeriod { get; set; }
    public List<BonusRuleRequest>? Rules { get; set; }
    public bool? IsActive { get; set; }
}

// Staff performance tracking model
[BsonIgnoreExtraElements]
public class StaffPerformanceRecord
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("staffId")]
    [BsonRepresentation(BsonType.ObjectId)]
    [Required]
    public string StaffId { get; set; } = string.Empty;

    [BsonElement("outletId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? OutletId { get; set; }

    [BsonElement("recordDate")]
    public DateTime RecordDate { get; set; } = DateTime.UtcNow;

    [BsonElement("period")]
    public string Period { get; set; } = string.Empty; // e.g., "2026-01", "2026-W04"

    // Performance metrics
    [BsonElement("scheduledHours")]
    public decimal ScheduledHours { get; set; } = 0;

    [BsonElement("actualHours")]
    public decimal ActualHours { get; set; } = 0;

    [BsonElement("overtimeHours")]
    public decimal OvertimeHours { get; set; } = 0;

    [BsonElement("undertimeHours")]
    public decimal UndertimeHours { get; set; } = 0;

    [BsonElement("snacksPrepared")]
    public int SnacksPrepared { get; set; } = 0;

    [BsonElement("expectedSnacks")]
    public int ExpectedSnacks { get; set; } = 0;

    [BsonElement("totalOrders")]
    public int TotalOrders { get; set; } = 0;

    [BsonElement("badOrders")]
    public int BadOrders { get; set; } = 0;

    [BsonElement("goodRatings")]
    public int GoodRatings { get; set; } = 0; // 4-5 star ratings

    [BsonElement("totalRatings")]
    public int TotalRatings { get; set; } = 0;

    [BsonElement("refundAmount")]
    public decimal RefundAmount { get; set; } = 0;

    [BsonElement("missingItemRefunds")]
    public decimal MissingItemRefunds { get; set; } = 0;

    // Calculated bonuses/deductions
    [BsonElement("totalBonus")]
    public decimal TotalBonus { get; set; } = 0;

    [BsonElement("totalDeductions")]
    public decimal TotalDeductions { get; set; } = 0;

    [BsonElement("netAmount")]
    public decimal NetAmount { get; set; } = 0;

    [BsonElement("bonusBreakdown")]
    public List<BonusCalculationDetail> BonusBreakdown { get; set; } = new();

    [BsonElement("isCalculated")]
    public bool IsCalculated { get; set; } = false;

    [BsonElement("calculatedAt")]
    public DateTime? CalculatedAt { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updatedAt")]
    public DateTime? UpdatedAt { get; set; }
}

public class BonusCalculationDetail
{
    [BsonElement("ruleName")]
    public string RuleName { get; set; } = string.Empty;

    [BsonElement("ruleType")]
    public string RuleType { get; set; } = string.Empty;

    [BsonElement("isBonus")]
    public bool IsBonus { get; set; } = true;

    [BsonElement("calculationDetails")]
    public string CalculationDetails { get; set; } = string.Empty;

    [BsonElement("amount")]
    public decimal Amount { get; set; } = 0;
}

