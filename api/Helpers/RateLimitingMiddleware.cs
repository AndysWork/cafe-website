using System.Collections.Concurrent;
using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;

namespace Cafe.Api.Helpers;

/// <summary>
/// Rate limiting middleware to prevent abuse and DoS attacks
/// </summary>
public class RateLimitingMiddleware : IFunctionsWorkerMiddleware
{
    private static readonly ConcurrentDictionary<string, RateLimitInfo> _rateLimits = new();
    private static DateTime _lastCleanup = DateTime.UtcNow;
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(10);
    private readonly ILogger<RateLimitingMiddleware> _logger;

    // Configuration
    private const int MaxRequestsPerMinute = 600;
    private const int MaxRequestsPerHour = 10000;
    private const int MaxLoginAttemptsPerHour = 10;
    private const int BlockDurationMinutes = 5;

    public RateLimitingMiddleware(ILogger<RateLimitingMiddleware> logger)
    {
        _logger = logger;
    }

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var requestData = await context.GetHttpRequestDataAsync();
        if (requestData == null)
        {
            await next(context);
            return;
        }

        var clientId = GetClientIdentifier(requestData);
        var endpoint = context.FunctionDefinition.Name;
        var isAuthEndpoint = endpoint.Contains("Login") || endpoint.Contains("Register");

        // Check if client is blocked
        if (IsClientBlocked(clientId))
        {
            _logger.LogWarning($"Blocked request from {clientId} - rate limit exceeded");
            var response = requestData.CreateResponse(HttpStatusCode.TooManyRequests);
            await response.WriteAsJsonAsync(new
            {
                success = false,
                error = "Too many requests. Please try again later.",
                retryAfter = GetBlockTimeRemaining(clientId)
            });
            context.GetInvocationResult().Value = response;
            return;
        }

        // Apply rate limiting
        var rateLimitKey = $"{clientId}:{endpoint}";
        var rateLimit = _rateLimits.GetOrAdd(rateLimitKey, _ => new RateLimitInfo());

        rateLimit.CleanupOldRequests();

        // Check per-minute limit
        if (rateLimit.RequestsInLastMinute >= MaxRequestsPerMinute)
        {
            _logger.LogWarning($"Rate limit exceeded for {clientId} on {endpoint} - per minute");
            BlockClient(clientId);
            var response = requestData.CreateResponse(HttpStatusCode.TooManyRequests);
            await response.WriteAsJsonAsync(new
            {
                success = false,
                error = "Rate limit exceeded. Maximum 60 requests per minute.",
                retryAfter = 60
            });
            context.GetInvocationResult().Value = response;
            return;
        }

        // Check per-hour limit
        if (rateLimit.RequestsInLastHour >= MaxRequestsPerHour)
        {
            _logger.LogWarning($"Rate limit exceeded for {clientId} on {endpoint} - per hour");
            BlockClient(clientId);
            var response = requestData.CreateResponse(HttpStatusCode.TooManyRequests);
            await response.WriteAsJsonAsync(new
            {
                success = false,
                error = "Rate limit exceeded. Maximum 1000 requests per hour.",
                retryAfter = 3600
            });
            context.GetInvocationResult().Value = response;
            return;
        }

        // Special limit for auth endpoints
        if (isAuthEndpoint && rateLimit.RequestsInLastHour >= MaxLoginAttemptsPerHour)
        {
            _logger.LogWarning($"Login rate limit exceeded for {clientId}");
            BlockClient(clientId);
            var response = requestData.CreateResponse(HttpStatusCode.TooManyRequests);
            await response.WriteAsJsonAsync(new
            {
                success = false,
                error = "Too many login attempts. Please try again later.",
                retryAfter = 3600
            });
            context.GetInvocationResult().Value = response;
            return;
        }

        // Record request
        rateLimit.RecordRequest();

        // Periodic cleanup of stale entries to prevent unbounded memory growth
        if (DateTime.UtcNow - _lastCleanup > CleanupInterval)
        {
            _lastCleanup = DateTime.UtcNow;
            CleanupStaleEntries();
        }

        // Continue to next middleware
        await next(context);

        // Add rate limit headers to response
        var httpResponse = context.GetHttpResponseData();
        if (httpResponse != null)
        {
            httpResponse.Headers.Add("X-RateLimit-Limit", MaxRequestsPerMinute.ToString());
            httpResponse.Headers.Add("X-RateLimit-Remaining", (MaxRequestsPerMinute - rateLimit.RequestsInLastMinute).ToString());
            httpResponse.Headers.Add("X-RateLimit-Reset", DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeSeconds().ToString());
        }
    }

    private string GetClientIdentifier(HttpRequestData request)
    {
        // Try to get IP from X-Forwarded-For header (for proxy/load balancer)
        if (request.Headers.TryGetValues("X-Forwarded-For", out var forwardedFor))
        {
            var ip = forwardedFor.First().Split(',')[0].Trim();
            if (!string.IsNullOrEmpty(ip))
                return ip;
        }

        // Try to get IP from X-Real-IP header
        if (request.Headers.TryGetValues("X-Real-IP", out var realIp))
        {
            var ip = realIp.First();
            if (!string.IsNullOrEmpty(ip))
                return ip;
        }

        // Try Azure-specific client IP header
        if (request.Headers.TryGetValues("X-Client-IP", out var clientIp))
        {
            var ip = clientIp.First();
            if (!string.IsNullOrEmpty(ip))
                return ip;
        }

        // Fallback to user ID if authenticated
        if (request.Headers.TryGetValues("Authorization", out var authHeader))
        {
            var token = authHeader.First().Replace("Bearer ", "");
            if (!string.IsNullOrEmpty(token))
                return $"user:{token.GetHashCode()}";
        }

        // Fallback: use a combination of available headers to differentiate clients
        var userAgent = request.Headers.TryGetValues("User-Agent", out var ua) ? ua.First() : "";
        var acceptLang = request.Headers.TryGetValues("Accept-Language", out var al) ? al.First() : "";
        if (!string.IsNullOrEmpty(userAgent))
            return $"anon:{(userAgent + acceptLang).GetHashCode()}";

        return "unknown";
    }

    private bool IsClientBlocked(string clientId)
    {
        var blockKey = $"block:{clientId}";
        if (_rateLimits.TryGetValue(blockKey, out var blockInfo))
        {
            if (blockInfo.BlockedUntil > DateTime.UtcNow)
                return true;

            // Remove expired block
            _rateLimits.TryRemove(blockKey, out _);
        }
        return false;
    }

    private void BlockClient(string clientId)
    {
        var blockKey = $"block:{clientId}";
        var blockInfo = new RateLimitInfo
        {
            BlockedUntil = DateTime.UtcNow.AddMinutes(BlockDurationMinutes)
        };
        _rateLimits.AddOrUpdate(blockKey, blockInfo, (_, __) => blockInfo);
    }

    private int GetBlockTimeRemaining(string clientId)
    {
        var blockKey = $"block:{clientId}";
        if (_rateLimits.TryGetValue(blockKey, out var blockInfo))
        {
            return (int)(blockInfo.BlockedUntil - DateTime.UtcNow).TotalSeconds;
        }
        return 0;
    }

    /// <summary>
    /// Remove entries that have no recent requests (older than 1 hour) to prevent unbounded memory growth.
    /// </summary>
    private void CleanupStaleEntries()
    {
        var staleKeys = new List<string>();
        foreach (var kvp in _rateLimits)
        {
            // Remove expired blocks
            if (kvp.Key.StartsWith("block:") && kvp.Value.BlockedUntil < DateTime.UtcNow)
            {
                staleKeys.Add(kvp.Key);
                continue;
            }

            // Remove rate limit entries with no recent requests
            if (!kvp.Key.StartsWith("block:") && kvp.Value.RequestsInLastHour == 0)
            {
                staleKeys.Add(kvp.Key);
            }
        }

        foreach (var key in staleKeys)
        {
            _rateLimits.TryRemove(key, out _);
        }
    }
}

public class RateLimitInfo
{
    private readonly object _lock = new();
    private readonly List<DateTime> _requests = new();
    public DateTime BlockedUntil { get; set; }

    public int RequestsInLastMinute
    {
        get
        {
            lock (_lock)
            {
                var cutoff = DateTime.UtcNow.AddMinutes(-1);
                return _requests.Count(r => r > cutoff);
            }
        }
    }

    public int RequestsInLastHour
    {
        get
        {
            lock (_lock)
            {
                var cutoff = DateTime.UtcNow.AddHours(-1);
                return _requests.Count(r => r > cutoff);
            }
        }
    }

    public void RecordRequest()
    {
        lock (_lock)
        {
            _requests.Add(DateTime.UtcNow);
        }
    }

    public void CleanupOldRequests()
    {
        lock (_lock)
        {
            var cutoff = DateTime.UtcNow.AddHours(-1);
            _requests.RemoveAll(r => r < cutoff);
        }
    }
}
