using System.Net;
using System.Security.Claims;
using Cafe.Api.Services;
using Microsoft.Azure.Functions.Worker.Http;

namespace Cafe.Api.Helpers;

public static class AuthorizationHelper
{
    private static async Task<(ClaimsPrincipal? principal, HttpResponseData? errorResponse)> AuthenticateAsync(
        HttpRequestData req, AuthService authService)
    {
        var authHeader = req.Headers.TryGetValues("Authorization", out var headerValues)
            ? headerValues.FirstOrDefault()
            : null;

        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer "))
        {
            var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauthorized.WriteAsJsonAsync(new { error = "Authorization header missing or invalid" });
            return (null, unauthorized);
        }

        var token = authHeader.Substring("Bearer ".Length).Trim();
        var principal = authService.ValidateToken(token);

        if (principal == null)
        {
            var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauthorized.WriteAsJsonAsync(new { error = "Invalid or expired token" });
            return (null, unauthorized);
        }

        return (principal, null);
    }

    /// <summary>
    /// Validates the caller holds at least one of the allowed roles (checks every role claim).
    /// Returns the caller's primary role.
    /// </summary>
    public static async Task<(bool isAuthorized, string? userId, string? role, HttpResponseData? errorResponse)>
        ValidateAnyRole(HttpRequestData req, AuthService authService, IEnumerable<string> allowedRoles, string forbiddenMessage)
    {
        var (principal, error) = await AuthenticateAsync(req, authService);
        if (principal == null) return (false, null, null, error);

        var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = principal.FindFirst(ClaimTypes.Role)?.Value;

        if (!RoleHelper.HasAnyRole(principal, allowedRoles))
        {
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            await forbidden.WriteAsJsonAsync(new { error = forbiddenMessage });
            return (false, userId, role, forbidden);
        }

        return (true, userId, role, null);
    }

    public static Task<(bool isAuthorized, string? userId, string? role, HttpResponseData? errorResponse)>
        ValidateAdminRole(HttpRequestData req, AuthService authService) =>
        ValidateAnyRole(req, authService, new[] { RoleHelper.Admin }, "Admin access required");

    public static string? GetRoleFromAuthorizationHeader(HttpRequestData req, AuthService authService)
    {
        var authHeader = req.Headers.TryGetValues("Authorization", out var values)
            ? values.FirstOrDefault()
            : null;

        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return null;

        var token = authHeader.Substring("Bearer ".Length).Trim();
        var principal = authService.ValidateToken(token);
        return principal?.FindFirst(ClaimTypes.Role)?.Value;
    }

    /// <summary>All roles of the caller (empty when unauthenticated).</summary>
    public static List<string> GetRolesFromAuthorizationHeader(HttpRequestData req, AuthService authService)
    {
        var authHeader = req.Headers.TryGetValues("Authorization", out var values)
            ? values.FirstOrDefault()
            : null;

        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return new List<string>();

        var token = authHeader.Substring("Bearer ".Length).Trim();
        return RoleHelper.GetRoles(authService.ValidateToken(token));
    }

    public static async Task<(bool isAuthorized, string? userId, string? role, HttpResponseData? errorResponse)>
        ValidateAuthenticatedUser(HttpRequestData req, AuthService authService)
    {
        var (principal, error) = await AuthenticateAsync(req, authService);
        if (principal == null) return (false, null, null, error);

        var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role = principal.FindFirst(ClaimTypes.Role)?.Value;

        return (true, userId, role, null);
    }

    public static Task<(bool isAuthorized, string? userId, string? role, HttpResponseData? errorResponse)>
        ValidateAdminOrManagerRole(HttpRequestData req, AuthService authService) =>
        ValidateAnyRole(req, authService, RoleHelper.ManagerRoles, "Admin or Manager access required");

    public static async Task<(bool isAuthorized, string? userId, string? role, HttpResponseData? errorResponse)>
        ValidateKitchenAccessRole(HttpRequestData req, AuthService authService)
    {
        var result = await ValidateAnyRole(req, authService, RoleHelper.KitchenRoles, "Kitchen access role required");
        return result.isAuthorized
            ? (true, result.userId, RoleHelper.Normalize(result.role), null)
            : result;
    }

    public static Task<(bool isAuthorized, string? userId, string? role, HttpResponseData? errorResponse)>
        ValidateWaiterRole(HttpRequestData req, AuthService authService) =>
        ValidateAnyRole(req, authService, RoleHelper.WaiterRoles, "Waiter access role required");

    public static Task<(bool isAuthorized, string? userId, string? role, HttpResponseData? errorResponse)>
        ValidateCashierRole(HttpRequestData req, AuthService authService) =>
        ValidateAnyRole(req, authService, RoleHelper.CashierRoles, "Cashier access role required");
}
