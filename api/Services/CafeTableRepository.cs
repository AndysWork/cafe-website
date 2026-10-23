using Cafe.Api.Models;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Cafe.Api.Services;

public class CafeTableRepository
{
    private readonly IMongoCollection<CafeTable> _tables;
    private readonly ILogger<CafeTableRepository> _logger;

    public CafeTableRepository(IMongoDatabase database, ILogger<CafeTableRepository> logger)
    {
        _tables = database.GetCollection<CafeTable>("CafeTables");
        _logger = logger;
        EnsureIndexes();
    }

    private void EnsureIndexes()
    {
        try
        {
            _tables.Indexes.CreateOne(new CreateIndexModel<CafeTable>(
                Builders<CafeTable>.IndexKeys
                    .Ascending(t => t.OutletId)
                    .Ascending(t => t.TableKey),
                new CreateIndexOptions { Name = "ux_outlet_table", Unique = true }));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create CafeTables indexes");
        }
    }

    public async Task<List<CafeTable>> GetByOutletAsync(string outletId, bool includeInactive)
    {
        var filter = Builders<CafeTable>.Filter.Eq(t => t.OutletId, outletId);
        if (!includeInactive)
            filter &= Builders<CafeTable>.Filter.Eq(t => t.IsActive, true);

        return await _tables.Find(filter)
            .SortBy(t => t.SortOrder)
            .ThenBy(t => t.TableKey)
            .ToListAsync();
    }

    public async Task<CafeTable?> GetByIdAsync(string id) =>
        await _tables.Find(t => t.Id == id).FirstOrDefaultAsync();

    public async Task<CafeTable?> GetByNumberAsync(string outletId, string tableNumber)
    {
        var key = CafeTable.ToKey(tableNumber);
        return await _tables.Find(t => t.OutletId == outletId && t.TableKey == key).FirstOrDefaultAsync();
    }

    public async Task<bool> AnyForOutletAsync(string outletId) =>
        await _tables.Find(t => t.OutletId == outletId).Limit(1).AnyAsync();

    public async Task<int> GetMaxSortOrderAsync(string outletId)
    {
        var last = await _tables.Find(t => t.OutletId == outletId)
            .SortByDescending(t => t.SortOrder)
            .Limit(1)
            .FirstOrDefaultAsync();
        return last?.SortOrder ?? 0;
    }

    /// <summary>Inserts the table; returns false if the number already exists in the outlet.</summary>
    public async Task<bool> TryCreateAsync(CafeTable table)
    {
        table.TableKey = CafeTable.ToKey(table.TableNumber);
        try
        {
            await _tables.InsertOneAsync(table);
            return true;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    /// <summary>Replaces the table; returns false on duplicate table number.</summary>
    public async Task<bool> TryReplaceAsync(CafeTable table)
    {
        table.TableKey = CafeTable.ToKey(table.TableNumber);
        table.UpdatedAt = DateTime.UtcNow;
        try
        {
            await _tables.ReplaceOneAsync(t => t.Id == table.Id, table);
            return true;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    public async Task<bool> SetHousekeepingStatusAsync(string outletId, string tableNumber, string status, string? updatedBy)
    {
        var key = CafeTable.ToKey(tableNumber);
        var result = await _tables.UpdateOneAsync(
            t => t.OutletId == outletId && t.TableKey == key,
            Builders<CafeTable>.Update
                .Set(t => t.HousekeepingStatus, status)
                .Set(t => t.UpdatedAt, DateTime.UtcNow)
                .Set(t => t.UpdatedBy, updatedBy));
        return result.MatchedCount > 0;
    }

    public async Task<bool> SetQrTokenHashAsync(string tableId, string tokenHash, string? updatedBy)
    {
        var result = await _tables.UpdateOneAsync(
            t => t.Id == tableId,
            Builders<CafeTable>.Update
                .Set(t => t.QrTokenHash, tokenHash)
                .Set(t => t.UpdatedAt, DateTime.UtcNow)
                .Set(t => t.UpdatedBy, updatedBy));
        return result.ModifiedCount == 1;
    }

    public async Task<bool> DeleteAsync(string id)
    {
        var result = await _tables.DeleteOneAsync(t => t.Id == id);
        return result.DeletedCount > 0;
    }
}
