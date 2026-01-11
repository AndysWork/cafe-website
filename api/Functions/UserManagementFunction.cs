using System.Net;
using Cafe.Api.Models;
using Cafe.Api.Services;
using Cafe.Api.Helpers;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;

namespace Cafe.Api.Functions;

public class UserManagementFunction
{
    private readonly MongoService _mongo;
    private readonly AuthService _auth;
    private readonly ILogger _log;

    public UserManagementFunction(MongoService mongo, AuthService auth, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _auth = auth;
        _log = loggerFactory.CreateLogger<UserManagementFunction>();
    }

    /// <summary>
    /// Retrieves all user accounts in the system (Admin only)
    /// </summary>
    /// <param name="req">HTTP request with authorization header</param>
    /// <returns>List of all users with details (excluding password hashes)</returns>
    /// <response code="200">Successfully retrieved users</response>
    /// <response code="401">User not authenticated</response>
    /// <response code="403">User not authorized (admin role required)</response>
    [Function("GetAllUsers")]
    [OpenApiOperation(operationId: "GetAllUsers", tags: new[] { "Users" }, Summary = "Get all users", Description = "Retrieves all user accounts (Admin only)")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(List<User>), Description = "Successfully retrieved users")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "User not authenticated")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Forbidden, Description = "User not authorized")]
    public async Task<HttpResponseData> GetAllUsers(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "users")] HttpRequestData req)
    {
        // Validate admin authorization
        var (isAuthorized, adminUserId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
        if (!isAuthorized) return errorResponse!;

        try
        {
            var users = await _mongo.GetAllUsersAsync();
            
            // Don't send password hashes to client
            var userDtos = users.Select(u => new
            {
                u.Id,
                u.Username,
                u.Email,
                u.Role,
                u.FirstName,
                u.LastName,
                u.PhoneNumber,
                u.IsActive,
                u.CreatedAt,
                u.LastLoginAt
            });

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, data = userDtos });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting users");
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to retrieve users" });
            return errorRes;
        }
    }

    [Function("PromoteUserToAdmin")]
    [OpenApiOperation(operationId: "PromoteUserToAdmin", tags: new[] { "Users" }, Summary = "Promote user to admin", Description = "Promotes a user to admin role (Admin only)")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "userId", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "User ID")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "User successfully promoted")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.NotFound, Description = "User not found")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "User not authenticated")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Forbidden, Description = "User not authorized")]
    public async Task<HttpResponseData> PromoteUserToAdmin(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "users/{userId}/promote")] HttpRequestData req,
        string userId)
    {
        // Validate admin authorization
        var (isAuthorized, adminUserId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
        if (!isAuthorized) return errorResponse!;

        try
        {
            var auditLogger = new AuditLogger(_log);
            var ipAddress = GetClientIp(req);

            // Get the user to promote
            var user = await _mongo.GetUserByIdAsync(userId);
            if (user == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { success = false, error = "User not found" });
                return notFound;
            }

            // Check if already admin
            if (user.Role == "admin")
            {
                var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                await conflict.WriteAsJsonAsync(new { success = false, error = "User is already an admin" });
                return conflict;
            }

            // Update role to admin
            var success = await _mongo.UpdateUserRoleAsync(userId, "admin");
            
            if (!success)
            {
                var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to update user role" });
                return errorRes;
            }

            // Log admin action
            auditLogger.LogAdminAction(
                adminUserId: adminUserId!,
                action: "Promote User to Admin",
                targetUserId: userId,
                details: $"User '{user.Username}' promoted to admin role"
            );

            _log.LogInformation($"User {user.Username} promoted to admin by {adminUserId}");

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                success = true,
                message = $"User '{user.Username}' has been promoted to admin",
                data = new
                {
                    userId = user.Id,
                    username = user.Username,
                    role = "admin"
                }
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error promoting user to admin");
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to promote user" });
            return errorRes;
        }
    }

    [Function("DemoteAdminToUser")]
    public async Task<HttpResponseData> DemoteAdminToUser(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "users/{userId}/demote")] HttpRequestData req,
        string userId)
    {
        // Validate admin authorization
        var (isAuthorized, adminUserId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
        if (!isAuthorized) return errorResponse!;

        try
        {
            var auditLogger = new AuditLogger(_log);

            // Prevent self-demotion
            if (userId == adminUserId)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { success = false, error = "Cannot demote yourself" });
                return forbidden;
            }

            // Get the user to demote
            var user = await _mongo.GetUserByIdAsync(userId);
            if (user == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { success = false, error = "User not found" });
                return notFound;
            }

            // Check if already regular user
            if (user.Role == "user")
            {
                var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                await conflict.WriteAsJsonAsync(new { success = false, error = "User is already a regular user" });
                return conflict;
            }

            // Update role to user
            var success = await _mongo.UpdateUserRoleAsync(userId, "user");
            
            if (!success)
            {
                var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to update user role" });
                return errorRes;
            }

            // Log admin action
            auditLogger.LogAdminAction(
                adminUserId: adminUserId!,
                action: "Demote Admin to User",
                targetUserId: userId,
                details: $"User '{user.Username}' demoted to regular user role"
            );

            _log.LogInformation($"User {user.Username} demoted to regular user by {adminUserId}");

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                success = true,
                message = $"User '{user.Username}' has been demoted to regular user",
                data = new
                {
                    userId = user.Id,
                    username = user.Username,
                    role = "user"
                }
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error demoting admin to user");
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to demote user" });
            return errorRes;
        }
    }

    [Function("ToggleUserActiveStatus")]
    public async Task<HttpResponseData> ToggleUserActiveStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "users/{userId}/toggle-status")] HttpRequestData req,
        string userId)
    {
        // Validate admin authorization
        var (isAuthorized, adminUserId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
        if (!isAuthorized) return errorResponse!;

        try
        {
            var auditLogger = new AuditLogger(_log);

            // Prevent self-deactivation
            if (userId == adminUserId)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { success = false, error = "Cannot deactivate yourself" });
                return forbidden;
            }

            // Get the user
            var user = await _mongo.GetUserByIdAsync(userId);
            if (user == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { success = false, error = "User not found" });
                return notFound;
            }

            // Toggle active status
            var newStatus = !user.IsActive;
            var success = await _mongo.UpdateUserActiveStatusAsync(userId, newStatus);
            
            if (!success)
            {
                var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to update user status" });
                return errorRes;
            }

            // Log admin action
            auditLogger.LogAdminAction(
                adminUserId: adminUserId!,
                action: newStatus ? "Activate User" : "Deactivate User",
                targetUserId: userId,
                details: $"User '{user.Username}' {(newStatus ? "activated" : "deactivated")}"
            );

            _log.LogInformation($"User {user.Username} status changed to {(newStatus ? "active" : "inactive")} by {adminUserId}");

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                success = true,
                message = $"User '{user.Username}' has been {(newStatus ? "activated" : "deactivated")}",
                data = new
                {
                    userId = user.Id,
                    username = user.Username,
                    isActive = newStatus
                }
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error toggling user active status");
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to update user status" });
            return errorRes;
        }
    }

    private string GetClientIp(HttpRequestData req)
    {
        // Try to get IP from X-Forwarded-For header (common in Azure)
        if (req.Headers.TryGetValues("X-Forwarded-For", out var forwardedFor))
        {
            return forwardedFor.First().Split(',')[0].Trim();
        }

        // Try X-Real-IP header
        if (req.Headers.TryGetValues("X-Real-IP", out var realIp))
        {
            return realIp.First();
        }

        return "unknown";
    }
}
