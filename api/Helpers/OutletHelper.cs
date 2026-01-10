using System.Linq;
using System.Security.Claims;
using Cafe.Api.Services;
using Microsoft.Azure.Functions.Worker.Http;

namespace Cafe.Api.Helpers;

/// <summary>
/// Helper methods for outlet context and multi-outlet support
/// </summary>
public static class OutletHelper
{
    private const string OutletIdHeaderKey = "X-Outlet-Id";

    /// <summary>
    /// Extracts outlet ID from request header or user's default outlet
    /// Priority: 1) X-Outlet-Id header, 2) User's DefaultOutletId from token, 3) null
    /// </summary>
    public static string? GetOutletIdFromRequest(HttpRequestData req, AuthService authService)
    {
        // Try to get from header first
        if (req.Headers.TryGetValues(OutletIdHeaderKey, out var headerValues))
        {
            var outletId = headerValues.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(outletId))
            {
                return outletId;
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
    public static async Task<(bool hasAccess, string? outletId, string? errorMessage)> 
        ValidateOutletAccess(HttpRequestData req, AuthService authService, MongoService mongoService, string? requestedOutletId = null)
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
        var role = principal.FindFirst(ClaimTypes.Role)?.Value;

        // Admin can access all outlets
        if (role == "admin")
        {
            var outletId = requestedOutletId ?? GetOutletIdFromRequest(req, authService);
            
            // If no outlet specified, admin can proceed without outlet filter
            if (string.IsNullOrWhiteSpace(outletId))
            {
                return (true, null, null);
            }

            // Verify outlet exists
            var outlet = await mongoService.GetOutletByIdAsync(outletId);
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

        var user = await mongoService.GetUserByIdAsync(userId);
        if (user == null)
        {
            return (false, null, "User not found");
        }

        var outletIdToCheck = requestedOutletId ?? GetOutletIdFromRequest(req, authService) ?? user.DefaultOutletId;

        if (string.IsNullOrWhiteSpace(outletIdToCheck))
        {
            return (false, null, "No outlet specified and user has no default outlet");
        }

        // Check if user has access to this outlet
        if (user.AssignedOutlets == null || !user.AssignedOutlets.Contains(outletIdToCheck))
        {
            return (false, null, $"User does not have access to outlet {outletIdToCheck}");
        }

        // Verify outlet exists and is active
        var outletCheck = await mongoService.GetOutletByIdAsync(outletIdToCheck);
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
}
