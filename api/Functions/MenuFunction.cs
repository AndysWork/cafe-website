using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Models;
using Cafe.Api.Helpers;
using System.Net;

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
    public async Task<HttpResponseData> GetMenu([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "menu")] HttpRequestData req)
    {
        try
        {
            var items = await _mongo.GetMenuAsync();
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

    // DELETE: Delete menu item (Admin only)
    [Function("DeleteMenuItem")]
    public async Task<HttpResponseData> DeleteMenuItem([HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "menu/{id}")] HttpRequestData req, string id)
    {
        try
        {
            // Validate admin authorization
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var success = await _mongo.DeleteMenuItemAsync(id);
            
            if (!success)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Menu item not found" });
                return notFound;
            }

            var res = req.CreateResponse(HttpStatusCode.NoContent);
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
}
