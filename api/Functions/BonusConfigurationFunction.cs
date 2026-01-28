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

public class BonusConfigurationFunction
{
    private readonly MongoService _mongo;
    private readonly AuthService _auth;
    private readonly ILogger _log;

    public BonusConfigurationFunction(MongoService mongo, AuthService auth, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _auth = auth;
        _log = loggerFactory.CreateLogger<BonusConfigurationFunction>();
    }

    /// <summary>
    /// Get all bonus configurations (Admin only)
    /// </summary>
    [Function("GetAllBonusConfigurations")]
    [OpenApiOperation(operationId: "GetAllBonusConfigurations", tags: new[] { "Bonus Configuration" }, Summary = "Get all bonus configurations", Description = "Retrieves all bonus configurations for the selected outlet (Admin only)")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(List<BonusConfiguration>), Description = "Successfully retrieved bonus configurations")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "User not authenticated")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Forbidden, Description = "User not authorized (admin role required)")]
    public async Task<HttpResponseData> GetAllBonusConfigurations(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "bonus-configurations")] HttpRequestData req)
    {
        var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
        if (!isAuthorized) return errorResponse!;

        try
        {
            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);
            var configurations = await _mongo.GetAllBonusConfigurationsAsync(outletId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, data = configurations });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting bonus configurations");
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to retrieve bonus configurations" });
            return errorRes;
        }
    }

    /// <summary>
    /// Get active bonus configurations (Admin only)
    /// </summary>
    [Function("GetActiveBonusConfigurations")]
    [OpenApiOperation(operationId: "GetActiveBonusConfigurations", tags: new[] { "Bonus Configuration" }, Summary = "Get active bonus configurations", Description = "Retrieves active bonus configurations (Admin only)")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(List<BonusConfiguration>), Description = "Successfully retrieved configurations")]
    public async Task<HttpResponseData> GetActiveBonusConfigurations(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "bonus-configurations/active")] HttpRequestData req)
    {
        var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
        if (!isAuthorized) return errorResponse!;

        try
        {
            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);
            var configurations = await _mongo.GetActiveBonusConfigurationsAsync(outletId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, data = configurations });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting active bonus configurations");
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to retrieve configurations" });
            return errorRes;
        }
    }

    /// <summary>
    /// Get bonus configuration by ID (Admin only)
    /// </summary>
    [Function("GetBonusConfigurationById")]
    [OpenApiOperation(operationId: "GetBonusConfigurationById", tags: new[] { "Bonus Configuration" }, Summary = "Get bonus configuration by ID", Description = "Retrieves a specific bonus configuration (Admin only)")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Configuration ID")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(BonusConfiguration), Description = "Successfully retrieved configuration")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.NotFound, Description = "Configuration not found")]
    public async Task<HttpResponseData> GetBonusConfigurationById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "bonus-configurations/{id}")] HttpRequestData req,
        string id)
    {
        var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
        if (!isAuthorized) return errorResponse!;

        try
        {
            var config = await _mongo.GetBonusConfigurationByIdAsync(id);
            if (config == null)
            {
                var notFoundRes = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundRes.WriteAsJsonAsync(new { success = false, error = "Bonus configuration not found" });
                return notFoundRes;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, data = config });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting bonus configuration {Id}", id);
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to retrieve configuration" });
            return errorRes;
        }
    }

    /// <summary>
    /// Create bonus configuration (Admin only)
    /// </summary>
    [Function("CreateBonusConfiguration")]
    [OpenApiOperation(operationId: "CreateBonusConfiguration", tags: new[] { "Bonus Configuration" }, Summary = "Create bonus configuration", Description = "Creates a new bonus configuration (Admin only)")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(CreateBonusConfigurationRequest), Required = true, Description = "Bonus configuration details")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Created, contentType: "application/json", bodyType: typeof(BonusConfiguration), Description = "Configuration created successfully")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.BadRequest, Description = "Invalid request data")]
    public async Task<HttpResponseData> CreateBonusConfiguration(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "bonus-configurations")] HttpRequestData req)
    {
        var (isAuthorized, adminUserId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
        if (!isAuthorized) return errorResponse!;

        try
        {
            var request = await req.ReadFromJsonAsync<CreateBonusConfigurationRequest>();
            if (request == null)
            {
                var badReqRes = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReqRes.WriteAsJsonAsync(new { success = false, error = "Invalid configuration data" });
                return badReqRes;
            }

            // Validate outlet access
            var (hasAccess, outletId, accessError) = await OutletHelper.ValidateOutletAccess(req, _auth, _mongo);
            if (!hasAccess)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = accessError });
                return forbidden;
            }

            var config = new BonusConfiguration
            {
                OutletId = outletId,
                ConfigurationName = request.ConfigurationName,
                Description = request.Description,
                ApplicableToAllStaff = request.ApplicableToAllStaff,
                ApplicablePositions = request.ApplicablePositions,
                CalculationPeriod = request.CalculationPeriod,
                Rules = request.Rules.Select(r => new BonusRule
                {
                    RuleType = r.RuleType,
                    RuleName = r.RuleName,
                    Description = r.Description,
                    IsBonus = r.IsBonus,
                    CalculationType = r.CalculationType,
                    RateAmount = r.RateAmount,
                    PercentageValue = r.PercentageValue,
                    Threshold = r.Threshold,
                    MaxAmount = r.MaxAmount,
                    IsActive = r.IsActive
                }).ToList(),
                CreatedBy = adminUserId
            };

            var created = await _mongo.CreateBonusConfigurationAsync(config);

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(new { success = true, data = created });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error creating bonus configuration");
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to create configuration" });
            return errorRes;
        }
    }

    /// <summary>
    /// Update bonus configuration (Admin only)
    /// </summary>
    [Function("UpdateBonusConfiguration")]
    [OpenApiOperation(operationId: "UpdateBonusConfiguration", tags: new[] { "Bonus Configuration" }, Summary = "Update bonus configuration", Description = "Updates an existing bonus configuration (Admin only)")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Configuration ID")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(UpdateBonusConfigurationRequest), Required = true, Description = "Updated configuration details")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(BonusConfiguration), Description = "Configuration updated successfully")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.NotFound, Description = "Configuration not found")]
    public async Task<HttpResponseData> UpdateBonusConfiguration(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "bonus-configurations/{id}")] HttpRequestData req,
        string id)
    {
        var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
        if (!isAuthorized) return errorResponse!;

        try
        {
            var existing = await _mongo.GetBonusConfigurationByIdAsync(id);
            if (existing == null)
            {
                var notFoundRes = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundRes.WriteAsJsonAsync(new { success = false, error = "Bonus configuration not found" });
                return notFoundRes;
            }

            var request = await req.ReadFromJsonAsync<UpdateBonusConfigurationRequest>();
            if (request == null)
            {
                var badReqRes = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReqRes.WriteAsJsonAsync(new { success = false, error = "Invalid configuration data" });
                return badReqRes;
            }

            // Update fields
            if (request.ConfigurationName != null) existing.ConfigurationName = request.ConfigurationName;
            if (request.Description != null) existing.Description = request.Description;
            if (request.ApplicableToAllStaff.HasValue) existing.ApplicableToAllStaff = request.ApplicableToAllStaff.Value;
            if (request.ApplicablePositions != null) existing.ApplicablePositions = request.ApplicablePositions;
            if (request.CalculationPeriod != null) existing.CalculationPeriod = request.CalculationPeriod;
            if (request.Rules != null)
            {
                existing.Rules = request.Rules.Select(r => new BonusRule
                {
                    RuleType = r.RuleType,
                    RuleName = r.RuleName,
                    Description = r.Description,
                    IsBonus = r.IsBonus,
                    CalculationType = r.CalculationType,
                    RateAmount = r.RateAmount,
                    PercentageValue = r.PercentageValue,
                    Threshold = r.Threshold,
                    MaxAmount = r.MaxAmount,
                    IsActive = r.IsActive
                }).ToList();
            }
            if (request.IsActive.HasValue) existing.IsActive = request.IsActive.Value;

            var success = await _mongo.UpdateBonusConfigurationAsync(id, existing);
            if (!success)
            {
                var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to update configuration" });
                return errorRes;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, data = existing });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error updating bonus configuration {Id}", id);
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to update configuration" });
            return errorRes;
        }
    }

    /// <summary>
    /// Delete bonus configuration (Admin only)
    /// </summary>
    [Function("DeleteBonusConfiguration")]
    [OpenApiOperation(operationId: "DeleteBonusConfiguration", tags: new[] { "Bonus Configuration" }, Summary = "Delete bonus configuration", Description = "Deletes a bonus configuration (Admin only)")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Configuration ID")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Configuration deleted successfully")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.NotFound, Description = "Configuration not found")]
    public async Task<HttpResponseData> DeleteBonusConfiguration(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "bonus-configurations/{id}")] HttpRequestData req,
        string id)
    {
        var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
        if (!isAuthorized) return errorResponse!;

        try
        {
            var success = await _mongo.DeleteBonusConfigurationAsync(id);
            if (!success)
            {
                var notFoundRes = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundRes.WriteAsJsonAsync(new { success = false, error = "Bonus configuration not found" });
                return notFoundRes;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, message = "Bonus configuration deleted successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error deleting bonus configuration {Id}", id);
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to delete configuration" });
            return errorRes;
        }
    }

    /// <summary>
    /// Toggle bonus configuration active status (Admin only)
    /// </summary>
    [Function("ToggleBonusConfigurationStatus")]
    [OpenApiOperation(operationId: "ToggleBonusConfigurationStatus", tags: new[] { "Bonus Configuration" }, Summary = "Toggle configuration status", Description = "Activates or deactivates a bonus configuration (Admin only)")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Configuration ID")]
    [OpenApiParameter(name: "isActive", In = ParameterLocation.Query, Required = true, Type = typeof(bool), Description = "Active status")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Status updated successfully")]
    public async Task<HttpResponseData> ToggleBonusConfigurationStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "bonus-configurations/{id}/toggle")] HttpRequestData req,
        string id)
    {
        var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
        if (!isAuthorized) return errorResponse!;

        try
        {
            var isActiveParam = req.Query["isActive"];
            if (string.IsNullOrEmpty(isActiveParam) || !bool.TryParse(isActiveParam, out var isActive))
            {
                var badReqRes = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReqRes.WriteAsJsonAsync(new { success = false, error = "Invalid isActive parameter" });
                return badReqRes;
            }

            var success = await _mongo.ToggleBonusConfigurationStatusAsync(id, isActive);
            if (!success)
            {
                var notFoundRes = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundRes.WriteAsJsonAsync(new { success = false, error = "Bonus configuration not found" });
                return notFoundRes;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, message = $"Configuration {(isActive ? "activated" : "deactivated")} successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error toggling bonus configuration status {Id}", id);
            var errorRes = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorRes.WriteAsJsonAsync(new { success = false, error = "Failed to update status" });
            return errorRes;
        }
    }
}
