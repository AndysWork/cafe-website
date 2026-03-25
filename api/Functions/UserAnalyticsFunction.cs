using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using System.Net;
using Cafe.Api.Helpers;
using Cafe.Api.Models;
using Cafe.Api.Services;

namespace Cafe.Api.Functions;

public class UserAnalyticsFunction
{
    private readonly ILogger<UserAnalyticsFunction> _log;
    private readonly MongoService _mongo;
    private readonly AuthService _auth;

    public UserAnalyticsFunction(ILogger<UserAnalyticsFunction> log, MongoService mongo, AuthService auth)
    {
        _log = log;
        _mongo = mongo;
        _auth = auth;
    }

    /// <summary>
    /// Track a single user activity event (called from frontend)
    /// </summary>
    [Function("TrackUserEvent")]
    [OpenApiOperation(operationId: "TrackUserEvent", tags: new[] { "UserAnalytics" })]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    public async Task<HttpResponseData> TrackEvent(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "analytics/track")] HttpRequestData req)
    {
        try
        {
            // Auth is optional — anonymous browsing is still tracked
            string? userId = null;
            string? username = null;
            string? role = null;

            var token = req.Headers.Contains("Authorization")
                ? req.Headers.GetValues("Authorization").FirstOrDefault()?.Replace("Bearer ", "")
                : null;

            if (!string.IsNullOrEmpty(token))
            {
                var principal = _auth.ValidateToken(token);
                if (principal != null)
                {
                    userId = _auth.GetUserIdFromToken(token);
                    role = _auth.GetRoleFromToken(token);
                    username = principal.Identity?.Name;
                }
            }

            var body = await req.ReadFromJsonAsync<TrackEventRequest>();
            if (body == null || string.IsNullOrWhiteSpace(body.EventType))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "EventType is required" });
                return badReq;
            }

            // Sanitize inputs
            var evt = new UserActivityEvent
            {
                UserId = userId,
                Username = username,
                EventType = InputSanitizer.Sanitize(body.EventType),
                FeatureName = body.FeatureName != null ? InputSanitizer.Sanitize(body.FeatureName) : null,
                Detail = body.Detail != null ? InputSanitizer.Sanitize(body.Detail) : null,
                ResponseTimeMs = body.ResponseTimeMs,
                HttpMethod = body.HttpMethod != null ? InputSanitizer.Sanitize(body.HttpMethod) : null,
                StatusCode = body.StatusCode,
                SessionId = body.SessionId != null ? InputSanitizer.Sanitize(body.SessionId) : null,
                UserRole = role,
                OutletId = req.Headers.Contains("X-Outlet-Id")
                    ? req.Headers.GetValues("X-Outlet-Id").FirstOrDefault()
                    : null
            };

            await _mongo.TrackEventAsync(evt);

            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(new { success = true });
            return res;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error tracking user event");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "Failed to track event" });
            return res;
        }
    }

    /// <summary>
    /// Track a batch of events (more efficient for API response time tracking)
    /// </summary>
    [Function("TrackUserEventsBatch")]
    [OpenApiOperation(operationId: "TrackUserEventsBatch", tags: new[] { "UserAnalytics" })]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    public async Task<HttpResponseData> TrackEventsBatch(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "analytics/track/batch")] HttpRequestData req)
    {
        try
        {
            string? userId = null;
            string? username = null;
            string? role = null;

            var token = req.Headers.Contains("Authorization")
                ? req.Headers.GetValues("Authorization").FirstOrDefault()?.Replace("Bearer ", "")
                : null;

            if (!string.IsNullOrEmpty(token))
            {
                var principal = _auth.ValidateToken(token);
                if (principal != null)
                {
                    userId = _auth.GetUserIdFromToken(token);
                    role = _auth.GetRoleFromToken(token);
                    username = principal.Identity?.Name;
                }
            }

            var body = await req.ReadFromJsonAsync<TrackBatchRequest>();
            if (body == null || body.Events.Count == 0)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Events array is required" });
                return badReq;
            }

            // Limit batch size to prevent abuse
            if (body.Events.Count > 50)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Maximum 50 events per batch" });
                return badReq;
            }

            var outletId = req.Headers.Contains("X-Outlet-Id")
                ? req.Headers.GetValues("X-Outlet-Id").FirstOrDefault()
                : null;

            var events = body.Events.Select(e => new UserActivityEvent
            {
                UserId = userId,
                Username = username,
                EventType = InputSanitizer.Sanitize(e.EventType),
                FeatureName = e.FeatureName != null ? InputSanitizer.Sanitize(e.FeatureName) : null,
                Detail = e.Detail != null ? InputSanitizer.Sanitize(e.Detail) : null,
                ResponseTimeMs = e.ResponseTimeMs,
                HttpMethod = e.HttpMethod != null ? InputSanitizer.Sanitize(e.HttpMethod) : null,
                StatusCode = e.StatusCode,
                SessionId = e.SessionId != null ? InputSanitizer.Sanitize(e.SessionId) : null,
                UserRole = role,
                OutletId = outletId
            }).ToList();

            await _mongo.TrackEventsBatchAsync(events);

            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(new { success = true, tracked = events.Count });
            return res;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error tracking batch events");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "Failed to track events" });
            return res;
        }
    }

    /// <summary>
    /// Record user login session
    /// </summary>
    [Function("TrackUserSession")]
    [OpenApiOperation(operationId: "TrackUserSession", tags: new[] { "UserAnalytics" })]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    public async Task<HttpResponseData> TrackSession(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "analytics/session")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, role, errorResponse) =
                await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var body = await req.ReadFromJsonAsync<Dictionary<string, string>>();
            var sessionId = body?.GetValueOrDefault("sessionId") ?? Guid.NewGuid().ToString();
            var action = body?.GetValueOrDefault("action") ?? "start";

            if (action == "end")
            {
                await _mongo.EndSessionAsync(InputSanitizer.Sanitize(sessionId));
            }
            else
            {
                var user = await _mongo.GetUserByIdAsync(userId!);
                await _mongo.CreateSessionAsync(
                    userId!,
                    user?.Username ?? "unknown",
                    role ?? "user",
                    InputSanitizer.Sanitize(sessionId));
            }

            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(new { success = true, sessionId });
            return res;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error tracking session");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "Failed to track session" });
            return res;
        }
    }

    /// <summary>
    /// Update session heartbeat
    /// </summary>
    [Function("HeartbeatSession")]
    [OpenApiOperation(operationId: "HeartbeatSession", tags: new[] { "UserAnalytics" })]
    public async Task<HttpResponseData> Heartbeat(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "analytics/heartbeat")] HttpRequestData req)
    {
        try
        {
            var body = await req.ReadFromJsonAsync<Dictionary<string, string>>();
            var sessionId = body?.GetValueOrDefault("sessionId");

            if (!string.IsNullOrEmpty(sessionId))
            {
                await _mongo.UpdateSessionActivityAsync(InputSanitizer.Sanitize(sessionId));
            }

            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(new { success = true });
            return res;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error updating heartbeat");
            var res = req.CreateResponse(HttpStatusCode.OK); // Don't fail on heartbeat
            return res;
        }
    }

    // ─── Admin-only analytics dashboard endpoints ───

    /// <summary>
    /// Get full analytics dashboard data (admin only)
    /// </summary>
    [Function("GetAnalyticsDashboard")]
    [OpenApiOperation(operationId: "GetAnalyticsDashboard", tags: new[] { "UserAnalytics" })]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    public async Task<HttpResponseData> GetDashboard(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "analytics/dashboard")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) =
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var dashboard = new AnalyticsDashboardResponse
            {
                UserMetrics = await _mongo.GetUserMetricsAsync(),
                TopFeatures = await _mongo.GetTopFeaturesAsync(),
                ApiPerformance = await _mongo.GetApiPerformanceAsync(),
                CartAnalytics = await _mongo.GetCartAnalyticsAsync(),
                DailyActiveUsers = await _mongo.GetDailyActiveUsersAsync(),
                HourlyActivity = await _mongo.GetHourlyActivityAsync(),
                RecentSessions = await _mongo.GetRecentSessionsAsync()
            };

            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(new { success = true, data = dashboard });
            return res;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error fetching analytics dashboard");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "Failed to load analytics dashboard" });
            return res;
        }
    }

    /// <summary>
    /// Get recent user sessions (admin only)
    /// </summary>
    [Function("GetUserSessions")]
    [OpenApiOperation(operationId: "GetUserSessions", tags: new[] { "UserAnalytics" })]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    public async Task<HttpResponseData> GetSessions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "analytics/sessions")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) =
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var sessions = await _mongo.GetRecentSessionsAsync(100);

            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(new { success = true, data = sessions });
            return res;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error fetching sessions");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "Failed to load sessions" });
            return res;
        }
    }

    /// <summary>
    /// Initialize analytics indexes (admin only, run once)
    /// </summary>
    [Function("InitAnalyticsIndexes")]
    [OpenApiOperation(operationId: "InitAnalyticsIndexes", tags: new[] { "UserAnalytics" })]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    public async Task<HttpResponseData> InitIndexes(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "analytics/init-indexes")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) =
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            await _mongo.EnsureAnalyticsIndexesAsync();

            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(new { success = true, message = "Analytics indexes created" });
            return res;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error creating analytics indexes");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "Failed to create indexes" });
            return res;
        }
    }
}
