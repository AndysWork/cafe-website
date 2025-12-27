using MongoDB.Driver;
using Cafe.Api.Models;

namespace Cafe.Api.Services;

public partial class MongoService
{
    // Note: _overheadCosts collection is initialized in main MongoService constructor

    // ===== OVERHEAD COSTS CRUD =====

    public async Task<List<OverheadCost>> GetAllOverheadCostsAsync()
    {
        return await _overheadCosts.Find(_ => true).ToListAsync();
    }

    public async Task<List<OverheadCost>> GetActiveOverheadCostsAsync()
    {
        return await _overheadCosts.Find(o => o.IsActive).ToListAsync();
    }

    public async Task<OverheadCost?> GetOverheadCostByIdAsync(string id)
    {
        return await _overheadCosts.Find(o => o.Id == id).FirstOrDefaultAsync();
    }

    public async Task<OverheadCost?> GetOverheadCostByTypeAsync(string costType)
    {
        return await _overheadCosts.Find(o => o.CostType == costType && o.IsActive)
            .FirstOrDefaultAsync();
    }

    public async Task<OverheadCost> CreateOverheadCostAsync(OverheadCost overheadCost)
    {
        overheadCost.CreatedAt = DateTime.UtcNow;
        overheadCost.UpdatedAt = DateTime.UtcNow;
        await _overheadCosts.InsertOneAsync(overheadCost);
        return overheadCost;
    }

    public async Task<bool> UpdateOverheadCostAsync(string id, OverheadCost overheadCost)
    {
        overheadCost.UpdatedAt = DateTime.UtcNow;
        
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

    public async Task<OverheadAllocation> CalculateOverheadAllocationAsync(int preparationTimeMinutes)
    {
        var activeOverheads = await GetActiveOverheadCostsAsync();
        
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
                AllocatedCost = Math.Round(allocatedCost, 2)
            });
        }

        allocation.TotalOverheadCost = Math.Round(
            allocation.Costs.Sum(c => c.AllocatedCost), 2
        );

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
}
