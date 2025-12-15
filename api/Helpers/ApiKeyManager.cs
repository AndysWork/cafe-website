using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Cafe.Api.Helpers;

/// <summary>
/// API Key management with rotation support
/// </summary>
public class ApiKeyManager
{
    private static readonly ConcurrentDictionary<string, ApiKey> _apiKeys = new();
    private const int KeyExpiryDays = 90;
    private const int RotationWarningDays = 7;

    /// <summary>
    /// Generates a new API key
    /// </summary>
    public static string GenerateApiKey(string serviceName, string description = "")
    {
        // Generate cryptographically secure random key
        var keyBytes = new byte[32];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(keyBytes);
        }
        var key = $"cafe_{Convert.ToBase64String(keyBytes).Replace("+", "").Replace("/", "").Replace("=", "").Substring(0, 32)}";

        var apiKey = new ApiKey
        {
            Key = key,
            ServiceName = serviceName,
            Description = description,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(KeyExpiryDays),
            IsActive = true,
            LastUsedAt = null,
            RequestCount = 0
        };

        _apiKeys.AddOrUpdate(key, apiKey, (_, __) => apiKey);

        return key;
    }

    /// <summary>
    /// Validates an API key
    /// </summary>
    public static (bool isValid, ApiKey? apiKey) ValidateApiKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return (false, null);

        if (!_apiKeys.TryGetValue(key, out var apiKey))
            return (false, null);

        // Check if key is active
        if (!apiKey.IsActive)
            return (false, apiKey);

        // Check if key is expired
        if (apiKey.ExpiresAt < DateTime.UtcNow)
        {
            apiKey.IsActive = false;
            return (false, apiKey);
        }

        // Update last used time and request count
        apiKey.LastUsedAt = DateTime.UtcNow;
        apiKey.RequestCount++;

        return (true, apiKey);
    }

    /// <summary>
    /// Rotates an API key (creates new key and marks old one for deprecation)
    /// </summary>
    public static (string newKey, DateTime deprecationDate) RotateApiKey(string oldKey)
    {
        if (!_apiKeys.TryGetValue(oldKey, out var oldApiKey))
            throw new InvalidOperationException("API key not found");

        // Generate new key
        var newKey = GenerateApiKey(oldApiKey.ServiceName, $"Rotated from {oldKey.Substring(0, 10)}...");

        // Mark old key for deprecation (30 days grace period)
        oldApiKey.DeprecatedAt = DateTime.UtcNow;
        oldApiKey.ExpiresAt = DateTime.UtcNow.AddDays(30);

        return (newKey, oldApiKey.ExpiresAt);
    }

    /// <summary>
    /// Revokes an API key immediately
    /// </summary>
    public static void RevokeApiKey(string key)
    {
        if (_apiKeys.TryGetValue(key, out var apiKey))
        {
            apiKey.IsActive = false;
            apiKey.RevokedAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Gets all API keys (for admin purposes)
    /// </summary>
    public static List<ApiKey> GetAllApiKeys()
    {
        return _apiKeys.Values.OrderByDescending(k => k.CreatedAt).ToList();
    }

    /// <summary>
    /// Gets API keys that need rotation soon
    /// </summary>
    public static List<ApiKey> GetKeysNeedingRotation()
    {
        var warningDate = DateTime.UtcNow.AddDays(RotationWarningDays);
        return _apiKeys.Values
            .Where(k => k.IsActive && k.ExpiresAt <= warningDate)
            .ToList();
    }

    /// <summary>
    /// Cleanup expired and revoked keys
    /// </summary>
    public static void CleanupExpiredKeys()
    {
        var expiredKeys = _apiKeys.Where(kvp =>
            !kvp.Value.IsActive ||
            kvp.Value.ExpiresAt < DateTime.UtcNow.AddDays(-30) // Keep for 30 days after expiry
        ).ToList();

        foreach (var kvp in expiredKeys)
        {
            _apiKeys.TryRemove(kvp.Key, out _);
        }
    }

    /// <summary>
    /// Gets API key statistics
    /// </summary>
    public static ApiKeyStatistics GetStatistics(string key)
    {
        if (!_apiKeys.TryGetValue(key, out var apiKey))
            throw new InvalidOperationException("API key not found");

        return new ApiKeyStatistics
        {
            Key = key.Substring(0, 10) + "...",
            ServiceName = apiKey.ServiceName,
            CreatedAt = apiKey.CreatedAt,
            ExpiresAt = apiKey.ExpiresAt,
            LastUsedAt = apiKey.LastUsedAt,
            RequestCount = apiKey.RequestCount,
            IsActive = apiKey.IsActive,
            DaysUntilExpiry = (apiKey.ExpiresAt - DateTime.UtcNow).Days,
            NeedsRotation = apiKey.ExpiresAt <= DateTime.UtcNow.AddDays(RotationWarningDays)
        };
    }
}

public class ApiKey
{
    public string Key { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? DeprecatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public bool IsActive { get; set; }
    public long RequestCount { get; set; }
}

public class ApiKeyStatistics
{
    public string Key { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public long RequestCount { get; set; }
    public bool IsActive { get; set; }
    public int DaysUntilExpiry { get; set; }
    public bool NeedsRotation { get; set; }
}
