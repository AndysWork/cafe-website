using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;

namespace Cafe.Api.Helpers;

/// <summary>
/// Middleware to handle API versioning via header (X-API-Version) or query parameter (api-version).
/// Current API version: 1.0
/// </summary>
public class ApiVersionMiddleware : IFunctionsWorkerMiddleware
{
    public const string CurrentVersion = "1.0";
    public const string VersionHeader = "X-API-Version";
    public const string VersionQueryParam = "api-version";

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        await next(context);

        var httpResponse = context.GetHttpResponseData();
        if (httpResponse != null)
        {
            httpResponse.Headers.Add(VersionHeader, CurrentVersion);
        }
    }
}
