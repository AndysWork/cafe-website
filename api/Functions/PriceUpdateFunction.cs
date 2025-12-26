using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Models;
using Cafe.Api.Helpers;
using System.Net;
using System.Text.Json;

namespace Cafe.Api.Functions;

public class PriceUpdateFunction
{
    private readonly MongoService _mongoService;
    private readonly MarketPriceService _priceService;
    private readonly AuthService _authService;
    private readonly ILogger<PriceUpdateFunction> _logger;

    public PriceUpdateFunction(
        MongoService mongoService,
        MarketPriceService priceService,
        AuthService authService,
        ILogger<PriceUpdateFunction> logger)
    {
        _mongoService = mongoService;
        _priceService = priceService;
        _authService = authService;
        _logger = logger;
    }

    // GET: Get price history for an ingredient
    [Function("GetPriceHistory")]
    public async Task<HttpResponseData> GetPriceHistory(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "ingredients/{id}/price-history")] HttpRequestData req,
        string id)
    {
        try
        {
            // Check authorization
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
            if (!isAuthorized) return errorResponse!;

            var daysParam = req.Query["days"];
            var days = int.TryParse(daysParam, out var d) ? d : 30;

            var history = await _mongoService.GetPriceHistoryAsync(id, days);
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                success = true,
                data = history,
                count = history.Count
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error getting price history for ingredient {id}");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { success = false, error = ex.Message });
            return response;
        }
    }

    // GET: Get price trends for an ingredient
    [Function("GetPriceTrends")]
    public async Task<HttpResponseData> GetPriceTrends(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "ingredients/{id}/price-trends")] HttpRequestData req,
        string id)
    {
        try
        {
            var daysParam = req.Query["days"];
            var days = int.TryParse(daysParam, out var d) ? d : 30;

            var trends = await _mongoService.GetPriceTrendsAsync(id, days);
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                success = true,
                data = trends
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error getting price trends for ingredient {id}");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { success = false, error = ex.Message });
            return response;
        }
    }

    // POST: Manually refresh price for a single ingredient
    [Function("RefreshIngredientPrice")]
    public async Task<HttpResponseData> RefreshIngredientPrice(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "ingredients/{id}/refresh-price")] HttpRequestData req,
        string id)
    {
        try
        {
            // Check authorization
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
            if (!isAuthorized) return errorResponse!;

            var ingredient = await _mongoService.GetIngredientByIdAsync(id);
            if (ingredient == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { success = false, error = "Ingredient not found" });
                return notFound;
            }

            _logger.LogInformation($"Fetching price for {ingredient.Name}");
            var priceResult = await _priceService.FetchPriceAsync(ingredient.Name, ingredient.Category, ingredient.Unit);

            if (!priceResult.Success)
            {
                var failed = req.CreateResponse(HttpStatusCode.OK);
                await failed.WriteAsJsonAsync(new
                {
                    success = false,
                    message = "Could not fetch price from external sources",
                    error = priceResult.ErrorMessage,
                    ingredient = ingredient.Name
                });
                return failed;
            }

            // Update ingredient with new price
            var updated = await _mongoService.UpdateIngredientPriceAsync(
                id,
                priceResult.Price,
                priceResult.Source,
                priceResult.MarketName
            );

            if (!updated)
            {
                var updateFailed = req.CreateResponse(HttpStatusCode.InternalServerError);
                await updateFailed.WriteAsJsonAsync(new { success = false, error = "Failed to update price" });
                return updateFailed;
            }

            // Get updated ingredient
            var updatedIngredient = await _mongoService.GetIngredientByIdAsync(id);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                success = true,
                message = "Price updated successfully",
                data = new
                {
                    ingredient = updatedIngredient,
                    priceChange = updatedIngredient?.PriceChangePercentage,
                    source = priceResult.Source,
                    market = priceResult.MarketName
                }
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error refreshing price for ingredient {id}");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { success = false, error = ex.Message });
            return response;
        }
    }

    // POST: Bulk refresh prices for multiple ingredients
    [Function("BulkRefreshPrices")]
    public async Task<HttpResponseData> BulkRefreshPrices(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "ingredients/bulk-refresh-prices")] HttpRequestData req)
    {
        try
        {
            // Check authorization
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
            if (!isAuthorized) return errorResponse!;

            // Get ingredients that have auto-update enabled
            var ingredients = await _mongoService.GetIngredientsForAutoUpdateAsync();
            
            if (ingredients.Count == 0)
            {
                var noIngredients = req.CreateResponse(HttpStatusCode.OK);
                await noIngredients.WriteAsJsonAsync(new
                {
                    success = true,
                    message = "No ingredients configured for auto-update",
                    updated = 0,
                    failed = 0
                });
                return noIngredients;
            }

            _logger.LogInformation($"Starting bulk price refresh for {ingredients.Count} ingredients");
            
            var results = await _priceService.FetchBulkPricesAsync(ingredients);
            var updated = 0;
            var failed = 0;
            var errors = new List<string>();

            foreach (var result in results)
            {
                if (result.Success)
                {
                    var ingredient = ingredients.FirstOrDefault(i => i.Name == result.IngredientName);
                    if (ingredient != null)
                    {
                        var success = await _mongoService.UpdateIngredientPriceAsync(
                            ingredient.Id!,
                            result.Price,
                            result.Source,
                            result.MarketName
                        );
                        if (success) updated++;
                        else failed++;
                    }
                }
                else
                {
                    failed++;
                    errors.Add($"{result.IngredientName}: {result.ErrorMessage}");
                }
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                success = true,
                message = $"Bulk price refresh completed: {updated} updated, {failed} failed",
                updated,
                failed,
                total = ingredients.Count,
                errors = errors.Take(10) // Limit error messages
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during bulk price refresh");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { success = false, error = ex.Message });
            return response;
        }
    }

    // GET: Get price update settings
    [Function("GetPriceUpdateSettings")]
    public async Task<HttpResponseData> GetPriceUpdateSettings(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "price-settings")] HttpRequestData req)
    {
        try
        {
            // Check authorization
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
            if (!isAuthorized) return errorResponse!;

            var settings = await _mongoService.GetPriceUpdateSettingsAsync();
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                success = true,
                data = settings ?? new PriceUpdateSettings()
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting price update settings");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { success = false, error = ex.Message });
            return response;
        }
    }

    // PUT: Update price update settings
    [Function("UpdatePriceUpdateSettings")]
    public async Task<HttpResponseData> UpdatePriceUpdateSettings(
        [HttpTrigger(AuthorizationLevel.Function, "put", Route = "price-settings")] HttpRequestData req)
    {
        try
        {
            // Check authorization
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
            if (!isAuthorized) return errorResponse!;

            var settings = await req.ReadFromJsonAsync<PriceUpdateSettings>();
            if (settings == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { success = false, error = "Invalid settings data" });
                return badRequest;
            }

            var savedSettings = await _mongoService.SavePriceUpdateSettingsAsync(settings);
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                success = true,
                message = "Settings updated successfully",
                data = savedSettings
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating price settings");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { success = false, error = ex.Message });
            return response;
        }
    }

    // POST: Toggle auto-update for an ingredient
    [Function("ToggleIngredientAutoUpdate")]
    public async Task<HttpResponseData> ToggleIngredientAutoUpdate(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "ingredients/{id}/toggle-auto-update")] HttpRequestData req,
        string id)
    {
        try
        {
            // Check authorization
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
            if (!isAuthorized) return errorResponse!;

            var ingredient = await _mongoService.GetIngredientByIdAsync(id);
            if (ingredient == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { success = false, error = "Ingredient not found" });
                return notFound;
            }

            ingredient.AutoUpdateEnabled = !ingredient.AutoUpdateEnabled;
            ingredient.UpdatedAt = DateTime.UtcNow;

            var updated = await _mongoService.UpdateIngredientAsync(id, ingredient);
            if (!updated)
            {
                var updateFailed = req.CreateResponse(HttpStatusCode.InternalServerError);
                await updateFailed.WriteAsJsonAsync(new { success = false, error = "Failed to update ingredient" });
                return updateFailed;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                success = true,
                message = $"Auto-update {(ingredient.AutoUpdateEnabled ? "enabled" : "disabled")} for {ingredient.Name}",
                autoUpdateEnabled = ingredient.AutoUpdateEnabled
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error toggling auto-update for ingredient {id}");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { success = false, error = ex.Message });
            return response;
        }
    }
}
