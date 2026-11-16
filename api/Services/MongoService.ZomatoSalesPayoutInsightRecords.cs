using MongoDB.Bson;
using MongoDB.Driver;
using Cafe.Api.Models;

namespace Cafe.Api.Services;

public partial class MongoService
{
    public async Task<ZomatoSalesPayoutInsightRecord> UpsertZomatoSalesPayoutInsightRecordAsync(
        string outletId,
        DateTime periodStartDate,
        DateTime periodEndDate,
        BsonDocument insightsPayload,
        string parsedBy)
    {
        if (string.IsNullOrWhiteSpace(outletId))
        {
            throw new ArgumentException("Outlet ID is required.", nameof(outletId));
        }

        var normalizedStart = periodStartDate.Date;
        var normalizedEnd = periodEndDate.Date;
        var filter = Builders<ZomatoSalesPayoutInsightRecord>.Filter.And(
            Builders<ZomatoSalesPayoutInsightRecord>.Filter.Eq(x => x.OutletId, outletId),
            Builders<ZomatoSalesPayoutInsightRecord>.Filter.Eq(x => x.PeriodStartDate, normalizedStart),
            Builders<ZomatoSalesPayoutInsightRecord>.Filter.Eq(x => x.PeriodEndDate, normalizedEnd));

        var now = GetIstNow();
        var existing = await _zomatoSalesPayoutInsightRecords.Find(filter).FirstOrDefaultAsync();

        var record = new ZomatoSalesPayoutInsightRecord
        {
            Id = existing?.Id,
            OutletId = outletId,
            PeriodStartDate = normalizedStart,
            PeriodEndDate = normalizedEnd,
            InsightsPayload = insightsPayload,
            ParsedBy = parsedBy,
            CreatedAt = existing?.CreatedAt ?? now,
            UpdatedAt = now
        };

        if (existing == null)
        {
            await _zomatoSalesPayoutInsightRecords.InsertOneAsync(record);
            return record;
        }

        await _zomatoSalesPayoutInsightRecords.ReplaceOneAsync(filter, record);
        return record;
    }

    public async Task<List<ZomatoSalesPayoutInsightRecord>> GetZomatoSalesPayoutInsightRecordsAsync(string outletId, int limit = 52)
    {
        if (string.IsNullOrWhiteSpace(outletId))
        {
            throw new ArgumentException("Outlet ID is required.", nameof(outletId));
        }

        return await _zomatoSalesPayoutInsightRecords
            .Find(Builders<ZomatoSalesPayoutInsightRecord>.Filter.Eq(x => x.OutletId, outletId))
            .SortByDescending(x => x.PeriodStartDate)
            .Limit(limit)
            .ToListAsync();
    }

    public async Task<long> DeleteZomatoSalesPayoutInsightRecordsByPeriodAsync(string outletId, DateTime startDate, DateTime endDate)
    {
        if (string.IsNullOrWhiteSpace(outletId))
        {
            throw new ArgumentException("Outlet ID is required.", nameof(outletId));
        }

        var normalizedStart = startDate.Date;
        var normalizedEnd = endDate.Date;

        var filter = Builders<ZomatoSalesPayoutInsightRecord>.Filter.And(
            Builders<ZomatoSalesPayoutInsightRecord>.Filter.Eq(x => x.OutletId, outletId),
            Builders<ZomatoSalesPayoutInsightRecord>.Filter.Lte(x => x.PeriodStartDate, normalizedEnd),
            Builders<ZomatoSalesPayoutInsightRecord>.Filter.Gte(x => x.PeriodEndDate, normalizedStart));

        var result = await _zomatoSalesPayoutInsightRecords.DeleteManyAsync(filter);
        return result.DeletedCount;
    }
}
