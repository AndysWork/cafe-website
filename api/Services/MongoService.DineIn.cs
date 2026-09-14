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
}
