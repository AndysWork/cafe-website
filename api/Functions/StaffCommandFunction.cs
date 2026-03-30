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
using System.Text.Json;

namespace Cafe.Api.Functions;

public class StaffCommandFunction
{
    private readonly IStaffRepository _mongo;
    private readonly AuthService _auth;
    private readonly IEmailService _emailService;
    private readonly IWhatsAppService _whatsApp;
    private readonly ILogger _log;

    public StaffCommandFunction(IStaffRepository mongo, AuthService auth, IEmailService emailService, IWhatsAppService whatsApp, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _auth = auth;
        _emailService = emailService;
        _whatsApp = whatsApp;
        _log = loggerFactory.CreateLogger<StaffCommandFunction>();
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
            var (staff, validationError) = await ValidationHelper.ValidateBody<Staff>(req);
            if (validationError != null) return validationError;

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

            var (updatedStaff, validationError) = await ValidationHelper.ValidateBody<Staff>(req);
            if (validationError != null) return validationError;

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

            var (request, validationError) = await ValidationHelper.ValidateBody<UpdateSalaryRequest>(req);
            if (validationError != null) return validationError;

            if (request.Salary <= 0)
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

            var (request, validationError) = await ValidationHelper.ValidateBody<UpdatePerformanceRatingRequest>(req);
            if (validationError != null) return validationError;

            if (request.Rating < 0 || request.Rating > 5)
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

            var (request, validationError) = await ValidationHelper.ValidateBody<UpdateLeaveBalancesRequest>(req);
            if (validationError != null) return validationError;

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
            var (shift, validationError) = await ValidationHelper.ValidateBody<StaffShift>(req);
            if (validationError != null) return validationError;

            // Validate day of week
            var validDays = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
            if (!string.IsNullOrWhiteSpace(shift.DayOfWeek) && !validDays.Contains(shift.DayOfWeek, StringComparer.OrdinalIgnoreCase))
            {
                var badReqRes = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReqRes.WriteAsJsonAsync(new { success = false, error = $"Invalid DayOfWeek. Must be one of: {string.Join(", ", validDays)}" });
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
            var (updatedShift, validationError) = await ValidationHelper.ValidateBody<StaffShift>(req);
            if (validationError != null) return validationError;

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
