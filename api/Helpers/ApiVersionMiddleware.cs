using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;

namespace Cafe.Api.Helpers;

/// <summary>
/// Middleware to handle API versioning via header (X-API-Version) or query parameter (api-version).
/// Supports version negotiation: clients can request a specific version.
/// Unrecognized versions are rejected; sunset versions return a deprecation warning.
/// </summary>
public class ApiVersionMiddleware : IFunctionsWorkerMiddleware
{
    public const string CurrentVersion = "1.0";
    public const string VersionHeader = "X-API-Version";
    public const string VersionQueryParam = "api-version";

    // Supported versions — add new entries when releasing a new API version
    private static readonly HashSet<string> SupportedVersions = new() { "1.0" };

    // Deprecated but still functional versions — clients get a Sunset + Deprecation header
    private static readonly Dictionary<string, string> DeprecatedVersions = new()
    {
        // Example: { "0.9", "2026-06-01" }  — sunset date in RFC 7231 format
    };

    private readonly ILogger<ApiVersionMiddleware> _logger;

    public ApiVersionMiddleware(ILogger<ApiVersionMiddleware> logger)
    {
        _logger = logger;
    }

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var requestData = await context.GetHttpRequestDataAsync();
        if (requestData == null)
        {
            await next(context);
            return;
        }

        // Resolve requested version from header or query param (default to current)
        var requestedVersion = ResolveVersion(requestData);

        // Reject unsupported versions
        if (requestedVersion != null
            && !SupportedVersions.Contains(requestedVersion)
            && !DeprecatedVersions.ContainsKey(requestedVersion))
        {
            _logger.LogWarning("Rejected request with unsupported API version: {Version}", requestedVersion);
            var response = requestData.CreateResponse(HttpStatusCode.BadRequest);
            await response.WriteAsJsonAsync(new
            {
                success = false,
                error = $"Unsupported API version '{requestedVersion}'. Supported versions: {string.Join(", ", SupportedVersions)}",
                currentVersion = CurrentVersion
            });
            context.GetInvocationResult().Value = response;
            return;
        }

        await next(context);

        // Add version headers to every response
        var httpResponse = context.GetHttpResponseData();
        if (httpResponse != null)
        {
            httpResponse.Headers.Add(VersionHeader, CurrentVersion);
            httpResponse.Headers.Add("X-API-Supported-Versions", string.Join(", ", SupportedVersions));

            // Add deprecation warning if client is using a deprecated version
            if (requestedVersion != null && DeprecatedVersions.TryGetValue(requestedVersion, out var sunsetDate))
            {
                httpResponse.Headers.Add("Sunset", sunsetDate);
                httpResponse.Headers.Add("Deprecation", "true");
                httpResponse.Headers.Add("X-API-Deprecation-Warning",
                    $"API version {requestedVersion} is deprecated and will be removed on {sunsetDate}. Please migrate to v{CurrentVersion}.");
            }
        }
    }

    private static string? ResolveVersion(HttpRequestData request)
    {
        // 1. Check header
        if (request.Headers.TryGetValues(VersionHeader, out var headerValues))
        {
            var version = headerValues.FirstOrDefault()?.Trim();
            if (!string.IsNullOrEmpty(version)) return version;
        }

        // 2. Check query parameter
        var query = System.Web.HttpUtility.ParseQueryString(request.Url.Query);
        var queryVersion = query[VersionQueryParam];
        if (!string.IsNullOrEmpty(queryVersion)) return queryVersion.Trim();

        // No explicit version requested — use default (current)
        return null;
    }
}
