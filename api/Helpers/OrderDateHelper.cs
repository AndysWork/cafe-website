using Cafe.Api.Models;
using MongoDB.Driver;

namespace Cafe.Api.Helpers;

public static class OrderDateHelper
{
    public static DateTime ToIstCreatedAt(Order order)
    {
        return order.CreatedAtIsUtc == true
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
        var utcFilters = new List<FilterDefinition<Order>> { builder.Eq(o => o.CreatedAtIsUtc, true) };
        var legacyFilters = new List<FilterDefinition<Order>> { builder.Ne(o => o.CreatedAtIsUtc, true) };
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
