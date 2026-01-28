using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using Cafe.Api.Models;
using Cafe.Api.Services;
using Cafe.Api.Helpers;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.OpenApi.Models;

namespace Cafe.Api.Functions;

public class DailyPerformanceFunction
{
    private readonly ILogger<DailyPerformanceFunction> _logger;
    private readonly MongoService _mongo;
    private readonly AuthService _auth;

    public DailyPerformanceFunction(
        ILogger<DailyPerformanceFunction> logger,
        MongoService mongo,
        AuthService auth)
    {
        _logger = logger;
        _mongo = mongo;
        _auth = auth;
    }

    [Function("GetDailyPerformanceByDate")]
    [OpenApiOperation(operationId: "GetDailyPerformanceByDate", tags: new[] { "DailyPerformance" })]
    [OpenApiParameter(name: "date", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Date in YYYY-MM-DD format")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(List<DailyPerformanceEntry>), Description = "The OK response with daily performance entries")]
    public async Task<HttpResponseData> GetDailyPerformanceByDate(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "dailyperformance/date/{date}")] HttpRequestData req,
        string date)
    {
        _logger.LogInformation($"Getting daily performance for date: {date}");

        try
        {
            // Validate JWT and get user info
            var (isAuthorized, adminUserId, user, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            // Get outlet ID for the admin
            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);
            if (string.IsNullOrEmpty(outletId))
            {
                var forbiddenRes = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbiddenRes.WriteAsJsonAsync(new { success = false, error = "Outlet ID not found for the user" });
                return forbiddenRes;
            }

            var entries = await _mongo.GetDailyPerformanceByDateAsync(date, outletId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(entries);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting daily performance by date");
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { success = false, error = ex.Message });
            return errorRes;
        }
    }

    [Function("GetDailyPerformanceByStaff")]
    [OpenApiOperation(operationId: "GetDailyPerformanceByStaff", tags: new[] { "DailyPerformance" })]
    [OpenApiParameter(name: "staffId", In = ParameterLocation.Path, Required = true, Type = typeof(string))]
    [OpenApiParameter(name: "startDate", In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "Start date in YYYY-MM-DD format")]
    [OpenApiParameter(name: "endDate", In = ParameterLocation.Query, Required = false, Type = typeof(string), Description = "End date in YYYY-MM-DD format")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(List<DailyPerformanceEntry>), Description = "The OK response with daily performance entries")]
    public async Task<HttpResponseData> GetDailyPerformanceByStaff(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "dailyperformance/staff/{staffId}")] HttpRequestData req,
        string staffId)
    {
        _logger.LogInformation($"Getting daily performance for staff: {staffId}");

        try
        {
            // Validate JWT
            var (isAuthorized, adminUserId, user, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            // Get query parameters
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var startDate = query["startDate"];
            var endDate = query["endDate"];

            var entries = await _mongo.GetDailyPerformanceByStaffAsync(staffId, startDate, endDate);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(entries);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting daily performance by staff");
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { success = false, error = ex.Message });
            return errorRes;
        }
    }

    [Function("GetDailyPerformanceByDateRange")]
    [OpenApiOperation(operationId: "GetDailyPerformanceByDateRange", tags: new[] { "DailyPerformance" })]
    [OpenApiParameter(name: "startDate", In = ParameterLocation.Query, Required = true, Type = typeof(string), Description = "Start date in YYYY-MM-DD format")]
    [OpenApiParameter(name: "endDate", In = ParameterLocation.Query, Required = true, Type = typeof(string), Description = "End date in YYYY-MM-DD format")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(List<DailyPerformanceEntry>), Description = "The OK response with daily performance entries")]
    public async Task<HttpResponseData> GetDailyPerformanceByDateRange(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "dailyperformance/range")] HttpRequestData req)
    {
        _logger.LogInformation("Getting daily performance for date range");

        try
        {
            // Validate JWT and get user info
            var (isAuthorized, adminUserId, user, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            // Get outlet ID for the admin
            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);
            if (string.IsNullOrEmpty(outletId))
            {
                var forbiddenRes = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbiddenRes.WriteAsJsonAsync(new { success = false, error = "Outlet ID not found for the user" });
                return forbiddenRes;
            }

            // Get query parameters
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var startDate = query["startDate"];
            var endDate = query["endDate"];

            if (string.IsNullOrEmpty(startDate) || string.IsNullOrEmpty(endDate))
            {
                var badReqRes = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReqRes.WriteAsJsonAsync(new { success = false, error = "Start date and end date are required" });
                return badReqRes;
            }

            var entries = await _mongo.GetDailyPerformanceByDateRangeAsync(startDate, endDate, outletId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(entries);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting daily performance by date range");
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { success = false, error = ex.Message });
            return errorRes;
        }
    }

    [Function("UpsertDailyPerformance")]
    [OpenApiOperation(operationId: "UpsertDailyPerformance", tags: new[] { "DailyPerformance" })]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(UpsertDailyPerformanceRequest), Description = "Daily performance entry data", Required = true)]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(DailyPerformanceEntry), Description = "The OK response with created/updated entry")]
    public async Task<HttpResponseData> UpsertDailyPerformance(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "dailyperformance")] HttpRequestData req)
    {
        _logger.LogInformation("Upserting daily performance entry");

        try
        {
            // Validate JWT and get user info
            var (isAuthorized, adminUserId, user, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            // Get outlet ID for the admin
            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);
            if (string.IsNullOrEmpty(outletId))
            {
                var forbiddenRes = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbiddenRes.WriteAsJsonAsync(new { success = false, error = "Outlet ID not found for the user" });
                return forbiddenRes;
            }

            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var request = JsonSerializer.Deserialize<UpsertDailyPerformanceRequest>(
                requestBody,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (request == null)
            {
                var badReqRes = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReqRes.WriteAsJsonAsync(new { success = false, error = "Invalid request body" });
                return badReqRes;
            }

            // Validate request
            if (string.IsNullOrEmpty(request.StaffId) || 
                string.IsNullOrEmpty(request.Date) ||
                string.IsNullOrEmpty(request.InTime) ||
                string.IsNullOrEmpty(request.OutTime))
            {
                var validationRes = req.CreateResponse(HttpStatusCode.BadRequest);
                await validationRes.WriteAsJsonAsync(new { success = false, error = "Staff ID, date, in time, and out time are required" });
                return validationRes;
            }

            var entry = await _mongo.UpsertDailyPerformanceAsync(request, outletId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(entry);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error upserting daily performance");
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { success = false, error = ex.Message });
            return errorRes;
        }
    }

    [Function("BulkUpsertDailyPerformance")]
    [OpenApiOperation(operationId: "BulkUpsertDailyPerformance", tags: new[] { "DailyPerformance" })]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(BulkDailyPerformanceRequest), Description = "Bulk daily performance data", Required = true)]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(List<DailyPerformanceEntry>), Description = "The OK response with created/updated entries")]
    public async Task<HttpResponseData> BulkUpsertDailyPerformance(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "dailyperformance/bulk")] HttpRequestData req)
    {
        _logger.LogInformation("Bulk upserting daily performance entries");

        try
        {
            // Validate JWT and get user info
            var (isAuthorized, adminUserId, user, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            // Get outlet ID for the admin
            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);
            if (string.IsNullOrEmpty(outletId))
            {
                var forbiddenRes = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbiddenRes.WriteAsJsonAsync(new { success = false, error = "Outlet ID not found for the user" });
                return forbiddenRes;
            }

            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var request = JsonSerializer.Deserialize<BulkDailyPerformanceRequest>(
                requestBody,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (request == null || request.Entries == null || !request.Entries.Any())
            {
                var badReqRes = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReqRes.WriteAsJsonAsync(new { success = false, error = "Invalid request body or no entries provided" });
                return badReqRes;
            }

            var entries = await _mongo.BulkUpsertDailyPerformanceAsync(request, outletId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(entries);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error bulk upserting daily performance");
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { success = false, error = ex.Message });
            return errorRes;
        }
    }

    [Function("DeleteDailyPerformance")]
    [OpenApiOperation(operationId: "DeleteDailyPerformance", tags: new[] { "DailyPerformance" })]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Entry ID")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "The OK response")]
    public async Task<HttpResponseData> DeleteDailyPerformance(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "dailyperformance/{id}")] HttpRequestData req,
        string id)
    {
        _logger.LogInformation($"Deleting daily performance entry: {id}");

        try
        {
            // Validate JWT
            var (isAuthorized, adminUserId, user, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var success = await _mongo.DeleteDailyPerformanceAsync(id);

            if (!success)
            {
                var notFoundRes = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundRes.WriteAsJsonAsync(new { success = false, error = "Entry not found" });
                return notFoundRes;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Entry deleted successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting daily performance");
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { success = false, error = ex.Message });
            return errorRes;
        }
    }
}
