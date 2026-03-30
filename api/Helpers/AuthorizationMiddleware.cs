using System.Net;
using System.Security.Claims;
using Cafe.Api.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;

namespace Cafe.Api.Helpers;

/// <summary>
/// Centralized authorization middleware that extracts JWT claims from every request
/// and stores them in FunctionContext.Items. Functions use the extension methods
/// (RequireAuthenticated, RequireAdmin, RequireAdminOrManager) instead of duplicating
/// auth logic in each function file.
/// </summary>
public class AuthorizationMiddleware : IFunctionsWorkerMiddleware
{
    private readonly AuthService _auth;
    private readonly ILogger<AuthorizationMiddleware> _logger;

    public const string AuthUserIdKey = "Auth:UserId";
    public const string AuthRoleKey = "Auth:Role";
    public const string AuthUsernameKey = "Auth:Username";
    public const string IsAuthenticatedKey = "Auth:IsAuthenticated";

    public AuthorizationMiddleware(AuthService auth, ILogger<AuthorizationMiddleware> logger)
    {
        _auth = auth;
        _logger = logger;
    }

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var requestData = await context.GetHttpRequestDataAsync();
        if (requestData == null)
        {
            // Non-HTTP triggers (timers, etc.) pass through
            await next(context);
            return;
        }

        var (isValid, userId, role, username) = ExtractAuthInfo(requestData);

        context.Items[IsAuthenticatedKey] = isValid;
        context.Items[AuthUserIdKey] = userId ?? string.Empty;
        context.Items[AuthRoleKey] = role ?? string.Empty;
        context.Items[AuthUsernameKey] = username ?? string.Empty;

        await next(context);
    }

    private (bool isValid, string? userId, string? role, string? username) ExtractAuthInfo(HttpRequestData req)
    {
        if (!req.Headers.TryGetValues("Authorization", out var headerValues))
            return (false, null, null, null);

        var authHeader = headerValues.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer "))
            return (false, null, null, null);

        var token = authHeader.Substring("Bearer ".Length).Trim();
        var principal = _auth.ValidateToken(token);
        if (principal == null)
            return (false, null, null, null);

        var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = principal.FindFirst(ClaimTypes.Role)?.Value;
        var username = principal.FindFirst(ClaimTypes.Name)?.Value;

        return (true, userId, role, username);
    }
}

/// <summary>
/// Extension methods for FunctionContext to simplify auth checks in function files.
/// Replaces the duplicated JWT parsing pattern with one-liner policy checks.
/// <example>
/// // Old pattern (repeated in every function):
/// var authHeader = req.Headers.GetValues("Authorization").FirstOrDefault();
/// var token = authHeader.Substring("Bearer ".Length).Trim();
/// var principal = _auth.ValidateToken(token);
/// ...
///
/// // New pattern:
/// var authError = await context.RequireAdmin(req);
/// if (authError != null) return authError;
/// var (userId, role, username, _) = context.GetAuthInfo();
/// </example>
/// </summary>
public static class FunctionContextAuthExtensions
{
    public static (string? userId, string? role, string? username, bool isAuthenticated) GetAuthInfo(this FunctionContext context)
    {
        var isAuth = context.Items.TryGetValue(AuthorizationMiddleware.IsAuthenticatedKey, out var authObj) && authObj is true;
        var userId = context.Items.TryGetValue(AuthorizationMiddleware.AuthUserIdKey, out var uidObj) ? uidObj as string : null;
        var role = context.Items.TryGetValue(AuthorizationMiddleware.AuthRoleKey, out var roleObj) ? roleObj as string : null;
        var username = context.Items.TryGetValue(AuthorizationMiddleware.AuthUsernameKey, out var unObj) ? unObj as string : null;

        return (
            string.IsNullOrEmpty(userId) ? null : userId,
            string.IsNullOrEmpty(role) ? null : role,
            string.IsNullOrEmpty(username) ? null : username,
            isAuth
        );
    }

    public static async Task<HttpResponseData?> RequireAuthenticated(this FunctionContext context, HttpRequestData req)
    {
        var (_, _, _, isAuth) = context.GetAuthInfo();
        if (!isAuth)
        {
            var response = req.CreateResponse(HttpStatusCode.Unauthorized);
            await response.WriteAsJsonAsync(new { error = "Authentication required" });
            return response;
        }
        return null;
    }

    public static async Task<HttpResponseData?> RequireAdmin(this FunctionContext context, HttpRequestData req)
    {
        var (_, role, _, isAuth) = context.GetAuthInfo();
        if (!isAuth)
        {
            var response = req.CreateResponse(HttpStatusCode.Unauthorized);
            await response.WriteAsJsonAsync(new { error = "Authentication required" });
            return response;
        }
        if (role != "admin")
        {
            var response = req.CreateResponse(HttpStatusCode.Forbidden);
            await response.WriteAsJsonAsync(new { error = "Admin access required" });
            return response;
        }
        return null;
    }

    public static async Task<HttpResponseData?> RequireAdminOrManager(this FunctionContext context, HttpRequestData req)
    {
        var (_, role, _, isAuth) = context.GetAuthInfo();
        if (!isAuth)
        {
            var response = req.CreateResponse(HttpStatusCode.Unauthorized);
            await response.WriteAsJsonAsync(new { error = "Authentication required" });
            return response;
        }
        if (role != "admin" && role != "manager")
        {
            var response = req.CreateResponse(HttpStatusCode.Forbidden);
            await response.WriteAsJsonAsync(new { error = "Admin or Manager access required" });
            return response;
        }
        return null;
    }
}
