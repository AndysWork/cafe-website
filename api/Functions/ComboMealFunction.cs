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
            foreach (var combo in combos)
            {
                if (combo.ComboWebPrice <= 0)
                    combo.ComboWebPrice = combo.ComboPrice;
            }
            var now = MongoService.GetIstNow();
            var active = combos.Where(c =>
                    c.IsActive &&
                    (c.ValidFrom == null || c.ValidFrom <= now) &&
                    (c.ValidTill == null || c.ValidTill >= now))
                .ToList();

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
            foreach (var combo in combos)
            {
                if (combo.ComboWebPrice <= 0)
                    combo.ComboWebPrice = combo.ComboPrice;
            }

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
            if (string.IsNullOrWhiteSpace(outletId))
            {
                var badOutlet = req.CreateResponse(HttpStatusCode.BadRequest);
                await badOutlet.WriteAsJsonAsync(new { error = "Outlet context is required" });
                return badOutlet;
            }

            if (request.ValidFrom.HasValue && request.ValidTill.HasValue && request.ValidFrom > request.ValidTill)
            {
                var badDates = req.CreateResponse(HttpStatusCode.BadRequest);
                await badDates.WriteAsJsonAsync(new { error = "validFrom cannot be later than validTill" });
                return badDates;
            }

            if (request.Items.Any(i => string.IsNullOrWhiteSpace(i.MenuItemId)))
            {
                var badItems = req.CreateResponse(HttpStatusCode.BadRequest);
                await badItems.WriteAsJsonAsync(new { error = "Each combo item must include a menuItemId" });
                return badItems;
            }

            if (request.Items.Any(i => i.Quantity < 1 || i.Quantity > 10))
            {
                var badQty = req.CreateResponse(HttpStatusCode.BadRequest);
                await badQty.WriteAsJsonAsync(new { error = "Each combo item quantity must be between 1 and 10" });
                return badQty;
            }

            if (request.Items.Any(i => i.SelectedPieces.HasValue && i.SelectedPieces.Value < 1))
            {
                var badPieces = req.CreateResponse(HttpStatusCode.BadRequest);
                await badPieces.WriteAsJsonAsync(new { error = "selectedPieces must be at least 1 when provided" });
                return badPieces;
            }

            var normalizedIds = request.Items.Select(i => i.MenuItemId.Trim()).ToList();
            if (normalizedIds.Count != normalizedIds.Distinct().Count())
            {
                var duplicateItems = req.CreateResponse(HttpStatusCode.BadRequest);
                await duplicateItems.WriteAsJsonAsync(new { error = "Duplicate menu items are not allowed in a combo" });
                return duplicateItems;
            }

            // Look up menu items to get names and prices
            var menuItemIds = normalizedIds;
            var menuItems = await _mongo.GetMenuItemsByIdsAsync(menuItemIds, outletId);
            var foundIds = menuItems.Where(m => !string.IsNullOrWhiteSpace(m.Id)).Select(m => m.Id!).ToHashSet();
            var missingIds = menuItemIds.Where(id => !foundIds.Contains(id)).ToList();
            if (missingIds.Count > 0)
            {
                var badRefs = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRefs.WriteAsJsonAsync(new { error = "Some menu items do not exist for this outlet", missingMenuItemIds = missingIds });
                return badRefs;
            }

            for (var idx = 0; idx < request.Items.Count; idx++)
            {
                var itemId = menuItemIds[idx];
                var mi = menuItems.First(m => m.Id == itemId);
                var basePieces = mi.Quantity > 0 ? mi.Quantity : 1;
                var selectedPieces = request.Items[idx].SelectedPieces ?? basePieces;
                if (selectedPieces > basePieces)
                {
                    var badPieces = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badPieces.WriteAsJsonAsync(new
                    {
                        error = "selectedPieces cannot be greater than the menu item total pieces",
                        menuItemId = itemId,
                        selectedPieces,
                        basePieces
                    });
                    return badPieces;
                }
            }

            var combo = new ComboMeal
            {
                OutletId = outletId,
                Name = InputSanitizer.Sanitize(request.Name),
                Description = InputSanitizer.Sanitize(request.Description),
                ImageUrl = request.ImageUrl,
                Items = request.Items.Select((i, idx) =>
                {
                    var itemId = menuItemIds[idx];
                    var mi = menuItems.First(m => m.Id == itemId);
                    var basePieces = mi.Quantity > 0 ? mi.Quantity : 1;
                    var selectedPieces = i.SelectedPieces ?? basePieces;
                    var portionFactor = Math.Round((decimal)selectedPieces / basePieces, 6, MidpointRounding.AwayFromZero);
                    return new ComboItem
                    {
                        MenuItemId = itemId,
                        MenuItemName = mi.Name,
                        Quantity = i.Quantity,
                        SelectedPieces = selectedPieces,
                        BasePieces = basePieces,
                        PortionFactor = portionFactor,
                        OriginalPrice = mi.WebPrice > 0 ? mi.WebPrice : (mi.ShopSellingPrice > 0 ? mi.ShopSellingPrice : mi.OnlinePrice)
                    };
                }).ToList(),
                ComboPrice = request.ComboPrice,
                ComboOnlinePrice = request.ComboOnlinePrice ?? request.ComboPrice,
                ComboWebPrice = request.ComboWebPrice ?? request.ComboPrice,
                ValidFrom = request.ValidFrom,
                ValidTill = request.ValidTill,
                IsActive = true,
                CreatedAt = MongoService.GetIstNow()
            };

            combo.OriginalPrice = combo.Items.Sum(i => i.OriginalPrice * i.Quantity * (i.PortionFactor <= 0 ? 1 : i.PortionFactor));
            if (combo.ComboPrice > combo.OriginalPrice)
            {
                var badPrice = req.CreateResponse(HttpStatusCode.BadRequest);
                await badPrice.WriteAsJsonAsync(new { error = "comboPrice cannot be greater than the total original price" });
                return badPrice;
            }

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

            if (request.ValidFrom.HasValue && request.ValidTill.HasValue && request.ValidFrom > request.ValidTill)
            {
                var badDates = req.CreateResponse(HttpStatusCode.BadRequest);
                await badDates.WriteAsJsonAsync(new { error = "validFrom cannot be later than validTill" });
                return badDates;
            }

            if (request.Items.Any(i => string.IsNullOrWhiteSpace(i.MenuItemId)))
            {
                var badItems = req.CreateResponse(HttpStatusCode.BadRequest);
                await badItems.WriteAsJsonAsync(new { error = "Each combo item must include a menuItemId" });
                return badItems;
            }

            if (request.Items.Any(i => i.Quantity < 1 || i.Quantity > 10))
            {
                var badQty = req.CreateResponse(HttpStatusCode.BadRequest);
                await badQty.WriteAsJsonAsync(new { error = "Each combo item quantity must be between 1 and 10" });
                return badQty;
            }

            if (request.Items.Any(i => i.SelectedPieces.HasValue && i.SelectedPieces.Value < 1))
            {
                var badPieces = req.CreateResponse(HttpStatusCode.BadRequest);
                await badPieces.WriteAsJsonAsync(new { error = "selectedPieces must be at least 1 when provided" });
                return badPieces;
            }

            var normalizedIds = request.Items.Select(i => i.MenuItemId.Trim()).ToList();
            if (normalizedIds.Count != normalizedIds.Distinct().Count())
            {
                var duplicateItems = req.CreateResponse(HttpStatusCode.BadRequest);
                await duplicateItems.WriteAsJsonAsync(new { error = "Duplicate menu items are not allowed in a combo" });
                return duplicateItems;
            }

            var menuItemIds = normalizedIds;
            var menuItems = await _mongo.GetMenuItemsByIdsAsync(menuItemIds, existing.OutletId);
            var foundIds = menuItems.Where(m => !string.IsNullOrWhiteSpace(m.Id)).Select(m => m.Id!).ToHashSet();
            var missingIds = menuItemIds.Where(itemId => !foundIds.Contains(itemId)).ToList();
            if (missingIds.Count > 0)
            {
                var badRefs = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRefs.WriteAsJsonAsync(new { error = "Some menu items do not exist for this outlet", missingMenuItemIds = missingIds });
                return badRefs;
            }

            for (var idx = 0; idx < request.Items.Count; idx++)
            {
                var itemId = menuItemIds[idx];
                var mi = menuItems.First(m => m.Id == itemId);
                var basePieces = mi.Quantity > 0 ? mi.Quantity : 1;
                var selectedPieces = request.Items[idx].SelectedPieces ?? basePieces;
                if (selectedPieces > basePieces)
                {
                    var badPieces = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badPieces.WriteAsJsonAsync(new
                    {
                        error = "selectedPieces cannot be greater than the menu item total pieces",
                        menuItemId = itemId,
                        selectedPieces,
                        basePieces
                    });
                    return badPieces;
                }
            }

            existing.Name = InputSanitizer.Sanitize(request.Name);
            existing.Description = InputSanitizer.Sanitize(request.Description);
            existing.ImageUrl = request.ImageUrl;
            existing.Items = request.Items.Select((i, idx) =>
            {
                var itemId = menuItemIds[idx];
                var mi = menuItems.First(m => m.Id == itemId);
                var basePieces = mi.Quantity > 0 ? mi.Quantity : 1;
                var selectedPieces = i.SelectedPieces ?? basePieces;
                var portionFactor = Math.Round((decimal)selectedPieces / basePieces, 6, MidpointRounding.AwayFromZero);
                return new ComboItem
                {
                    MenuItemId = itemId,
                    MenuItemName = mi.Name,
                    Quantity = i.Quantity,
                    SelectedPieces = selectedPieces,
                    BasePieces = basePieces,
                    PortionFactor = portionFactor,
                    OriginalPrice = mi.WebPrice > 0 ? mi.WebPrice : (mi.ShopSellingPrice > 0 ? mi.ShopSellingPrice : mi.OnlinePrice)
                };
            }).ToList();
            existing.OriginalPrice = existing.Items.Sum(i => i.OriginalPrice * i.Quantity * (i.PortionFactor <= 0 ? 1 : i.PortionFactor));
            existing.ComboPrice = request.ComboPrice;
            existing.ComboOnlinePrice = request.ComboOnlinePrice ?? request.ComboPrice;
            existing.ComboWebPrice = request.ComboWebPrice ?? request.ComboPrice;
            if (existing.ComboPrice > existing.OriginalPrice)
            {
                var badPrice = req.CreateResponse(HttpStatusCode.BadRequest);
                await badPrice.WriteAsJsonAsync(new { error = "comboPrice cannot be greater than the total original price" });
                return badPrice;
            }
            existing.SavingsAmount = existing.OriginalPrice - existing.ComboPrice;
            existing.SavingsPercent = existing.OriginalPrice > 0
                ? Math.Round((existing.SavingsAmount / existing.OriginalPrice) * 100, 1)
                : 0;
            existing.ValidFrom = request.ValidFrom;
            existing.ValidTill = request.ValidTill;

            var updated = await _mongo.UpdateComboMealAsync(id, existing);
            if (!updated)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Combo not found" });
                return notFound;
            }

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

            var deleted = await _mongo.DeleteComboMealAsync(id);
            if (!deleted)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Combo not found" });
                return notFound;
            }

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
