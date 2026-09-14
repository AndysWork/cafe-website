using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Cafe.Api.Services;
using Cafe.Api.Models;
using Cafe.Api.Helpers;
using Cafe.Api.Repositories;

namespace Cafe.Api.Functions;

public class InventoryCategoryFunction
{
    private readonly IInventoryRepository _inventoryRepo;
    private readonly AuthService _authService;
    private readonly MongoService _mongoService;
    private readonly ILogger<InventoryCategoryFunction> _logger;

    public InventoryCategoryFunction(
        IInventoryRepository inventoryRepo,
        AuthService authService,
        MongoService mongoService,
        ILogger<InventoryCategoryFunction> logger)
    {
        _inventoryRepo = inventoryRepo;
        _authService = authService;
        _mongoService = mongoService;
        _logger = logger;
    }

    [Function("GetInventoryCategories")]
    public async Task<HttpResponseData> GetInventoryCategories(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "inventory/categories")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, role, authError) = await AuthorizationHelper.ValidateKitchenAccessRole(req, _authService);
            if (!isAuthorized) return authError!;

            var outletId = OutletHelper.GetOutletIdFromRequest(req, _authService) 
                           ?? OutletHelper.GetOutletIdForAdmin(req, _authService);

            if (string.IsNullOrWhiteSpace(outletId))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Outlet ID is required to fetch inventory categories" });
                return badReq;
            }

            var categories = await _inventoryRepo.GetInventoryCategoriesAsync(outletId);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(categories);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting inventory categories");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { error = "An internal error occurred" });
            return response;
        }
    }

    [Function("CreateInventoryCategory")]
    public async Task<HttpResponseData> CreateInventoryCategory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "inventory/categories")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, role, authError) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _authService);
            if (!isAuthorized) return authError!;

            var (hasAccess, outletId, accessError) = await OutletHelper.ValidateOutletAccess(req, _authService, _mongoService);
            if (!hasAccess || string.IsNullOrWhiteSpace(outletId))
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = accessError ?? "Outlet access required" });
                return forbidden;
            }

            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var category = JsonSerializer.Deserialize<InventoryCategory>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (category == null || string.IsNullOrWhiteSpace(category.Name))
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Category name is required");
                return badRequestResponse;
            }

            category.OutletId = outletId;
            category.CreatedBy = userId ?? "admin";
            category.LastUpdatedBy = userId ?? "admin";

            var created = await _inventoryRepo.CreateInventoryCategoryAsync(category);

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(created);
            return response;
        }
        catch (InvalidOperationException ex)
        {
            var conflict = req.CreateResponse(HttpStatusCode.Conflict);
            await conflict.WriteAsJsonAsync(new { error = ex.Message });
            return conflict;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating inventory category");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { error = "An internal error occurred" });
            return response;
        }
    }

    [Function("UpdateInventoryCategory")]
    public async Task<HttpResponseData> UpdateInventoryCategory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "inventory/categories/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthorized, userId, role, authError) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _authService);
            if (!isAuthorized) return authError!;

            var (hasAccess, outletId, accessError) = await OutletHelper.ValidateOutletAccess(req, _authService, _mongoService);
            if (!hasAccess || string.IsNullOrWhiteSpace(outletId))
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = accessError ?? "Outlet access required" });
                return forbidden;
            }

            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var category = JsonSerializer.Deserialize<InventoryCategory>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (category == null || string.IsNullOrWhiteSpace(category.Name))
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Category name is required");
                return badRequestResponse;
            }

            category.OutletId = outletId;
            category.LastUpdatedBy = userId ?? "admin";

            var success = await _inventoryRepo.UpdateInventoryCategoryAsync(id, category);
            if (!success)
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteStringAsync("Category not found");
                return notFoundResponse;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Category updated successfully" });
            return response;
        }
        catch (InvalidOperationException ex)
        {
            var conflict = req.CreateResponse(HttpStatusCode.Conflict);
            await conflict.WriteAsJsonAsync(new { error = ex.Message });
            return conflict;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating inventory category: {Id}", id);
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { error = "An internal error occurred" });
            return response;
        }
    }

    [Function("DeleteInventoryCategory")]
    public async Task<HttpResponseData> DeleteInventoryCategory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "inventory/categories/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthorized, userId, role, authError) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _authService);
            if (!isAuthorized) return authError!;

            var (hasAccess, outletId, accessError) = await OutletHelper.ValidateOutletAccess(req, _authService, _mongoService);
            if (!hasAccess || string.IsNullOrWhiteSpace(outletId))
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = accessError ?? "Outlet access required" });
                return forbidden;
            }

            var (success, errorMessage) = await _inventoryRepo.DeleteInventoryCategoryAsync(id, outletId, userId ?? "admin");
            if (!success)
            {
                if (!string.IsNullOrEmpty(errorMessage))
                {
                    var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                    await conflict.WriteAsJsonAsync(new { error = errorMessage });
                    return conflict;
                }

                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteStringAsync("Category not found");
                return notFound;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Category deleted successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting inventory category: {Id}", id);
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { error = "An internal error occurred" });
            return response;
        }
    }
}
