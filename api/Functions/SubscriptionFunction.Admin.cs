using System.Net;
using Cafe.Api.Helpers;
using Cafe.Api.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Cafe.Api.Functions;

public partial class SubscriptionFunction
{
    [Function("GetSubscriptionOverview")]
    public async Task<HttpResponseData> GetSubscriptionOverview(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/subscriptions/overview")] HttpRequestData req)
    {
        try
        {
            var (authorized, _, _, error) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!authorized) return error!;
            var overview = await _mongo.GetSubscriptionOverviewAsync(OutletHelper.GetOutletIdForAdmin(req, _auth) ?? "default");
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(overview);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to load subscription overview");
            return await AdminSubscriptionError(req, "Failed to load subscription overview");
        }
    }

    [Function("GetSubscriptionMenuSets")]
    public async Task<HttpResponseData> GetSubscriptionMenuSets(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/subscriptions/menu-sets")] HttpRequestData req)
    {
        try
        {
            var (authorized, _, _, error) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!authorized) return error!;
            var sets = await _mongo.GetSubscriptionMenuSetsAsync(OutletHelper.GetOutletIdForAdmin(req, _auth) ?? "default");
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(sets);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to load subscription menu sets");
            return await AdminSubscriptionError(req, "Failed to load saved menu sets");
        }
    }

    [Function("CreateSubscriptionMenuSet")]
    public async Task<HttpResponseData> CreateSubscriptionMenuSet(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/subscriptions/menu-sets")] HttpRequestData req)
    {
        try
        {
            var (authorized, _, _, error) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!authorized) return error!;
            var (request, validation) = await ValidationHelper.ValidateBody<CreateSubscriptionMenuSetRequest>(req);
            if (validation != null) return validation;
            var itemsError = await ValidatePlanItems(req, request.Items);
            if (itemsError != null) return itemsError;
            var set = await _mongo.CreateSubscriptionMenuSetAsync(new SubscriptionMenuSet
            {
                OutletId = OutletHelper.GetOutletIdForAdmin(req, _auth) ?? "default",
                Name = InputSanitizer.Sanitize(request.Name.Trim()),
                Items = CopyPlanItems(request.Items)
            });
            var response = req.CreateResponse(HttpStatusCode.Created);
            response.Headers.Add("Location", $"/api/manage/subscriptions/menu-sets/{set.Id}");
            await response.WriteAsJsonAsync(set);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to save subscription menu set");
            return await AdminSubscriptionError(req, "Failed to save menu set");
        }
    }

    [Function("DeleteSubscriptionMenuSet")]
    public async Task<HttpResponseData> DeleteSubscriptionMenuSet(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "manage/subscriptions/menu-sets/{id}")] HttpRequestData req, string id)
    {
        try
        {
            var (authorized, _, _, error) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!authorized) return error!;
            var deleted = await _mongo.DeleteSubscriptionMenuSetAsync(OutletHelper.GetOutletIdForAdmin(req, _auth) ?? "default", id);
            if (!deleted)
            {
                var missing = req.CreateResponse(HttpStatusCode.NotFound);
                await missing.WriteAsJsonAsync(new { error = "Saved menu set not found" });
                return missing;
            }
            return req.CreateResponse(HttpStatusCode.NoContent);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to delete subscription menu set {Id}", id);
            return await AdminSubscriptionError(req, "Failed to delete menu set");
        }
    }

    private static async Task<HttpResponseData?> ValidatePlanItems(HttpRequestData req, List<SubscriptionItem>? items)
    {
        var allowed = new HashSet<string> { "Everyday", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
        if (items is { Count: > 0 } && items.All(i => i != null && !string.IsNullOrWhiteSpace(i.MenuItemId)
                && !string.IsNullOrWhiteSpace(i.MenuItemName) && i.DailyQuantity is >= 1 and <= 20
                && i.ServingDays is { Count: > 0 } && i.ServingDays.All(allowed.Contains))
            && items.Select(i => i.MenuItemId).Distinct().Count() == items.Count) return null;
        var response = req.CreateResponse(HttpStatusCode.BadRequest);
        await response.WriteAsJsonAsync(new { error = "Select unique menu items, a quantity from 1 to 20, and valid serving days for every item." });
        return response;
    }

    private static List<SubscriptionItem> CopyPlanItems(List<SubscriptionItem>? items) =>
        (items ?? new()).Select(i => new SubscriptionItem
        {
            MenuItemId = i.MenuItemId, MenuItemName = InputSanitizer.Sanitize(i.MenuItemName),
            DailyQuantity = i.DailyQuantity, UnitPrice = i.UnitPrice, CategoryName = i.CategoryName,
            ImageUrl = i.ImageUrl, IsVeg = i.IsVeg,
            ServingDays = i.ServingDays.Contains("Everyday") ? new() { "Everyday" } : i.ServingDays.Distinct().ToList()
        }).ToList();

    private static async Task<HttpResponseData> AdminSubscriptionError(HttpRequestData req, string message)
    {
        var response = req.CreateResponse(HttpStatusCode.InternalServerError);
        await response.WriteAsJsonAsync(new { error = message });
        return response;
    }
}
