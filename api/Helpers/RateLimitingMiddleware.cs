using System.Collections.Concurrent;
using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;

namespace Cafe.Api.Helpers;

/// <summary>
/// Tiered rate limiting middleware. Endpoints are classified into tiers with different limits:
///   Auth          — 10/min,  30/hr   (Login, Register, ResetPassword)
///   AdminWrite    — 60/min,  600/hr  (Create*, Update*, Delete*, Upload*, Migrate*, Initialize*)
///   ExportReport  — 20/min,  200/hr  (Export*, Report*, Backup*, Forecast*, Analytics*)
///   PublicRead    — 300/min, 5000/hr (default — all GET / read endpoints)
/// </summary>
public class RateLimitingMiddleware : IFunctionsWorkerMiddleware
{
    private static readonly ConcurrentDictionary<string, RateLimitInfo> _rateLimits = new();
    private static DateTime _lastCleanup = DateTime.UtcNow;
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(10);
    private readonly ILogger<RateLimitingMiddleware> _logger;

    private const int BlockDurationMinutes = 5;

    public RateLimitingMiddleware(ILogger<RateLimitingMiddleware> logger)
    {
        _logger = logger;
    }

    // ── Endpoint tier classification ─────────────────────────────────────

    private enum EndpointTier { Auth, AdminWrite, ExportReport, PublicRead }

    private static readonly (int PerMinute, int PerHour)[] TierLimits = new[]
    {
        (  10,    30),   // Auth
        (  60,   600),   // AdminWrite
        (  20,   200),   // ExportReport
        ( 300,  5000),   // PublicRead
    };

    private static readonly string[] AuthKeywords = { "Login", "Register", "ResetPassword", "ChangePassword", "RefreshToken" };
    private static readonly string[] ExportKeywords = { "Export", "Report", "Backup", "Forecast", "Analytics", "Performance", "Reconciliation", "PublicStats" };
    private static readonly string[] WriteKeywords = { "Create", "Update", "Delete", "Upload", "Add", "Remove", "Set", "Migrate", "Initialize", "Approve", "Reject", "Assign", "Redeem", "Adjust" };

    private static EndpointTier ClassifyEndpoint(string functionName)
    {
        if (AuthKeywords.Any(k => functionName.Contains(k, StringComparison.OrdinalIgnoreCase)))
            return EndpointTier.Auth;

        if (ExportKeywords.Any(k => functionName.Contains(k, StringComparison.OrdinalIgnoreCase)))
            return EndpointTier.ExportReport;

        if (WriteKeywords.Any(k => functionName.Contains(k, StringComparison.OrdinalIgnoreCase)))
            return EndpointTier.AdminWrite;

        return EndpointTier.PublicRead;
    }

    // ── Main pipeline ────────────────────────────────────────────────────

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
        var tier = ClassifyEndpoint(endpoint);
        var (limitPerMinute, limitPerHour) = TierLimits[(int)tier];

        // Check if client is blocked
        if (IsClientBlocked(clientId))
        {
            _logger.LogWarning("Blocked request from {ClientId} — rate limit exceeded", clientId);
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

        // Use a per-client + per-tier key so limits accumulate across endpoints in the same tier
        var rateLimitKey = $"{clientId}:{tier}";
        var rateLimit = _rateLimits.GetOrAdd(rateLimitKey, _ => new RateLimitInfo());
        rateLimit.CleanupOldRequests();

        // Per-minute check
        if (rateLimit.RequestsInLastMinute >= limitPerMinute)
        {
            _logger.LogWarning("Rate limit exceeded ({Tier}) for {ClientId} on {Endpoint} — {Limit}/min",
                tier, clientId, endpoint, limitPerMinute);
            BlockClient(clientId);
            var response = requestData.CreateResponse(HttpStatusCode.TooManyRequests);
            await response.WriteAsJsonAsync(new
            {
                success = false,
                error = $"Rate limit exceeded. Maximum {limitPerMinute} requests per minute for this endpoint category.",
                retryAfter = 60
            });
            context.GetInvocationResult().Value = response;
            return;
        }

        // Per-hour check
        if (rateLimit.RequestsInLastHour >= limitPerHour)
        {
            _logger.LogWarning("Rate limit exceeded ({Tier}) for {ClientId} on {Endpoint} — {Limit}/hr",
                tier, clientId, endpoint, limitPerHour);
            BlockClient(clientId);
            var response = requestData.CreateResponse(HttpStatusCode.TooManyRequests);
            await response.WriteAsJsonAsync(new
            {
                success = false,
                error = $"Rate limit exceeded. Maximum {limitPerHour} requests per hour for this endpoint category.",
                retryAfter = 3600
            });
            context.GetInvocationResult().Value = response;
            return;
        }

        rateLimit.RecordRequest();

        // Periodic cleanup
        if (DateTime.UtcNow - _lastCleanup > CleanupInterval)
        {
            _lastCleanup = DateTime.UtcNow;
            CleanupStaleEntries();
        }

        await next(context);

        // Rate limit response headers
        var httpResponse = context.GetHttpResponseData();
        if (httpResponse != null)
        {
            httpResponse.Headers.Add("X-RateLimit-Limit", limitPerMinute.ToString());
            httpResponse.Headers.Add("X-RateLimit-Remaining",
                Math.Max(0, limitPerMinute - rateLimit.RequestsInLastMinute).ToString());
            httpResponse.Headers.Add("X-RateLimit-Reset",
                DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeSeconds().ToString());
            httpResponse.Headers.Add("X-RateLimit-Tier", tier.ToString());
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
