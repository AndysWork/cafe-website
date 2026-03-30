using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Models;
using Cafe.Api.Helpers;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;

namespace Cafe.Api.Functions;

public class InventoryQueryFunction
{
    private readonly MongoService _mongoService;
    private readonly AuthService _authService;
    private readonly ILogger<InventoryQueryFunction> _logger;

    public InventoryQueryFunction(
        MongoService mongoService,
        AuthService authService,
        ILogger<InventoryQueryFunction> logger)
    {
        _mongoService = mongoService;
        _authService = authService;
        _logger = logger;
    }

    [Function("GetAllInventory")]
    [OpenApiOperation(operationId: "GetAllInventory", tags: new[] { "Inventory" }, Summary = "Get all inventory", Description = "Retrieves all inventory items for the specified outlet")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "X-Outlet-Id", In = ParameterLocation.Header, Required = false, Type = typeof(string), Description = "Outlet ID")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(List<Inventory>), Description = "Successfully retrieved inventory")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "User not authenticated")]
    public async Task<HttpResponseData> GetAllInventory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "inventory")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, authError) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
            if (!isAuthorized) return authError!;

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _authService);
            var (page, pageSize) = Helpers.PaginationHelper.ParsePagination(req);
            var inventory = await _mongoService.GetAllInventoryAsync(outletId, page, pageSize);
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            if (page.HasValue && pageSize.HasValue)
            {
                var total = await _mongoService.GetAllInventoryCountAsync(outletId);
                await response.WriteAsJsonAsync(new { items = inventory, total, page, pageSize });
            }
            else
            {
                await response.WriteAsJsonAsync(inventory);
            }
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all inventory");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { error = "An internal error occurred" });
            return response;
        }
    }

    [Function("GetActiveInventory")]
    [OpenApiOperation(operationId: "GetActiveInventory", tags: new[] { "Inventory" }, Summary = "Get active inventory", Description = "Retrieves all active inventory items with stock")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "X-Outlet-Id", In = ParameterLocation.Header, Required = false, Type = typeof(string), Description = "Outlet ID")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(List<Inventory>), Description = "Successfully retrieved active inventory")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "User not authenticated")]
    public async Task<HttpResponseData> GetActiveInventory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "inventory/active")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, authError) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
            if (!isAuthorized) return authError!;

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _authService);
            var (page, pageSize) = Helpers.PaginationHelper.ParsePagination(req);
            var inventory = await _mongoService.GetActiveInventoryAsync(outletId, page, pageSize);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(inventory);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active inventory");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { error = "An internal error occurred" });
            return response;
        }
    }

    [Function("GetInventoryByIngredientId")]
    [OpenApiOperation(operationId: "GetInventoryByIngredientId", tags: new[] { "Inventory" }, Summary = "Get inventory by ingredient", Description = "Retrieves inventory for a specific ingredient")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "ingredientId", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Ingredient ID")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(Inventory), Description = "Successfully retrieved inventory")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.NotFound, Description = "Inventory not found")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "User not authenticated")]
    public async Task<HttpResponseData> GetInventoryByIngredientId(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "inventory/ingredient/{ingredientId}")] HttpRequestData req,
        string ingredientId)
    {
        try
        {
            var (isAuthorized, _, _, authError) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
            if (!isAuthorized) return authError!;

            var inventory = await _mongoService.GetInventoryByIngredientIdAsync(ingredientId);
            if (inventory == null)
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteStringAsync("Inventory item not found for this ingredient");
                return notFoundResponse;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(inventory);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting inventory by ingredient ID: {IngredientId}", ingredientId);
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { error = "An internal error occurred" });
            return response;
        }
    }

    [Function("GetLowStockItems")]
    public async Task<HttpResponseData> GetLowStockItems(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "inventory/low-stock")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, authError) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
            if (!isAuthorized) return authError!;

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _authService);
            var items = await _mongoService.GetLowStockItemsAsync(outletId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(items);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting low stock items");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { error = "An internal error occurred" });
            return response;
        }
    }

    [Function("GetOutOfStockItems")]
    public async Task<HttpResponseData> GetOutOfStockItems(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "inventory/out-of-stock")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, authError) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
            if (!isAuthorized) return authError!;

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _authService);
            var items = await _mongoService.GetOutOfStockItemsAsync(outletId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(items);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting out of stock items");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { error = "An internal error occurred" });
            return response;
        }
    }

    [Function("GetExpiringItems")]
    public async Task<HttpResponseData> GetExpiringItems(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "inventory/expiring")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, authError) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
            if (!isAuthorized) return authError!;

            var daysThreshold = 7;
            if (req.Query["days"] != null && int.TryParse(req.Query["days"], out var days))
            {
                daysThreshold = days;
            }

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _authService);
            var items = await _mongoService.GetExpiringItemsAsync(daysThreshold, outletId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(items);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting expiring items");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { error = "An internal error occurred" });
            return response;
        }
    }

    [Function("GetInventoryTransactions")]
    public async Task<HttpResponseData> GetInventoryTransactions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "inventory/item/{id}/transactions")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthorized, _, _, authError) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
            if (!isAuthorized) return authError!;

            var limit = 50;
            if (req.Query["limit"] != null && int.TryParse(req.Query["limit"], out var l))
            {
                limit = l;
            }

            var transactions = await _mongoService.GetTransactionsByInventoryIdAsync(id, limit);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(transactions);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting transactions: {Id}", id);
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { error = "An internal error occurred" });
            return response;
        }
    }

    [Function("GetRecentTransactions")]
    public async Task<HttpResponseData> GetRecentTransactions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "inventory/transactions/recent")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, authError) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
            if (!isAuthorized) return authError!;

            var limit = 20;
            if (req.Query["limit"] != null && int.TryParse(req.Query["limit"], out var l))
            {
                limit = l;
            }

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _authService);
            var transactions = await _mongoService.GetRecentTransactionsAsync(limit, outletId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(transactions);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting recent transactions");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { error = "An internal error occurred" });
            return response;
        }
    }

    [Function("GetStockAlerts")]
    public async Task<HttpResponseData> GetStockAlerts(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "inventory/alerts")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, authError) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
            if (!isAuthorized) return authError!;

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _authService);
            var alerts = await _mongoService.GetAllAlertsAsync(outletId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(alerts);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting stock alerts");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { error = "An internal error occurred" });
            return response;
        }
    }

    [Function("GetCriticalAlerts")]
    public async Task<HttpResponseData> GetCriticalAlerts(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "inventory/alerts/critical")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, authError) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
            if (!isAuthorized) return authError!;

            var alerts = await _mongoService.GetCriticalAlertsAsync();
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(alerts);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting critical alerts");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { error = "An internal error occurred" });
            return response;
        }
    }

    [Function("GetInventoryReport")]
    public async Task<HttpResponseData> GetInventoryReport(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "inventory/report")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, authError) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
            if (!isAuthorized) return authError!;

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _authService);
            var report = await _mongoService.GetInventoryReportAsync(outletId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(report);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting inventory report");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { error = "An internal error occurred" });
            return response;
        }
    }

    [Function("GetInventoryById")]
    public async Task<HttpResponseData> GetInventoryById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "inventory/item/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthorized, _, _, authError) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
            if (!isAuthorized) return authError!;

            // Validate that the ID is a valid MongoDB ObjectId format (24 hex characters)
            if (string.IsNullOrEmpty(id) || id.Length != 24 || !System.Text.RegularExpressions.Regex.IsMatch(id, "^[0-9a-fA-F]{24}$"))
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Invalid inventory ID format");
                return badRequestResponse;
            }

            var inventory = await _mongoService.GetInventoryByIdAsync(id);
            if (inventory == null)
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteStringAsync("Inventory item not found");
                return notFoundResponse;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(inventory);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting inventory by ID: {Id}", id);
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { error = "An internal error occurred" });
            return response;
        }
    }
}
