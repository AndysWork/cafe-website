using Cafe.Api.Models;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using System.Text.Json;

namespace Cafe.Api.Services;

/// <summary>
/// Records immutable event log entries for critical state transitions.
/// Provides full traceability for Orders, Payments, Inventory, and Loyalty operations.
/// Events are fire-and-forget — failures never break the primary operation.
/// </summary>
public class EventLogService
{
    private readonly IMongoCollection<EventLog> _eventLogs;
    private readonly ILogger<EventLogService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public EventLogService(IMongoDatabase database, ILogger<EventLogService> logger)
    {
        _eventLogs = database.GetCollection<EventLog>("EventLogs");
        _logger = logger;
        EnsureIndexes();
    }

    private void EnsureIndexes()
    {
        try
        {
            var indexes = new[]
            {
                new CreateIndexModel<EventLog>(
                    Builders<EventLog>.IndexKeys.Combine(
                        Builders<EventLog>.IndexKeys.Ascending(e => e.EntityType),
                        Builders<EventLog>.IndexKeys.Ascending(e => e.EntityId),
                        Builders<EventLog>.IndexKeys.Descending(e => e.Timestamp)),
                    new CreateIndexOptions { Name = "ix_entity_timeline" }),
                new CreateIndexModel<EventLog>(
                    Builders<EventLog>.IndexKeys.Descending(e => e.Timestamp),
                    new CreateIndexOptions { Name = "ix_timestamp", ExpireAfter = TimeSpan.FromDays(365) }),
                new CreateIndexModel<EventLog>(
                    Builders<EventLog>.IndexKeys.Combine(
                        Builders<EventLog>.IndexKeys.Ascending(e => e.ActorId),
                        Builders<EventLog>.IndexKeys.Descending(e => e.Timestamp)),
                    new CreateIndexOptions { Name = "ix_actor_timeline" })
            };
            _eventLogs.Indexes.CreateMany(indexes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create EventLog indexes");
        }
    }

    /// <summary>
    /// Record a domain event. This method never throws — event logging
    /// should never break the primary business operation.
    /// </summary>
    public async Task LogEventAsync(
        string entityType,
        string entityId,
        string eventType,
        string? actorId = null,
        string? actorRole = null,
        object? oldState = null,
        object? newState = null,
        Dictionary<string, string>? metadata = null,
        string? outletId = null)
    {
        try
        {
            var entry = new EventLog
            {
                EntityType = entityType,
                EntityId = entityId,
                EventType = eventType,
                ActorId = actorId,
                ActorRole = actorRole,
                OldState = oldState != null ? JsonSerializer.Serialize(oldState, JsonOptions) : null,
                NewState = newState != null ? JsonSerializer.Serialize(newState, JsonOptions) : null,
                Metadata = metadata,
                OutletId = outletId,
                Timestamp = DateTime.UtcNow
            };

            await _eventLogs.InsertOneAsync(entry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log event: {EntityType}/{EntityId}/{EventType}",
                entityType, entityId, eventType);
        }
    }

    /// <summary>
    /// Get the event history for a specific entity, newest first.
    /// </summary>
    public async Task<List<EventLog>> GetEntityEventsAsync(string entityType, string entityId, int limit = 50)
    {
        return await _eventLogs
            .Find(e => e.EntityType == entityType && e.EntityId == entityId)
            .SortByDescending(e => e.Timestamp)
            .Limit(limit)
            .ToListAsync();
    }

    /// <summary>
    /// Get recent events by type (e.g., all recent Order events).
    /// </summary>
    public async Task<List<EventLog>> GetRecentEventsByTypeAsync(string entityType, int limit = 100)
    {
        return await _eventLogs
            .Find(e => e.EntityType == entityType)
            .SortByDescending(e => e.Timestamp)
            .Limit(limit)
            .ToListAsync();
    }
}
