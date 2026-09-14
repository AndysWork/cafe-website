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
        var normTable = tableNumber.Trim();
        var filter = Builders<DineInSession>.Filter.And(
            Builders<DineInSession>.Filter.Eq(s => s.OutletId, outletId),
            Builders<DineInSession>.Filter.Regex(s => s.TableNumber, new MongoDB.Bson.BsonRegularExpression($"^{normTable}$", "i")),
            Builders<DineInSession>.Filter.In(s => s.Status, activeStatuses)
        );

        return await _dineInSessions.Find(filter).SortByDescending(s => s.CreatedAt).FirstOrDefaultAsync();
    }

    public async Task<DineInSession?> GetDineInSessionByIdAsync(string sessionId)
    {
        return await _dineInSessions.Find(s => s.Id == sessionId).FirstOrDefaultAsync();
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
        session.UpdatedAt = GetIstNow();
        var result = await _dineInSessions.ReplaceOneAsync(s => s.Id == session.Id, session);
        return result.ModifiedCount > 0;
    }
}
