using System.Security.Cryptography;
using System.Text;
using Cafe.Api.Models;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Cafe.Api.Services;

public class IdempotencyService
{
    private readonly IMongoCollection<IdempotencyRecord> _records;
    private readonly ILogger<IdempotencyService> _logger;

    public IdempotencyService(IMongoDatabase database, ILogger<IdempotencyService> logger)
    {
        _records = database.GetCollection<IdempotencyRecord>("IdempotencyRecords");
        _logger = logger;
        EnsureIndexes();
    }

    private void EnsureIndexes()
    {
        try
        {
            var indexes = new[]
            {
                new CreateIndexModel<IdempotencyRecord>(
                    Builders<IdempotencyRecord>.IndexKeys.Combine(
                        Builders<IdempotencyRecord>.IndexKeys.Ascending(x => x.Key),
                        Builders<IdempotencyRecord>.IndexKeys.Ascending(x => x.Action),
                        Builders<IdempotencyRecord>.IndexKeys.Ascending(x => x.UserId)),
                    new CreateIndexOptions { Name = "ux_idempotency_key_action_user", Unique = true }),
                new CreateIndexModel<IdempotencyRecord>(
                    Builders<IdempotencyRecord>.IndexKeys.Ascending(x => x.ExpiresAt),
                    new CreateIndexOptions { Name = "ix_idempotency_ttl", ExpireAfter = TimeSpan.Zero })
            };

            _records.Indexes.CreateMany(indexes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create idempotency indexes");
        }
    }

    public static string ComputeRequestHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }

    public async Task<IdempotencyStartResult> TryBeginAsync(string key, string action, string userId, string requestHash, int ttlMinutes = 15)
    {
        var now = DateTime.UtcNow;
        var record = new IdempotencyRecord
        {
            Key = key,
            Action = action,
            UserId = userId,
            RequestHash = requestHash,
            Status = "in-progress",
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = now.AddMinutes(ttlMinutes)
        };

        try
        {
            await _records.InsertOneAsync(record);
            return new IdempotencyStartResult { CanExecute = true };
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            var existing = await _records.Find(x => x.Key == key && x.Action == action && x.UserId == userId).FirstOrDefaultAsync();
            if (existing == null)
            {
                return new IdempotencyStartResult { CanExecute = false, IsInProgress = true };
            }

            if (!string.Equals(existing.RequestHash, requestHash, StringComparison.OrdinalIgnoreCase))
            {
                return new IdempotencyStartResult { CanExecute = false, IsConflict = true };
            }

            if (existing.Status == "completed" && existing.ResponseStatusCode.HasValue)
            {
                return new IdempotencyStartResult
                {
                    CanExecute = false,
                    ReplayStatusCode = existing.ResponseStatusCode.Value,
                    ReplayBody = existing.ResponseBody
                };
            }

            return new IdempotencyStartResult { CanExecute = false, IsInProgress = true };
        }
    }

    public async Task MarkCompletedAsync(string key, string action, string userId, int statusCode, string responseBody)
    {
        await _records.UpdateOneAsync(
            x => x.Key == key && x.Action == action && x.UserId == userId,
            Builders<IdempotencyRecord>.Update
                .Set(x => x.Status, "completed")
                .Set(x => x.ResponseStatusCode, statusCode)
                .Set(x => x.ResponseBody, responseBody)
                .Set(x => x.UpdatedAt, DateTime.UtcNow));
    }

    public async Task MarkFailedAsync(string key, string action, string userId, string error)
    {
        await _records.UpdateOneAsync(
            x => x.Key == key && x.Action == action && x.UserId == userId,
            Builders<IdempotencyRecord>.Update
                .Set(x => x.Status, "failed")
                .Set(x => x.Error, error)
                .Set(x => x.UpdatedAt, DateTime.UtcNow));
    }
}

public class IdempotencyStartResult
{
    public bool CanExecute { get; set; }
    public bool IsConflict { get; set; }
    public bool IsInProgress { get; set; }
    public int? ReplayStatusCode { get; set; }
    public string? ReplayBody { get; set; }
}
