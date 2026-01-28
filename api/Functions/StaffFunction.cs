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
using System.Text.Json;

namespace Cafe.Api.Functions;

public class StaffFunction
{
    private readonly MongoService _mongo;
    private readonly AuthService _auth;
    private readonly IEmailService _emailService;
    private readonly ILogger _log;

    public StaffFunction(MongoService mongo, AuthService auth, IEmailService emailService, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _auth = auth;
        _emailService = emailService;
        _log = loggerFactory.CreateLogger<StaffFunction>();
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

            var staff = activeOnly 
                ? await _mongo.GetActiveStaffAsync() 
                : await _mongo.GetAllStaffAsync();

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, data = staff });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting staff members");
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to retrieve staff members" });
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
            await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to retrieve statistics" });
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
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "staff/{staffId}")] HttpRequestData req,
        string staffId)
    {
        var (isAuthorized, adminUserId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
        if (!isAuthorized) return errorResponse!;

        // Validate staffId is a valid ObjectId format (24 character hex string)
        if (string.IsNullOrEmpty(staffId) || staffId.Length != 24 || !System.Text.RegularExpressions.Regex.IsMatch(staffId, "^[0-9a-fA-F]{24}$"))
        {
            var badRequestRes = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequestRes.WriteAsJsonAsync(new { success = false, error = "Invalid staff ID format" });
            return badRequestRes;
        }

        try
        {
            var staff = await _mongo.GetStaffByIdAsync(staffId);
            if (staff == null)
            {
                var notFoundRes = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundRes.WriteAsJsonAsync(new { success = false, error = "Staff member not found" });
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
            await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to retrieve staff member" });
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
            await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to retrieve staff members" });
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
                await badReqRes.WriteAsJsonAsync(new { success = false, error = "Search term is required" });
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
            await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to search staff members" });
            return errorRes;
        }
    }

    /// <summary>
    /// Create a new staff member (Admin only)
    /// </summary>
    [Function("CreateStaff")]
    [OpenApiOperation(operationId: "CreateStaff", tags: new[] { "Staff" }, Summary = "Create new staff member", Description = "Creates a new staff member (Admin only)")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(Staff), Required = true, Description = "Staff member details")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Created, contentType: "application/json", bodyType: typeof(Staff), Description = "Staff member created successfully")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.BadRequest, Description = "Invalid request data")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Conflict, Description = "Staff member with this Employee ID or Email already exists")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "User not authenticated")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Forbidden, Description = "User not authorized")]
    public async Task<HttpResponseData> CreateStaff(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "staff")] HttpRequestData req)
    {
        var (isAuthorized, adminUserId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
        if (!isAuthorized) return errorResponse!;

        try
        {
            var staff = await req.ReadFromJsonAsync<Staff>();
            if (staff == null)
            {
                var badReqRes = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReqRes.WriteAsJsonAsync(new { success = false, error = "Invalid staff data" });
                return badReqRes;
            }

            // Validate required fields
            var validationErrors = new List<string>();
            if (string.IsNullOrWhiteSpace(staff.EmployeeId))
                validationErrors.Add("Employee ID is required");
            if (string.IsNullOrWhiteSpace(staff.FirstName))
                validationErrors.Add("First name is required");
            if (string.IsNullOrWhiteSpace(staff.LastName))
                validationErrors.Add("Last name is required");
            if (string.IsNullOrWhiteSpace(staff.Email))
                validationErrors.Add("Email is required");
            if (string.IsNullOrWhiteSpace(staff.PhoneNumber))
                validationErrors.Add("Phone number is required");
            if (string.IsNullOrWhiteSpace(staff.Position))
                validationErrors.Add("Position is required");

            if (validationErrors.Any())
            {
                var validationRes = req.CreateResponse(HttpStatusCode.BadRequest);
                await validationRes.WriteAsJsonAsync(new { success = false, errors = validationErrors });
                return validationRes;
            }

            // Check for duplicate employee ID
            var existingByEmployeeId = await _mongo.GetStaffByEmployeeIdAsync(staff.EmployeeId);
            if (existingByEmployeeId != null)
            {
                var conflictRes = req.CreateResponse(HttpStatusCode.Conflict);
                await conflictRes.WriteAsJsonAsync(new { success = false, error = "A staff member with this Employee ID already exists" });
                return conflictRes;
            }

            // Check for duplicate email
            var existingByEmail = await _mongo.GetStaffByEmailAsync(staff.Email);
            if (existingByEmail != null)
            {
                var conflictRes = req.CreateResponse(HttpStatusCode.Conflict);
                await conflictRes.WriteAsJsonAsync(new { success = false, error = "A staff member with this email already exists" });
                return conflictRes;
            }

            // Set audit fields
            staff.CreatedBy = adminUserId;
            staff.CreatedAt = MongoService.GetIstNow();

            var createdStaff = await _mongo.CreateStaffAsync(staff);

            // Log audit
            var auditLogger = new AuditLogger(_log);
            auditLogger.LogAdminAction(adminUserId!, "CREATE_STAFF", staff.Id, $"Created staff member: {staff.EmployeeId} - {staff.FirstName} {staff.LastName}");

            // Send welcome email to the new staff member
            try
            {
                await _emailService.SendStaffWelcomeEmailAsync(createdStaff);
                _log.LogInformation($"Welcome email sent to {createdStaff.Email}");
            }
            catch (Exception emailEx)
            {
                // Log error but don't fail the staff creation if email fails
                _log.LogError(emailEx, $"Failed to send welcome email to {createdStaff.Email}");
            }

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(new { success = true, data = createdStaff });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error creating staff member");
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to create staff member" });
            return errorRes;
        }
    }

    /// <summary>
    /// Update staff member (Admin only)
    /// </summary>
    [Function("UpdateStaff")]
    [OpenApiOperation(operationId: "UpdateStaff", tags: new[] { "Staff" }, Summary = "Update staff member", Description = "Updates an existing staff member (Admin only)")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "staffId", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Staff member ID")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(Staff), Required = true, Description = "Updated staff member details")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(Staff), Description = "Staff member updated successfully")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.NotFound, Description = "Staff member not found")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.BadRequest, Description = "Invalid request data")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "User not authenticated")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Forbidden, Description = "User not authorized")]
    public async Task<HttpResponseData> UpdateStaff(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "staff/{staffId}")] HttpRequestData req,
        string staffId)
    {
        var (isAuthorized, adminUserId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
        if (!isAuthorized) return errorResponse!;

        try
        {
            var existingStaff = await _mongo.GetStaffByIdAsync(staffId);
            if (existingStaff == null)
            {
                var notFoundRes = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundRes.WriteAsJsonAsync(new { success = false, error = "Staff member not found" });
                return notFoundRes;
            }

            var updatedStaff = await req.ReadFromJsonAsync<Staff>();
            if (updatedStaff == null)
            {
                var badReqRes = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReqRes.WriteAsJsonAsync(new { success = false, error = "Invalid staff data" });
                return badReqRes;
            }

            // Preserve ID and audit fields
            updatedStaff.Id = staffId;
            updatedStaff.CreatedAt = existingStaff.CreatedAt;
            updatedStaff.CreatedBy = existingStaff.CreatedBy;
            updatedStaff.UpdatedBy = adminUserId;
            updatedStaff.UpdatedAt = MongoService.GetIstNow();

            // Check for duplicate employee ID (if changed)
            if (updatedStaff.EmployeeId != existingStaff.EmployeeId)
            {
                var existingByEmployeeId = await _mongo.GetStaffByEmployeeIdAsync(updatedStaff.EmployeeId);
                if (existingByEmployeeId != null)
                {
                    var conflictRes = req.CreateResponse(HttpStatusCode.Conflict);
                    await conflictRes.WriteAsJsonAsync(new { success = false, error = "A staff member with this Employee ID already exists" });
                    return conflictRes;
                }
            }

            // Check for duplicate email (if changed)
            if (updatedStaff.Email != existingStaff.Email)
            {
                var existingByEmail = await _mongo.GetStaffByEmailAsync(updatedStaff.Email);
                if (existingByEmail != null)
                {
                    var conflictRes = req.CreateResponse(HttpStatusCode.Conflict);
                    await conflictRes.WriteAsJsonAsync(new { success = false, error = "A staff member with this email already exists" });
                    return conflictRes;
                }
            }

            var success = await _mongo.UpdateStaffAsync(staffId, updatedStaff);
            if (!success)
            {
                var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to update staff member" });
                return errorRes;
            }

            // Log audit
            var auditLogger = new AuditLogger(_log);
            auditLogger.LogAdminAction(adminUserId!, "UPDATE_STAFF", staffId, $"Updated staff member: {updatedStaff.EmployeeId}");

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, data = updatedStaff });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error updating staff member {StaffId}", staffId);
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to update staff member" });
            return errorRes;
        }
    }

    /// <summary>
    /// Deactivate staff member (Admin only)
    /// </summary>
    [Function("DeactivateStaff")]
    [OpenApiOperation(operationId: "DeactivateStaff", tags: new[] { "Staff" }, Summary = "Deactivate staff member", Description = "Deactivates a staff member (soft delete) (Admin only)")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "staffId", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Staff member ID")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Staff member deactivated successfully")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.NotFound, Description = "Staff member not found")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "User not authenticated")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Forbidden, Description = "User not authorized")]
    public async Task<HttpResponseData> DeactivateStaff(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "staff/{staffId}/deactivate")] HttpRequestData req,
        string staffId)
    {
        var (isAuthorized, adminUserId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
        if (!isAuthorized) return errorResponse!;

        try
        {
            var staff = await _mongo.GetStaffByIdAsync(staffId);
            if (staff == null)
            {
                var notFoundRes = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundRes.WriteAsJsonAsync(new { success = false, error = "Staff member not found" });
                return notFoundRes;
            }

            var success = await _mongo.UpdateStaffActiveStatusAsync(staffId, false, adminUserId!);
            if (!success)
            {
                var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to deactivate staff member" });
                return errorRes;
            }

            // Log audit
            var auditLogger = new AuditLogger(_log);
            auditLogger.LogAdminAction(adminUserId!, "DEACTIVATE_STAFF", staffId, $"Deactivated staff member: {staff.EmployeeId}");

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, message = "Staff member deactivated successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error deactivating staff member {StaffId}", staffId);
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to deactivate staff member" });
            return errorRes;
        }
    }

    /// <summary>
    /// Activate staff member (Admin only)
    /// </summary>
    [Function("ActivateStaff")]
    [OpenApiOperation(operationId: "ActivateStaff", tags: new[] { "Staff" }, Summary = "Activate staff member", Description = "Activates a previously deactivated staff member (Admin only)")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "staffId", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Staff member ID")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Staff member activated successfully")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.NotFound, Description = "Staff member not found")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "User not authenticated")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Forbidden, Description = "User not authorized")]
    public async Task<HttpResponseData> ActivateStaff(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "staff/{staffId}/activate")] HttpRequestData req,
        string staffId)
    {
        var (isAuthorized, adminUserId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
        if (!isAuthorized) return errorResponse!;

        try
        {
            var staff = await _mongo.GetStaffByIdAsync(staffId);
            if (staff == null)
            {
                var notFoundRes = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundRes.WriteAsJsonAsync(new { success = false, error = "Staff member not found" });
                return notFoundRes;
            }

            var success = await _mongo.UpdateStaffActiveStatusAsync(staffId, true, adminUserId!);
            if (!success)
            {
                var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to activate staff member" });
                return errorRes;
            }

            // Log audit
            var auditLogger = new AuditLogger(_log);
            auditLogger.LogAdminAction(adminUserId!, "ACTIVATE_STAFF", staffId, $"Activated staff member: {staff.EmployeeId}");

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, message = "Staff member activated successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error activating staff member {StaffId}", staffId);
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to activate staff member" });
            return errorRes;
        }
    }

    /// <summary>
    /// Delete staff member permanently (Admin only)
    /// </summary>
    [Function("DeleteStaff")]
    [OpenApiOperation(operationId: "DeleteStaff", tags: new[] { "Staff" }, Summary = "Delete staff member permanently", Description = "Permanently deletes a staff member (use with caution) (Admin only)")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "staffId", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Staff member ID")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Staff member deleted successfully")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.NotFound, Description = "Staff member not found")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "User not authenticated")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Forbidden, Description = "User not authorized")]
    public async Task<HttpResponseData> DeleteStaff(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "staff/{staffId}")] HttpRequestData req,
        string staffId)
    {
        var (isAuthorized, adminUserId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
        if (!isAuthorized) return errorResponse!;

        try
        {
            var staff = await _mongo.GetStaffByIdAsync(staffId);
            if (staff == null)
            {
                var notFoundRes = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundRes.WriteAsJsonAsync(new { success = false, error = "Staff member not found" });
                return notFoundRes;
            }

            var success = await _mongo.HardDeleteStaffAsync(staffId);
            if (!success)
            {
                var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to delete staff member" });
                return errorRes;
            }

            // Log audit
            var auditLogger = new AuditLogger(_log);
            auditLogger.LogAdminAction(adminUserId!, "DELETE_STAFF", staffId, $"Permanently deleted staff member: {staff.EmployeeId}");

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, message = "Staff member deleted successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error deleting staff member {StaffId}", staffId);
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to delete staff member" });
            return errorRes;
        }
    }

    /// <summary>
    /// Update staff salary (Admin only)
    /// </summary>
    [Function("UpdateStaffSalary")]
    [OpenApiOperation(operationId: "UpdateStaffSalary", tags: new[] { "Staff" }, Summary = "Update staff salary", Description = "Updates a staff member's salary (Admin only)")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "staffId", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Staff member ID")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(UpdateSalaryRequest), Required = true, Description = "New salary amount")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Salary updated successfully")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.NotFound, Description = "Staff member not found")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.BadRequest, Description = "Invalid salary amount")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "User not authenticated")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Forbidden, Description = "User not authorized")]
    public async Task<HttpResponseData> UpdateStaffSalary(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "staff/{staffId}/salary")] HttpRequestData req,
        string staffId)
    {
        var (isAuthorized, adminUserId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
        if (!isAuthorized) return errorResponse!;

        try
        {
            var staff = await _mongo.GetStaffByIdAsync(staffId);
            if (staff == null)
            {
                var notFoundRes = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundRes.WriteAsJsonAsync(new { success = false, error = "Staff member not found" });
                return notFoundRes;
            }

            var request = await req.ReadFromJsonAsync<UpdateSalaryRequest>();
            if (request == null || request.Salary <= 0)
            {
                var badReqRes = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReqRes.WriteAsJsonAsync(new { success = false, error = "Invalid salary amount" });
                return badReqRes;
            }

            var success = await _mongo.UpdateStaffSalaryAsync(staffId, request.Salary, adminUserId!);
            if (!success)
            {
                var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to update salary" });
                return errorRes;
            }

            // Log audit
            var auditLogger = new AuditLogger(_log);
            auditLogger.LogAdminAction(adminUserId!, "UPDATE_STAFF_SALARY", staffId, $"Updated salary for staff member: {staff.EmployeeId} to {request.Salary}");

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, message = "Salary updated successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error updating salary for staff member {StaffId}", staffId);
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to update salary" });
            return errorRes;
        }
    }

    /// <summary>
    /// Update staff performance rating (Admin only)
    /// </summary>
    [Function("UpdateStaffPerformanceRating")]
    [OpenApiOperation(operationId: "UpdateStaffPerformanceRating", tags: new[] { "Staff" }, Summary = "Update staff performance rating", Description = "Updates a staff member's performance rating (Admin only)")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "staffId", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Staff member ID")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(UpdatePerformanceRatingRequest), Required = true, Description = "Performance rating (0-5)")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Rating updated successfully")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.NotFound, Description = "Staff member not found")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.BadRequest, Description = "Invalid rating")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "User not authenticated")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Forbidden, Description = "User not authorized")]
    public async Task<HttpResponseData> UpdateStaffPerformanceRating(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "staff/{staffId}/performance")] HttpRequestData req,
        string staffId)
    {
        var (isAuthorized, adminUserId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
        if (!isAuthorized) return errorResponse!;

        try
        {
            var staff = await _mongo.GetStaffByIdAsync(staffId);
            if (staff == null)
            {
                var notFoundRes = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundRes.WriteAsJsonAsync(new { success = false, error = "Staff member not found" });
                return notFoundRes;
            }

            var request = await req.ReadFromJsonAsync<UpdatePerformanceRatingRequest>();
            if (request == null || request.Rating < 0 || request.Rating > 5)
            {
                var badReqRes = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReqRes.WriteAsJsonAsync(new { success = false, error = "Rating must be between 0 and 5" });
                return badReqRes;
            }

            var success = await _mongo.UpdateStaffPerformanceRatingAsync(staffId, request.Rating, adminUserId!);
            if (!success)
            {
                var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to update rating" });
                return errorRes;
            }

            // Log audit
            var auditLogger = new AuditLogger(_log);
            auditLogger.LogAdminAction(adminUserId!, "UPDATE_STAFF_PERFORMANCE", staffId, $"Updated performance rating for staff member: {staff.EmployeeId} to {request.Rating}");

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, message = "Performance rating updated successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error updating performance rating for staff member {StaffId}", staffId);
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to update rating" });
            return errorRes;
        }
    }

    /// <summary>
    /// Update staff leave balances (Admin only)
    /// </summary>
    [Function("UpdateStaffLeaveBalances")]
    [OpenApiOperation(operationId: "UpdateStaffLeaveBalances", tags: new[] { "Staff" }, Summary = "Update staff leave balances", Description = "Updates a staff member's leave balances (Admin only)")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "staffId", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Staff member ID")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(UpdateLeaveBalancesRequest), Required = true, Description = "Leave balances")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Leave balances updated successfully")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.NotFound, Description = "Staff member not found")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.BadRequest, Description = "Invalid leave balance data")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "User not authenticated")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Forbidden, Description = "User not authorized")]
    public async Task<HttpResponseData> UpdateStaffLeaveBalances(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "staff/{staffId}/leave-balances")] HttpRequestData req,
        string staffId)
    {
        var (isAuthorized, adminUserId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
        if (!isAuthorized) return errorResponse!;

        try
        {
            var staff = await _mongo.GetStaffByIdAsync(staffId);
            if (staff == null)
            {
                var notFoundRes = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundRes.WriteAsJsonAsync(new { success = false, error = "Staff member not found" });
                return notFoundRes;
            }

            var request = await req.ReadFromJsonAsync<UpdateLeaveBalancesRequest>();
            if (request == null)
            {
                var badReqRes = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReqRes.WriteAsJsonAsync(new { success = false, error = "Invalid leave balance data" });
                return badReqRes;
            }

            var success = await _mongo.UpdateStaffLeaveBalancesAsync(
                staffId, 
                request.AnnualLeave, 
                request.SickLeave, 
                request.CasualLeave, 
                adminUserId!);

            if (!success)
            {
                var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to update leave balances" });
                return errorRes;
            }

            // Log audit
            var auditLogger = new AuditLogger(_log);
            auditLogger.LogAdminAction(adminUserId!, "UPDATE_STAFF_LEAVE_BALANCES", staffId, $"Updated leave balances for staff member: {staff.EmployeeId}");

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, message = "Leave balances updated successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error updating leave balances for staff member {StaffId}", staffId);
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to update leave balances" });
            return errorRes;
        }
    }

    // Helper method to get client IP address
    private string GetClientIp(HttpRequestData req)
    {
        if (req.Headers.TryGetValues("X-Forwarded-For", out var forwardedFor))
        {
            return forwardedFor.FirstOrDefault()?.Split(',')[0].Trim() ?? "Unknown";
        }
        return "Unknown";
    }
}

// Request models for updates
public class UpdateSalaryRequest
{
    public decimal Salary { get; set; }
}

public class UpdatePerformanceRatingRequest
{
    public decimal Rating { get; set; }
}

public class UpdateLeaveBalancesRequest
{
    public int AnnualLeave { get; set; }
    public int SickLeave { get; set; }
    public int CasualLeave { get; set; }
}
