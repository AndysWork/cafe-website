using System.Net;
using Cafe.Api.Helpers;
using Cafe.Api.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Cafe.Api.Functions;

/// <summary>
/// Security management endpoints for admins
/// </summary>
public class SecurityAdminFunction
{
    private readonly AuthService _auth;
    private readonly ILogger _log;

    public SecurityAdminFunction(AuthService auth, ILoggerFactory loggerFactory)
    {
        _auth = auth;
        _log = loggerFactory.CreateLogger<SecurityAdminFunction>();
    }

    // CSRF Token Management

    [Function("GenerateCsrfToken")]
    public async Task<HttpResponseData> GenerateCsrfToken(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "security/csrf/generate")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            var token = CsrfTokenManager.GenerateToken(userId!);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                success = true,
                csrfToken = token,
                expiresIn = 3600 // 60 minutes
            });

            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error generating CSRF token");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { success = false, error = "Failed to generate CSRF token" });
            return res;
        }
    }

    [Function("ValidateCsrfToken")]
    public async Task<HttpResponseData> ValidateCsrfToken(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "security/csrf/validate")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            var requestBody = await req.ReadFromJsonAsync<CsrfValidationRequest>();
            if (requestBody == null || string.IsNullOrWhiteSpace(requestBody.Token))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { success = false, error = "Token is required" });
                return badRequest;
            }

            var isValid = CsrfTokenManager.ValidateToken(requestBody.Token, userId!);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                success = true,
                valid = isValid
            });

            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error validating CSRF token");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { success = false, error = "Failed to validate CSRF token" });
            return res;
        }
    }

    // API Key Management

    [Function("GenerateApiKey")]
    public async Task<HttpResponseData> GenerateApiKey(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "security/apikeys/generate")] HttpRequestData req)
    {
        var auditLogger = new AuditLogger(_log);

        try
        {
            var (isAuthorized, userId, username, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            var requestBody = await req.ReadFromJsonAsync<ApiKeyGenerationRequest>();
            if (requestBody == null || string.IsNullOrWhiteSpace(requestBody.ServiceName))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { success = false, error = "Service name is required" });
                return badRequest;
            }

            var apiKey = ApiKeyManager.GenerateApiKey(requestBody.ServiceName, requestBody.Description ?? "");

            auditLogger.LogAdminAction(userId!, "Generate API Key", null, 
                $"Service: {requestBody.ServiceName}, Description: {requestBody.Description}");

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(new
            {
                success = true,
                apiKey = apiKey,
                serviceName = requestBody.ServiceName,
                expiresIn = 90 // days
            });

            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error generating API key");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { success = false, error = "Failed to generate API key" });
            return res;
        }
    }

    [Function("GetApiKeys")]
    public async Task<HttpResponseData> GetApiKeys(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "security/apikeys")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            var apiKeys = ApiKeyManager.GetAllApiKeys();
            var keysNeedingRotation = ApiKeyManager.GetKeysNeedingRotation();

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                success = true,
                apiKeys = apiKeys.Select(k => new
                {
                    key = k.Key.Substring(0, 15) + "...",
                    serviceName = k.ServiceName,
                    description = k.Description,
                    createdAt = k.CreatedAt,
                    expiresAt = k.ExpiresAt,
                    lastUsedAt = k.LastUsedAt,
                    isActive = k.IsActive,
                    requestCount = k.RequestCount,
                    needsRotation = keysNeedingRotation.Any(kr => kr.Key == k.Key)
                })
            });

            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error retrieving API keys");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { success = false, error = "Failed to retrieve API keys" });
            return res;
        }
    }

    [Function("RotateApiKey")]
    public async Task<HttpResponseData> RotateApiKey(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "security/apikeys/{key}/rotate")] HttpRequestData req, string key)
    {
        var auditLogger = new AuditLogger(_log);

        try
        {
            var (isAuthorized, userId, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            var (newKey, deprecationDate) = ApiKeyManager.RotateApiKey(key);

            auditLogger.LogAdminAction(userId!, "Rotate API Key", null, 
                $"Old key: {key.Substring(0, 10)}..., Deprecation date: {deprecationDate}");

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                success = true,
                newKey = newKey,
                oldKeyDeprecationDate = deprecationDate,
                message = "API key rotated successfully. Old key will expire on " + deprecationDate.ToString("yyyy-MM-dd")
            });

            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error rotating API key");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { success = false, error = ex.Message });
            return res;
        }
    }

    [Function("RevokeApiKey")]
    public async Task<HttpResponseData> RevokeApiKey(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "security/apikeys/{key}")] HttpRequestData req, string key)
    {
        var auditLogger = new AuditLogger(_log);

        try
        {
            var (isAuthorized, userId, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            ApiKeyManager.RevokeApiKey(key);

            auditLogger.LogAdminAction(userId!, "Revoke API Key", null, $"Key: {key.Substring(0, 10)}...");

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                success = true,
                message = "API key revoked successfully"
            });

            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error revoking API key");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { success = false, error = "Failed to revoke API key" });
            return res;
        }
    }

    // Audit Logs

    [Function("GetAuditLogs")]
    public async Task<HttpResponseData> GetAuditLogs(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "security/audit/logs")] HttpRequestData req)
    {
        var auditLogger = new AuditLogger(_log);

        try
        {
            var (isAuthorized, userId, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            // Parse query parameters
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var categoryStr = query["category"];
            var targetUserId = query["userId"];
            var startDateStr = query["startDate"];
            var endDateStr = query["endDate"];
            var maxResultsStr = query["maxResults"];

            AuditCategory? category = null;
            if (!string.IsNullOrEmpty(categoryStr) && Enum.TryParse<AuditCategory>(categoryStr, true, out var cat))
                category = cat;

            DateTime? startDate = null;
            if (!string.IsNullOrEmpty(startDateStr) && DateTime.TryParse(startDateStr, out var sd))
                startDate = sd;

            DateTime? endDate = null;
            if (!string.IsNullOrEmpty(endDateStr) && DateTime.TryParse(endDateStr, out var ed))
                endDate = ed;

            int maxResults = 100;
            if (!string.IsNullOrEmpty(maxResultsStr) && int.TryParse(maxResultsStr, out var mr))
                maxResults = Math.Min(mr, 1000); // Cap at 1000

            var logs = auditLogger.GetLogs(category, targetUserId, startDate, endDate, maxResults);

            auditLogger.LogAdminAction(userId!, "View Audit Logs", null, 
                $"Category: {category}, Count: {logs.Count}");

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                success = true,
                count = logs.Count,
                logs = logs
            });

            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error retrieving audit logs");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { success = false, error = "Failed to retrieve audit logs" });
            return res;
        }
    }

    [Function("GetSecurityAlerts")]
    public async Task<HttpResponseData> GetSecurityAlerts(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "security/audit/alerts")] HttpRequestData req)
    {
        var auditLogger = new AuditLogger(_log);

        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var hoursStr = query["hours"];
            int hours = 24;
            if (!string.IsNullOrEmpty(hoursStr) && int.TryParse(hoursStr, out var h))
                hours = h;

            var alerts = auditLogger.GetSecurityAlerts(hours);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                success = true,
                count = alerts.Count,
                alerts = alerts,
                period = $"Last {hours} hours"
            });

            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error retrieving security alerts");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { success = false, error = "Failed to retrieve security alerts" });
            return res;
        }
    }

    [Function("ExportAuditLogs")]
    public async Task<HttpResponseData> ExportAuditLogs(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "security/audit/export")] HttpRequestData req)
    {
        var auditLogger = new AuditLogger(_log);

        try
        {
            var (isAuthorized, userId, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            var requestBody = await req.ReadFromJsonAsync<AuditExportRequest>();
            if (requestBody == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { success = false, error = "Invalid request" });
                return badRequest;
            }

            var exportData = await auditLogger.ExportLogs(
                requestBody.StartDate,
                requestBody.EndDate,
                requestBody.Format ?? "json");

            auditLogger.LogAdminAction(userId!, "Export Audit Logs", null,
                $"Format: {requestBody.Format}, Period: {requestBody.StartDate:yyyy-MM-dd} to {requestBody.EndDate:yyyy-MM-dd}");

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", 
                requestBody.Format?.ToLower() == "csv" ? "text/csv" : "application/json");
            response.Headers.Add("Content-Disposition", 
                $"attachment; filename=audit-logs-{MongoService.GetIstNow():yyyyMMdd-HHmmss}.{requestBody.Format ?? "json"}");
            
            await response.WriteStringAsync(exportData);

            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error exporting audit logs");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { success = false, error = ex.Message });
            return res;
        }
    }
}

// Request DTOs
public class CsrfValidationRequest
{
    public string Token { get; set; } = string.Empty;
}

public class ApiKeyGenerationRequest
{
    public string ServiceName { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class AuditExportRequest
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string? Format { get; set; } = "json"; // json or csv
}
