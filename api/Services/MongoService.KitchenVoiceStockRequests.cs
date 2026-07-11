using Cafe.Api.Models;
using MongoDB.Driver;

namespace Cafe.Api.Services;

public partial class MongoService
{
    public async Task<KitchenVoiceStockRequest> CreateKitchenVoiceStockRequestAsync(KitchenVoiceStockRequest request)
    {
        request.Status = "pending";
        request.CreatedAt = GetIstNow();
        request.UpdatedAt = request.CreatedAt;
        await _kitchenVoiceStockRequests.InsertOneAsync(request);
        return request;
    }

    public async Task<List<KitchenVoiceStockRequest>> GetKitchenVoiceStockRequestsAsync(string? outletId = null, string? status = null, int limit = 100)
    {
        var safeLimit = Math.Clamp(limit, 1, 500);
        var filter = Builders<KitchenVoiceStockRequest>.Filter.Empty;

        if (!string.IsNullOrWhiteSpace(outletId))
        {
            filter &= Builders<KitchenVoiceStockRequest>.Filter.Eq(r => r.OutletId, outletId);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalizedStatus = status.Trim().ToLowerInvariant();
            filter &= Builders<KitchenVoiceStockRequest>.Filter.Eq(r => r.Status, normalizedStatus);
        }

        return await _kitchenVoiceStockRequests
            .Find(filter)
            .SortByDescending(r => r.CreatedAt)
            .Limit(safeLimit)
            .ToListAsync();
    }

    public async Task<KitchenVoiceStockRequest?> GetKitchenVoiceStockRequestByIdAsync(string id)
    {
        return await _kitchenVoiceStockRequests.Find(r => r.Id == id).FirstOrDefaultAsync();
    }

    public async Task<bool> ReviewKitchenVoiceStockRequestAsync(string id, string decision, string reviewedByUserId, string reviewedByName, string? note)
    {
        var normalizedDecision = decision.Trim().ToLowerInvariant();
        var update = Builders<KitchenVoiceStockRequest>.Update
            .Set(r => r.Status, normalizedDecision)
            .Set(r => r.ReviewedByUserId, reviewedByUserId)
            .Set(r => r.ReviewedByName, reviewedByName)
            .Set(r => r.ReviewNote, string.IsNullOrWhiteSpace(note) ? null : note.Trim())
            .Set(r => r.ReviewedAt, GetIstNow())
            .Set(r => r.UpdatedAt, GetIstNow());

        var result = await _kitchenVoiceStockRequests.UpdateOneAsync(
            r => r.Id == id && r.Status == "pending",
            update);

        return result.ModifiedCount > 0;
    }
}
