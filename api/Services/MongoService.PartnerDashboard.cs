using Cafe.Api.Helpers;
using Cafe.Api.Models;
using MongoDB.Driver;

namespace Cafe.Api.Services;

public partial class MongoService
{
    public async Task<List<Order>> GetDeliveryDashboardOrdersAsync(string outletId, DateTime start, DateTime endExclusive)
    {
        var filter = Builders<Order>.Filter;
        var utcStart = IstDateHelper.IstWallTimeToUtc(start);
        var utcEnd = IstDateHelper.IstWallTimeToUtc(endExclusive);
        var wallStart = DateTime.SpecifyKind(start, DateTimeKind.Utc);
        var wallEnd = DateTime.SpecifyKind(endExclusive, DateTimeKind.Utc);
        var activity = OrderDateHelper.CreatedInRange(start, endExclusive)
            | (filter.Gte(o => o.DeliveryAssignedAtUtc, utcStart) & filter.Lt(o => o.DeliveryAssignedAtUtc, utcEnd))
            | (filter.Eq(o => o.CompletedAtIsUtc, true)
                & filter.Gte(o => o.CompletedAt, utcStart) & filter.Lt(o => o.CompletedAt, utcEnd))
            | (filter.Ne(o => o.CompletedAtIsUtc, true)
                & filter.Gte(o => o.CompletedAt, wallStart) & filter.Lt(o => o.CompletedAt, wallEnd))
            | (filter.Eq(o => o.Status, "cancelled")
                & filter.Gte(o => o.UpdatedAt, wallStart) & filter.Lt(o => o.UpdatedAt, wallEnd));
        return await _orders.Find(filter.Eq(o => o.OutletId, outletId)
            & filter.Eq(o => o.OrderType, "delivery") & filter.Ne(o => o.IsDeleted, true) & activity)
            .Project<Order>(_orderListProjection).ToListAsync();
    }
}
