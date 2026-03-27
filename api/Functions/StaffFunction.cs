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
    private readonly IWhatsAppService _whatsApp;
    private readonly ILogger _log;

    public StaffFunction(MongoService mongo, AuthService auth, IEmailService emailService, IWhatsAppService whatsApp, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _auth = auth;
        _emailService = emailService;
        _whatsApp = whatsApp;
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
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "staff/{staffId:length(24)}")] HttpRequestData req,
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

            // Send WhatsApp welcome notification to the new staff member
            try
            {
                var staffName = $"{createdStaff.FirstName} {createdStaff.LastName}";
                var message = $"Welcome to the team!\n\n"
                    + $"Employee ID: {createdStaff.EmployeeId}\n"
                    + $"Position: {createdStaff.Position}\n\n"
                    + $"Your account has been created successfully. You can now access the system using your credentials.";
                
                await _whatsApp.SendStaffNotificationAsync(
                    createdStaff.PhoneNumber,
                    staffName,
                    "Welcome to Maa Tara Cafe",
                    message
                );
                _log.LogInformation($"WhatsApp welcome notification sent to {createdStaff.PhoneNumber}");
            }
            catch (Exception whatsAppEx)
            {
                // Log error but don't fail the staff creation if WhatsApp fails
                _log.LogError(whatsAppEx, $"Failed to send WhatsApp notification to {createdStaff.PhoneNumber}");
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
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "staff/{staffId:length(24)}")] HttpRequestData req,
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
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "staff/{staffId:length(24)}/deactivate")] HttpRequestData req,
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
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "staff/{staffId:length(24)}/activate")] HttpRequestData req,
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
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "staff/{staffId:length(24)}")] HttpRequestData req,
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
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "staff/{staffId:length(24)}/salary")] HttpRequestData req,
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
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "staff/{staffId:length(24)}/performance")] HttpRequestData req,
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
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "staff/{staffId:length(24)}/leave-balances")] HttpRequestData req,
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

    /// <summary>
    /// Add shift to staff member (Admin only)
    /// </summary>
    [Function("AddStaffShift")]
    [OpenApiOperation(operationId: "AddStaffShift", tags: new[] { "Staff" }, Summary = "Add shift to staff", Description = "Adds a new shift to staff member's schedule (Admin only)")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "staffId", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Staff ID")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(StaffShift), Required = true, Description = "Shift details")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(StaffShift), Description = "Shift added successfully")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.BadRequest, Description = "Invalid request data")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.NotFound, Description = "Staff member not found")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "User not authenticated")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Forbidden, Description = "User not authorized")]
    public async Task<HttpResponseData> AddStaffShift(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "staff/{staffId:length(24)}/shifts")] HttpRequestData req,
        string staffId)
    {
        var (isAuthorized, adminUserId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
        if (!isAuthorized) return errorResponse!;

        try
        {
            var shift = await req.ReadFromJsonAsync<StaffShift>();
            if (shift == null)
            {
                var badReqRes = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReqRes.WriteAsJsonAsync(new { success = false, error = "Invalid shift data" });
                return badReqRes;
            }

            // Validate required fields
            var validationErrors = new List<string>();
            if (string.IsNullOrWhiteSpace(shift.DayOfWeek))
                validationErrors.Add("DayOfWeek is required");
            if (string.IsNullOrWhiteSpace(shift.StartTime))
                validationErrors.Add("StartTime is required");
            if (string.IsNullOrWhiteSpace(shift.EndTime))
                validationErrors.Add("EndTime is required");

            // Validate day of week
            var validDays = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
            if (!string.IsNullOrWhiteSpace(shift.DayOfWeek) && !validDays.Contains(shift.DayOfWeek, StringComparer.OrdinalIgnoreCase))
            {
                validationErrors.Add($"Invalid DayOfWeek. Must be one of: {string.Join(", ", validDays)}");
            }

            if (validationErrors.Any())
            {
                var validationRes = req.CreateResponse(HttpStatusCode.BadRequest);
                await validationRes.WriteAsJsonAsync(new { success = false, errors = validationErrors });
                return validationRes;
            }

            // Get staff member
            var staff = await _mongo.GetStaffByIdAsync(staffId);
            if (staff == null)
            {
                var notFoundRes = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundRes.WriteAsJsonAsync(new { success = false, error = "Staff member not found" });
                return notFoundRes;
            }

            // Generate new shift ID and set timestamps
            shift.Id = Guid.NewGuid().ToString();
            shift.CreatedAt = MongoService.GetIstNow();
            shift.IsActive = true;

            // Add shift to staff
            staff.Shifts.Add(shift);
            staff.UpdatedAt = MongoService.GetIstNow();
            staff.UpdatedBy = adminUserId;

            var success = await _mongo.UpdateStaffAsync(staffId, staff);
            if (!success)
            {
                var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to add shift" });
                return errorRes;
            }

            // Log audit
            var auditLogger = new AuditLogger(_log);
            auditLogger.LogAdminAction(adminUserId!, "ADD_STAFF_SHIFT", staffId, 
                $"Added shift: {shift.ShiftName} on {shift.DayOfWeek} ({shift.StartTime} - {shift.EndTime})");

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, data = shift, message = "Shift added successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error adding shift to staff {StaffId}", staffId);
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to add shift" });
            return errorRes;
        }
    }

    /// <summary>
    /// Update staff shift (Admin only)
    /// </summary>
    [Function("UpdateStaffShift")]
    [OpenApiOperation(operationId: "UpdateStaffShift", tags: new[] { "Staff" }, Summary = "Update staff shift", Description = "Updates a specific shift in staff member's schedule (Admin only)")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "staffId", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Staff ID")]
    [OpenApiParameter(name: "shiftId", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Shift ID")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(StaffShift), Required = true, Description = "Updated shift details")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(StaffShift), Description = "Shift updated successfully")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.NotFound, Description = "Staff member or shift not found")]
    public async Task<HttpResponseData> UpdateStaffShift(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "staff/{staffId:length(24)}/shifts/{shiftId}")] HttpRequestData req,
        string staffId,
        string shiftId)
    {
        var (isAuthorized, adminUserId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
        if (!isAuthorized) return errorResponse!;

        try
        {
            var updatedShift = await req.ReadFromJsonAsync<StaffShift>();
            if (updatedShift == null)
            {
                var badReqRes = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReqRes.WriteAsJsonAsync(new { success = false, error = "Invalid shift data" });
                return badReqRes;
            }

            // Get staff member
            var staff = await _mongo.GetStaffByIdAsync(staffId);
            if (staff == null)
            {
                var notFoundRes = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundRes.WriteAsJsonAsync(new { success = false, error = "Staff member not found" });
                return notFoundRes;
            }

            // Find and update shift
            var shift = staff.Shifts.FirstOrDefault(s => s.Id == shiftId);
            if (shift == null)
            {
                var notFoundRes = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundRes.WriteAsJsonAsync(new { success = false, error = "Shift not found" });
                return notFoundRes;
            }

            // Update shift properties
            shift.ShiftName = updatedShift.ShiftName ?? shift.ShiftName;
            shift.DayOfWeek = updatedShift.DayOfWeek ?? shift.DayOfWeek;
            shift.StartTime = updatedShift.StartTime ?? shift.StartTime;
            shift.EndTime = updatedShift.EndTime ?? shift.EndTime;
            shift.BreakDuration = updatedShift.BreakDuration;
            shift.OutletId = updatedShift.OutletId ?? shift.OutletId;
            shift.Notes = updatedShift.Notes ?? shift.Notes;
            shift.IsActive = updatedShift.IsActive;
            shift.UpdatedAt = MongoService.GetIstNow();

            staff.UpdatedAt = MongoService.GetIstNow();
            staff.UpdatedBy = adminUserId;

            var success = await _mongo.UpdateStaffAsync(staffId, staff);
            if (!success)
            {
                var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to update shift" });
                return errorRes;
            }

            // Log audit
            var auditLogger = new AuditLogger(_log);
            auditLogger.LogAdminAction(adminUserId!, "UPDATE_STAFF_SHIFT", staffId, 
                $"Updated shift: {shift.ShiftName} on {shift.DayOfWeek}");

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, data = shift, message = "Shift updated successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error updating shift {ShiftId} for staff {StaffId}", shiftId, staffId);
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to update shift" });
            return errorRes;
        }
    }

    /// <summary>
    /// Delete staff shift (Admin only)
    /// </summary>
    [Function("DeleteStaffShift")]
    [OpenApiOperation(operationId: "DeleteStaffShift", tags: new[] { "Staff" }, Summary = "Delete staff shift", Description = "Deletes a specific shift from staff member's schedule (Admin only)")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "staffId", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Staff ID")]
    [OpenApiParameter(name: "shiftId", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Shift ID")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Shift deleted successfully")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.NotFound, Description = "Staff member or shift not found")]
    public async Task<HttpResponseData> DeleteStaffShift(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "staff/{staffId:length(24)}/shifts/{shiftId}")] HttpRequestData req,
        string staffId,
        string shiftId)
    {
        var (isAuthorized, adminUserId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
        if (!isAuthorized) return errorResponse!;

        try
        {
            // Get staff member
            var staff = await _mongo.GetStaffByIdAsync(staffId);
            if (staff == null)
            {
                var notFoundRes = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundRes.WriteAsJsonAsync(new { success = false, error = "Staff member not found" });
                return notFoundRes;
            }

            // Find shift
            var shift = staff.Shifts.FirstOrDefault(s => s.Id == shiftId);
            if (shift == null)
            {
                var notFoundRes = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundRes.WriteAsJsonAsync(new { success = false, error = "Shift not found" });
                return notFoundRes;
            }

            // Remove shift
            staff.Shifts.Remove(shift);
            staff.UpdatedAt = MongoService.GetIstNow();
            staff.UpdatedBy = adminUserId;

            var success = await _mongo.UpdateStaffAsync(staffId, staff);
            if (!success)
            {
                var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to delete shift" });
                return errorRes;
            }

            // Log audit
            var auditLogger = new AuditLogger(_log);
            auditLogger.LogAdminAction(adminUserId!, "DELETE_STAFF_SHIFT", staffId, 
                $"Deleted shift: {shift.ShiftName} on {shift.DayOfWeek}");

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, message = "Shift deleted successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error deleting shift {ShiftId} for staff {StaffId}", shiftId, staffId);
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to delete shift" });
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
                await notFoundRes.WriteAsJsonAsync(new { success = false, error = "Staff member not found" });
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
            await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to retrieve shifts" });
            return errorRes;
        }
    }

    /// <summary>
    /// Send email notification to a staff member
    /// </summary>
    [Function("SendStaffEmail")]
    [OpenApiOperation(operationId: "SendStaffEmail", tags: new[] { "Staff" }, Summary = "Send email to staff member", Description = "Sends a custom email notification to a staff member (Admin only)")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "staffId", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Staff ID")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(SendStaffEmailRequest), Required = true, Description = "Email details")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.OK, Description = "Email sent successfully")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.NotFound, Description = "Staff member not found")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.BadRequest, Description = "Invalid request")]
    public async Task<HttpResponseData> SendStaffEmail(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "staff/{staffId:length(24)}/send-email")] HttpRequestData req,
        string staffId)
    {
        var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
        if (!isAuthorized) return errorResponse!;

        try
        {
            var requestBody = await req.ReadAsStringAsync();
            var emailRequest = JsonSerializer.Deserialize<SendStaffEmailRequest>(requestBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (emailRequest == null || string.IsNullOrWhiteSpace(emailRequest.Subject) || string.IsNullOrWhiteSpace(emailRequest.Message))
            {
                var badReqRes = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReqRes.WriteAsJsonAsync(new { success = false, error = "Subject and message are required" });
                return badReqRes;
            }

            var staff = await _mongo.GetStaffByIdAsync(staffId);
            if (staff == null)
            {
                var notFoundRes = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundRes.WriteAsJsonAsync(new { success = false, error = "Staff member not found" });
                return notFoundRes;
            }

            var staffName = $"{staff.FirstName} {staff.LastName}";
            
            // Send email
            var emailSent = await _emailService.SendPromotionalEmailAsync(
                staff.Email,
                staffName,
                emailRequest.Subject,
                emailRequest.Message
            );

            // Send WhatsApp notification if enabled
            var whatsappSent = false;
            if (emailRequest.SendWhatsApp && !string.IsNullOrWhiteSpace(staff.PhoneNumber))
            {
                try
                {
                    whatsappSent = await _whatsApp.SendStaffNotificationAsync(
                        staff.PhoneNumber,
                        staffName,
                        emailRequest.Subject,
                        emailRequest.Message
                    );
                }
                catch (Exception whatsappEx)
                {
                    _log.LogWarning(whatsappEx, "Failed to send WhatsApp notification to staff {StaffId}", staffId);
                }
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new 
            { 
                success = true, 
                message = "Notification sent successfully",
                emailSent = emailSent,
                whatsappSent = whatsappSent
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error sending email to staff {StaffId}", staffId);
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to send email" });
            return errorRes;
        }
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

public class SendStaffEmailRequest
{
    public string Subject { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool SendWhatsApp { get; set; } = false;
}
