using System.Net;
using Cafe.Api.Models;
using Cafe.Api.Services;
using Cafe.Api.Repositories;
using Cafe.Api.Helpers;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;

namespace Cafe.Api.Functions;

public class StaffQueryFunction
{
    private readonly IStaffRepository _mongo;
    private readonly AuthService _auth;
    private readonly ILogger _log;

    public StaffQueryFunction(IStaffRepository mongo, AuthService auth, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _auth = auth;
        _log = loggerFactory.CreateLogger<StaffQueryFunction>();
    }

    /// <summary>
    /// Get all staff members (Admin only)
    /// </summary>
    [Function("GetAllStaff")]
    [OpenApiOperation(operationId: "GetAllStaff", tags: new[] { "Staff" }, Summary = "Get all staff members", Description = "Retrieves all staff members (Admin only)")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "activeOnly", In = ParameterLocation.Query, Required = false, Type = typeof(bool), Description = "Filter to show only active staff")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(List<Staff>), Description = "Successfully retrieved staff members")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "User not authenticated")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Forbidden, Description = "User not authorized (admin role required)")]
    public async Task<HttpResponseData> GetAllStaff(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "staff")] HttpRequestData req)
    {
        var (isAuthorized, adminUserId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
        if (!isAuthorized) return errorResponse!;

        try
        {
            var activeOnlyParam = req.Query["activeOnly"];
            var activeOnly = !string.IsNullOrEmpty(activeOnlyParam) && bool.Parse(activeOnlyParam);

            var (page, pageSize) = PaginationHelper.ParsePagination(req);
            var staff = activeOnly 
                ? await _mongo.GetActiveStaffAsync() 
                : await _mongo.GetAllStaffAsync(page, pageSize);

            var response = req.CreateResponse(HttpStatusCode.OK);
            if (!activeOnly && page.HasValue && pageSize.HasValue)
            {
                var totalCount = await _mongo.GetAllStaffCountAsync();
                PaginationHelper.AddPaginationHeaders(response, totalCount, page.Value, pageSize.Value);
            }
            await response.WriteAsJsonAsync(new { success = true, data = staff });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting staff members");
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { error = "Failed to retrieve staff members" });
            return errorRes;
        }
    }

    /// <summary>
    /// Get staff statistics (Admin only)
    /// </summary>
    [Function("GetStaffStatistics")]
    [OpenApiOperation(operationId: "GetStaffStatistics", tags: new[] { "Staff" }, Summary = "Get staff statistics", Description = "Retrieves comprehensive staff statistics (Admin only)")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(StaffStatistics), Description = "Successfully retrieved statistics")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "User not authenticated")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Forbidden, Description = "User not authorized")]
    public async Task<HttpResponseData> GetStaffStatistics(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "staff/statistics")] HttpRequestData req)
    {
        var (isAuthorized, adminUserId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
        if (!isAuthorized) return errorResponse!;

        try
        {
            var statistics = await _mongo.GetStaffStatisticsAsync();

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, data = statistics });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting staff statistics");
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { error = "Failed to retrieve statistics" });
            return errorRes;
        }
    }

    /// <summary>
    /// Get staff member by ID (Admin only)
    /// </summary>
    [Function("GetStaffById")]
    [OpenApiOperation(operationId: "GetStaffById", tags: new[] { "Staff" }, Summary = "Get staff member by ID", Description = "Retrieves a specific staff member by ID (Admin only)")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "staffId", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Staff member ID")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(Staff), Description = "Successfully retrieved staff member")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.NotFound, Description = "Staff member not found")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "User not authenticated")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Forbidden, Description = "User not authorized")]
    public async Task<HttpResponseData> GetStaffById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "staff/{staffId:length(24)}")] HttpRequestData req,
        string staffId)
    {
        var (isAuthorized, adminUserId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
        if (!isAuthorized) return errorResponse!;

        // Validate staffId is a valid ObjectId format (24 character hex string)
        if (string.IsNullOrEmpty(staffId) || staffId.Length != 24 || !System.Text.RegularExpressions.Regex.IsMatch(staffId, "^[0-9a-fA-F]{24}$"))
        {
            var badRequestRes = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequestRes.WriteAsJsonAsync(new { error = "Invalid staff ID format" });
            return badRequestRes;
        }

        try
        {
            var staff = await _mongo.GetStaffByIdAsync(staffId);
            if (staff == null)
            {
                var notFoundRes = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundRes.WriteAsJsonAsync(new { error = "Staff member not found" });
                return notFoundRes;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, data = staff });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting staff member {StaffId}", staffId);
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { error = "Failed to retrieve staff member" });
            return errorRes;
        }
    }

    /// <summary>
    /// Get staff members by outlet (Admin only)
    /// </summary>
    [Function("GetStaffByOutlet")]
    [OpenApiOperation(operationId: "GetStaffByOutlet", tags: new[] { "Staff" }, Summary = "Get staff by outlet", Description = "Retrieves staff members assigned to a specific outlet (Admin only)")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "outletId", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Outlet ID")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(List<Staff>), Description = "Successfully retrieved staff members")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "User not authenticated")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Forbidden, Description = "User not authorized")]
    public async Task<HttpResponseData> GetStaffByOutlet(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "staff/outlet/{outletId}")] HttpRequestData req,
        string outletId)
    {
        var (isAuthorized, adminUserId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
        if (!isAuthorized) return errorResponse!;

        try
        {
            var staff = await _mongo.GetStaffByOutletAsync(outletId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, data = staff });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting staff for outlet {OutletId}", outletId);
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { error = "Failed to retrieve staff members" });
            return errorRes;
        }
    }

    /// <summary>
    /// Search staff members (Admin only)
    /// </summary>
    [Function("SearchStaff")]
    [OpenApiOperation(operationId: "SearchStaff", tags: new[] { "Staff" }, Summary = "Search staff members", Description = "Search staff members by name, email, or employee ID (Admin only)")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "q", In = ParameterLocation.Query, Required = true, Type = typeof(string), Description = "Search term")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(List<Staff>), Description = "Successfully retrieved search results")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.BadRequest, Description = "Search term is required")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "User not authenticated")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Forbidden, Description = "User not authorized")]
    public async Task<HttpResponseData> SearchStaff(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "staff/search")] HttpRequestData req)
    {
        var (isAuthorized, adminUserId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
        if (!isAuthorized) return errorResponse!;

        try
        {
            var searchTerm = req.Query["q"];
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                var badReqRes = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReqRes.WriteAsJsonAsync(new { error = "Search term is required" });
                return badReqRes;
            }

            var staff = await _mongo.SearchStaffAsync(searchTerm);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, data = staff });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error searching staff");
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { error = "Failed to search staff members" });
            return errorRes;
        }
    }

    /// <summary>
    /// Get all shifts for a staff member
    /// </summary>
    [Function("GetStaffShifts")]
    [OpenApiOperation(operationId: "GetStaffShifts", tags: new[] { "Staff" }, Summary = "Get staff shifts", Description = "Retrieves all shifts for a staff member")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "staffId", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Staff ID")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(List<StaffShift>), Description = "Successfully retrieved shifts")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.NotFound, Description = "Staff member not found")]
    public async Task<HttpResponseData> GetStaffShifts(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "staff/{staffId:length(24)}/shifts")] HttpRequestData req,
        string staffId)
    {
        var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
        if (!isAuthorized) return errorResponse!;

        try
        {
            var staff = await _mongo.GetStaffByIdAsync(staffId);
            if (staff == null)
            {
                var notFoundRes = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundRes.WriteAsJsonAsync(new { error = "Staff member not found" });
                return notFoundRes;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, data = staff.Shifts });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting shifts for staff {StaffId}", staffId);
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { error = "Failed to retrieve shifts" });
            return errorRes;
        }
    }
}
