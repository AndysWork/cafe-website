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
    private readonly ICustomerJourneyService? _journeys;

    public UserAnalyticsFunction(ILogger<UserAnalyticsFunction> log, MongoService mongo, AuthService auth,
        ICustomerJourneyService? journeys = null)
    {
        _log = log;
        _mongo = mongo;
        _auth = auth;
        _journeys = journeys;
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
            var (body, validationError) = await ValidationHelper.ValidateBody<TrackEventRequest>(req);
            if (validationError != null) return validationError;
            var (session, consentError) = await ConsentedSession(req, body.Journey);
            if (consentError != null) return consentError;

            // Sanitize inputs
            var evt = new UserActivityEvent
            {
                UserId = session!.AccountId,
                Username = session.AccountLabel,
                EventType = InputSanitizer.Sanitize(body.EventType),
                FeatureName = body.FeatureName != null ? InputSanitizer.Sanitize(body.FeatureName) : null,
                Detail = body.Detail != null ? InputSanitizer.Sanitize(body.Detail) : null,
                ResponseTimeMs = body.ResponseTimeMs,
                HttpMethod = body.HttpMethod != null ? InputSanitizer.Sanitize(body.HttpMethod) : null,
                StatusCode = body.StatusCode,
                SessionId = session.Id,
                UserRole = session.UserRole,
                OutletId = session.OutletId,
                ExpiresAt = session.ExpiresAt
            };

            await _mongo.TrackEventAsync(evt);
            await _journeys!.EraseIfRevokedAsync(session.Id, CancellationToken.None);

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
            var (body, validationError) = await ValidationHelper.ValidateBody<TrackBatchRequest>(req);
            if (validationError != null) return validationError;
            var (session, consentError) = await ConsentedSession(req, body.Journey);
            if (consentError != null) return consentError;

            if (body.Events.Count == 0)
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

            var events = body.Events.Select(e => new UserActivityEvent
            {
                UserId = session!.AccountId,
                Username = session.AccountLabel,
                EventType = InputSanitizer.Sanitize(e.EventType),
                FeatureName = e.FeatureName != null ? InputSanitizer.Sanitize(e.FeatureName) : null,
                Detail = e.Detail != null ? InputSanitizer.Sanitize(e.Detail) : null,
                ResponseTimeMs = e.ResponseTimeMs,
                HttpMethod = e.HttpMethod != null ? InputSanitizer.Sanitize(e.HttpMethod) : null,
                StatusCode = e.StatusCode,
                SessionId = session.Id,
                UserRole = session.UserRole,
                OutletId = session.OutletId,
                ExpiresAt = session.ExpiresAt
            }).ToList();

            await _mongo.TrackEventsBatchAsync(events);
            await _journeys!.EraseIfRevokedAsync(session!.Id, CancellationToken.None);

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
            var (body, validationError) = await ValidationHelper.ValidateBody<LegacyJourneySessionRequest>(req);
            if (validationError != null) return validationError;
            var (session, consentError) = await ConsentedSession(req, body.Journey);
            if (consentError != null) return consentError;
            if (session!.AccountId == null)
            {
                var denied = req.CreateResponse(HttpStatusCode.Forbidden);
                await denied.WriteAsJsonAsync(new { error = "Separate account-linking consent is required for account sessions." });
                return denied;
            }
            var sessionId = session.Id;
            var action = body.Action;

            if (action == "end")
            {
                await _mongo.EndSessionAsync(InputSanitizer.Sanitize(sessionId));
            }
            else if (action == "start")
            {
                await _mongo.CreateSessionAsync(
                    session.AccountId, session.AccountLabel ?? "Account",
                    session.UserRole, session.Id, session.ExpiresAt);
            }
            else
            {
                var invalid = req.CreateResponse(HttpStatusCode.BadRequest);
                await invalid.WriteAsJsonAsync(new { error = "Action must be start or end." });
                return invalid;
            }
            await _journeys!.EraseIfRevokedAsync(session.Id, CancellationToken.None);

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
            var (body, validationError) = await ValidationHelper.ValidateBody<LegacyJourneySessionRequest>(req);
            if (validationError != null) return validationError;
            var (session, consentError) = await ConsentedSession(req, body.Journey);
            if (consentError != null) return consentError;
            await _mongo.UpdateSessionActivityAsync(session!.Id);
            await _journeys!.EraseIfRevokedAsync(session.Id, CancellationToken.None);

            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(new { success = true });
            return res;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error updating heartbeat");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "Could not update analytics heartbeat." });
            return res;
        }
    }

    private async Task<(JourneySession? session, HttpResponseData? error)> ConsentedSession(HttpRequestData req, JourneyContext? context)
    {
        try
        {
            if (_journeys == null) throw new InvalidOperationException("Journey consent validation is unavailable.");
            return (await _journeys.ValidateAsync(context, CancellationToken.None), null);
        }
        catch (UnauthorizedAccessException)
        {
            _log.LogWarning("Rejected legacy analytics without a valid consented session");
            var denied = req.CreateResponse(HttpStatusCode.Forbidden);
            await denied.WriteAsJsonAsync(new { error = "A valid, explicitly consented journey session is required." });
            return (null, denied);
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

            // Parse period from query string: daily, weekly, monthly, yearly (default = all)
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var period = query["period"]?.ToLowerInvariant();

            var now = MongoService.GetIstNow();
            DateTime? periodStart = period switch
            {
                "daily" => now.Date,
                "weekly" => now.Date.AddDays(-(int)now.DayOfWeek),
                "monthly" => new DateTime(now.Year, now.Month, 1),
                "yearly" => new DateTime(now.Year, 1, 1),
                _ => null
            };

            var daysForChart = period switch
            {
                "daily" => 1,
                "weekly" => 7,
                "monthly" => DateTime.DaysInMonth(now.Year, now.Month),
                "yearly" => (now - new DateTime(now.Year, 1, 1)).Days + 1,
                _ => 30
            };

            var dashboard = new AnalyticsDashboardResponse
            {
                UserMetrics = await _mongo.GetUserMetricsAsync(),
                TopFeatures = await _mongo.GetTopFeaturesAsync(15, periodStart),
                ApiPerformance = await _mongo.GetApiPerformanceAsync(20, periodStart),
                CartAnalytics = await _mongo.GetCartAnalyticsAsync(periodStart),
                DailyActiveUsers = await _mongo.GetDailyActiveUsersAsync(daysForChart),
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

