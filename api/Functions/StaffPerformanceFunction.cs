using System.Net;
using Cafe.Api.Models;
using Cafe.Api.Services;
using Cafe.Api.Helpers;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;

namespace Cafe.Api.Functions;

public class StaffPerformanceFunction
{
    private readonly MongoService _mongo;
    private readonly AuthService _auth;
    private readonly ILogger _log;

    public StaffPerformanceFunction(MongoService mongo, AuthService auth, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _auth = auth;
        _log = loggerFactory.CreateLogger<StaffPerformanceFunction>();
    }

    /// <summary>
    /// Get staff performance records for a period (Admin only)
    /// </summary>
    [Function("GetStaffPerformanceRecords")]
    [OpenApiOperation(operationId: "GetStaffPerformanceRecords", tags: new[] { "Staff Performance" }, Summary = "Get performance records", Description = "Retrieves staff performance records for a specific period (Admin only)")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "staffId", In = ParameterLocation.Query, Required = true, Type = typeof(string), Description = "Staff member ID")]
    [OpenApiParameter(name: "period", In = ParameterLocation.Query, Required = true, Type = typeof(string), Description = "Period (e.g., '2026-01' for month, '2026-W04' for week)")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(List<StaffPerformanceRecord>), Description = "Successfully retrieved records")]
    public async Task<HttpResponseData> GetStaffPerformanceRecords(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "staff-performance")] HttpRequestData req)
    {
        var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
        if (!isAuthorized) return errorResponse!;

        try
        {
            var staffId = req.Query["staffId"];
            var period = req.Query["period"];

            if (string.IsNullOrEmpty(staffId) || string.IsNullOrEmpty(period))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { success = false, error = "staffId and period are required" });
                return badReq;
            }

            var records = await _mongo.GetStaffPerformanceRecordsAsync(staffId, period);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, data = records });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting performance records");
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to retrieve records" });
            return errorRes;
        }
    }

    /// <summary>
    /// Get outlet performance records for a period (Admin only)
    /// </summary>
    [Function("GetOutletPerformanceRecords")]
    [OpenApiOperation(operationId: "GetOutletPerformanceRecords", tags: new[] { "Staff Performance" }, Summary = "Get outlet performance", Description = "Retrieves all staff performance records for an outlet in a period (Admin only)")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "period", In = ParameterLocation.Query, Required = true, Type = typeof(string), Description = "Period (e.g., '2026-01')")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(List<StaffPerformanceRecord>), Description = "Successfully retrieved records")]
    public async Task<HttpResponseData> GetOutletPerformanceRecords(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "staff-performance/outlet")] HttpRequestData req)
    {
        var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
        if (!isAuthorized) return errorResponse!;

        try
        {
            var period = req.Query["period"];
            if (string.IsNullOrEmpty(period))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { success = false, error = "period is required" });
                return badReq;
            }

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);
            var records = await _mongo.GetOutletPerformanceRecordsAsync(outletId, period);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, data = records });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting outlet performance records");
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to retrieve records" });
            return errorRes;
        }
    }

    /// <summary>
    /// Create or update staff performance record (Admin only)
    /// </summary>
    [Function("UpsertStaffPerformanceRecord")]
    [OpenApiOperation(operationId: "UpsertStaffPerformanceRecord", tags: new[] { "Staff Performance" }, Summary = "Create/update performance record", Description = "Creates or updates staff performance record (Admin only)")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(StaffPerformanceRecord), Required = true, Description = "Performance record details")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(StaffPerformanceRecord), Description = "Record saved successfully")]
    public async Task<HttpResponseData> UpsertStaffPerformanceRecord(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "staff-performance")] HttpRequestData req)
    {
        var (isAuthorized, adminUserId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
        if (!isAuthorized) return errorResponse!;

        try
        {
            var (record, validationError) = await ValidationHelper.ValidateBody<StaffPerformanceRecord>(req);
            if (validationError != null) return validationError;

            // Validate outlet access
            var (hasAccess, outletId, accessError) = await OutletHelper.ValidateOutletAccess(req, _auth, _mongo);
            if (!hasAccess)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = accessError });
                return forbidden;
            }

            record.OutletId = outletId;
            
            // Calculate overtime/undertime
            if (record.ActualHours > record.ScheduledHours)
                record.OvertimeHours = record.ActualHours - record.ScheduledHours;
            else if (record.ActualHours < record.ScheduledHours)
                record.UndertimeHours = record.ScheduledHours - record.ActualHours;

            var saved = await _mongo.UpsertStaffPerformanceRecordAsync(record);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, data = saved });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error upserting performance record");
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to save record" });
            return errorRes;
        }
    }

    /// <summary>
    /// Calculate bonus for staff performance record (Admin only)
    /// </summary>
    [Function("CalculateStaffBonus")]
    [OpenApiOperation(operationId: "CalculateStaffBonus", tags: new[] { "Staff Performance" }, Summary = "Calculate bonus", Description = "Calculates bonus/deductions for a performance record (Admin only)")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "recordId", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Performance record ID")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(StaffPerformanceRecord), Description = "Bonus calculated successfully")]
    public async Task<HttpResponseData> CalculateStaffBonus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "staff-performance/{recordId}/calculate")] HttpRequestData req,
        string recordId)
    {
        var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
        if (!isAuthorized) return errorResponse!;

        try
        {
            var calculated = await _mongo.CalculateStaffBonusAsync(recordId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, data = calculated });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error calculating bonus for record {RecordId}", recordId);
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { success = false, error = "An internal error occurred" });
            return errorRes;
        }
    }
}
