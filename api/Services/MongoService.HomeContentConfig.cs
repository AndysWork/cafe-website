using Cafe.Api.Models;
using MongoDB.Driver;

namespace Cafe.Api.Services;

public partial class MongoService
{
    public async Task<HomeContentConfig> GetHomeContentConfigAsync(string? outletId = null)
    {
        HomeContentConfig? config = null;

        if (!string.IsNullOrWhiteSpace(outletId))
        {
            config = await _homeContentConfigs.Find(c => c.OutletId == outletId).FirstOrDefaultAsync();
        }

        config ??= await _homeContentConfigs.Find(c => c.OutletId == null).FirstOrDefaultAsync();

        // Fallback to most recently updated config if outlet/global-specific docs are missing.
        config ??= await _homeContentConfigs
            .Find(_ => true)
            .SortByDescending(c => c.UpdatedAt)
            .FirstOrDefaultAsync();

        return config ?? new HomeContentConfig
        {
            OutletId = outletId,
            AnnouncementEnabled = false,
            AnnouncementTitle = string.Empty,
            AnnouncementMessage = string.Empty,
            FeaturedMenuItemIds = new List<string>()
        };
    }

    public async Task<HomeContentConfig> UpsertHomeContentConfigAsync(string? outletId, UpdateHomeContentConfigRequest request, string updatedBy)
    {
        var now = GetIstNow();
        var cleanFeaturedIds = (request.FeaturedMenuItemIds ?? new List<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .Take(12)
            .ToList();

        var filter = string.IsNullOrWhiteSpace(outletId)
            ? Builders<HomeContentConfig>.Filter.Eq(c => c.OutletId, (string?)null)
            : Builders<HomeContentConfig>.Filter.Eq(c => c.OutletId, outletId);

        var existing = await _homeContentConfigs.Find(filter).FirstOrDefaultAsync();

        if (existing == null)
        {
            var created = new HomeContentConfig
            {
                OutletId = outletId,
                AnnouncementEnabled = request.AnnouncementEnabled,
                AnnouncementTitle = request.AnnouncementTitle?.Trim(),
                AnnouncementMessage = request.AnnouncementMessage?.Trim(),
                FeaturedMenuItemIds = cleanFeaturedIds,
                CreatedAt = now,
                UpdatedAt = now,
                UpdatedBy = updatedBy
            };

            await _homeContentConfigs.InsertOneAsync(created);
            return created;
        }

        existing.AnnouncementEnabled = request.AnnouncementEnabled;
        existing.AnnouncementTitle = request.AnnouncementTitle?.Trim();
        existing.AnnouncementMessage = request.AnnouncementMessage?.Trim();
        existing.FeaturedMenuItemIds = cleanFeaturedIds;
        existing.UpdatedAt = now;
        existing.UpdatedBy = updatedBy;

        await _homeContentConfigs.ReplaceOneAsync(filter, existing);
        return existing;
    }
}
