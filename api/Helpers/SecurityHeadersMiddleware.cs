using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;

namespace Cafe.Api.Helpers;

/// <summary>
/// Middleware to add security headers to all HTTP responses
/// </summary>
public class SecurityHeadersMiddleware : IFunctionsWorkerMiddleware
{
    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        await next(context);

        var httpResponse = context.GetHttpResponseData();
        if (httpResponse != null)
        {
            // Prevent clickjacking attacks
            httpResponse.Headers.Add("X-Frame-Options", "DENY");

            // Prevent MIME type sniffing
            httpResponse.Headers.Add("X-Content-Type-Options", "nosniff");

            // Enable XSS protection in browsers
            httpResponse.Headers.Add("X-XSS-Protection", "1; mode=block");

            // Content Security Policy - restrict resource loading
            httpResponse.Headers.Add("Content-Security-Policy",
                "default-src 'self'; " +
                "script-src 'self' 'unsafe-inline' 'unsafe-eval'; " +
                "style-src 'self' 'unsafe-inline'; " +
                "img-src 'self' data: https:; " +
                "font-src 'self' data:; " +
                "connect-src 'self'; " +
                "frame-ancestors 'none'");

            // Referrer Policy - control referrer information
            httpResponse.Headers.Add("Referrer-Policy", "strict-origin-when-cross-origin");

            // Permissions Policy - control browser features
            httpResponse.Headers.Add("Permissions-Policy",
                "geolocation=(), " +
                "microphone=(), " +
                "camera=(), " +
                "payment=(), " +
                "usb=(), " +
                "magnetometer=()");

            // HSTS - enforce HTTPS (only in production)
            // Uncomment when using HTTPS in production
            // httpResponse.Headers.Add("Strict-Transport-Security", "max-age=31536000; includeSubDomains");

            // Remove server information header
            httpResponse.Headers.Remove("Server");
            httpResponse.Headers.Remove("X-Powered-By");

            // CORS headers are already handled by Azure Functions, but we ensure they're strict
            // These will be overridden by CORS configuration in Program.cs
        }
    }
}
