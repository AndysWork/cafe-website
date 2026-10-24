using Cafe.Api.Models;
using MongoDB.Driver;

namespace Cafe.Api.Services;

public partial class MongoService
{
    public async Task<DineInSession> CreateDineInSessionAsync(DineInSession session)
    {
        session.CreatedAt = GetIstNow();
        session.UpdatedAt = GetIstNow();
        await _dineInSessions.InsertOneAsync(session);
        return session;
    }

    public async Task<DineInSession?> GetActiveDineInSessionByTableAsync(string outletId, string tableNumber)
    {
        var activeStatuses = new[] { "active", "bill_requested" };
        var cleanTable = System.Text.RegularExpressions.Regex.Replace(tableNumber.Trim(), @"(?i)^table\s*", string.Empty).Trim();
        var tablePattern = string.IsNullOrWhiteSpace(cleanTable)
            ? System.Text.RegularExpressions.Regex.Escape(tableNumber.Trim())
            : $"^(?:Table\\s*)?{System.Text.RegularExpressions.Regex.Escape(cleanTable)}$";

        var filterBuilder = Builders<DineInSession>.Filter;
        var filters = new List<FilterDefinition<DineInSession>>
        {
            filterBuilder.Regex(s => s.TableNumber, new MongoDB.Bson.BsonRegularExpression(tablePattern, "i")),
            filterBuilder.In(s => s.Status, activeStatuses)
        };

        if (!string.IsNullOrWhiteSpace(outletId))
        {
            var cleanOutlet = outletId.Trim();
            if (MongoDB.Bson.ObjectId.TryParse(cleanOutlet, out var oId))
            {
                filters.Add(filterBuilder.Or(
                    filterBuilder.Eq("outletId", cleanOutlet),
                    filterBuilder.Eq("outletId", oId)
                ));
            }
            else
            {
                filters.Add(filterBuilder.Eq("outletId", cleanOutlet));
            }
        }

        return await _dineInSessions.Find(filterBuilder.And(filters)).SortByDescending(s => s.CreatedAt).FirstOrDefaultAsync();
    }

    public async Task<DineInSession?> GetLatestDineInSessionByTableAsync(string outletId, string tableNumber)
    {
        var cleanTable = System.Text.RegularExpressions.Regex.Replace(tableNumber.Trim(), @"(?i)^table\s*", string.Empty).Trim();
        var tablePattern = string.IsNullOrWhiteSpace(cleanTable)
            ? System.Text.RegularExpressions.Regex.Escape(tableNumber.Trim())
            : $"^(?:Table\\s*)?{System.Text.RegularExpressions.Regex.Escape(cleanTable)}$";

        var filterBuilder = Builders<DineInSession>.Filter;
        var filters = new List<FilterDefinition<DineInSession>>
        {
            filterBuilder.Regex(s => s.TableNumber, new MongoDB.Bson.BsonRegularExpression(tablePattern, "i"))
        };

        if (!string.IsNullOrWhiteSpace(outletId))
        {
            var cleanOutlet = outletId.Trim();
            if (MongoDB.Bson.ObjectId.TryParse(cleanOutlet, out var oId))
            {
                filters.Add(filterBuilder.Or(
                    filterBuilder.Eq("outletId", cleanOutlet),
                    filterBuilder.Eq("outletId", oId)
                ));
            }
            else
            {
                filters.Add(filterBuilder.Eq("outletId", cleanOutlet));
            }
        }

        return await _dineInSessions.Find(filterBuilder.And(filters)).SortByDescending(s => s.CreatedAt).FirstOrDefaultAsync();
    }

    public async Task<DineInSession?> GetDineInSessionByIdAsync(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return null;
        var cleanId = sessionId.Trim();
        var filter = Builders<DineInSession>.Filter.Eq("_id", cleanId);
        if (MongoDB.Bson.ObjectId.TryParse(cleanId, out var objId))
        {
            filter = Builders<DineInSession>.Filter.Or(filter, Builders<DineInSession>.Filter.Eq("_id", objId));
        }
        return await _dineInSessions.Find(filter).FirstOrDefaultAsync();
    }

    public async Task<List<DineInSession>> GetActiveDineInSessionsAsync(string outletId)
    {
        var activeStatuses = new[] { "active", "bill_requested" };
        var filter = Builders<DineInSession>.Filter.And(
            Builders<DineInSession>.Filter.Eq(s => s.OutletId, outletId),
            Builders<DineInSession>.Filter.In(s => s.Status, activeStatuses)
        );

        return await _dineInSessions.Find(filter).SortByDescending(s => s.CreatedAt).ToListAsync();
    }

    public async Task<bool> TryCloseCancelledDineInSessionAsync(
        string sessionId,
        DateTime expectedUpdatedAt,
        string actor)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(actor)) return false;

        var cleanId = sessionId.Trim();
        var filterBuilder = Builders<DineInSession>.Filter;
        var idFilter = filterBuilder.Eq("_id", cleanId);
        if (MongoDB.Bson.ObjectId.TryParse(cleanId, out var objId))
        {
            idFilter = filterBuilder.Or(idFilter, filterBuilder.Eq("_id", objId));
        }

        var noActivePaymentEntries = filterBuilder.Not(
            filterBuilder.ElemMatch(
                session => session.PaymentEntries,
                entry => entry.Status != "rejected"));
        var filter = filterBuilder.And(
            idFilter,
            filterBuilder.In(session => session.Status, new[] { "active", "bill_requested" }),
            filterBuilder.Eq(session => session.PaymentStatus, "unpaid"),
            filterBuilder.Eq(session => session.AmountPaid, 0),
            filterBuilder.Or(
                filterBuilder.Exists(session => session.RazorpayOrderId, false),
                filterBuilder.Eq(session => session.RazorpayOrderId, (string?)null),
                filterBuilder.Eq(session => session.RazorpayOrderId, string.Empty)),
            filterBuilder.Eq(session => session.UpdatedAt, expectedUpdatedAt),
            noActivePaymentEntries);

        var now = GetIstNow();
        var update = Builders<DineInSession>.Update
            .Set(session => session.Status, "cancelled")
            .Set(session => session.UpdatedAt, now)
            .Push(session => session.Timeline, new DineInSessionEvent
            {
                EventType = "session_cancelled_cleanup",
                Message = "Stale table session closed because all linked order rounds were cancelled",
                Actor = actor,
                CreatedAt = now
            });
        var result = await _dineInSessions.UpdateOneAsync(filter, update);
        return result.ModifiedCount == 1;
    }

    public async Task<List<DineInSession>> GetDineInSessionsByDateRangeAsync(
        string outletId, DateTime start, DateTime endExclusive)
    {
        var filter = Builders<DineInSession>.Filter.And(
            Builders<DineInSession>.Filter.Eq(s => s.OutletId, outletId),
            Builders<DineInSession>.Filter.Gte(s => s.CreatedAt, start),
            Builders<DineInSession>.Filter.Lt(s => s.CreatedAt, endExclusive));
        return await _dineInSessions.Find(filter).SortByDescending(s => s.CreatedAt).ToListAsync();
    }

    public async Task<bool> TryUpdateDineInOrderItemStatusAsync(
        string orderId,
        int itemIndex,
        string expectedOrderStatus,
        string expectedItemStatus,
        string newItemStatus,
        string userId)
    {
        if (itemIndex < 0) return false;
        var itemPath = $"items.{itemIndex}";
        var filter = Builders<Order>.Filter.And(
            Builders<Order>.Filter.Eq(order => order.Id, orderId),
            Builders<Order>.Filter.Eq(order => order.OrderType, "dine-in"),
            Builders<Order>.Filter.Eq(order => order.Status, expectedOrderStatus),
            expectedItemStatus == "pending"
                ? Builders<Order>.Filter.Or(
                    Builders<Order>.Filter.Eq($"{itemPath}.kitchenStatus", expectedItemStatus),
                    Builders<Order>.Filter.Exists($"{itemPath}.kitchenStatus", false),
                    Builders<Order>.Filter.Eq($"{itemPath}.kitchenStatus", (string?)null))
                : Builders<Order>.Filter.Eq($"{itemPath}.kitchenStatus", expectedItemStatus),
            Builders<Order>.Filter.Ne(order => order.IsDeleted, true));
        var now = GetIstNow();
        var update = Builders<Order>.Update
            .Set($"{itemPath}.kitchenStatus", newItemStatus)
            .Set(x => x.UpdatedAt, now);
        if (newItemStatus == "ready")
        {
            update = update
                .Set($"{itemPath}.kitchenReadyAt", now)
                .Set($"{itemPath}.kitchenReadyByUserId", userId);
        }
        else if (newItemStatus == "served")
        {
            update = update
                .Set($"{itemPath}.servedAt", now)
                .Set($"{itemPath}.servedByUserId", userId);
        }

        var result = await _orders.UpdateOneAsync(filter, update);
        return result.ModifiedCount == 1;
    }

    public async Task<bool> TryCompleteDineInOrderPreparationAsync(
        string orderId,
        List<KitchenChecklistItem> checklist,
        DateTime readyAt,
        DateTime prepStartedAt,
        decimal kptMinutes)
    {
        var filter = Builders<Order>.Filter.And(
            Builders<Order>.Filter.Eq(order => order.Id, orderId),
            Builders<Order>.Filter.Eq(order => order.OrderType, "dine-in"),
            Builders<Order>.Filter.Eq(order => order.Status, "preparing"),
            Builders<Order>.Filter.Ne(order => order.IsDeleted, true));
        var update = Builders<Order>.Update
            .Set(order => order.Status, "ready")
            .Set(order => order.KitchenChecklist, checklist)
            .Set(order => order.KitchenReadyAt, readyAt)
            .Set(order => order.KitchenPrepStartedAt, prepStartedAt)
            .Set(order => order.KptMinutes, kptMinutes)
            .Set(order => order.UpdatedAt, readyAt);
        var result = await _orders.UpdateOneAsync(filter, update);
        return result.ModifiedCount == 1;
    }

    public async Task<List<DineInSession>> GetUserDineInSessionsAsync(string userId)
    {
        return await _dineInSessions.Find(s => s.UserId == userId).SortByDescending(s => s.CreatedAt).Limit(50).ToListAsync();
    }

    public async Task<bool> UpdateDineInSessionAsync(DineInSession session)
    {
        if (session == null || string.IsNullOrWhiteSpace(session.Id)) return false;
        session.UpdatedAt = GetIstNow();
        var cleanId = session.Id.Trim();
        var filter = Builders<DineInSession>.Filter.Eq("_id", cleanId);
        if (MongoDB.Bson.ObjectId.TryParse(cleanId, out var objId))
        {
            filter = Builders<DineInSession>.Filter.Or(filter, Builders<DineInSession>.Filter.Eq("_id", objId));
        }
        var result = await _dineInSessions.ReplaceOneAsync(filter, session);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> TransitionDineInPaymentStatusAsync(
        string sessionId,
        IReadOnlyCollection<string> expectedStatuses,
        string newStatus)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || expectedStatuses.Count == 0) return false;

        var cleanId = sessionId.Trim();
        var idFilter = Builders<DineInSession>.Filter.Eq("_id", cleanId);
        if (MongoDB.Bson.ObjectId.TryParse(cleanId, out var objId))
        {
            idFilter = Builders<DineInSession>.Filter.Or(
                idFilter,
                Builders<DineInSession>.Filter.Eq("_id", objId));
        }

        var filter = Builders<DineInSession>.Filter.And(
            idFilter,
            Builders<DineInSession>.Filter.In(s => s.PaymentStatus, expectedStatuses));
        var update = Builders<DineInSession>.Update
            .Set(s => s.PaymentStatus, newStatus)
            .Set(s => s.UpdatedAt, GetIstNow());
        var result = await _dineInSessions.UpdateOneAsync(filter, update);
        return result.ModifiedCount == 1;
    }
}
