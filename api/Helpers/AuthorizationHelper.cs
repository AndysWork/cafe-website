using System.Net;
using Cafe.Api.Services;
using Microsoft.Azure.Functions.Worker.Http;

namespace Cafe.Api.Helpers;

public static class AuthorizationHelper
{
    private static bool IsAdminLikeRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role)) return false;

        var normalized = role.Trim().ToLowerInvariant();
        return normalized == "admin";
    }

    public static async Task<(bool isAuthorized, string? userId, string? role, HttpResponseData? errorResponse)> 
        ValidateAdminRole(HttpRequestData req, AuthService authService)
    {
        var authHeader = req.Headers.TryGetValues("Authorization", out var headerValues) 
            ? headerValues.FirstOrDefault() 
            : null;
        
        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer "))
        {
            var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauthorized.WriteAsJsonAsync(new { error = "Authorization header missing or invalid" });
            return (false, null, null, unauthorized);
        }

        var token = authHeader.Substring("Bearer ".Length).Trim();
        var principal = authService.ValidateToken(token);

        if (principal == null)
        {
            var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauthorized.WriteAsJsonAsync(new { error = "Invalid or expired token" });
            return (false, null, null, unauthorized);
        }

        var userId = principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var role = principal.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

        if (!IsAdminLikeRole(role))
        {
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            await forbidden.WriteAsJsonAsync(new { error = "Admin access required" });
            return (false, userId, role, forbidden);
        }

        return (true, userId, role, null);
    }

    public static async Task<(bool isAuthorized, string? userId, string? role, HttpResponseData? errorResponse)> 
        ValidateAuthenticatedUser(HttpRequestData req, AuthService authService)
    {
        var authHeader = req.Headers.TryGetValues("Authorization", out var headerValues) 
            ? headerValues.FirstOrDefault() 
            : null;
        
        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer "))
        {
            var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauthorized.WriteAsJsonAsync(new { error = "Authorization header missing or invalid" });
            return (false, null, null, unauthorized);
        }

        var token = authHeader.Substring("Bearer ".Length).Trim();
        var principal = authService.ValidateToken(token);

        if (principal == null)
        {
            var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauthorized.WriteAsJsonAsync(new { error = "Invalid or expired token" });
            return (false, null, null, unauthorized);
        }

        var userId = principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var role = principal.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

        return (true, userId, role, null);
    }

    public static async Task<(bool isAuthorized, string? userId, string? role, HttpResponseData? errorResponse)> 
        ValidateAdminOrManagerRole(HttpRequestData req, AuthService authService)
    {
        var authHeader = req.Headers.TryGetValues("Authorization", out var headerValues) 
            ? headerValues.FirstOrDefault() 
            : null;
        
        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer "))
        {
            var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauthorized.WriteAsJsonAsync(new { error = "Authorization header missing or invalid" });
            return (false, null, null, unauthorized);
        }

        var token = authHeader.Substring("Bearer ".Length).Trim();
        var principal = authService.ValidateToken(token);

        if (principal == null)
        {
            var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauthorized.WriteAsJsonAsync(new { error = "Invalid or expired token" });
            return (false, null, null, unauthorized);
        }

        var userId = principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var role = principal.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

        var normalizedRole = role?.Trim().ToLowerInvariant();
        if (!IsAdminLikeRole(role) && normalizedRole != "manager")
        {
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            await forbidden.WriteAsJsonAsync(new { error = "Admin or Manager access required" });
            return (false, userId, role, forbidden);
        }

        return (true, userId, role, null);
    }

    public static async Task<(bool isAuthorized, string? userId, string? role, HttpResponseData? errorResponse)>
        ValidateKitchenAccessRole(HttpRequestData req, AuthService authService)
    {
        var authHeader = req.Headers.TryGetValues("Authorization", out var headerValues)
            ? headerValues.FirstOrDefault()
            : null;

        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer "))
        {
            var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauthorized.WriteAsJsonAsync(new { error = "Authorization header missing or invalid" });
            return (false, null, null, unauthorized);
        }

        var token = authHeader.Substring("Bearer ".Length).Trim();
        var principal = authService.ValidateToken(token);

        if (principal == null)
        {
            var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauthorized.WriteAsJsonAsync(new { error = "Invalid or expired token" });
            return (false, null, null, unauthorized);
        }

        var userId = principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var role = principal.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value?.ToLowerInvariant();
        var kitchenRoles = new[] { "admin", "manager", "cook", "chef", "sous-chef" };

        if (string.IsNullOrWhiteSpace(role) || !kitchenRoles.Contains(role))
        {
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            await forbidden.WriteAsJsonAsync(new { error = "Kitchen access role required" });
            return (false, userId, role, forbidden);
        }

        return (true, userId, role, null);
    }
}
