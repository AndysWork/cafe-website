using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Cafe.Api.Services;
using Cafe.Api.Models;
using Cafe.Api.Helpers;

namespace Cafe.Api.Functions;

public class OverheadCostFunction
{
    private readonly MongoService _mongoService;
    private readonly AuthService _authService;
    private readonly ILogger<OverheadCostFunction> _logger;

    public OverheadCostFunction(MongoService mongoService, AuthService authService, ILogger<OverheadCostFunction> logger)
    {
        _mongoService = mongoService;
        _authService = authService;
        _logger = logger;
    }

    [Function("GetAllOverheadCosts")]
    public async Task<HttpResponseData> GetAllOverheadCosts(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "overhead-costs")] HttpRequestData req)
    {
        try
        {
            var outletId = OutletHelper.GetOutletIdFromRequest(req, _authService);
            _logger.LogInformation($"GetAllOverheadCosts called with outletId: {outletId}");
            
            var overheadCosts = await _mongoService.GetAllOverheadCostsAsync(outletId);
            _logger.LogInformation($"Found {overheadCosts.Count} overhead costs for outlet {outletId}");
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(overheadCosts);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all overhead costs");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("GetActiveOverheadCosts")]
    public async Task<HttpResponseData> GetActiveOverheadCosts(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "overhead-costs/active")] HttpRequestData req)
    {
        try
        {
            var outletId = OutletHelper.GetOutletIdFromRequest(req, _authService);
            var overheadCosts = await _mongoService.GetActiveOverheadCostsAsync(outletId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(overheadCosts);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active overhead costs");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("GetOverheadCostById")]
    public async Task<HttpResponseData> GetOverheadCostById(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "overhead-costs/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var overheadCost = await _mongoService.GetOverheadCostByIdAsync(id);
            if (overheadCost == null)
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteStringAsync("Overhead cost not found");
                return notFoundResponse;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(overheadCost);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting overhead cost by ID: {Id}", id);
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("CreateOverheadCost")]
    public async Task<HttpResponseData> CreateOverheadCost(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "overhead-costs")] HttpRequestData req)
    {
        try
        {
            var outletId = OutletHelper.GetOutletIdFromRequest(req, _authService);
            
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var overheadCost = JsonSerializer.Deserialize<OverheadCost>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (overheadCost == null)
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Invalid overhead cost data");
                return badRequestResponse;
            }

            // Validate required fields
            if (string.IsNullOrEmpty(overheadCost.CostType))
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Cost type is required");
                return badRequestResponse;
            }

            // Set the OutletId from the request header
            overheadCost.OutletId = outletId;
            overheadCost.CreatedAt = DateTime.UtcNow;
            overheadCost.UpdatedAt = DateTime.UtcNow;

            var createdOverheadCost = await _mongoService.CreateOverheadCostAsync(overheadCost);

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(createdOverheadCost);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating overhead cost");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("UpdateOverheadCost")]
    public async Task<HttpResponseData> UpdateOverheadCost(
        [HttpTrigger(AuthorizationLevel.Function, "put", Route = "overhead-costs/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var outletId = OutletHelper.GetOutletIdFromRequest(req, _authService);
            
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var overheadCost = JsonSerializer.Deserialize<OverheadCost>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (overheadCost == null)
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Invalid overhead cost data");
                return badRequestResponse;
            }

            // Ensure the OutletId matches the request header
            overheadCost.OutletId = outletId;
            overheadCost.UpdatedAt = DateTime.UtcNow;

            var success = await _mongoService.UpdateOverheadCostAsync(id, overheadCost);
            if (!success)
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteStringAsync("Overhead cost not found");
                return notFoundResponse;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Overhead cost updated successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating overhead cost: {Id}", id);
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("DeleteOverheadCost")]
    public async Task<HttpResponseData> DeleteOverheadCost(
        [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "overhead-costs/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var outletId = OutletHelper.GetOutletIdFromRequest(req, _authService);
            
            // Verify the overhead cost belongs to the outlet before deleting
            var existingCost = await _mongoService.GetOverheadCostByIdAsync(id);
            if (existingCost == null)
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteStringAsync("Overhead cost not found");
                return notFoundResponse;
            }

            if (existingCost.OutletId != outletId && existingCost.OutletId != null)
            {
                var forbiddenResponse = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbiddenResponse.WriteStringAsync("Cannot delete overhead cost from another outlet");
                return forbiddenResponse;
            }

            var success = await _mongoService.DeleteOverheadCostAsync(id);
            if (!success)
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteStringAsync("Overhead cost not found");
                return notFoundResponse;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Overhead cost deleted successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting overhead cost: {Id}", id);
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("CalculateOverheadAllocation")]
    public async Task<HttpResponseData> CalculateOverheadAllocation(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "overhead-costs/calculate")] HttpRequestData req)
    {
        try
        {
            var outletId = OutletHelper.GetOutletIdFromRequest(req, _authService);
            _logger.LogInformation("CalculateOverheadAllocation called for outlet {OutletId}", outletId);
            
            if (!int.TryParse(req.Query["preparationTimeMinutes"], out var preparationTimeMinutes) || preparationTimeMinutes <= 0)
            {
                _logger.LogWarning("Invalid preparationTimeMinutes parameter");
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Valid preparation time in minutes is required");
                return badRequestResponse;
            }

            _logger.LogInformation("Calculating allocation for {Minutes} minutes", preparationTimeMinutes);
            var allocation = await _mongoService.CalculateOverheadAllocationAsync(preparationTimeMinutes, outletId);
            _logger.LogInformation("Allocation calculated successfully");

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(allocation);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating overhead allocation");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("InitializeDefaultOverheadCosts")]
    public async Task<HttpResponseData> InitializeDefaultOverheadCosts(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "overhead-costs/initialize")] HttpRequestData req)
    {
        try
        {
            await _mongoService.InitializeDefaultOverheadCostsAsync();

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Default overhead costs initialized successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing default overhead costs");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("MigrateOverheadCostOutlets")]
    [OpenApiOperation(operationId: "MigrateOverheadCostOutlets", tags: new[] { "OverheadCosts" }, Summary = "Migrate overhead costs to add OutletId", Description = "Assigns the specified OutletId to all overhead costs that don't have one.")]
    [OpenApiParameter(name: "targetOutletId", In = ParameterLocation.Query, Required = true, Type = typeof(string), Description = "The OutletId to assign to overhead costs without an outlet")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Migration completed successfully")]
    public async Task<HttpResponseData> MigrateOverheadCostOutlets(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "overhead-costs/migrate-outlets")] HttpRequestData req)
    {
        try
        {
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var targetOutletId = query["targetOutletId"];

            if (string.IsNullOrWhiteSpace(targetOutletId))
            {
                var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await errorResponse.WriteStringAsync("targetOutletId parameter is required");
                return errorResponse;
            }

            var migratedCount = await _mongoService.MigrateOverheadCostOutletIdsAsync(targetOutletId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                message = "Migration completed successfully",
                migratedCount = migratedCount,
                targetOutletId = targetOutletId
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error migrating overhead cost outlets");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }
}
