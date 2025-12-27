using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Cafe.Api.Services;
using Cafe.Api.Models;

namespace Cafe.Api.Functions;

public class OverheadCostFunction
{
    private readonly MongoService _mongoService;
    private readonly ILogger<OverheadCostFunction> _logger;

    public OverheadCostFunction(MongoService mongoService, ILogger<OverheadCostFunction> logger)
    {
        _mongoService = mongoService;
        _logger = logger;
    }

    [Function("GetAllOverheadCosts")]
    public async Task<HttpResponseData> GetAllOverheadCosts(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "overhead-costs")] HttpRequestData req)
    {
        try
        {
            var overheadCosts = await _mongoService.GetAllOverheadCostsAsync();
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
            var overheadCosts = await _mongoService.GetActiveOverheadCostsAsync();
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
            if (!int.TryParse(req.Query["preparationTimeMinutes"], out var preparationTimeMinutes) || preparationTimeMinutes <= 0)
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Valid preparation time in minutes is required");
                return badRequestResponse;
            }

            var allocation = await _mongoService.CalculateOverheadAllocationAsync(preparationTimeMinutes);

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
}
