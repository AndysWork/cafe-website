using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Cafe.Api.Services;
using Cafe.Api.Models;
using Cafe.Api.Helpers;

namespace Cafe.Api.Functions;

public class InventoryFunction
{
    private readonly MongoService _mongoService;
    private readonly IEmailService _emailService;
    private readonly ILogger<InventoryFunction> _logger;

    public InventoryFunction(
        MongoService mongoService,
        IEmailService emailService,
        ILogger<InventoryFunction> logger)
    {
        _mongoService = mongoService;
        _emailService = emailService;
        _logger = logger;
    }

    [Function("GetAllInventory")]
    public async Task<HttpResponseData> GetAllInventory(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "inventory")] HttpRequestData req)
    {
        try
        {
            var inventory = await _mongoService.GetAllInventoryAsync();
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(inventory);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all inventory");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("GetActiveInventory")]
    public async Task<HttpResponseData> GetActiveInventory(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "inventory/active")] HttpRequestData req)
    {
        try
        {
            var inventory = await _mongoService.GetActiveInventoryAsync();
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(inventory);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active inventory");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("GetInventoryByIngredientId")]
    public async Task<HttpResponseData> GetInventoryByIngredientId(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "inventory/ingredient/{ingredientId}")] HttpRequestData req,
        string ingredientId)
    {
        try
        {
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
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("GetLowStockItems")]
    public async Task<HttpResponseData> GetLowStockItems(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "inventory/low-stock")] HttpRequestData req)
    {
        try
        {
            var items = await _mongoService.GetLowStockItemsAsync();
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(items);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting low stock items");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("GetOutOfStockItems")]
    public async Task<HttpResponseData> GetOutOfStockItems(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "inventory/out-of-stock")] HttpRequestData req)
    {
        try
        {
            var items = await _mongoService.GetOutOfStockItemsAsync();
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(items);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting out of stock items");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("GetExpiringItems")]
    public async Task<HttpResponseData> GetExpiringItems(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "inventory/expiring")] HttpRequestData req)
    {
        try
        {
            var daysThreshold = 7;
            if (req.Query["days"] != null && int.TryParse(req.Query["days"], out var days))
            {
                daysThreshold = days;
            }

            var items = await _mongoService.GetExpiringItemsAsync(daysThreshold);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(items);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting expiring items");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("CreateInventory")]
    public async Task<HttpResponseData> CreateInventory(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "inventory")] HttpRequestData req)
    {
        try
        {
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var inventory = JsonSerializer.Deserialize<Inventory>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            });

            if (inventory == null)
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Invalid inventory data");
                return badRequestResponse;
            }

            // Validate required fields
            if (string.IsNullOrEmpty(inventory.IngredientName) || string.IsNullOrEmpty(inventory.Unit))
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Ingredient name and unit are required");
                return badRequestResponse;
            }

            var createdInventory = await _mongoService.CreateInventoryAsync(inventory);

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(createdInventory);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating inventory");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("UpdateInventory")]
    public async Task<HttpResponseData> UpdateInventory(
        [HttpTrigger(AuthorizationLevel.Function, "put", Route = "inventory/item/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var inventory = JsonSerializer.Deserialize<Inventory>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            });

            if (inventory == null)
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Invalid inventory data");
                return badRequestResponse;
            }

            var success = await _mongoService.UpdateInventoryAsync(id, inventory);
            if (!success)
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteStringAsync("Inventory item not found");
                return notFoundResponse;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Inventory updated successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating inventory: {Id}", id);
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("DeleteInventory")]
    public async Task<HttpResponseData> DeleteInventory(
        [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "inventory/item/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var success = await _mongoService.DeleteInventoryAsync(id);
            if (!success)
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteStringAsync("Inventory item not found");
                return notFoundResponse;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Inventory deleted successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting inventory: {Id}", id);
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("StockIn")]
    public async Task<HttpResponseData> StockIn(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "inventory/item/{id}/stock-in")] HttpRequestData req,
        string id)
    {
        try
        {
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var data = JsonSerializer.Deserialize<StockInRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (data == null || data.Quantity <= 0)
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Invalid stock-in data. Quantity must be positive.");
                return badRequestResponse;
            }

            var success = await _mongoService.StockInAsync(
                id,
                data.Quantity,
                data.CostPerUnit,
                data.SupplierName,
                data.ReferenceNumber,
                data.PerformedBy ?? "System"
            );

            if (!success)
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteStringAsync("Inventory item not found");
                return notFoundResponse;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Stock added successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding stock: {Id}", id);
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("StockOut")]
    public async Task<HttpResponseData> StockOut(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "inventory/item/{id}/stock-out")] HttpRequestData req,
        string id)
    {
        try
        {
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var data = JsonSerializer.Deserialize<StockOutRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (data == null || data.Quantity <= 0)
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Invalid stock-out data. Quantity must be positive.");
                return badRequestResponse;
            }

            var success = await _mongoService.StockOutAsync(
                id,
                data.Quantity,
                data.Reason ?? "Stock usage",
                data.PerformedBy ?? "System"
            );

            if (!success)
            {
                var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await errorResponse.WriteStringAsync("Inventory item not found or insufficient stock");
                return errorResponse;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Stock removed successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing stock: {Id}", id);
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("AdjustStock")]
    public async Task<HttpResponseData> AdjustStock(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "inventory/item/{id}/adjust")] HttpRequestData req,
        string id)
    {
        try
        {
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var data = JsonSerializer.Deserialize<StockAdjustmentRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (data == null || data.QuantityChange == 0)
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Invalid adjustment data");
                return badRequestResponse;
            }

            var success = await _mongoService.AdjustStockAsync(
                id,
                data.QuantityChange,
                TransactionType.Adjustment,
                data.Reason ?? "Stock adjustment",
                data.PerformedBy ?? "System",
                data.ReferenceNumber
            );

            if (!success)
            {
                var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await errorResponse.WriteStringAsync("Inventory item not found or invalid adjustment");
                return errorResponse;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Stock adjusted successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adjusting stock: {Id}", id);
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("GetInventoryTransactions")]
    public async Task<HttpResponseData> GetInventoryTransactions(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "inventory/item/{id}/transactions")] HttpRequestData req,
        string id)
    {
        try
        {
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
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("GetRecentTransactions")]
    public async Task<HttpResponseData> GetRecentTransactions(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "inventory/transactions/recent")] HttpRequestData req)
    {
        try
        {
            var limit = 20;
            if (req.Query["limit"] != null && int.TryParse(req.Query["limit"], out var l))
            {
                limit = l;
            }

            var transactions = await _mongoService.GetRecentTransactionsAsync(limit);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(transactions);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting recent transactions");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("GetStockAlerts")]
    public async Task<HttpResponseData> GetStockAlerts(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "inventory/alerts")] HttpRequestData req)
    {
        try
        {
            var alerts = await _mongoService.GetAllAlertsAsync();
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(alerts);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting stock alerts");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("GetCriticalAlerts")]
    public async Task<HttpResponseData> GetCriticalAlerts(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "inventory/alerts/critical")] HttpRequestData req)
    {
        try
        {
            var alerts = await _mongoService.GetCriticalAlertsAsync();
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(alerts);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting critical alerts");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("ResolveAlert")]
    public async Task<HttpResponseData> ResolveAlert(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "inventory/alerts/{alertId}/resolve")] HttpRequestData req,
        string alertId)
    {
        try
        {
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var data = JsonSerializer.Deserialize<ResolveAlertRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            var success = await _mongoService.ResolveAlertAsync(alertId, data?.ResolvedBy ?? "System");
            if (!success)
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteStringAsync("Alert not found");
                return notFoundResponse;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Alert resolved successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving alert: {AlertId}", alertId);
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("GetInventoryReport")]
    public async Task<HttpResponseData> GetInventoryReport(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "inventory/report")] HttpRequestData req)
    {
        try
        {
            var report = await _mongoService.GetInventoryReportAsync();
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(report);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting inventory report");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    [Function("GetInventoryById")]
    public async Task<HttpResponseData> GetInventoryById(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "inventory/item/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
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
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }
}

// Request DTOs
public class StockInRequest
{
    public decimal Quantity { get; set; }
    public decimal? CostPerUnit { get; set; }
    public string? SupplierName { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? PerformedBy { get; set; }
}

public class StockOutRequest
{
    public decimal Quantity { get; set; }
    public string? Reason { get; set; }
    public string? PerformedBy { get; set; }
}

public class StockAdjustmentRequest
{
    public decimal QuantityChange { get; set; }
    public string? Reason { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? PerformedBy { get; set; }
}

public class ResolveAlertRequest
{
    public string? ResolvedBy { get; set; }
}
