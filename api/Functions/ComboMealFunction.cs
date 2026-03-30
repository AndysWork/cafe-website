using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Repositories;
using Cafe.Api.Models;
using Cafe.Api.Helpers;
using System.Net;

namespace Cafe.Api.Functions;

public class ComboMealFunction
{
    private readonly IMenuRepository _mongo;
    private readonly AuthService _auth;
    private readonly ILogger _log;

    public ComboMealFunction(IMenuRepository mongo, AuthService auth, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _auth = auth;
        _log = loggerFactory.CreateLogger<ComboMealFunction>();
    }

    [Function("GetActiveCombos")]
    public async Task<HttpResponseData> GetActiveCombos(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "combos")] HttpRequestData req)
    {
        try
        {
            var outletId = req.Query["outletId"] ?? "default";
            var combos = await _mongo.GetComboMealsAsync(outletId);
            var now = MongoService.GetIstNow();
            var active = combos.Where(c => c.IsActive && (c.ValidTill == null || c.ValidTill >= now)).ToList();

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(active);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting active combos");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("GetAllCombos")]
    public async Task<HttpResponseData> GetAllCombos(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/combos")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);
            var combos = await _mongo.GetComboMealsAsync(outletId ?? "default");

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(combos);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting combos");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("CreateComboMeal")]
    public async Task<HttpResponseData> CreateComboMeal(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/combos")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var (request, validationError) = await ValidationHelper.ValidateBody<CreateComboMealRequest>(req);
            if (validationError != null) return validationError;

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);

            // Look up menu items to get names and prices
            var menuItemIds = request.Items.Select(i => i.MenuItemId).ToList();
            var menuItems = await _mongo.GetMenuItemsByIdsAsync(menuItemIds, outletId);

            var combo = new ComboMeal
            {
                OutletId = outletId ?? "default",
                Name = InputSanitizer.Sanitize(request.Name),
                Description = request.Description != null ? InputSanitizer.Sanitize(request.Description) : null,
                ImageUrl = request.ImageUrl,
                Items = request.Items.Select(i =>
                {
                    var mi = menuItems.FirstOrDefault(m => m.Id == i.MenuItemId);
                    return new ComboItem
                    {
                        MenuItemId = i.MenuItemId,
                        MenuItemName = mi?.Name ?? "Unknown",
                        Quantity = i.Quantity,
                        OriginalPrice = mi?.OnlinePrice ?? 0
                    };
                }).ToList(),
                ComboPrice = request.ComboPrice,
                ValidFrom = request.ValidFrom,
                ValidTill = request.ValidTill,
                IsActive = true,
                CreatedAt = MongoService.GetIstNow()
            };

            combo.OriginalPrice = combo.Items.Sum(i => i.OriginalPrice * i.Quantity);
            combo.SavingsAmount = combo.OriginalPrice - combo.ComboPrice;
            combo.SavingsPercent = combo.OriginalPrice > 0
                ? Math.Round((combo.SavingsAmount / combo.OriginalPrice) * 100, 1)
                : 0;

            await _mongo.CreateComboMealAsync(combo);

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(combo);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error creating combo meal");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("UpdateComboMeal")]
    public async Task<HttpResponseData> UpdateComboMeal(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "manage/combos/{id}")] HttpRequestData req, string id)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var (request, validationError) = await ValidationHelper.ValidateBody<CreateComboMealRequest>(req);
            if (validationError != null) return validationError;

            var existing = await _mongo.GetComboMealByIdAsync(id);
            if (existing == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Combo not found" });
                return notFound;
            }

            var menuItemIds = request.Items.Select(i => i.MenuItemId).ToList();
            var menuItems = await _mongo.GetMenuItemsByIdsAsync(menuItemIds, existing.OutletId);

            existing.Name = InputSanitizer.Sanitize(request.Name);
            existing.Description = request.Description != null ? InputSanitizer.Sanitize(request.Description) : null;
            existing.ImageUrl = request.ImageUrl;
            existing.Items = request.Items.Select(i =>
            {
                var mi = menuItems.FirstOrDefault(m => m.Id == i.MenuItemId);
                return new ComboItem
                {
                    MenuItemId = i.MenuItemId,
                    MenuItemName = mi?.Name ?? "Unknown",
                    Quantity = i.Quantity,
                    OriginalPrice = mi?.OnlinePrice ?? 0
                };
            }).ToList();
            existing.OriginalPrice = existing.Items.Sum(i => i.OriginalPrice * i.Quantity);
            existing.ComboPrice = request.ComboPrice;
            existing.SavingsAmount = existing.OriginalPrice - existing.ComboPrice;
            existing.SavingsPercent = existing.OriginalPrice > 0
                ? Math.Round((existing.SavingsAmount / existing.OriginalPrice) * 100, 1)
                : 0;
            existing.ValidFrom = request.ValidFrom;
            existing.ValidTill = request.ValidTill;

            await _mongo.UpdateComboMealAsync(id, existing);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(existing);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error updating combo meal");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("DeleteComboMeal")]
    public async Task<HttpResponseData> DeleteComboMeal(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "manage/combos/{id}")] HttpRequestData req, string id)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            await _mongo.DeleteComboMealAsync(id);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Combo meal deleted" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error deleting combo meal");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }
}
