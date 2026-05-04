using System.Net;
using System.Text.Json;
using Cafe.Api.Helpers;
using Cafe.Api.Models;
using Cafe.Api.Services;
using Cafe.Api.Repositories;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Microsoft.OpenApi.Models;

namespace Cafe.Api.Functions;

public class NotificationFunction
{
    private readonly INotificationRepository _mongo;
    private readonly AuthService _auth;
    private readonly ILogger<NotificationFunction> _log;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public NotificationFunction(INotificationRepository mongo, AuthService auth, ILogger<NotificationFunction> log)
    {
        _mongo = mongo;
        _auth = auth;
        _log = log;
    }

    /// <summary>
    /// Get paginated list of notifications for the authenticated user.
    /// </summary>
    [Function("GetNotifications")]
    [OpenApiOperation(operationId: "GetNotifications", tags: new[] { "Notifications" })]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(NotificationListResponse))]
    public async Task<HttpResponseData> GetNotifications(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "notifications")] HttpRequestData req)
    {
        var (isAuth, userId, _, authError) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
        if (string.IsNullOrEmpty(userId))
        {
            var unauth = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauth.WriteAsJsonAsync(new { error = "Authentication required" });
            return unauth;
        }

        var page = 1;
        var pageSize = 20;
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        if (int.TryParse(query["page"], out var p) && p > 0) page = p;
        if (int.TryParse(query["pageSize"], out var ps) && ps > 0 && ps <= 50) pageSize = ps;

        var notifications = await _mongo.GetUserNotificationsAsync(userId, page, pageSize);
        var unreadCount = await _mongo.GetUnreadNotificationCountAsync(userId);
        var totalCount = await _mongo.GetTotalNotificationCountAsync(userId);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new NotificationListResponse
        {
            Notifications = notifications,
            UnreadCount = (int)unreadCount,
            TotalCount = (int)totalCount,
            Page = page,
            PageSize = pageSize
        });
        return response;
    }

    /// <summary>
    /// Get unread notification count for the authenticated user.
    /// </summary>
    [Function("GetUnreadNotificationCount")]
    [OpenApiOperation(operationId: "GetUnreadNotificationCount", tags: new[] { "Notifications" })]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object))]
    public async Task<HttpResponseData> GetUnreadNotificationCount(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "notifications/unread-count")] HttpRequestData req)
    {
        var (isAuth, userId, _, authError) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
        if (string.IsNullOrEmpty(userId))
        {
            var unauth = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauth.WriteAsJsonAsync(new { error = "Authentication required" });
            return unauth;
        }

        try
        {
            var count = await _mongo.GetUnreadNotificationCountAsync(userId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { unreadCount = count });
            return response;
        }
        catch (Exception ex) when (ex is TimeoutException || ex is MongoConnectionException || ex is MongoException)
        {
            _log.LogWarning(ex, "Transient MongoDB issue while fetching unread count for user {UserId}. Returning fallback count.", userId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { unreadCount = 0, degraded = true });
            return response;
        }
    }

    /// <summary>
    /// Mark a specific notification as read.
    /// </summary>
    [Function("MarkNotificationAsRead")]
    [OpenApiOperation(operationId: "MarkNotificationAsRead", tags: new[] { "Notifications" })]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    public async Task<HttpResponseData> MarkNotificationAsRead(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "notifications/{id}/read")] HttpRequestData req,
        string id)
    {
        var (isAuth, userId, _, authError) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
        if (string.IsNullOrEmpty(userId))
        {
            var unauth = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauth.WriteAsJsonAsync(new { error = "Authentication required" });
            return unauth;
        }

        var success = await _mongo.MarkNotificationAsReadAsync(id, userId);
        if (!success)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Notification not found" });
            return notFound;
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { message = "Notification marked as read" });
        return response;
    }

    /// <summary>
    /// Mark all notifications as read for the authenticated user.
    /// </summary>
    [Function("MarkAllNotificationsAsRead")]
    [OpenApiOperation(operationId: "MarkAllNotificationsAsRead", tags: new[] { "Notifications" })]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    public async Task<HttpResponseData> MarkAllNotificationsAsRead(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "notifications/read-all")] HttpRequestData req)
    {
        var (isAuth, userId, _, authError) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
        if (string.IsNullOrEmpty(userId))
        {
            var unauth = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauth.WriteAsJsonAsync(new { error = "Authentication required" });
            return unauth;
        }

        var count = await _mongo.MarkAllNotificationsAsReadAsync(userId);
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { message = $"{count} notifications marked as read", markedCount = count });
        return response;
    }

    /// <summary>
    /// Delete a specific notification.
    /// </summary>
    [Function("DeleteNotification")]
    [OpenApiOperation(operationId: "DeleteNotification", tags: new[] { "Notifications" })]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    public async Task<HttpResponseData> DeleteNotification(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "notifications/{id}")] HttpRequestData req,
        string id)
    {
        var (isAuth, userId, _, authError) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
        if (string.IsNullOrEmpty(userId))
        {
            var unauth = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauth.WriteAsJsonAsync(new { error = "Authentication required" });
            return unauth;
        }

        var success = await _mongo.DeleteNotificationAsync(id, userId);
        if (!success)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Notification not found" });
            return notFound;
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { message = "Notification deleted" });
        return response;
    }

    /// <summary>
    /// Delete all notifications for the authenticated user.
    /// </summary>
    [Function("DeleteAllNotifications")]
    [OpenApiOperation(operationId: "DeleteAllNotifications", tags: new[] { "Notifications" })]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    public async Task<HttpResponseData> DeleteAllNotifications(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "notifications/all")] HttpRequestData req)
    {
        var (isAuth, userId, _, authError) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
        if (string.IsNullOrEmpty(userId))
        {
            var unauth = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauth.WriteAsJsonAsync(new { error = "Authentication required" });
            return unauth;
        }

        var count = await _mongo.DeleteAllNotificationsAsync(userId);
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { message = $"{count} notifications deleted", deletedCount = count });
        return response;
    }

    /// <summary>
    /// Get notification preferences for the authenticated user.
    /// </summary>
    [Function("GetNotificationPreferences")]
    [OpenApiOperation(operationId: "GetNotificationPreferences", tags: new[] { "Notifications" })]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(NotificationPreferences))]
    public async Task<HttpResponseData> GetNotificationPreferences(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "notifications/preferences")] HttpRequestData req)
    {
        var (isAuth, userId, _, authError) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
        if (string.IsNullOrEmpty(userId))
        {
            var unauth = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauth.WriteAsJsonAsync(new { error = "Authentication required" });
            return unauth;
        }

        var prefs = await _mongo.GetNotificationPreferencesAsync(userId);
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(prefs);
        return response;
    }

    /// <summary>
    /// Update notification preferences for the authenticated user.
    /// </summary>
    [Function("UpdateNotificationPreferences")]
    [OpenApiOperation(operationId: "UpdateNotificationPreferences", tags: new[] { "Notifications" })]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(UpdateNotificationPreferencesRequest))]
    public async Task<HttpResponseData> UpdateNotificationPreferences(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "notifications/preferences")] HttpRequestData req)
    {
        var (isAuth, userId, _, authError) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
        if (string.IsNullOrEmpty(userId))
        {
            var unauth = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauth.WriteAsJsonAsync(new { error = "Authentication required" });
            return unauth;
        }

        var body = await req.ReadAsStringAsync();
        if (string.IsNullOrEmpty(body))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "Request body is required" });
            return bad;
        }

        var update = JsonSerializer.Deserialize<UpdateNotificationPreferencesRequest>(body, _jsonOptions);
        if (update == null)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "Invalid request body" });
            return bad;
        }

        // Get existing preferences and merge the update
        var existing = await _mongo.GetNotificationPreferencesAsync(userId);
        if (update.OrderUpdates.HasValue) existing.OrderUpdates = update.OrderUpdates.Value;
        if (update.LoyaltyPoints.HasValue) existing.LoyaltyPoints = update.LoyaltyPoints.Value;
        if (update.Offers.HasValue) existing.Offers = update.Offers.Value;
        if (update.SystemNotifications.HasValue) existing.SystemNotifications = update.SystemNotifications.Value;
        if (update.EmailNotifications.HasValue) existing.EmailNotifications = update.EmailNotifications.Value;
        if (update.PushNotifications.HasValue) existing.PushNotifications = update.PushNotifications.Value;

        await _mongo.UpdateNotificationPreferencesAsync(userId, existing);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { message = "Notification preferences updated", preferences = existing });
        return response;
    }
}
