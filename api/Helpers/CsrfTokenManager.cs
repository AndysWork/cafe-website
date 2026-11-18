using System.Security.Cryptography;
using System.Text;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Cafe.Api.Helpers;

/// <summary>
/// Stores user-bound CSRF tokens in the shared database so they survive worker changes and restarts.
/// </summary>
public sealed class CsrfTokenManager
{
    private const int TokenExpiryMinutes = 60;
    private const int MaxTokensPerUser = 10;
    private readonly IMongoCollection<CsrfToken> _tokens;
    private readonly object _indexLock = new();
    private Task? _indexInitialization;

    public CsrfTokenManager(IMongoDatabase database)
    {
        _tokens = database.GetCollection<CsrfToken>("CsrfTokens");
    }

    public async Task<string> GenerateTokenAsync(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        await EnsureIndexesAsync();

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var now = DateTime.UtcNow;
        await _tokens.InsertOneAsync(new CsrfToken
        {
            TokenHash = Hash(token),
            UserId = userId,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(TokenExpiryMinutes)
        });

        var oldestTokens = await _tokens.Find(t => t.UserId == userId)
            .SortByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.TokenHash)
            .Skip(MaxTokensPerUser)
            .ToListAsync();
        if (oldestTokens.Count > 0)
        {
            await _tokens.DeleteManyAsync(Builders<CsrfToken>.Filter.In(
                t => t.TokenHash, oldestTokens.Select(t => t.TokenHash)));
        }

        return token;
    }

    public async Task<bool> ValidateTokenAsync(string token, string userId)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(userId))
            return false;

        return await _tokens.Find(ValidTokenFilter(token, userId)).FirstOrDefaultAsync() != null;
    }

    public async Task<bool> ValidateAndConsumeTokenAsync(string token, string userId)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(userId))
            return false;

        return await _tokens.FindOneAndDeleteAsync(ValidTokenFilter(token, userId)) != null;
    }

    public Task RevokeTokenAsync(string token) =>
        _tokens.DeleteOneAsync(t => t.TokenHash == Hash(token));

    public Task RevokeUserTokensAsync(string userId) =>
        _tokens.DeleteManyAsync(t => t.UserId == userId);

    public Task CleanupExpiredTokensAsync() =>
        _tokens.DeleteManyAsync(t => t.ExpiresAt <= DateTime.UtcNow);

    private static FilterDefinition<CsrfToken> ValidTokenFilter(string token, string userId) =>
        Builders<CsrfToken>.Filter.Eq(t => t.TokenHash, Hash(token))
        & Builders<CsrfToken>.Filter.Eq(t => t.UserId, userId)
        & Builders<CsrfToken>.Filter.Gt(t => t.ExpiresAt, DateTime.UtcNow);

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private Task EnsureIndexesAsync()
    {
        lock (_indexLock)
        {
            if (_indexInitialization == null || _indexInitialization.IsFaulted || _indexInitialization.IsCanceled)
            {
                _indexInitialization = _tokens.Indexes.CreateManyAsync(new[]
                {
                    new CreateIndexModel<CsrfToken>(Builders<CsrfToken>.IndexKeys.Ascending(t => t.ExpiresAt),
                        new CreateIndexOptions { Name = "csrf_token_expiry", ExpireAfter = TimeSpan.Zero }),
                    new CreateIndexModel<CsrfToken>(Builders<CsrfToken>.IndexKeys
                        .Ascending(t => t.UserId).Descending(t => t.CreatedAt).Descending(t => t.TokenHash))
                });
            }
            return _indexInitialization;
        }
    }
}

public sealed class CsrfToken
{
    [BsonId]
    public string TokenHash { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
