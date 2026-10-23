using System.Linq;
using System.Security.Claims;
using Cafe.Api.Services;
using Cafe.Api.Repositories;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.AspNetCore.Http;

namespace Cafe.Api.Helpers;

/// <summary>
/// Helper methods for outlet context and multi-outlet support
/// </summary>
public static class OutletHelper
{
    private const string OutletIdHeaderKey = "X-Outlet-Id";

    public static string? GetOrderingUnavailableReason(Cafe.Api.Models.Outlet? outlet, string orderType)
    {
        if (outlet == null || !outlet.IsActive || outlet.IsDeleted)
            return "This outlet is unavailable for new orders.";

        var (enabled, label) = orderType switch
        {
            "delivery" => (outlet.Settings.AcceptsOnlineOrders, "Online delivery"),
            "pickup" => (outlet.Settings.AcceptsTakeaway, "Takeaway"),
            "dine-in" => (outlet.Settings.AcceptsDineIn, "Dine-in"),
            "counter" => (outlet.Settings.AcceptsCounterSales, "Counter sales"),
            _ => (false, "This ordering channel")
        };

        return enabled ? null : $"{label} ordering is temporarily paused for this outlet.";
    }

    /// <summary>
    /// Extracts outlet ID from request header or user's default outlet
    /// Priority: 1) X-Outlet-Id header, 2) User's DefaultOutletId from token, 3) null
    /// If X-Outlet-Id header is present but empty, returns null (for querying all outlets)
    /// </summary>
    public static string? GetOutletIdFromRequest(HttpRequestData req, AuthService authService)
    {
        // Try to get from header first
        if (req.Headers.TryGetValues(OutletIdHeaderKey, out var headerValues))
        {
            var outletId = headerValues.FirstOrDefault();
            
            // If header is present (even if empty), use it and don't fall back to default
            // Empty string means "return all outlets"
            if (outletId != null)
            {
                return string.IsNullOrWhiteSpace(outletId) ? null : outletId;
            }
        }

        // Try to get from Authorization token (user's default outlet)
        if (req.Headers.TryGetValues("Authorization", out var authHeaders))
        {
            var authHeader = authHeaders.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(authHeader) && authHeader.StartsWith("Bearer "))
            {
                var token = authHeader.Substring("Bearer ".Length).Trim();
                var principal = authService.ValidateToken(token);
                
                if (principal != null)
                {
                    var defaultOutletId = principal.FindFirst("DefaultOutletId")?.Value;
                    if (!string.IsNullOrWhiteSpace(defaultOutletId))
                    {
                        return defaultOutletId;
                    }
                }
            }
        }

        return null; // No outlet context provided
    }

    /// <summary>
    /// Gets outlet ID from request and validates user has access to it
    /// </summary>
    public static Task<(bool hasAccess, string? outletId, string? errorMessage)>
        ValidateOutletAccess(HttpRequestData req, AuthService authService, MongoService mongoService, string? requestedOutletId = null) =>
        ValidateOutletAccess(req, authService, mongoService, mongoService, mongoService, requestedOutletId);

    public static async Task<(bool hasAccess, string? outletId, string? errorMessage)>
        ValidateOutletAccess(
            HttpRequestData req,
            AuthService authService,
            IUserRepository userRepository,
            IStaffRepository staffRepository,
            IOutletRepository outletRepository,
            string? requestedOutletId = null)
    {
        if (!req.Headers.TryGetValues("Authorization", out var authHeaders))
        {
            return (false, null, "Authorization header missing");
        }

        var authHeader = authHeaders.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer "))
        {
            return (false, null, "Authorization header invalid");
        }

        var token = authHeader.Substring("Bearer ".Length).Trim();
        var principal = authService.ValidateToken(token);

        if (principal == null)
        {
            return (false, null, "Invalid or expired token");
        }

        var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        // Admin can access all outlets
        if (RoleHelper.HasAnyRole(principal, new[] { RoleHelper.Admin }))
        {
            var outletId = requestedOutletId ?? GetOutletIdFromRequest(req, authService);
            
            // If no outlet specified, admin can proceed without outlet filter
            if (string.IsNullOrWhiteSpace(outletId))
            {
                return (true, null, null);
            }

            // Verify outlet exists
            var outlet = await outletRepository.GetOutletByIdAsync(outletId);
            if (outlet == null)
            {
                return (false, null, $"Outlet {outletId} not found");
            }

            return (true, outletId, null);
        }

        // For non-admin users, check assigned outlets
        if (string.IsNullOrWhiteSpace(userId))
        {
            return (false, null, "User ID not found in token");
        }

        var user = await userRepository.GetUserByIdAsync(userId);
        if (user == null)
        {
            return (false, null, "User not found");
        }

        var outletIdToCheck = requestedOutletId ?? GetOutletIdFromRequest(req, authService) ?? user.DefaultOutletId;

        if (string.IsNullOrWhiteSpace(outletIdToCheck))
        {
            return (false, null, "No outlet specified and user has no default outlet");
        }

        var hasAssignment = user.AssignedOutlets?.Contains(outletIdToCheck) == true;
        if (!hasAssignment && RoleHelper.HasAnyRole(principal, RoleHelper.StaffRoles))
        {
            var staff = await staffRepository.GetStaffByUserIdAsync(userId);
            if (staff == null && !string.IsNullOrWhiteSpace(user.Email))
                staff = await staffRepository.GetStaffByEmailAsync(user.Email);

            // Legacy staff profiles can be mapped by email, but never use another account's linked profile.
            hasAssignment = staff is { IsActive: true }
                && (staff.UserId == userId
                    || (string.IsNullOrWhiteSpace(staff.UserId)
                        && !string.IsNullOrWhiteSpace(user.Email)
                        && string.Equals(staff.Email, user.Email, StringComparison.OrdinalIgnoreCase)))
                && staff.OutletIds?.Contains(outletIdToCheck) == true;
        }

        if (!hasAssignment)
        {
            return (false, null, $"User does not have access to outlet {outletIdToCheck}");
        }

        // Verify outlet exists and is active
        var outletCheck = await outletRepository.GetOutletByIdAsync(outletIdToCheck);
        if (outletCheck == null)
        {
            return (false, null, $"Outlet {outletIdToCheck} not found");
        }

        if (!outletCheck.IsActive)
        {
            return (false, null, $"Outlet {outletCheck.OutletName} is not active");
        }

        return (true, outletIdToCheck, null);
    }

    /// <summary>
    /// Extracts outlet ID for admin users (less strict validation)
    /// </summary>
    public static string? GetOutletIdForAdmin(HttpRequestData req, AuthService authService)
    {
        return GetOutletIdFromRequest(req, authService);
    }

    /// <summary>
    /// Extracts outlet ID from ASP.NET Core HttpRequest (for older function patterns)
    /// Priority: 1) X-Outlet-Id header, 2) User's DefaultOutletId from token, 3) null
    /// If X-Outlet-Id header is present but empty, returns null (for querying all outlets)
    /// </summary>
    public static string? GetOutletIdFromRequest(HttpRequest req, AuthService authService)
    {
        // Try to get from header first
        if (req.Headers.TryGetValue(OutletIdHeaderKey, out var headerValues))
        {
            var outletId = headerValues.FirstOrDefault();
            
            // If header is present (even if empty), use it and don't fall back to default
            // Empty string means "return all outlets"
            if (outletId != null)
            {
                return string.IsNullOrWhiteSpace(outletId) ? null : outletId;
            }
        }

        // Try to get from Authorization token (user's default outlet)
        if (req.Headers.TryGetValue("Authorization", out var authHeaders))
        {
            var authHeader = authHeaders.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(authHeader) && authHeader.StartsWith("Bearer "))
            {
                var token = authHeader.Substring("Bearer ".Length).Trim();
                var principal = authService.ValidateToken(token);
                
                if (principal != null)
                {
                    var defaultOutletId = principal.FindFirst("DefaultOutletId")?.Value;
                    if (!string.IsNullOrWhiteSpace(defaultOutletId))
                    {
                        return defaultOutletId;
                    }
                }
            }
        }

        return null; // No outlet context provided
    }
}
