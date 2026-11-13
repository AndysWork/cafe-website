using Cafe.Api.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Cafe.Api.Helpers;

public static class OrderDateHelper
{
    public static DateTimeOffset ToKitchenCreatedAtIst(Order order)
    {
        var isUtc = IsCreatedAtUtc(order);
        return DineInTimestampHelper.ToIstOffset(order.CreatedAt, isUtc, !isUtc);
    }

    public static bool IsCreatedAtUtc(Order order)
    {
        if (order.CreatedAtIsUtc.HasValue) return order.CreatedAtIsUtc.Value;
        if (PhoneDeliveryOrderRules.IsPhoneOrder(order)) return true;
        return order.OrderType == "dine-in"
            && !string.IsNullOrWhiteSpace(order.DineInSessionId)
            && !DineInTimestampHelper.IsLegacyWallClock(order.CreatedAt, order.Id);
    }

    public static DateTime ToIstCreatedAt(Order order)
    {
        return IsCreatedAtUtc(order)
            ? IstDateHelper.NormalizeToIst(DateTime.SpecifyKind(order.CreatedAt, DateTimeKind.Utc))
            : DateTime.SpecifyKind(order.CreatedAt, DateTimeKind.Unspecified);
    }

    public static bool IsCreatedInRange(Order order, DateTime? start, DateTime? endExclusive)
    {
        var created = ToIstCreatedAt(order);
        return (!start.HasValue || created >= start.Value)
            && (!endExclusive.HasValue || created < endExclusive.Value);
    }

    public static List<Order> TakeLatest(IEnumerable<Order> orders, int limit)
    {
        return orders.OrderByDescending(ToIstCreatedAt)
            .ThenByDescending(order => order.Id, StringComparer.Ordinal)
            .Take(limit)
            .ToList();
    }

    public static FilterDefinition<Order> CreatedInRange(DateTime? start, DateTime? endExclusive)
    {
        var builder = Builders<Order>.Filter;
        // Match the same ObjectId-based legacy detection used for unmarked dine-in rounds.
        var objectIdDate = new BsonDocument("$convert", new BsonDocument
        {
            { "input", "$_id" }, { "to", "date" }, { "onError", BsonNull.Value }, { "onNull", BsonNull.Value }
        });
        var difference = new BsonDocument("$subtract", new BsonArray { "$createdAt", objectIdDate });
        var legacyDineIn = new BsonDocumentFilterDefinition<Order>(new BsonDocument("$expr",
            new BsonDocument("$and", new BsonArray
            {
                new BsonDocument("$eq", new BsonArray { new BsonDocument("$type", "$_id"), "objectId" }),
                new BsonDocument("$gte", new BsonArray { difference, 325 * 60 * 1000 }),
                new BsonDocument("$lte", new BsonArray { difference, 335 * 60 * 1000 })
            })));
        var nonLegacyDineIn = new BsonDocumentFilterDefinition<Order>(
            new BsonDocument("$nor", new BsonArray { legacyDineIn.Document }));
        var unmarkedUtc = builder.Eq(o => o.CreatedAtIsUtc, null) &
            (builder.Eq(o => o.OrderSource, "phone") |
             (builder.Eq(o => o.OrderType, "dine-in") &
              builder.Ne(o => o.DineInSessionId, null) &
              nonLegacyDineIn));
        var isUtc = builder.Eq(o => o.CreatedAtIsUtc, true) | unmarkedUtc;
        var utcFilters = new List<FilterDefinition<Order>> { isUtc };
        var legacyFilters = new List<FilterDefinition<Order>> { !isUtc };
        if (start.HasValue)
        {
            utcFilters.Add(builder.Gte(o => o.CreatedAt, IstDateHelper.IstWallTimeToUtc(start.Value)));
            legacyFilters.Add(builder.Gte(o => o.CreatedAt, DateTime.SpecifyKind(start.Value, DateTimeKind.Utc)));
        }
        if (endExclusive.HasValue)
        {
            utcFilters.Add(builder.Lt(o => o.CreatedAt, IstDateHelper.IstWallTimeToUtc(endExclusive.Value)));
            legacyFilters.Add(builder.Lt(o => o.CreatedAt, DateTime.SpecifyKind(endExclusive.Value, DateTimeKind.Utc)));
        }
        return builder.Or(builder.And(utcFilters), builder.And(legacyFilters));
    }
}
