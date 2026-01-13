using MongoDB.Driver;
using Cafe.Api.Models;

namespace Cafe.Api.Services;

public partial class MongoService
{
    // Note: _overheadCosts collection is initialized in main MongoService constructor

    // ===== OVERHEAD COSTS CRUD =====

    // Helper method to ensure overhead cost has required properties with default values
    private void EnsureOverheadCostDefaults(OverheadCost overheadCost)
    {
        if (overheadCost.OperationalHoursPerDay == 0)
        {
            overheadCost.OperationalHoursPerDay = 11;
        }
        if (overheadCost.WorkingDaysPerMonth == 0)
        {
            overheadCost.WorkingDaysPerMonth = 30;
        }
    }

    public async Task<List<OverheadCost>> GetAllOverheadCostsAsync(string? outletId = null)
    {
        // If no outlet is selected, return empty list instead of all data
        if (string.IsNullOrWhiteSpace(outletId))
            return new List<OverheadCost>();
        
        // First, try to get outlet-specific overhead costs
        var outletSpecificFilter = Builders<OverheadCost>.Filter.Eq(o => o.OutletId, outletId);
        var outletCosts = await _overheadCosts.Find(outletSpecificFilter).ToListAsync();
        
        // If outlet has specific costs, return only those (don't include shared costs)
        if (outletCosts.Any())
        {
            Console.WriteLine($"[Overhead Costs] GetAll: Returning {outletCosts.Count} outlet-specific costs for outlet {outletId}");
            foreach (var cost in outletCosts)
            {
                EnsureOverheadCostDefaults(cost);
            }
            return outletCosts;
        }
        
        // Only if outlet has NO specific overhead costs, return shared costs (null OutletId)
        Console.WriteLine($"[Overhead Costs] GetAll: No outlet-specific costs, returning shared costs");
        var sharedFilter = Builders<OverheadCost>.Filter.Eq(o => o.OutletId, null);
        var overheadCosts = await _overheadCosts.Find(sharedFilter).ToListAsync();
        
        // Ensure all overhead costs have default values
        foreach (var cost in overheadCosts)
        {
            EnsureOverheadCostDefaults(cost);
        }
        
        return overheadCosts;
    }

    public async Task<List<OverheadCost>> GetActiveOverheadCostsAsync(string? outletId = null)
    {
        var filterBuilder = Builders<OverheadCost>.Filter;
        List<OverheadCost> overheadCosts;

        if (outletId != null)
        {
            // First, try to get outlet-specific overhead costs
            var outletSpecificFilter = filterBuilder.And(
                filterBuilder.Eq(o => o.IsActive, true),
                filterBuilder.Eq(o => o.OutletId, outletId)
            );
            
            overheadCosts = await _overheadCosts.Find(outletSpecificFilter).ToListAsync();
            
            // If outlet has its own overhead costs, use only those (don't mix with shared costs)
            if (overheadCosts.Any())
            {
                Console.WriteLine($"[Overhead Costs] Found {overheadCosts.Count} outlet-specific overhead costs for outlet {outletId}");
            }
            else
            {
                // Only if outlet has NO specific overhead costs, fall back to shared costs
                Console.WriteLine($"[Overhead Costs] No outlet-specific costs found for outlet {outletId}, using shared costs");
                var sharedFilter = filterBuilder.And(
                    filterBuilder.Eq(o => o.IsActive, true),
                    filterBuilder.Eq(o => o.OutletId, null)
                );
                
                overheadCosts = await _overheadCosts.Find(sharedFilter).ToListAsync();
            }
        }
        else
        {
            // If no outlet specified, return all active overhead costs
            var filter = filterBuilder.Eq(o => o.IsActive, true);
            overheadCosts = await _overheadCosts.Find(filter).ToListAsync();
        }
        
        // Ensure all overhead costs have default values
        foreach (var cost in overheadCosts)
        {
            EnsureOverheadCostDefaults(cost);
        }
        
        return overheadCosts;
    }

    public async Task<OverheadCost?> GetOverheadCostByIdAsync(string id)
    {
        var overheadCost = await _overheadCosts.Find(o => o.Id == id).FirstOrDefaultAsync();
        if (overheadCost != null)
        {
            EnsureOverheadCostDefaults(overheadCost);
        }
        return overheadCost;
    }

    public async Task<OverheadCost?> GetOverheadCostByTypeAsync(string costType)
    {
        return await _overheadCosts.Find(o => o.CostType == costType && o.IsActive)
            .FirstOrDefaultAsync();
    }

    public async Task<OverheadCost> CreateOverheadCostAsync(OverheadCost overheadCost)
    {
        // Ensure defaults before saving
        EnsureOverheadCostDefaults(overheadCost);
        
        overheadCost.CreatedAt = DateTime.UtcNow;
        overheadCost.UpdatedAt = DateTime.UtcNow;
        
        Console.WriteLine($"[Overhead Cost] Creating: {overheadCost.CostType}, Monthly: ₹{overheadCost.MonthlyCost}, PerMin: ₹{overheadCost.CostPerMinute:F4}, OutletId: {overheadCost.OutletId ?? "SHARED"}");
        
        await _overheadCosts.InsertOneAsync(overheadCost);
        return overheadCost;
    }

    public async Task<bool> UpdateOverheadCostAsync(string id, OverheadCost overheadCost)
    {
        // Ensure defaults before updating
        EnsureOverheadCostDefaults(overheadCost);
        
        overheadCost.UpdatedAt = DateTime.UtcNow;
        
        Console.WriteLine($"[Overhead Cost] Updating: {overheadCost.CostType}, Monthly: ₹{overheadCost.MonthlyCost}, PerMin: ₹{overheadCost.CostPerMinute:F4}, OutletId: {overheadCost.OutletId ?? "SHARED"}");
        
        var result = await _overheadCosts.ReplaceOneAsync(
            o => o.Id == id,
            overheadCost
        );
        
        return result.IsAcknowledged && result.ModifiedCount > 0;
    }

    public async Task<bool> DeleteOverheadCostAsync(string id)
    {
        var result = await _overheadCosts.DeleteOneAsync(o => o.Id == id);
        return result.IsAcknowledged && result.DeletedCount > 0;
    }

    // ===== OVERHEAD CALCULATIONS =====

    public async Task<OverheadAllocation> CalculateOverheadAllocationAsync(int preparationTimeMinutes, string? outletId = null)
    {
        Console.WriteLine($"[Overhead Calculation] Starting calculation for {preparationTimeMinutes} minutes, OutletId: {outletId ?? "NULL"}");
        
        var activeOverheads = await GetActiveOverheadCostsAsync(outletId);
        
        Console.WriteLine($"[Overhead Calculation] Retrieved {activeOverheads.Count} active overhead costs");
        foreach (var oh in activeOverheads)
        {
            Console.WriteLine($"[Overhead Calculation]   - {oh.CostType}: Monthly=₹{oh.MonthlyCost}, PerMin=₹{oh.CostPerMinute:F4}, OutletId={oh.OutletId ?? "SHARED"}");
        }
        
        var allocation = new OverheadAllocation
        {
            PreparationTimeMinutes = preparationTimeMinutes,
            Costs = new List<OverheadCostCalculation>()
        };

        foreach (var overhead in activeOverheads)
        {
            var allocatedCost = overhead.CostPerMinute * preparationTimeMinutes;
            
            allocation.Costs.Add(new OverheadCostCalculation
            {
                CostType = overhead.CostType,
                MonthlyCost = overhead.MonthlyCost,
                CostPerMinute = overhead.CostPerMinute,
                AllocatedCost = Math.Round(allocatedCost, 2),
                OperationalHoursPerDay = overhead.OperationalHoursPerDay,
                WorkingDaysPerMonth = overhead.WorkingDaysPerMonth
            });
        }

        allocation.TotalOverheadCost = Math.Round(
            allocation.Costs.Sum(c => c.AllocatedCost), 2
        );

        Console.WriteLine($"[Overhead Calculation] Total overhead cost: ₹{allocation.TotalOverheadCost:F2}");
        Console.WriteLine($"[Overhead Calculation] Breakdown: {string.Join(", ", allocation.Costs.Select(c => $"{c.CostType}=₹{c.AllocatedCost:F2}"))}");

        return allocation;
    }

    // Initialize default overhead costs
    public async Task InitializeDefaultOverheadCostsAsync()
    {
        var existingCount = await _overheadCosts.CountDocumentsAsync(_ => true);
        
        if (existingCount == 0)
        {
            var defaultCosts = new List<OverheadCost>
            {
                new OverheadCost
                {
                    CostType = "Rent",
                    MonthlyCost = 6000,
                    OperationalHoursPerDay = 11,
                    WorkingDaysPerMonth = 30,
                    Description = "Monthly cafe rent allocation",
                    IsActive = true
                },
                new OverheadCost
                {
                    CostType = "Labour",
                    MonthlyCost = 15000,
                    OperationalHoursPerDay = 11,
                    WorkingDaysPerMonth = 30,
                    Description = "Monthly labour cost allocation",
                    IsActive = true
                },
                new OverheadCost
                {
                    CostType = "Electricity",
                    MonthlyCost = 3000,
                    OperationalHoursPerDay = 11,
                    WorkingDaysPerMonth = 30,
                    Description = "Monthly electricity cost allocation",
                    IsActive = true
                }
            };

            await _overheadCosts.InsertManyAsync(defaultCosts);
        }
    }

    // ===== MIGRATION =====

    public async Task<int> MigrateOverheadCostOutletIdsAsync(string targetOutletId)
    {
        // Find all overhead costs without an OutletId
        var filter = Builders<OverheadCost>.Filter.Or(
            Builders<OverheadCost>.Filter.Eq(o => o.OutletId, null),
            Builders<OverheadCost>.Filter.Eq(o => o.OutletId, "")
        );

        var update = Builders<OverheadCost>.Update
            .Set(o => o.OutletId, targetOutletId)
            .Set(o => o.UpdatedAt, DateTime.UtcNow);

        var result = await _overheadCosts.UpdateManyAsync(filter, update);
        return (int)result.ModifiedCount;
    }
}
