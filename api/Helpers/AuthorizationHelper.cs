using System.Net;
using Cafe.Api.Services;
using Microsoft.Azure.Functions.Worker.Http;

namespace Cafe.Api.Helpers;

public static class AuthorizationHelper
{
    public static async Task<(bool isAuthorized, string? userId, string? role, HttpResponseData? errorResponse)> 
        ValidateAdminRole(HttpRequestData req, AuthService authService)
    {
        var authHeader = req.Headers.GetValues("Authorization").FirstOrDefault();
        
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

        if (role != "admin")
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
        var authHeader = req.Headers.GetValues("Authorization").FirstOrDefault();
        
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
        var authHeader = req.Headers.GetValues("Authorization").FirstOrDefault();
        
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

        if (role != "admin" && role != "manager")
        {
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            await forbidden.WriteAsJsonAsync(new { error = "Admin or Manager access required" });
            return (false, userId, role, forbidden);
        }

        return (true, userId, role, null);
    }
}
