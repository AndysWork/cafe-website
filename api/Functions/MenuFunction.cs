using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Models;
using Cafe.Api.Helpers;
using System.Net;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;

namespace Cafe.Api.Functions;

public class MenuFunction
{
    private readonly MongoService _mongo;
    private readonly AuthService _auth;
    private readonly ILogger _log;

    public MenuFunction(MongoService mongo, AuthService auth, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _auth = auth;
        _log = loggerFactory.CreateLogger<MenuFunction>();
    }

    /// <summary>
    /// Retrieves all menu items
    /// </summary>
    /// <param name="req">HTTP request</param>
    /// <returns>List of all menu items with pricing and details</returns>
    /// <response code="200">Successfully retrieved menu items</response>
    // GET: Get all menu items
    [Function("GetMenu")]
    [OpenApiOperation(operationId: "GetMenu", tags: new[] { "Menu" }, Summary = "Get all menu items", Description = "Retrieves all menu items with pricing and availability")]
    [OpenApiParameter(name: "X-Outlet-Id", In = ParameterLocation.Header, Required = false, Type = typeof(string), Description = "Outlet ID for filtering menu items")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(List<CafeMenuItem>), Description = "Successfully retrieved menu items")]
    public async Task<HttpResponseData> GetMenu([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "menu")] HttpRequestData req)
    {
        try
        {
            var outletId = OutletHelper.GetOutletIdFromRequest(req, _auth);
            var items = await _mongo.GetMenuAsync(outletId);
            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(items);
            return res;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting menu items");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = ex.Message });
            return res;
        }
    }

    /// <summary>
    /// Retrieves menu items for a specific category
    /// </summary>
    /// <param name="req">HTTP request</param>
    /// <param name="categoryId">The category ID to filter by</param>
    /// <returns>List of menu items in the specified category</returns>
    /// <response code="200">Successfully retrieved menu items</response>
    // GET: Get menu items by CategoryId
    [Function("GetMenuItemsByCategory")]
    [OpenApiOperation(operationId: "GetMenuItemsByCategory", tags: new[] { "Menu" }, Summary = "Get menu items by category", Description = "Retrieves all menu items in a specific category")]
    [OpenApiParameter(name: "categoryId", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Category ID")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(List<CafeMenuItem>), Description = "Successfully retrieved menu items")]
    public async Task<HttpResponseData> GetMenuItemsByCategory([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "categories/{categoryId}/menu")] HttpRequestData req, string categoryId)
    {
        try
        {
            var items = await _mongo.GetMenuItemsByCategoryAsync(categoryId);
            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(items);
            return res;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting menu items for category {CategoryId}", categoryId);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = ex.Message });
            return res;
        }
    }

    /// <summary>
    /// Retrieves menu items for a specific subcategory
    /// </summary>
    /// <param name="req">HTTP request</param>
    /// <param name="subCategoryId">The subcategory ID to filter by</param>
    /// <returns>List of menu items in the specified subcategory</returns>
    /// <response code="200">Successfully retrieved menu items</response>
    // GET: Get menu items by SubCategoryId
    [Function("GetMenuItemsBySubCategory")]
    [OpenApiOperation(operationId: "GetMenuItemsBySubCategory", tags: new[] { "Menu" }, Summary = "Get menu items by subcategory", Description = "Retrieves all menu items in a specific subcategory")]
    [OpenApiParameter(name: "subCategoryId", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Subcategory ID")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(List<CafeMenuItem>), Description = "Successfully retrieved menu items")]
    public async Task<HttpResponseData> GetMenuItemsBySubCategory([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "subcategories/{subCategoryId}/menu")] HttpRequestData req, string subCategoryId)
    {
        try
        {
            var items = await _mongo.GetMenuItemsBySubCategoryAsync(subCategoryId);
            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(items);
            return res;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting menu items for subcategory {SubCategoryId}", subCategoryId);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = ex.Message });
            return res;
        }
    }

    /// <summary>
    /// Retrieves a specific menu item by ID
    /// </summary>
    /// <param name="req">HTTP request</param>
    /// <param name="id">The menu item ID</param>
    /// <returns>Menu item details including name, description, pricing, and category</returns>
    /// <response code="200">Successfully retrieved menu item</response>
    /// <response code="404">Menu item not found</response>
    // GET: Get single menu item by ID
    [Function("GetMenuItem")]
    [OpenApiOperation(operationId: "GetMenuItem", tags: new[] { "Menu" }, Summary = "Get menu item by ID", Description = "Retrieves a specific menu item by its ID")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Menu item ID")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(CafeMenuItem), Description = "Successfully retrieved menu item")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.NotFound, Description = "Menu item not found")]
    public async Task<HttpResponseData> GetMenuItem([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "menu/{id}")] HttpRequestData req, string id)
    {
        try
        {
            var item = await _mongo.GetMenuItemAsync(id);
            if (item == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Menu item not found" });
                return notFound;
            }

            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(item);
            return res;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting menu item {Id}", id);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = ex.Message });
            return res;
        }
    }

    /// <summary>
    /// Creates a new menu item (Admin only)
    /// </summary>
    /// <param name="req">HTTP request with menu item data</param>
    /// <returns>Created menu item with ID</returns>
    /// <response code="201">Menu item successfully created</response>
    /// <response code="400">Invalid menu item data</response>
    /// <response code="401">User not authenticated</response>
    /// <response code="403">User not authorized (admin role required)</response>
    // POST: Create new menu item (Admin only)
    [Function("CreateMenuItem")]
    [OpenApiOperation(operationId: "CreateMenuItem", tags: new[] { "Menu" }, Summary = "Create a new menu item", Description = "Creates a new menu item (Admin only)")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(CafeMenuItem), Required = true, Description = "Menu item details")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Created, contentType: "application/json", bodyType: typeof(CafeMenuItem), Description = "Menu item successfully created")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.BadRequest, Description = "Invalid menu item data")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "User not authenticated")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Forbidden, Description = "User not authorized")]
    public async Task<HttpResponseData> CreateMenuItem([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "menu")] HttpRequestData req)
    {
        try
        {
            // Validate admin authorization
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var item = await req.ReadFromJsonAsync<CafeMenuItem>();
            if (item == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { success = false, error = "Invalid menu item data" });
                return badRequest;
            }

            // Get or validate outlet ID
            var (hasAccess, outletId, accessError) = await OutletHelper.ValidateOutletAccess(req, _auth, _mongo);
            if (!hasAccess)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { success = false, error = accessError });
                return forbidden;
            }
            
            // Set the outlet ID if not provided
            if (string.IsNullOrEmpty(item.OutletId))
            {
                item.OutletId = outletId!;
            }

            // Validate request
            if (!ValidationHelper.TryValidate(item, out var validationError))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(validationError!.Value);
                return badRequest;
            }

            // Validate CategoryId exists
            if (!string.IsNullOrEmpty(item.CategoryId))
            {
                var category = await _mongo.GetCategoryAsync(item.CategoryId);
                if (category == null)
                {
                    var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequest.WriteAsJsonAsync(new { success = false, error = $"Category with ID {item.CategoryId} not found" });
                    return badRequest;
                }
            }

            // Validate SubCategoryId exists and belongs to the specified Category
            if (!string.IsNullOrEmpty(item.SubCategoryId))
            {
                var subCategory = await _mongo.GetSubCategoryAsync(item.SubCategoryId);
                if (subCategory == null)
                {
                    var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequest.WriteAsJsonAsync(new { success = false, error = $"SubCategory with ID {item.SubCategoryId} not found" });
                    return badRequest;
                }

                // Verify SubCategory belongs to the specified Category
                if (!string.IsNullOrEmpty(item.CategoryId) && subCategory.CategoryId != item.CategoryId)
                {
                    var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequest.WriteAsJsonAsync(new { success = false, error = $"SubCategory {item.SubCategoryId} does not belong to Category {item.CategoryId}" });
                    return badRequest;
                }
            }

            var created = await _mongo.CreateMenuItemAsync(item);
            var res = req.CreateResponse(HttpStatusCode.Created);
            await res.WriteAsJsonAsync(new { success = true, data = created });
            return res;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error creating menu item");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = ex.Message });
            return res;
        }
    }

    // PUT: Update menu item (Admin only)
    [Function("UpdateMenuItem")]
    [OpenApiOperation(operationId: "UpdateMenuItem", tags: new[] { "Menu" }, Summary = "Update menu item", Description = "Updates an existing menu item (Admin only)")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Menu item ID")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(CafeMenuItem), Required = true, Description = "Updated menu item details")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(CafeMenuItem), Description = "Successfully updated menu item")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.NotFound, Description = "Menu item not found")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "User not authenticated")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Forbidden, Description = "User not authorized")]
    public async Task<HttpResponseData> UpdateMenuItem([HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "menu/{id}")] HttpRequestData req, string id)
    {
        try
        {
            // Validate admin authorization
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var item = await req.ReadFromJsonAsync<CafeMenuItem>();
            if (item == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid menu item data" });
                return badRequest;
            }

            // Ensure outlet ID is set
            if (string.IsNullOrEmpty(item.OutletId))
            {
                // Get outlet ID from request
                var (hasAccess, outletId, accessError) = await OutletHelper.ValidateOutletAccess(req, _auth, _mongo);
                if (!hasAccess)
                {
                    var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                    await forbidden.WriteAsJsonAsync(new { error = accessError });
                    return forbidden;
                }
                item.OutletId = outletId!;
            }

            // Validate CategoryId exists
            if (!string.IsNullOrEmpty(item.CategoryId))
            {
                var category = await _mongo.GetCategoryAsync(item.CategoryId);
                if (category == null)
                {
                    var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequest.WriteAsJsonAsync(new { error = $"Category with ID {item.CategoryId} not found" });
                    return badRequest;
                }
            }

            // Validate SubCategoryId exists and belongs to the specified Category
            if (!string.IsNullOrEmpty(item.SubCategoryId))
            {
                var subCategory = await _mongo.GetSubCategoryAsync(item.SubCategoryId);
                if (subCategory == null)
                {
                    var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequest.WriteAsJsonAsync(new { error = $"SubCategory with ID {item.SubCategoryId} not found" });
                    return badRequest;
                }

                // Verify SubCategory belongs to the specified Category
                if (!string.IsNullOrEmpty(item.CategoryId) && subCategory.CategoryId != item.CategoryId)
                {
                    var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequest.WriteAsJsonAsync(new { error = $"SubCategory {item.SubCategoryId} does not belong to Category {item.CategoryId}" });
                    return badRequest;
                }
            }

            item.Id = id;
            var success = await _mongo.UpdateMenuItemAsync(id, item);
            
            if (!success)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Menu item not found" });
                return notFound;
            }

            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(item);
            return res;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error updating menu item {Id}", id);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = ex.Message });
            return res;
        }
    }

    // PATCH: Toggle menu item availability (Admin only)
    [Function("ToggleMenuItemAvailability")]
    [OpenApiOperation(operationId: "ToggleMenuItemAvailability", tags: new[] { "Menu" }, Summary = "Toggle menu item availability", Description = "Toggles the availability status of a menu item")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Menu item ID")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(CafeMenuItem), Description = "Successfully toggled availability")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.NotFound, Description = "Menu item not found")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "User not authenticated")]
    public async Task<HttpResponseData> ToggleMenuItemAvailability(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "menu/{id}/toggle-availability")] HttpRequestData req, 
        string id)
    {
        try
        {
            // Validate admin authorization
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var success = await _mongo.ToggleMenuItemAvailabilityAsync(id);
            
            if (!success)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Menu item not found" });
                return notFound;
            }

            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(new { success = true, message = "Availability toggled successfully" });
            return res;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error toggling availability for menu item {Id}", id);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = ex.Message });
            return res;
        }
    }

    /// <summary>
    /// Deletes a menu item (Admin only)
    /// </summary>
    /// <param name="req">HTTP request</param>
    /// <param name="id">The menu item ID to delete</param>
    /// <returns>Success message</returns>
    /// <response code="200">Menu item successfully deleted</response>
    /// <response code="404">Menu item not found</response>
    /// <response code="401">User not authenticated</response>
    /// <response code="403">User not authorized (admin role required)</response>
    [Function("DeleteMenuItem")]
    [OpenApiOperation(operationId: "DeleteMenuItem", tags: new[] { "Menu" }, Summary = "Delete a menu item", Description = "Deletes a menu item (Admin only)")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Menu item ID")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(object), Description = "Menu item successfully deleted")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.NotFound, Description = "Menu item not found")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "User not authenticated")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Forbidden, Description = "User not authorized")]
    public async Task<HttpResponseData> DeleteMenuItem([HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "menu/{id}")] HttpRequestData req, string id)
    {
        try
        {
            // Validate admin authorization
            var (isAuthorized, _, username, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var success = await _mongo.DeleteMenuItemAsync(id);
            
            if (!success)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { success = false, error = "Menu item not found" });
                return notFound;
            }

            _log.LogInformation("Menu item deleted - ID: {Id}, User: {User}", id, username);
            
            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(new { success = true, message = "Menu item deleted successfully" });
            return res;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error deleting menu item {Id}", id);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = ex.Message });
            return res;
        }
    }

    /// <summary>
    /// Copy menu item data (recipe, price forecast, future prices, oil usage, ingredients) from one outlet to another
    /// </summary>
    /// <param name="req">HTTP request with menuItemName, sourceOutletId, targetOutletId</param>
    /// <returns>Success message with copied data details</returns>
    /// <response code="200">Successfully copied menu item data</response>
    /// <response code="404">Source menu item not found</response>
    /// <response code="400">Invalid request parameters</response>
    [Function("CopyMenuItemFromOutlet")]
    public async Task<HttpResponseData> CopyMenuItemFromOutlet(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "menu/copy-from-outlet")] HttpRequestData req)
    {
        try
        {
            // Validate admin or manager authorization
            var (isAuthorized, userId, username, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var body = await req.ReadFromJsonAsync<CopyMenuItemRequest>();
            if (body == null || string.IsNullOrEmpty(body.MenuItemName) || 
                string.IsNullOrEmpty(body.SourceOutletId) || string.IsNullOrEmpty(body.TargetOutletId))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "MenuItemName, SourceOutletId, and TargetOutletId are required" });
                return badRequest;
            }

            if (body.SourceOutletId == body.TargetOutletId)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Source and target outlets cannot be the same" });
                return badRequest;
            }

            var copiedData = new CopyMenuItemResponse
            {
                MenuItemName = body.MenuItemName,
                SourceOutletId = body.SourceOutletId,
                TargetOutletId = body.TargetOutletId
            };

            // Copy Recipe
            var copiedRecipe = await _mongo.CopyRecipeFromOutletAsync(body.MenuItemName, body.SourceOutletId, body.TargetOutletId);
            copiedData.RecipeCopied = copiedRecipe != null;
            if (copiedRecipe != null)
            {
                copiedData.CopiedRecipeId = copiedRecipe.Id;
                _log.LogInformation("Recipe copied for {MenuItemName} from {SourceOutlet} to {TargetOutlet} by {User}", 
                    body.MenuItemName, body.SourceOutletId, body.TargetOutletId, username);
            }

            // Copy Price Forecast
            var copiedForecast = await _mongo.CopyPriceForecastFromOutletAsync(body.MenuItemName, body.SourceOutletId, body.TargetOutletId);
            copiedData.PriceForecastCopied = copiedForecast != null;
            if (copiedForecast != null)
            {
                copiedData.CopiedForecastId = copiedForecast.Id;
                _log.LogInformation("Price forecast copied for {MenuItemName} from {SourceOutlet} to {TargetOutlet} by {User}", 
                    body.MenuItemName, body.SourceOutletId, body.TargetOutletId, username);
            }

            // Update future prices in target outlet's menu item if available
            if (copiedForecast != null && !string.IsNullOrEmpty(copiedForecast.MenuItemId))
            {
                var targetMenuItem = await _mongo.GetMenuItemsByNameAndOutletAsync(body.MenuItemName, body.TargetOutletId);
                if (targetMenuItem != null)
                {
                    await _mongo.UpdateMenuItemFuturePricesAsync(targetMenuItem.Id, 
                        copiedForecast.FutureShopPrice, 
                        copiedForecast.FutureOnlinePrice);
                    copiedData.FuturePricesUpdated = true;
                }
            }

            if (!copiedData.RecipeCopied && !copiedData.PriceForecastCopied)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { 
                    error = $"No recipe or price forecast found for menu item '{body.MenuItemName}' in source outlet",
                    menuItemName = body.MenuItemName,
                    sourceOutletId = body.SourceOutletId
                });
                return notFound;
            }

            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(new { 
                success = true,
                message = "Menu item data copied successfully",
                data = copiedData
            });
            
            // Log audit trail
            _log.LogInformation("Menu item data copied - Action: {Action}, User: {User}, MenuItem: {MenuItem}, From: {Source}, To: {Target}, RecipeCopied: {RecipeCopied}, ForecastCopied: {ForecastCopied}",
                "CopyMenuItemFromOutlet", username, body.MenuItemName, body.SourceOutletId, body.TargetOutletId, 
                copiedData.RecipeCopied, copiedData.PriceForecastCopied);

            return res;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error copying menu item data");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = ex.Message });
            return res;
        }
    }
}

public class CopyMenuItemRequest
{
    public string MenuItemName { get; set; } = string.Empty;
    public string SourceOutletId { get; set; } = string.Empty;
    public string TargetOutletId { get; set; } = string.Empty;
}

public class CopyMenuItemResponse
{
    public string MenuItemName { get; set; } = string.Empty;
    public string SourceOutletId { get; set; } = string.Empty;
    public string TargetOutletId { get; set; } = string.Empty;
    public bool RecipeCopied { get; set; }
    public string? CopiedRecipeId { get; set; }
    public bool PriceForecastCopied { get; set; }
    public string? CopiedForecastId { get; set; }
    public bool FuturePricesUpdated { get; set; }
}
