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
        "/api/payments/webhook/razorpay"
    };

    private readonly AuthService _authService;

    public CsrfValidationMiddleware(AuthService authService)
    {
        _authService = authService;
    }

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
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
        if (ExcludedPaths.Contains(requestPath))
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
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            await forbidden.WriteAsJsonAsync(new { error = "Invalid or missing CSRF token" });
            context.GetInvocationResult().Value = forbidden;
            return;
        }

        await next(context);
    }
}
