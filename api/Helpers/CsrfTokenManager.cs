using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Cafe.Api.Helpers;

/// <summary>
/// CSRF token management for preventing Cross-Site Request Forgery attacks
/// </summary>
public class CsrfTokenManager
{
    private static readonly ConcurrentDictionary<string, CsrfToken> _tokens = new();
    private const int TokenExpiryMinutes = 60;
    private const int MaxTokensPerUser = 10;

    /// <summary>
    /// Generates a new CSRF token for a user
    /// </summary>
    public static string GenerateToken(string userId)
    {
        // Generate cryptographically secure random token
        var tokenBytes = new byte[32];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(tokenBytes);
        }
        var token = Convert.ToBase64String(tokenBytes);

        // Store token with expiry
        var csrfToken = new CsrfToken
        {
            Token = token,
            UserId = userId,
            ExpiresAt = DateTime.UtcNow.AddMinutes(TokenExpiryMinutes),
            CreatedAt = DateTime.UtcNow
        };

        _tokens.AddOrUpdate(token, csrfToken, (_, __) => csrfToken);

        // Cleanup old tokens for this user
        CleanupUserTokens(userId);

        return token;
    }

    /// <summary>
    /// Validates a CSRF token
    /// </summary>
    public static bool ValidateToken(string token, string userId)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(userId))
            return false;

        if (!_tokens.TryGetValue(token, out var csrfToken))
            return false;

        // Check if token belongs to user
        if (csrfToken.UserId != userId)
            return false;

        // Check if token is expired
        if (csrfToken.ExpiresAt < DateTime.UtcNow)
        {
            _tokens.TryRemove(token, out _);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Validates and consumes a CSRF token (one-time use)
    /// </summary>
    public static bool ValidateAndConsumeToken(string token, string userId)
    {
        if (!ValidateToken(token, userId))
            return false;

        // Remove token after validation (one-time use)
        _tokens.TryRemove(token, out _);
        return true;
    }

    /// <summary>
    /// Removes a specific token
    /// </summary>
    public static void RevokeToken(string token)
    {
        _tokens.TryRemove(token, out _);
    }

    /// <summary>
    /// Removes all tokens for a user
    /// </summary>
    public static void RevokeUserTokens(string userId)
    {
        var userTokens = _tokens.Where(kvp => kvp.Value.UserId == userId).ToList();
        foreach (var kvp in userTokens)
        {
            _tokens.TryRemove(kvp.Key, out _);
        }
    }

    /// <summary>
    /// Cleanup expired tokens and limit tokens per user
    /// </summary>
    private static void CleanupUserTokens(string userId)
    {
        // Remove expired tokens
        var expiredTokens = _tokens.Where(kvp => kvp.Value.ExpiresAt < DateTime.UtcNow).ToList();
        foreach (var kvp in expiredTokens)
        {
            _tokens.TryRemove(kvp.Key, out _);
        }

        // Limit tokens per user
        var userTokens = _tokens.Where(kvp => kvp.Value.UserId == userId)
            .OrderBy(kvp => kvp.Value.CreatedAt)
            .ToList();

        if (userTokens.Count > MaxTokensPerUser)
        {
            var tokensToRemove = userTokens.Take(userTokens.Count - MaxTokensPerUser);
            foreach (var kvp in tokensToRemove)
            {
                _tokens.TryRemove(kvp.Key, out _);
            }
        }
    }

    /// <summary>
    /// Periodic cleanup of expired tokens (call from background task)
    /// </summary>
    public static void CleanupExpiredTokens()
    {
        var expiredTokens = _tokens.Where(kvp => kvp.Value.ExpiresAt < DateTime.UtcNow).ToList();
        foreach (var kvp in expiredTokens)
        {
            _tokens.TryRemove(kvp.Key, out _);
        }
    }
}

public class CsrfToken
{
    public string Token { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
