using Cafe.Api.Models;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Cafe.Api.Services;

/// <summary>
/// Manages the transactional outbox for reliable side-effect delivery.
/// Side effects (notifications, loyalty points, emails) are written as outbox messages
/// alongside the primary operation, then processed asynchronously by OutboxProcessorFunction.
/// This replaces fire-and-forget Task.Run patterns, ensuring eventual consistency.
/// </summary>
public class OutboxService
{
    private readonly IMongoCollection<OutboxMessage> _outbox;
    private readonly ILogger<OutboxService> _logger;

    public OutboxService(IMongoDatabase database, ILogger<OutboxService> logger)
    {
        _outbox = database.GetCollection<OutboxMessage>("OutboxMessages");
        _logger = logger;
        EnsureIndexes();
    }

    private void EnsureIndexes()
    {
        try
        {
            var indexes = new[]
            {
                new CreateIndexModel<OutboxMessage>(
                    Builders<OutboxMessage>.IndexKeys.Combine(
                        Builders<OutboxMessage>.IndexKeys.Ascending(m => m.Status),
                        Builders<OutboxMessage>.IndexKeys.Ascending(m => m.NextRetryAt)),
                    new CreateIndexOptions { Name = "ix_pending_messages" }),
                new CreateIndexModel<OutboxMessage>(
                    Builders<OutboxMessage>.IndexKeys.Combine(
                        Builders<OutboxMessage>.IndexKeys.Ascending(m => m.AggregateType),
                        Builders<OutboxMessage>.IndexKeys.Ascending(m => m.AggregateId)),
                    new CreateIndexOptions { Name = "ix_aggregate" }),
                new CreateIndexModel<OutboxMessage>(
                    Builders<OutboxMessage>.IndexKeys.Ascending(m => m.CreatedAt),
                    new CreateIndexOptions { Name = "ix_created", ExpireAfter = TimeSpan.FromDays(30) }),
                new CreateIndexModel<OutboxMessage>(
                    Builders<OutboxMessage>.IndexKeys.Combine(
                        Builders<OutboxMessage>.IndexKeys.Ascending(m => m.Status),
                        Builders<OutboxMessage>.IndexKeys.Descending(m => m.CreatedAt)),
                    new CreateIndexOptions { Name = "ix_status_created" })
            };
            _outbox.Indexes.CreateMany(indexes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create OutboxMessage indexes");
        }
    }

    public async Task EnqueueAsync(string eventType, string aggregateType, string aggregateId, object payload)
    {
        var message = new OutboxMessage
        {
            EventType = eventType,
            AggregateType = aggregateType,
            AggregateId = aggregateId,
            Payload = System.Text.Json.JsonSerializer.Serialize(payload),
            Status = "pending",
            RetryCount = 0,
            CreatedAt = DateTime.UtcNow,
            NextRetryAt = DateTime.UtcNow
        };

        await _outbox.InsertOneAsync(message);
    }

    public async Task<List<OutboxMessage>> GetPendingMessagesAsync(int batchSize = 25)
    {
        var filter = Builders<OutboxMessage>.Filter.And(
            Builders<OutboxMessage>.Filter.In(m => m.Status, new[] { "pending", "failed" }),
            Builders<OutboxMessage>.Filter.Lte(m => m.NextRetryAt, DateTime.UtcNow),
            Builders<OutboxMessage>.Filter.Where(m => m.RetryCount < m.MaxRetries)
        );

        return await _outbox
            .Find(filter)
            .SortBy(m => m.NextRetryAt)
            .Limit(batchSize)
            .ToListAsync();
    }

    public async Task<bool> MarkProcessingAsync(string messageId)
    {
        var result = await _outbox.UpdateOneAsync(
            m => m.Id == messageId
                 && (m.Status == "pending" || m.Status == "failed")
                 && m.RetryCount < m.MaxRetries,
            Builders<OutboxMessage>.Update
                .Set(m => m.Status, "processing")
                .Set(m => m.LastAttemptAt, DateTime.UtcNow));
        return result.ModifiedCount > 0;
    }

    public async Task MarkCompletedAsync(string messageId)
    {
        await _outbox.UpdateOneAsync(
            m => m.Id == messageId,
            Builders<OutboxMessage>.Update
                .Set(m => m.Status, "completed")
                .Set(m => m.ProcessedAt, DateTime.UtcNow)
                .Set(m => m.LastAttemptAt, DateTime.UtcNow));
    }

    public async Task MarkFailedAsync(string messageId, string error)
    {
        var message = await _outbox.Find(m => m.Id == messageId).FirstOrDefaultAsync();
        if (message == null)
        {
            return;
        }

        var retryCount = message.RetryCount + 1;
        // Exponential backoff: 30s, 2m, 8m, 32m, ~2h
        var nextRetry = DateTime.UtcNow.AddSeconds(30 * Math.Pow(4, retryCount - 1));

        var status = retryCount >= (message?.MaxRetries ?? 5) ? "dead-letter" : "failed";

        await _outbox.UpdateOneAsync(
            m => m.Id == messageId,
            Builders<OutboxMessage>.Update
                .Set(m => m.Status, status)
                .Set(m => m.Error, error)
                .Inc(m => m.RetryCount, 1)
                .Set(m => m.NextRetryAt, status == "failed" ? nextRetry : null)
                .Set(m => m.LastAttemptAt, DateTime.UtcNow)
                .Set(m => m.DeadLetteredAt, status == "dead-letter" ? DateTime.UtcNow : null));
    }

    public async Task<List<OutboxMessage>> GetDeadLetterMessagesAsync(int limit = 100)
    {
        var safeLimit = Math.Clamp(limit, 1, 500);
        return await _outbox.Find(m => m.Status == "dead-letter")
            .SortByDescending(m => m.DeadLetteredAt)
            .Limit(safeLimit)
            .ToListAsync();
    }

    public async Task<OutboxHealthSummary> GetHealthSummaryAsync()
    {
        var now = DateTime.UtcNow;
        var fifteenMinutesAgo = now.AddMinutes(-15);

        var pending = await _outbox.CountDocumentsAsync(m => m.Status == "pending");
        var processing = await _outbox.CountDocumentsAsync(m => m.Status == "processing");
        var retrying = await _outbox.CountDocumentsAsync(m => m.Status == "failed");
        var deadLetter = await _outbox.CountDocumentsAsync(m => m.Status == "dead-letter");
        var completedRecent = await _outbox.CountDocumentsAsync(m => m.Status == "completed" && m.ProcessedAt >= fifteenMinutesAgo);

        return new OutboxHealthSummary
        {
            PendingCount = pending,
            ProcessingCount = processing,
            RetryingCount = retrying,
            DeadLetterCount = deadLetter,
            CompletedLast15Minutes = completedRecent,
            CapturedAtUtc = now
        };
    }

    /// <summary>
    /// Remove completed messages older than 7 days to prevent unbounded growth.
    /// </summary>
    public async Task<long> CleanupOldMessagesAsync()
    {
        var cutoff = DateTime.UtcNow.AddDays(-7);
        var filter = Builders<OutboxMessage>.Filter.And(
            Builders<OutboxMessage>.Filter.Eq(m => m.Status, "completed"),
            Builders<OutboxMessage>.Filter.Lt(m => m.ProcessedAt, cutoff)
        );
        var result = await _outbox.DeleteManyAsync(filter);
        return result.DeletedCount;
    }
}

public class OutboxHealthSummary
{
    public long PendingCount { get; set; }
    public long ProcessingCount { get; set; }
    public long RetryingCount { get; set; }
    public long DeadLetterCount { get; set; }
    public long CompletedLast15Minutes { get; set; }
    public DateTime CapturedAtUtc { get; set; }
}
