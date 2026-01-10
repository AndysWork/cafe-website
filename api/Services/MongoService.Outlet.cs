using MongoDB.Driver;
using Cafe.Api.Models;

namespace Cafe.Api.Services;

public partial class MongoService
{
    #region Outlet Management

    // Get all outlets
    public async Task<List<Outlet>> GetAllOutletsAsync() =>
        await _outlets.Find(_ => true).ToListAsync();

    // Get active outlets only
    public async Task<List<Outlet>> GetActiveOutletsAsync() =>
        await _outlets.Find(o => o.IsActive).ToListAsync();

    // Get single outlet by ID
    public async Task<Outlet?> GetOutletByIdAsync(string id) =>
        await _outlets.Find(o => o.Id == id).FirstOrDefaultAsync();

    // Get outlet by code
    public async Task<Outlet?> GetOutletByCodeAsync(string code) =>
        await _outlets.Find(o => o.OutletCode == code).FirstOrDefaultAsync();

    // Create new outlet
    public async Task<Outlet> CreateOutletAsync(CreateOutletRequest request, string userId)
    {
        // Check if outlet code already exists
        var existing = await GetOutletByCodeAsync(request.OutletCode);
        if (existing != null)
        {
            throw new InvalidOperationException($"Outlet with code '{request.OutletCode}' already exists");
        }

        var outlet = new Outlet
        {
            OutletName = request.OutletName,
            OutletCode = request.OutletCode,
            Address = request.Address ?? string.Empty,
            City = request.City ?? string.Empty,
            State = request.State ?? string.Empty,
            PhoneNumber = request.PhoneNumber,
            Email = request.Email,
            ManagerName = request.ManagerName,
            IsActive = true,
            Settings = request.Settings ?? new OutletSettings(),
            CreatedBy = userId,
            CreatedDate = GetIstNow(),
            LastUpdatedBy = userId,
            LastUpdated = GetIstNow()
        };

        await _outlets.InsertOneAsync(outlet);
        return outlet;
    }

    // Update existing outlet
    public async Task<bool> UpdateOutletAsync(string id, UpdateOutletRequest request, string userId)
    {
        var updateBuilder = Builders<Outlet>.Update;
        var updates = new List<UpdateDefinition<Outlet>>();

        // Always update LastUpdated
        updates.Add(updateBuilder.Set(o => o.LastUpdated, GetIstNow()));
        updates.Add(updateBuilder.Set(o => o.LastUpdatedBy, userId));

        // Update fields if they are provided
        if (!string.IsNullOrEmpty(request.OutletName))
            updates.Add(updateBuilder.Set(o => o.OutletName, request.OutletName));

        if (!string.IsNullOrEmpty(request.Address))
            updates.Add(updateBuilder.Set(o => o.Address, request.Address));

        if (!string.IsNullOrEmpty(request.City))
            updates.Add(updateBuilder.Set(o => o.City, request.City));

        if (!string.IsNullOrEmpty(request.State))
            updates.Add(updateBuilder.Set(o => o.State, request.State));

        if (!string.IsNullOrEmpty(request.PhoneNumber))
            updates.Add(updateBuilder.Set(o => o.PhoneNumber, request.PhoneNumber));

        if (!string.IsNullOrEmpty(request.Email))
            updates.Add(updateBuilder.Set(o => o.Email, request.Email));

        if (!string.IsNullOrEmpty(request.ManagerName))
            updates.Add(updateBuilder.Set(o => o.ManagerName, request.ManagerName));

        if (request.IsActive.HasValue)
            updates.Add(updateBuilder.Set(o => o.IsActive, request.IsActive.Value));

        if (request.Settings != null)
            updates.Add(updateBuilder.Set(o => o.Settings, request.Settings));

        var combinedUpdate = updateBuilder.Combine(updates);
        var result = await _outlets.UpdateOneAsync(o => o.Id == id, combinedUpdate);

        return result.ModifiedCount > 0;
    }

    // Delete outlet
    public async Task<bool> DeleteOutletAsync(string id)
    {
        // Check if outlet has any associated data
        var hasSales = await _sales.Find(s => s.OutletId == id).AnyAsync();
        var hasExpenses = await _expenses.Find(e => e.OutletId == id).AnyAsync();
        var hasOrders = await _orders.Find(o => o.OutletId == id).AnyAsync();
        var hasInventory = await _inventory.Find(i => i.OutletId == id).AnyAsync();

        if (hasSales || hasExpenses || hasOrders || hasInventory)
        {
            throw new InvalidOperationException("Cannot delete outlet with associated data. Deactivate it instead.");
        }

        var result = await _outlets.DeleteOneAsync(o => o.Id == id);
        return result.DeletedCount > 0;
    }

    // Toggle outlet active status
    public async Task<bool> ToggleOutletStatusAsync(string id)
    {
        var outlet = await GetOutletByIdAsync(id);
        if (outlet == null)
            return false;

        var update = Builders<Outlet>.Update
            .Set(o => o.IsActive, !outlet.IsActive)
            .Set(o => o.LastUpdated, GetIstNow());

        var result = await _outlets.UpdateOneAsync(o => o.Id == id, update);
        return result.ModifiedCount > 0;
    }

    // Ensure default outlet exists for existing data
    public async Task EnsureDefaultOutletAsync()
    {
        var outletCount = await _outlets.CountDocumentsAsync(_ => true);
        if (outletCount == 0)
        {
            var defaultOutlet = new Outlet
            {
                OutletName = "Maa Tara Cafe - Main Outlet",
                OutletCode = "MTC001",
                Address = "Main Location",
                City = "City",
                State = "State",
                IsActive = true,
                Settings = new OutletSettings(),
                CreatedBy = "System",
                CreatedDate = GetIstNow(),
                LastUpdatedBy = "System",
                LastUpdated = GetIstNow()
            };

            await _outlets.InsertOneAsync(defaultOutlet);
            Console.WriteLine($"✓ Default outlet created: {defaultOutlet.OutletName} ({defaultOutlet.OutletCode})");
        }
    }

    #endregion
}
