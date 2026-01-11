using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Models;
using Cafe.Api.Helpers;
using System.Net;
using System.Text.Json;

namespace Cafe.Api.Functions;

public class PriceForecastFunction
{
    private readonly MongoService _mongo;
    private readonly AuthService _auth;
    private readonly ILogger _log;

    public PriceForecastFunction(MongoService mongo, AuthService auth, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _auth = auth;
        _log = loggerFactory.CreateLogger<PriceForecastFunction>();
    }

    private void CalculateProfits(PriceForecast forecast)
    {
        // Online Payout = ((Online Price + Packaging) - Discount%) - (((Online Price + Packaging) - Discount%) × Deduction%)
        var baseAmount = forecast.OnlinePrice + forecast.PackagingCost;
        var discountAmount = (baseAmount * forecast.OnlineDiscount) / 100;
        var afterDiscount = baseAmount - discountAmount;
        var deductionAmount = (afterDiscount * forecast.OnlineDeduction) / 100;
        forecast.OnlinePayout = Math.Max(0, afterDiscount - deductionAmount);

        // Online Profit = Online Payout - Making Price
        forecast.OnlineProfit = Math.Max(0, forecast.OnlinePayout - forecast.MakePrice);

        // Offline Profit = Shop Price - Making Price
        forecast.OfflineProfit = Math.Max(0, forecast.ShopPrice - forecast.MakePrice);

        // Takeaway Profit = Shop Delivery Price - (Making Price + Packaging Price)
        forecast.TakeawayProfit = Math.Max(0, forecast.ShopDeliveryPrice - (forecast.MakePrice + forecast.PackagingCost));

        // Keep backward compatibility with PayoutCalculation
        forecast.PayoutCalculation = forecast.OnlinePayout;
    }

    // GET: Get all price forecasts
    [Function("GetPriceForecasts")]
    public async Task<HttpResponseData> GetPriceForecasts(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "priceforecasts")] HttpRequestData req)
    {
        try
        {
            // Validate admin or manager authorization
            var (isAuthorized, userId, username, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var outletId = OutletHelper.GetOutletIdFromRequest(req, _auth);
            var forecasts = await _mongo.GetPriceForecastsAsync(outletId);
            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(forecasts);
            return res;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting price forecasts");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = ex.Message });
            return res;
        }
    }

    // GET: Get price forecasts by menu item ID
    [Function("GetPriceForecastsByMenuItem")]
    public async Task<HttpResponseData> GetPriceForecastsByMenuItem(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "priceforecasts/menuitem/{menuItemId}")] HttpRequestData req,
        string menuItemId)
    {
        try
        {
            // Validate admin or manager authorization
            var (isAuthorized, userId, username, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var forecasts = await _mongo.GetPriceForecastsByMenuItemAsync(menuItemId);
            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(forecasts);
            return res;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting price forecasts for menu item {MenuItemId}", menuItemId);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = ex.Message });
            return res;
        }
    }

    // GET: Get single price forecast by ID
    [Function("GetPriceForecast")]
    public async Task<HttpResponseData> GetPriceForecast(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "priceforecasts/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            // Validate admin or manager authorization
            var (isAuthorized, userId, username, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var forecast = await _mongo.GetPriceForecastAsync(id);
            if (forecast == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Price forecast not found" });
                return notFound;
            }

            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(forecast);
            return res;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting price forecast {Id}", id);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = ex.Message });
            return res;
        }
    }

    // POST: Create a new price forecast
    [Function("CreatePriceForecast")]
    public async Task<HttpResponseData> CreatePriceForecast(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "priceforecasts")] HttpRequestData req)
    {
        try
        {
            // Validate admin or manager authorization
            var (isAuthorized, userId, role, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var outletId = OutletHelper.GetOutletIdFromRequest(req, _auth);

            var forecast = await req.ReadFromJsonAsync<PriceForecast>();
            if (forecast == null)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Invalid price forecast data" });
                return badReq;
            }

            // Validate that menu item exists
            if (string.IsNullOrEmpty(forecast.MenuItemId))
            {
                var badReq2 = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq2.WriteAsJsonAsync(new { error = "Menu item ID is required" });
                return badReq2;
            }

            var menuItem = await _mongo.GetMenuItemAsync(forecast.MenuItemId);
            if (menuItem == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Menu item not found" });
                return notFound;
            }

            forecast.OutletId = outletId;
            forecast.MenuItemName = menuItem.Name;
            forecast.CreatedBy = userId ?? "System";
            forecast.LastUpdatedBy = userId ?? "System";

            // Calculate all profit metrics
            CalculateProfits(forecast);

            var created = await _mongo.CreatePriceForecastAsync(forecast);
            var res = req.CreateResponse(HttpStatusCode.Created);
            await res.WriteAsJsonAsync(created);
            return res;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error creating price forecast");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = ex.Message });
            return res;
        }
    }

    // PUT: Update an existing price forecast
    [Function("UpdatePriceForecast")]
    public async Task<HttpResponseData> UpdatePriceForecast(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "priceforecasts/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            // Validate admin or manager authorization
            var (isAuthorized, userId, role, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var outletId = OutletHelper.GetOutletIdFromRequest(req, _auth);

            var existingForecast = await _mongo.GetPriceForecastAsync(id);
            if (existingForecast == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Price forecast not found" });
                return notFound;
            }

            if (existingForecast.IsFinalized)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Cannot update finalized price forecast" });
                return badReq;
            }

            var forecast = await req.ReadFromJsonAsync<PriceForecast>();
            if (forecast == null)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Invalid price forecast data" });
                return badReq;
            }

            // Add current state to history before updating
            var historyEntry = new PriceHistory
            {
                ChangeDate = MongoService.GetIstNow(),
                ChangedBy = userId ?? "System",
                MakePrice = existingForecast.MakePrice,
                PackagingCost = existingForecast.PackagingCost,
                ShopPrice = existingForecast.ShopPrice,
                ShopDeliveryPrice = existingForecast.ShopDeliveryPrice,
                OnlinePrice = existingForecast.OnlinePrice,
                UpdatedShopPrice = existingForecast.UpdatedShopPrice,
                UpdatedOnlinePrice = existingForecast.UpdatedOnlinePrice,
                OnlineDeduction = existingForecast.OnlineDeduction,
                OnlineDiscount = existingForecast.OnlineDiscount,
                PayoutCalculation = existingForecast.PayoutCalculation,
                OnlinePayout = existingForecast.OnlinePayout,
                OnlineProfit = existingForecast.OnlineProfit,
                OfflineProfit = existingForecast.OfflineProfit,
                TakeawayProfit = existingForecast.TakeawayProfit,
                ChangeReason = "Price update"
            };

            forecast.History = existingForecast.History ?? new List<PriceHistory>();
            forecast.History.Add(historyEntry);
            forecast.Id = id;
            forecast.OutletId = outletId;
            forecast.CreatedBy = existingForecast.CreatedBy;
            forecast.CreatedDate = existingForecast.CreatedDate;
            forecast.LastUpdatedBy = userId ?? "System";
            forecast.IsFinalized = existingForecast.IsFinalized;
            forecast.FinalizedDate = existingForecast.FinalizedDate;
            forecast.FinalizedBy = existingForecast.FinalizedBy;

            // Calculate all profit metrics
            CalculateProfits(forecast);

            var updated = await _mongo.UpdatePriceForecastAsync(id, forecast);
            if (!updated)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Failed to update price forecast" });
                return notFound;
            }

            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(forecast);
            return res;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error updating price forecast {Id}", id);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = ex.Message });
            return res;
        }
    }

    // DELETE: Delete a price forecast
    [Function("DeletePriceForecast")]
    public async Task<HttpResponseData> DeletePriceForecast(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "priceforecasts/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            // Validate admin authorization
            var (isAuthorized, userId, role, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var outletId = OutletHelper.GetOutletIdFromRequest(req, _auth);

            var existingForecast = await _mongo.GetPriceForecastAsync(id);
            if (existingForecast == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Price forecast not found" });
                return notFound;
            }

            if (existingForecast.OutletId != outletId && existingForecast.OutletId != null)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Cannot delete price forecast from another outlet" });
                return forbidden;
            }

            if (existingForecast.IsFinalized)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Cannot delete finalized price forecast" });
                return badReq;
            }

            var deleted = await _mongo.DeletePriceForecastAsync(id);
            if (!deleted)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Price forecast not found" });
                return notFound;
            }

            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(new { message = "Price forecast deleted successfully" });
            return res;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error deleting price forecast {Id}", id);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = ex.Message });
            return res;
        }
    }

    // POST: Finalize price forecast and update menu item
    [Function("FinalizePriceForecast")]
    public async Task<HttpResponseData> FinalizePriceForecast(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "priceforecasts/{id}/finalize")] HttpRequestData req,
        string id)
    {
        try
        {
            // Validate admin authorization
            var (isAuthorized, userId, role, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var finalized = await _mongo.FinalizePriceForecastAsync(id, userId ?? "System");
            
            if (!finalized)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Failed to finalize price forecast. It may already be finalized or not found." });
                return badReq;
            }

            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(new { message = "Price forecast finalized and menu item updated successfully" });
            return res;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error finalizing price forecast {Id}", id);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = ex.Message });
            return res;
        }
    }
}
