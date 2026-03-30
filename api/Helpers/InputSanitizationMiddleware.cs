using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;

namespace Cafe.Api.Helpers;

/// <summary>
/// Global middleware that blocks requests containing potentially dangerous content.
/// Acts as a security firewall for XSS, script injection, and other attacks
/// before request bodies reach function handlers.
/// </summary>
public class InputSanitizationMiddleware : IFunctionsWorkerMiddleware
{
    private readonly ILogger<InputSanitizationMiddleware> _logger;

    public InputSanitizationMiddleware(ILogger<InputSanitizationMiddleware> logger)
    {
        _logger = logger;
    }

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var requestData = await context.GetHttpRequestDataAsync();
        if (requestData == null)
        {
            // Non-HTTP trigger (timer, warmup, etc.) — skip validation
            await next(context);
            return;
        }

        // Check query parameters for dangerous content
        var queryString = requestData.Url.Query;
        if (!string.IsNullOrEmpty(queryString))
        {
            var queryParams = System.Web.HttpUtility.ParseQueryString(queryString);
            foreach (string? key in queryParams.AllKeys)
            {
                var value = key != null ? queryParams[key] : null;
                if (value != null && InputSanitizer.IsPotentiallyDangerous(value))
                {
                    _logger.LogWarning("Blocked request with dangerous query parameter for {Function}",
                        context.FunctionDefinition.Name);
                    await RejectRequest(requestData, context);
                    return;
                }
            }
        }

        // Check request body for write operations (POST/PUT/PATCH)
        var method = requestData.Method.ToUpperInvariant();
        if (method is "POST" or "PUT" or "PATCH")
        {
            if (requestData.Body != null && requestData.Body.CanRead && requestData.Body.CanSeek)
            {
                try
                {
                    var originalPosition = requestData.Body.Position;
                    requestData.Body.Position = 0;

                    using var reader = new StreamReader(requestData.Body, leaveOpen: true);
                    var body = await reader.ReadToEndAsync();

                    // Reset stream position so the function can still read the body
                    requestData.Body.Position = originalPosition;

                    if (!string.IsNullOrEmpty(body) && InputSanitizer.IsPotentiallyDangerous(body))
                    {
                        _logger.LogWarning("Blocked request with dangerous body content for {Function}",
                            context.FunctionDefinition.Name);
                        await RejectRequest(requestData, context);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    // Don't block the request on body read failure — let the function handle it
                    _logger.LogDebug(ex, "Could not read request body for sanitization check");
                }
            }
        }

        await next(context);
    }

    private static async Task RejectRequest(HttpRequestData requestData, FunctionContext context)
    {
        var response = requestData.CreateResponse(HttpStatusCode.BadRequest);
        await response.WriteAsJsonAsync(new { success = false, error = "Invalid input detected" });
        context.GetInvocationResult().Value = response;
    }
}
