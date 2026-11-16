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
        string parsedBy,
        BsonDocument sourceData)
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
            SourceData = sourceData,
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
            .Project<ZomatoSalesPayoutInsightRecord>(Builders<ZomatoSalesPayoutInsightRecord>.Projection.Exclude(x => x.SourceData))
            .SortByDescending(x => x.PeriodStartDate)
            .Limit(limit)
            .ToListAsync();
    }

    public async Task<List<ZomatoSalesPayoutInsightRecord>> GetZomatoSourceRecordsByPeriodAsync(
        string outletId, DateTime startDate, DateTime endDate, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(outletId))
            throw new ArgumentException("Outlet ID is required.", nameof(outletId));

        var filter = Builders<ZomatoSalesPayoutInsightRecord>.Filter.And(
            Builders<ZomatoSalesPayoutInsightRecord>.Filter.Eq(x => x.OutletId, outletId),
            Builders<ZomatoSalesPayoutInsightRecord>.Filter.Lte(x => x.PeriodStartDate, endDate.Date),
            Builders<ZomatoSalesPayoutInsightRecord>.Filter.Gte(x => x.PeriodEndDate, startDate.Date));
        return await _zomatoSalesPayoutInsightRecords.Find(filter)
            .Project<ZomatoSalesPayoutInsightRecord>(Builders<ZomatoSalesPayoutInsightRecord>.Projection.Exclude(x => x.InsightsPayload))
            .ToListAsync(cancellationToken);
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
