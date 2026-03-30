using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Cafe.Api.Services;
using Cafe.Api.Models;
using Cafe.Api.Helpers;

namespace Cafe.Api.Functions;

public class InventoryCommandFunction
{
    private readonly MongoService _mongoService;
    private readonly AuthService _authService;
    private readonly IEmailService _emailService;
    private readonly ILogger<InventoryCommandFunction> _logger;

    public InventoryCommandFunction(
        MongoService mongoService,
        AuthService authService,
        IEmailService emailService,
        ILogger<InventoryCommandFunction> logger)
    {
        _mongoService = mongoService;
        _authService = authService;
        _emailService = emailService;
        _logger = logger;
    }

    [Function("CreateInventory")]
    public async Task<HttpResponseData> CreateInventory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "inventory")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, authError) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
            if (!isAuthorized) return authError!;

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

            // Validate outlet access and assign outlet
            var (hasAccess, outletId, accessError) = await OutletHelper.ValidateOutletAccess(req, _authService, _mongoService);
            if (!hasAccess)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = accessError });
                return forbidden;
            }
            
            inventory.OutletId = outletId;
            var createdInventory = await _mongoService.CreateInventoryAsync(inventory);

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(createdInventory);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating inventory");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { error = "An internal error occurred" });
            return response;
        }
    }

    [Function("UpdateInventory")]
    public async Task<HttpResponseData> UpdateInventory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "inventory/item/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthorized, _, _, authError) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
            if (!isAuthorized) return authError!;

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
            await response.WriteAsJsonAsync(new { error = "An internal error occurred" });
            return response;
        }
    }

    [Function("DeleteInventory")]
    public async Task<HttpResponseData> DeleteInventory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "inventory/item/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthorized, _, _, authError) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
            if (!isAuthorized) return authError!;

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
            await response.WriteAsJsonAsync(new { error = "An internal error occurred" });
            return response;
        }
    }

    [Function("StockIn")]
    public async Task<HttpResponseData> StockIn(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "inventory/item/{id}/stock-in")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthorized, _, _, authError) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
            if (!isAuthorized) return authError!;

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
            await response.WriteAsJsonAsync(new { error = "An internal error occurred" });
            return response;
        }
    }

    [Function("StockOut")]
    public async Task<HttpResponseData> StockOut(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "inventory/item/{id}/stock-out")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthorized, _, _, authError) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
            if (!isAuthorized) return authError!;

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
            await response.WriteAsJsonAsync(new { error = "An internal error occurred" });
            return response;
        }
    }

    [Function("AdjustStock")]
    public async Task<HttpResponseData> AdjustStock(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "inventory/item/{id}/adjust")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthorized, _, _, authError) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
            if (!isAuthorized) return authError!;

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
            await response.WriteAsJsonAsync(new { error = "An internal error occurred" });
            return response;
        }
    }

    [Function("ResolveAlert")]
    public async Task<HttpResponseData> ResolveAlert(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "inventory/alerts/{alertId}/resolve")] HttpRequestData req,
        string alertId)
    {
        try
        {
            var (isAuthorized, _, _, authError) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
            if (!isAuthorized) return authError!;

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
            await response.WriteAsJsonAsync(new { error = "An internal error occurred" });
            return response;
        }
    }

    [Function("MigrateInventoryTransactionOutlets")]
    public async Task<HttpResponseData> MigrateInventoryTransactionOutlets(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "inventory/migrate-transaction-outlets")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, authError) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
            if (!isAuthorized) return authError!;

            _logger.LogInformation("Starting migration of inventory transaction outlet IDs");
            
            // Parse request body to get default outlet ID
            string? defaultOutletId = null;
            try
            {
                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                if (!string.IsNullOrEmpty(requestBody))
                {
                    var jsonDoc = System.Text.Json.JsonDocument.Parse(requestBody);
                    if (jsonDoc.RootElement.TryGetProperty("defaultOutletId", out var outletIdElement))
                    {
                        defaultOutletId = outletIdElement.GetString();
                    }
                }
            }
            catch
            {
                // If parsing fails, continue without default outlet ID
            }
            
            var updated = await _mongoService.MigrateInventoryTransactionOutletIdsAsync(defaultOutletId);
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { 
                success = true, 
                message = $"Successfully updated {updated} inventory transactions with outlet IDs",
                updatedCount = updated,
                defaultOutletIdUsed = defaultOutletId
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error migrating inventory transaction outlet IDs");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { 
                success = false, 
                error = "An internal error occurred" 
            });
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
