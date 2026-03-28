using MongoDB.Driver;
using MongoDB.Bson;
using Cafe.Api.Models;
using Microsoft.Extensions.Logging;

namespace Cafe.Api.Services;

public partial class MongoService
{
    private IMongoCollection<UserActivityEvent>? _activityEvents;
    private IMongoCollection<UserSession>? _userSessions;

    private IMongoCollection<UserActivityEvent> ActivityEvents =>
        _activityEvents ??= _database.GetCollection<UserActivityEvent>("UserActivityEvents");

    private IMongoCollection<UserSession> UserSessions =>
        _userSessions ??= _database.GetCollection<UserSession>("UserSessions");

    // ─── Event Tracking ───

    public async Task TrackEventAsync(UserActivityEvent evt)
    {
        try
        {
            evt.Timestamp = DateTime.UtcNow;
            await ActivityEvents.InsertOneAsync(evt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to track analytics event of type {EventType}", evt.EventType);
        }
    }

    public async Task TrackEventsBatchAsync(List<UserActivityEvent> events)
    {
        if (events.Count == 0) return;
        try
        {
            foreach (var evt in events) evt.Timestamp = DateTime.UtcNow;
            await ActivityEvents.InsertManyAsync(events);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to track {Count} analytics events in batch", events.Count);
        }
    }

    // ─── Session Management ───

    public async Task<UserSession> CreateSessionAsync(string userId, string username, string role, string sessionId)
    {
        // Step 1: Mark any previous active sessions for this user as inactive
        var filter = Builders<UserSession>.Filter.And(
            Builders<UserSession>.Filter.Eq(s => s.UserId, userId),
            Builders<UserSession>.Filter.Eq(s => s.IsActive, true)
        );
        var update = Builders<UserSession>.Update
            .Set(s => s.IsActive, false)
            .Set(s => s.LogoutTime, DateTime.UtcNow);

        // Capture existing active sessions for compensation
        var previousActiveSessions = await UserSessions.Find(filter).ToListAsync();
        await UserSessions.UpdateManyAsync(filter, update);

        // Step 2: Create new session — compensate by restoring old sessions on failure
        var session = new UserSession
        {
            UserId = userId,
            Username = username,
            UserRole = role,
            SessionId = sessionId,
            LoginTime = DateTime.UtcNow,
            LastActiveTime = DateTime.UtcNow,
            IsActive = true
        };

        try
        {
            await UserSessions.InsertOneAsync(session);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to insert new session for user {UserId} — restoring previous sessions", userId);
            // Compensate: re-activate the sessions we just deactivated
            foreach (var prev in previousActiveSessions)
            {
                var restoreFilter = Builders<UserSession>.Filter.Eq(s => s.Id, prev.Id);
                var restoreUpdate = Builders<UserSession>.Update
                    .Set(s => s.IsActive, true)
                    .Unset(s => s.LogoutTime);
                await UserSessions.UpdateOneAsync(restoreFilter, restoreUpdate);
            }
            throw;
        }

        return session;
    }

    public async Task EndSessionAsync(string sessionId)
    {
        try
        {
            var filter = Builders<UserSession>.Filter.Eq(s => s.SessionId, sessionId);
            var update = Builders<UserSession>.Update
                .Set(s => s.IsActive, false)
                .Set(s => s.LogoutTime, DateTime.UtcNow);
            await UserSessions.UpdateOneAsync(filter, update);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to end session {SessionId}", sessionId);
        }
    }

    public async Task UpdateSessionActivityAsync(string sessionId)
    {
        try
        {
            var filter = Builders<UserSession>.Filter.Eq(s => s.SessionId, sessionId);
            var update = Builders<UserSession>.Update.Set(s => s.LastActiveTime, DateTime.UtcNow);
            await UserSessions.UpdateOneAsync(filter, update);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update session activity for {SessionId}", sessionId);
        }
    }

    // ─── Analytics Queries ───

    public async Task<UserMetrics> GetUserMetricsAsync()
    {
        var now = DateTime.UtcNow;
        var todayStart = now.Date;
        var weekStart = todayStart.AddDays(-(int)todayStart.DayOfWeek);
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        // Consider sessions inactive if no activity for 30 minutes
        var activeThreshold = now.AddMinutes(-30);

        var totalUsers = await _users.CountDocumentsAsync(FilterDefinition<User>.Empty);

        var activeSessions = await UserSessions.CountDocumentsAsync(
            Builders<UserSession>.Filter.And(
                Builders<UserSession>.Filter.Eq(s => s.IsActive, true),
                Builders<UserSession>.Filter.Gte(s => s.LastActiveTime, activeThreshold)
            ));

        var loginFilter = Builders<UserActivityEvent>.Filter.Eq(e => e.EventType, "Login");
        var todayLoginFilter = Builders<UserActivityEvent>.Filter.And(
            loginFilter,
            Builders<UserActivityEvent>.Filter.Gte(e => e.Timestamp, todayStart));
        var weekLoginFilter = Builders<UserActivityEvent>.Filter.And(
            loginFilter,
            Builders<UserActivityEvent>.Filter.Gte(e => e.Timestamp, weekStart));
        var monthLoginFilter = Builders<UserActivityEvent>.Filter.And(
            loginFilter,
            Builders<UserActivityEvent>.Filter.Gte(e => e.Timestamp, monthStart));

        var todayLogins = await ActivityEvents.CountDocumentsAsync(todayLoginFilter);
        var weekLogins = await ActivityEvents.CountDocumentsAsync(weekLoginFilter);
        var monthLogins = await ActivityEvents.CountDocumentsAsync(monthLoginFilter);
        var totalLogins = await ActivityEvents.CountDocumentsAsync(loginFilter);

        // Unique users
        var uniqueToday = await ActivityEvents.Distinct<string>("UserId", todayLoginFilter).ToListAsync();
        var uniqueWeek = await ActivityEvents.Distinct<string>("UserId", weekLoginFilter).ToListAsync();
        var uniqueMonth = await ActivityEvents.Distinct<string>("UserId", monthLoginFilter).ToListAsync();

        return new UserMetrics
        {
            TotalRegisteredUsers = (int)totalUsers,
            CurrentlyActiveUsers = (int)activeSessions,
            TodayLogins = (int)todayLogins,
            WeekLogins = (int)weekLogins,
            MonthLogins = (int)monthLogins,
            TotalLoginEvents = (int)totalLogins,
            UniqueUsersToday = uniqueToday.Count(u => u != null),
            UniqueUsersThisWeek = uniqueWeek.Count(u => u != null),
            UniqueUsersThisMonth = uniqueMonth.Count(u => u != null)
        };
    }

    public async Task<List<FeatureUsageStat>> GetTopFeaturesAsync(int limit = 15, DateTime? periodStart = null)
    {
        var matchFilter = new BsonDocument("EventType", "FeatureUsage");
        if (periodStart.HasValue)
            matchFilter.Add("Timestamp", new BsonDocument("$gte", periodStart.Value));

        var pipeline = new[]
        {
            new BsonDocument("$match", matchFilter),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", "$FeatureName" },
                { "UsageCount", new BsonDocument("$sum", 1) },
                { "UniqueUsers", new BsonDocument("$addToSet", "$UserId") }
            }),
            new BsonDocument("$project", new BsonDocument
            {
                { "FeatureName", "$_id" },
                { "UsageCount", 1 },
                { "UniqueUsers", new BsonDocument("$size", "$UniqueUsers") }
            }),
            new BsonDocument("$sort", new BsonDocument("UsageCount", -1)),
            new BsonDocument("$limit", limit)
        };

        using var cursor = await ActivityEvents.AggregateAsync<BsonDocument>(pipeline);
        var results = await cursor.ToListAsync();

        return results.Select(r => new FeatureUsageStat
        {
            FeatureName = r.GetValue("FeatureName", "Unknown").AsString,
            UsageCount = r.GetValue("UsageCount", 0).ToInt64(),
            UniqueUsers = r.GetValue("UniqueUsers", 0).ToInt64()
        }).ToList();
    }

    public async Task<List<ApiPerformanceStat>> GetApiPerformanceAsync(int limit = 20, DateTime? periodStart = null)
    {
        var apiMatchFilter = new BsonDocument("EventType", "ApiCall");
        if (periodStart.HasValue)
            apiMatchFilter.Add("Timestamp", new BsonDocument("$gte", periodStart.Value));

        var pipeline = new[]
        {
            new BsonDocument("$match", apiMatchFilter),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", "$Detail" },
                { "TotalCalls", new BsonDocument("$sum", 1) },
                { "AvgResponseTimeMs", new BsonDocument("$avg", "$ResponseTimeMs") },
                { "MaxResponseTimeMs", new BsonDocument("$max", "$ResponseTimeMs") },
                { "MinResponseTimeMs", new BsonDocument("$min", "$ResponseTimeMs") },
                { "ResponseTimes", new BsonDocument("$push", "$ResponseTimeMs") },
                { "ErrorCount", new BsonDocument("$sum",
                    new BsonDocument("$cond", new BsonArray
                    {
                        new BsonDocument("$gte", new BsonArray { "$StatusCode", 400 }),
                        1, 0
                    }))
                }
            }),
            new BsonDocument("$sort", new BsonDocument("TotalCalls", -1)),
            new BsonDocument("$limit", limit)
        };

        using var cursor = await ActivityEvents.AggregateAsync<BsonDocument>(pipeline);
        var results = await cursor.ToListAsync();

        return results.Select(r =>
        {
            var responseTimes = r.GetValue("ResponseTimes", new BsonArray())
                .AsBsonArray.Select(v => v.ToDouble()).OrderBy(v => v).ToList();
            var p95Index = (int)(responseTimes.Count * 0.95);
            var p95 = responseTimes.Count > 0 ? responseTimes[Math.Min(p95Index, responseTimes.Count - 1)] : 0;

            return new ApiPerformanceStat
            {
                Endpoint = r.GetValue("_id", "Unknown").AsString,
                TotalCalls = r.GetValue("TotalCalls", 0).ToInt64(),
                AvgResponseTimeMs = Math.Round(r.GetValue("AvgResponseTimeMs", 0).ToDouble(), 1),
                MaxResponseTimeMs = r.GetValue("MaxResponseTimeMs", 0).ToDouble(),
                MinResponseTimeMs = r.GetValue("MinResponseTimeMs", 0).ToDouble(),
                P95ResponseTimeMs = Math.Round(p95, 1),
                ErrorCount = r.GetValue("ErrorCount", 0).ToInt64()
            };
        }).ToList();
    }

    public async Task<CartAnalytics> GetCartAnalyticsAsync(DateTime? periodStart = null)
    {
        var timeFilter = periodStart.HasValue
            ? Builders<UserActivityEvent>.Filter.Gte(e => e.Timestamp, periodStart.Value)
            : Builders<UserActivityEvent>.Filter.Empty;

        var cartViewFilter = Builders<UserActivityEvent>.Filter.Eq(e => e.EventType, "CartView") & timeFilter;
        var cartAddFilter = Builders<UserActivityEvent>.Filter.Eq(e => e.EventType, "CartAdd") & timeFilter;
        var cartRemoveFilter = Builders<UserActivityEvent>.Filter.Eq(e => e.EventType, "CartRemove") & timeFilter;

        var totalViews = await ActivityEvents.CountDocumentsAsync(cartViewFilter);
        var totalAdds = await ActivityEvents.CountDocumentsAsync(cartAddFilter);
        var totalRemovals = await ActivityEvents.CountDocumentsAsync(cartRemoveFilter);

        var uniqueCarted = await ActivityEvents.Distinct<string>("UserId", cartAddFilter).ToListAsync();
        var uniqueBrowsed = await ActivityEvents.Distinct<string>("UserId", cartViewFilter).ToListAsync();

        // Top carted items
        var cartAddMatchFilter = new BsonDocument("EventType", "CartAdd");
        if (periodStart.HasValue)
            cartAddMatchFilter.Add("Timestamp", new BsonDocument("$gte", periodStart.Value));

        var pipeline = new[]
        {
            new BsonDocument("$match", cartAddMatchFilter),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", "$FeatureName" },
                { "AddCount", new BsonDocument("$sum", 1) }
            }),
            new BsonDocument("$sort", new BsonDocument("AddCount", -1)),
            new BsonDocument("$limit", 10)
        };

        using var cursor = await ActivityEvents.AggregateAsync<BsonDocument>(pipeline);
        var topItems = await cursor.ToListAsync();

        return new CartAnalytics
        {
            TotalCartViews = totalViews,
            TotalAddToCart = totalAdds,
            TotalCartRemovals = totalRemovals,
            UniqueUsersWhoCarted = uniqueCarted.Count(u => u != null),
            UniqueUsersWhoBrowsed = uniqueBrowsed.Count(u => u != null),
            TopCartedItems = topItems.Select(r => new CartItemStat
            {
                ItemName = r.GetValue("_id", "Unknown").AsString,
                AddCount = r.GetValue("AddCount", 0).ToInt64()
            }).ToList()
        };
    }

    public async Task<List<DailyActiveUserStat>> GetDailyActiveUsersAsync(int days = 30)
    {
        var startDate = DateTime.UtcNow.Date.AddDays(-days);

        var pipeline = new[]
        {
            new BsonDocument("$match", new BsonDocument
            {
                { "Timestamp", new BsonDocument("$gte", startDate) },
                { "EventType", "Login" }
            }),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", new BsonDocument
                    {
                        { "$dateToString", new BsonDocument { { "format", "%Y-%m-%d" }, { "date", "$Timestamp" } } }
                    }
                },
                { "ActiveUsers", new BsonDocument("$addToSet", "$UserId") },
                { "LoginCount", new BsonDocument("$sum", 1) }
            }),
            new BsonDocument("$project", new BsonDocument
            {
                { "Date", "$_id" },
                { "ActiveUsers", new BsonDocument("$size", "$ActiveUsers") },
                { "LoginCount", 1 }
            }),
            new BsonDocument("$sort", new BsonDocument("_id", 1))
        };

        using var cursor = await ActivityEvents.AggregateAsync<BsonDocument>(pipeline);
        var results = await cursor.ToListAsync();

        return results.Select(r => new DailyActiveUserStat
        {
            Date = r.GetValue("Date", "").AsString,
            ActiveUsers = r.GetValue("ActiveUsers", 0).ToInt32(),
            LoginCount = r.GetValue("LoginCount", 0).ToInt32()
        }).ToList();
    }

    public async Task<List<HourlyActivityStat>> GetHourlyActivityAsync()
    {
        var todayStart = DateTime.UtcNow.Date;
        var pipeline = new[]
        {
            new BsonDocument("$match", new BsonDocument("Timestamp",
                new BsonDocument("$gte", todayStart))),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", new BsonDocument("$hour", "$Timestamp") },
                { "EventCount", new BsonDocument("$sum", 1) }
            }),
            new BsonDocument("$sort", new BsonDocument("_id", 1))
        };

        using var cursor = await ActivityEvents.AggregateAsync<BsonDocument>(pipeline);
        var results = await cursor.ToListAsync();

        return results.Select(r => new HourlyActivityStat
        {
            Hour = r.GetValue("_id", 0).ToInt32(),
            EventCount = r.GetValue("EventCount", 0).ToInt64()
        }).ToList();
    }

    public async Task<List<RecentSessionInfo>> GetRecentSessionsAsync(int limit = 50)
    {
        var sessions = await UserSessions
            .Find(FilterDefinition<UserSession>.Empty)
            .SortByDescending(s => s.LoginTime)
            .Limit(limit)
            .ToListAsync();

        return sessions.Select(s => new RecentSessionInfo
        {
            Username = s.Username,
            UserRole = s.UserRole,
            LoginTime = s.LoginTime,
            LogoutTime = s.LogoutTime,
            LastActiveTime = s.LastActiveTime,
            IsActive = s.IsActive && s.LastActiveTime > DateTime.UtcNow.AddMinutes(-30)
        }).ToList();
    }

    public async Task EnsureAnalyticsIndexesAsync()
    {
        // Index on Timestamp for time-range queries
        await ActivityEvents.Indexes.CreateOneAsync(
            new CreateIndexModel<UserActivityEvent>(
                Builders<UserActivityEvent>.IndexKeys.Descending(e => e.Timestamp)));

        // Compound index on EventType + Timestamp
        await ActivityEvents.Indexes.CreateOneAsync(
            new CreateIndexModel<UserActivityEvent>(
                Builders<UserActivityEvent>.IndexKeys
                    .Ascending(e => e.EventType)
                    .Descending(e => e.Timestamp)));

        // Index on UserId for per-user queries
        await ActivityEvents.Indexes.CreateOneAsync(
            new CreateIndexModel<UserActivityEvent>(
                Builders<UserActivityEvent>.IndexKeys.Ascending(e => e.UserId)));

        // Session indexes
        await UserSessions.Indexes.CreateOneAsync(
            new CreateIndexModel<UserSession>(
                Builders<UserSession>.IndexKeys.Ascending(s => s.SessionId)));

        await UserSessions.Indexes.CreateOneAsync(
            new CreateIndexModel<UserSession>(
                Builders<UserSession>.IndexKeys
                    .Ascending(s => s.IsActive)
                    .Descending(s => s.LastActiveTime)));

        // TTL index to auto-delete old events after 90 days
        await ActivityEvents.Indexes.CreateOneAsync(
            new CreateIndexModel<UserActivityEvent>(
                Builders<UserActivityEvent>.IndexKeys.Ascending(e => e.Timestamp),
                new CreateIndexOptions { ExpireAfter = TimeSpan.FromDays(90), Name = "ttl_activity_90d" }));
    }
}
