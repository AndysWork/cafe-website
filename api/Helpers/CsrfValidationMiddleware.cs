using System.Net;
using Cafe.Api.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;

namespace Cafe.Api.Helpers;

/// <summary>
/// Validates CSRF token for authenticated, state-changing HTTP requests.
/// </summary>
public class CsrfValidationMiddleware : IFunctionsWorkerMiddleware
{
    private static readonly HashSet<string> MethodsRequiringCsrf = new(StringComparer.OrdinalIgnoreCase)
    {
        "POST", "PUT", "PATCH", "DELETE"
    };

    private static readonly HashSet<string> ExcludedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/auth/login",
        "/api/auth/register",
        "/api/auth/password/forgot",
        "/api/auth/password/reset",
        "/api/offers/validate",
        "/api/recipes/sync-prices",
        "/api/payments/webhook/razorpay"
    };

    // Dynamic endpoint patterns that should not require CSRF.
    // These are protected by JWT role checks and primarily use multipart uploads.
    private static readonly System.Text.RegularExpressions.Regex[] ExcludedPathPatterns =
    {
        new("^/api/menu/[^/]+/image$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled),
        new("^/api/recipes(?:/.*)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled),
        new("^/api/priceforecasts(?:/.*)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled),
        new("^/api/analytics/(track(?:/batch)?|session|heartbeat)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled)
    };

    private readonly AuthService _authService;
    private readonly bool _csrfValidationEnabled;

    public CsrfValidationMiddleware(AuthService authService)
    {
        _authService = authService;

        var disableFlag = Environment.GetEnvironmentVariable("Security__DisableCsrf");
        var isDisabledByFlag = bool.TryParse(disableFlag, out var disableParsed) && disableParsed;

        var environment = Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? "Production";
        var isDevelopment = environment.Equals("Development", StringComparison.OrdinalIgnoreCase);

        _csrfValidationEnabled = !(isDisabledByFlag || isDevelopment);
    }

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        if (!_csrfValidationEnabled)
        {
            await next(context);
            return;
        }

        var req = await context.GetHttpRequestDataAsync();
        if (req == null)
        {
            await next(context);
            return;
        }

        if (!MethodsRequiringCsrf.Contains(req.Method))
        {
            await next(context);
            return;
        }

        var requestPath = req.Url.AbsolutePath;
        if (IsCsrfExcludedPath(requestPath))
        {
            await next(context);
            return;
        }

        if (!req.Headers.TryGetValues("Authorization", out var authValues))
        {
            await next(context);
            return;
        }

        var authHeader = authValues.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var token = authHeader["Bearer ".Length..].Trim();
        var principal = _authService.ValidateToken(token);
        if (principal == null)
        {
            await next(context);
            return;
        }

        var userId = principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userId))
        {
            var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
            await unauthorized.WriteAsJsonAsync(new { error = "Authentication required" });
            context.GetInvocationResult().Value = unauthorized;
            return;
        }

        var csrfToken = req.Headers.TryGetValues("X-CSRF-Token", out var csrfValues)
            ? csrfValues.FirstOrDefault()
            : null;

        if (string.IsNullOrWhiteSpace(csrfToken) || !CsrfTokenManager.ValidateToken(csrfToken, userId))
        {
            var refreshedCsrfToken = CsrfTokenManager.GenerateToken(userId);
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            forbidden.Headers.Add("X-CSRF-Token", refreshedCsrfToken);
            await forbidden.WriteAsJsonAsync(new
            {
                error = "Invalid or missing CSRF token",
                code = "csrf_token_invalid",
                csrfToken = refreshedCsrfToken
            });
            context.GetInvocationResult().Value = forbidden;
            return;
        }

        await next(context);
    }

    private static bool IsCsrfExcludedPath(string requestPath)
    {
        if (ExcludedPaths.Contains(requestPath))
            return true;

        foreach (var pattern in ExcludedPathPatterns)
        {
            if (pattern.IsMatch(requestPath))
                return true;
        }

        return false;
    }
}
