using MongoDB.Driver;
using Cafe.Api.Models;

namespace Cafe.Api.Services;

public partial class MongoService
{
    #region BonusConfiguration Operations

    // Get all bonus configurations
    public async Task<List<BonusConfiguration>> GetAllBonusConfigurationsAsync(string? outletId = null)
    {
        if (outletId == null)
            return new List<BonusConfiguration>();
        
        var filter = Builders<BonusConfiguration>.Filter.Eq(b => b.OutletId, outletId);
        return await _bonusConfigurations.Find(filter)
            .SortByDescending(b => b.CreatedAt)
            .ToListAsync();
    }

    // Get active bonus configurations
    public async Task<List<BonusConfiguration>> GetActiveBonusConfigurationsAsync(string? outletId = null)
    {
        if (outletId == null)
            return new List<BonusConfiguration>();
        
        var filter = Builders<BonusConfiguration>.Filter.And(
            Builders<BonusConfiguration>.Filter.Eq(b => b.OutletId, outletId),
            Builders<BonusConfiguration>.Filter.Eq(b => b.IsActive, true)
        );
        
        return await _bonusConfigurations.Find(filter)
            .SortByDescending(b => b.CreatedAt)
            .ToListAsync();
    }

    // Get bonus configuration by ID
    public async Task<BonusConfiguration?> GetBonusConfigurationByIdAsync(string id)
    {
        return await _bonusConfigurations.Find(b => b.Id == id).FirstOrDefaultAsync();
    }

    // Get bonus configurations applicable to a staff member
    public async Task<List<BonusConfiguration>> GetBonusConfigurationsForStaffAsync(Staff staff)
    {
        var outletId = staff.OutletIds.FirstOrDefault();
        if (outletId == null)
            return new List<BonusConfiguration>();
        
        var filter = Builders<BonusConfiguration>.Filter.And(
            Builders<BonusConfiguration>.Filter.Eq(b => b.OutletId, outletId),
            Builders<BonusConfiguration>.Filter.Eq(b => b.IsActive, true),
            Builders<BonusConfiguration>.Filter.Or(
                Builders<BonusConfiguration>.Filter.Eq(b => b.ApplicableToAllStaff, true),
                Builders<BonusConfiguration>.Filter.AnyEq(b => b.ApplicablePositions, staff.Position)
            )
        );
        
        return await _bonusConfigurations.Find(filter).ToListAsync();
    }

    // Create bonus configuration
    public async Task<BonusConfiguration> CreateBonusConfigurationAsync(BonusConfiguration config)
    {
        config.CreatedAt = GetIstNow();
        config.UpdatedAt = GetIstNow();
        await _bonusConfigurations.InsertOneAsync(config);
        return config;
    }

    // Update bonus configuration
    public async Task<bool> UpdateBonusConfigurationAsync(string id, BonusConfiguration config)
    {
        config.UpdatedAt = GetIstNow();
        var result = await _bonusConfigurations.ReplaceOneAsync(b => b.Id == id, config);
        return result.ModifiedCount > 0;
    }

    // Delete bonus configuration
    public async Task<bool> DeleteBonusConfigurationAsync(string id)
    {
        var result = await _bonusConfigurations.DeleteOneAsync(b => b.Id == id);
        return result.DeletedCount > 0;
    }

    // Toggle bonus configuration active status
    public async Task<bool> ToggleBonusConfigurationStatusAsync(string id, bool isActive)
    {
        var update = Builders<BonusConfiguration>.Update
            .Set(b => b.IsActive, isActive)
            .Set(b => b.UpdatedAt, GetIstNow());
        
        var result = await _bonusConfigurations.UpdateOneAsync(b => b.Id == id, update);
        return result.ModifiedCount > 0;
    }

    // Get bonus description for staff (for email)
    public async Task<string> GetBonusDescriptionForStaffAsync(Staff staff)
    {
        var configs = await GetBonusConfigurationsForStaffAsync(staff);
        
        if (!configs.Any())
        {
            return staff.SalaryType == "Monthly" 
                ? "Monthly performance-based bonus eligibility"
                : "Incentives based on hours worked and performance";
        }

        var descriptions = new List<string>();
        foreach (var config in configs)
        {
            if (!string.IsNullOrEmpty(config.Description))
            {
                descriptions.Add(config.Description);
            }
            else
            {
                // Generate description from rules
                var ruleDescriptions = config.Rules.Where(r => r.IsActive).Select(r => 
                    r.IsBonus 
                        ? $"+ {r.RuleName}: {r.Description}" 
                        : $"- {r.RuleName}: {r.Description}"
                );
                if (ruleDescriptions.Any())
                    descriptions.Add(string.Join("; ", ruleDescriptions));
            }
        }

        return descriptions.Any() 
            ? string.Join(" | ", descriptions) 
            : "Performance-based bonus/deduction system in effect";
    }

    #endregion

    #region Staff Performance Operations

    // Get staff performance record by ID
    public async Task<StaffPerformanceRecord?> GetStaffPerformanceRecordByIdAsync(string id)
    {
        return await _staffPerformanceRecords.Find(r => r.Id == id).FirstOrDefaultAsync();
    }

    // Get staff performance records for a period
    public async Task<List<StaffPerformanceRecord>> GetStaffPerformanceRecordsAsync(string staffId, string period)
    {
        var filter = Builders<StaffPerformanceRecord>.Filter.And(
            Builders<StaffPerformanceRecord>.Filter.Eq(r => r.StaffId, staffId),
            Builders<StaffPerformanceRecord>.Filter.Eq(r => r.Period, period)
        );
        
        return await _staffPerformanceRecords.Find(filter)
            .SortByDescending(r => r.RecordDate)
            .ToListAsync();
    }

    // Get all performance records for an outlet in a period
    public async Task<List<StaffPerformanceRecord>> GetOutletPerformanceRecordsAsync(string? outletId, string period)
    {
        if (outletId == null)
            return new List<StaffPerformanceRecord>();
        
        var filter = Builders<StaffPerformanceRecord>.Filter.And(
            Builders<StaffPerformanceRecord>.Filter.Eq(r => r.OutletId, outletId),
            Builders<StaffPerformanceRecord>.Filter.Eq(r => r.Period, period)
        );
        
        return await _staffPerformanceRecords.Find(filter)
            .SortBy(r => r.StaffId)
            .ToListAsync();
    }

    // Create or update staff performance record
    public async Task<StaffPerformanceRecord> UpsertStaffPerformanceRecordAsync(StaffPerformanceRecord record)
    {
        var existing = await _staffPerformanceRecords.Find(r => 
            r.StaffId == record.StaffId && r.Period == record.Period).FirstOrDefaultAsync();

        if (existing != null)
        {
            record.Id = existing.Id;
            record.CreatedAt = existing.CreatedAt;
            record.UpdatedAt = GetIstNow();
            await _staffPerformanceRecords.ReplaceOneAsync(r => r.Id == existing.Id, record);
        }
        else
        {
            record.CreatedAt = GetIstNow();
            record.UpdatedAt = GetIstNow();
            await _staffPerformanceRecords.InsertOneAsync(record);
        }

        return record;
    }

    // Calculate bonus for staff performance record
    public async Task<StaffPerformanceRecord> CalculateStaffBonusAsync(string recordId)
    {
        var record = await GetStaffPerformanceRecordByIdAsync(recordId);
        if (record == null)
            throw new Exception("Performance record not found");

        var staff = await GetStaffByIdAsync(record.StaffId);
        if (staff == null)
            throw new Exception("Staff member not found");

        var configs = await GetBonusConfigurationsForStaffAsync(staff);
        
        record.BonusBreakdown = new List<BonusCalculationDetail>();
        record.TotalBonus = 0;
        record.TotalDeductions = 0;

        foreach (var config in configs.Where(c => c.IsActive))
        {
            foreach (var rule in config.Rules.Where(r => r.IsActive))
            {
                var detail = CalculateBonusRule(rule, record, staff);
                if (detail != null)
                {
                    record.BonusBreakdown.Add(detail);
                    if (detail.IsBonus)
                        record.TotalBonus += detail.Amount;
                    else
                        record.TotalDeductions += detail.Amount;
                }
            }
        }

        record.NetAmount = record.TotalBonus - record.TotalDeductions;
        record.IsCalculated = true;
        record.CalculatedAt = GetIstNow();
        record.UpdatedAt = GetIstNow();

        await _staffPerformanceRecords.ReplaceOneAsync(r => r.Id == recordId, record);
        return record;
    }

    private BonusCalculationDetail? CalculateBonusRule(BonusRule rule, StaffPerformanceRecord record, Staff staff)
    {
        decimal amount = 0;
        string calculationDetails = "";

        switch (rule.RuleType)
        {
            case "OvertimeHours":
                if (record.OvertimeHours > 0 && (rule.Threshold == null || record.OvertimeHours >= rule.Threshold))
                {
                    amount = record.OvertimeHours * rule.RateAmount;
                    calculationDetails = $"{record.OvertimeHours} overtime hours × ₹{rule.RateAmount} = ₹{amount}";
                }
                break;

            case "UndertimeHours":
                if (record.UndertimeHours > 0 && (rule.Threshold == null || record.UndertimeHours >= rule.Threshold))
                {
                    amount = record.UndertimeHours * rule.RateAmount;
                    calculationDetails = $"{record.UndertimeHours} undertime hours × ₹{rule.RateAmount} = ₹{amount}";
                }
                break;

            case "SnacksPreparation":
                var excessSnacks = record.SnacksPrepared - record.ExpectedSnacks;
                if (excessSnacks > 0 && (rule.Threshold == null || excessSnacks >= rule.Threshold))
                {
                    amount = excessSnacks * rule.RateAmount;
                    calculationDetails = $"{excessSnacks} excess snacks × ₹{rule.RateAmount} = ₹{amount}";
                }
                break;

            case "BadOrders":
                if (record.BadOrders > 0 && (rule.Threshold == null || record.BadOrders >= rule.Threshold))
                {
                    amount = record.BadOrders * rule.RateAmount;
                    calculationDetails = $"{record.BadOrders} bad orders × ₹{rule.RateAmount} = ₹{amount}";
                }
                break;

            case "GoodRatings":
                if (record.GoodRatings > 0 && (rule.Threshold == null || record.GoodRatings >= rule.Threshold))
                {
                    amount = record.GoodRatings * rule.RateAmount;
                    calculationDetails = $"{record.GoodRatings} good ratings × ₹{rule.RateAmount} = ₹{amount}";
                }
                break;

            case "RefundDeduction":
                if (record.MissingItemRefunds > 0)
                {
                    var percentage = rule.PercentageValue ?? 50; // Default 50%
                    amount = record.MissingItemRefunds * (percentage / 100);
                    calculationDetails = $"₹{record.MissingItemRefunds} refund × {percentage}% = ₹{amount}";
                }
                break;
        }

        // Apply max amount cap if specified
        if (rule.MaxAmount.HasValue && amount > rule.MaxAmount.Value)
        {
            calculationDetails += $" (capped at ₹{rule.MaxAmount.Value})";
            amount = rule.MaxAmount.Value;
        }

        if (amount <= 0)
            return null;

        return new BonusCalculationDetail
        {
            RuleName = rule.RuleName,
            RuleType = rule.RuleType,
            IsBonus = rule.IsBonus,
            CalculationDetails = calculationDetails,
            Amount = amount
        };
    }

    #endregion
}
