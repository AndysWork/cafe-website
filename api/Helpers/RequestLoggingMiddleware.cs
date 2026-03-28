using System.Diagnostics;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;

namespace Cafe.Api.Helpers;

public class RequestLoggingMiddleware : IFunctionsWorkerMiddleware
{
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(ILogger<RequestLoggingMiddleware> logger)
    {
        _logger = logger;
    }

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var sw = Stopwatch.StartNew();
        var functionName = context.FunctionDefinition.Name;
        var invocationId = context.InvocationId;

        var httpRequest = await context.GetHttpRequestDataAsync();
        var method = httpRequest?.Method ?? "N/A";
        var url = httpRequest?.Url?.PathAndQuery ?? "N/A";

        _logger.LogInformation("Request started: {Method} {Url} | Function: {FunctionName} | InvocationId: {InvocationId}",
            method, url, functionName, invocationId);

        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Request failed: {Method} {Url} | Function: {FunctionName} | Duration: {Duration}ms | InvocationId: {InvocationId}",
                method, url, functionName, sw.ElapsedMilliseconds, invocationId);
            throw;
        }

        sw.Stop();
        var httpResponse = context.GetHttpResponseData();
        var statusCode = httpResponse?.StatusCode.ToString() ?? "N/A";

        _logger.LogInformation("Request completed: {Method} {Url} | Status: {StatusCode} | Duration: {Duration}ms | Function: {FunctionName} | InvocationId: {InvocationId}",
            method, url, statusCode, sw.ElapsedMilliseconds, functionName, invocationId);
    }
}
