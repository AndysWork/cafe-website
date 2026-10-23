using System.Security.Claims;
using Cafe.Api.Models;

namespace Cafe.Api.Helpers;

/// <summary>
/// Central role definitions and multi-role checks. A user has a primary <c>Role</c>
/// (kept for backward compatibility) plus an optional <c>Roles</c> list.
/// </summary>
public static class RoleHelper
{
    public const string Admin = "admin";
    public const string Manager = "manager";
    public const string Waiter = "waiter";
    public const string Cashier = "cashier";
    public const string Kitchen = "kitchen";
    public const string User = "user";

    // Ordered by priority; the highest-priority role becomes the primary role.
    public static readonly IReadOnlyList<string> AssignableRoles = new[]
    {
        Admin,
        Manager,
        "chef",
        "sous-chef",
        "cook",
        Kitchen,
        Cashier,
        Waiter,
        "partner",
        "delivery-partner",
        User
    };

    public static readonly IReadOnlySet<string> KitchenRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Admin, Manager, "cook", "chef", "sous-chef", Kitchen, "kitchen-staff", "kitchen-helper"
    };

    public static readonly IReadOnlySet<string> WaiterRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Admin, Manager, Waiter
    };

    public static readonly IReadOnlySet<string> CashierRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Admin, Manager, Cashier
    };

    public static readonly IReadOnlySet<string> ManagerRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Admin, Manager
    };

    /// <summary>Any in-store staff role (used for staff-only dine-in access).</summary>
    public static readonly IReadOnlySet<string> StaffRoles = new HashSet<string>(
        KitchenRoles.Concat(WaiterRoles).Concat(CashierRoles).Append("staff"),
        StringComparer.OrdinalIgnoreCase);

    public static string Normalize(string? role)
    {
        if (string.IsNullOrWhiteSpace(role)) return string.Empty;
        var normalized = role.Trim().ToLowerInvariant().Replace("_", "-").Replace(" ", "-");
        return normalized == "checf" ? "chef" : normalized;
    }

    public static bool IsAssignable(string? role) =>
        AssignableRoles.Contains(Normalize(role), StringComparer.OrdinalIgnoreCase);

    /// <summary>Normalizes, de-duplicates and orders roles by priority (unknown roles last).</summary>
    public static List<string> NormalizeRoles(IEnumerable<string?>? roles)
    {
        if (roles == null) return new List<string>();

        return roles
            .Select(Normalize)
            .Where(r => !string.IsNullOrEmpty(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(PriorityOf)
            .ToList();
    }

    public static string GetPrimaryRole(IEnumerable<string?>? roles)
    {
        var normalized = NormalizeRoles(roles);
        return normalized.FirstOrDefault() ?? User;
    }

    /// <summary>All roles of a user, combining the legacy single role with the roles list.</summary>
    public static List<string> GetEffectiveRoles(User user)
    {
        var all = new List<string?> { user.Role };
        if (user.Roles != null) all.AddRange(user.Roles);
        var normalized = NormalizeRoles(all);
        return normalized.Count > 0 ? normalized : new List<string> { User };
    }

    public static List<string> GetRoles(ClaimsPrincipal? principal)
    {
        if (principal == null) return new List<string>();
        return NormalizeRoles(principal.FindAll(ClaimTypes.Role).Select(c => c.Value));
    }

    public static bool HasAnyRole(ClaimsPrincipal? principal, IEnumerable<string> allowed) =>
        HasAnyRole(GetRoles(principal), allowed);

    public static bool HasAnyRole(IEnumerable<string>? userRoles, IEnumerable<string> allowed)
    {
        if (userRoles == null) return false;
        var allowedSet = allowed as IReadOnlySet<string>
            ?? new HashSet<string>(allowed.Select(Normalize), StringComparer.OrdinalIgnoreCase);
        return userRoles.Select(Normalize).Any(r => allowedSet.Contains(r));
    }

    private static int PriorityOf(string role)
    {
        for (var i = 0; i < AssignableRoles.Count; i++)
        {
            if (string.Equals(AssignableRoles[i], role, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return int.MaxValue;
    }
}
