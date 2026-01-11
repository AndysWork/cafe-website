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

public class CategoryFunction
{
    private readonly MongoService _mongo;
    private readonly AuthService _auth;
    private readonly ILogger _log;

    public CategoryFunction(MongoService mongo, AuthService auth, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _auth = auth;
        _log = loggerFactory.CreateLogger<CategoryFunction>();
    }

    /// <summary>
    /// Retrieves all menu categories
    /// </summary>
    /// <param name="req">HTTP request</param>
    /// <returns>List of all categories with names and descriptions</returns>
    /// <response code="200">Successfully retrieved categories</response>
    // GET: Get all categories
    [Function("GetCategories")]
    [OpenApiOperation(operationId: "GetCategories", tags: new[] { "Categories" }, Summary = "Get all categories", Description = "Retrieves all menu categories")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(List<MenuCategory>), Description = "Successfully retrieved categories")]
    public async Task<HttpResponseData> GetCategories([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "categories")] HttpRequestData req)
    {
        try
        {
            var categories = await _mongo.GetCategoriesAsync();
            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(categories);
            return res;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting categories");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = ex.Message });
            return res;
        }
    }

    /// <summary>
    /// Retrieves a specific category by ID
    /// </summary>
    /// <param name="req">HTTP request</param>
    /// <param name="id">The category ID</param>
    /// <returns>Category details</returns>
    /// <response code="200">Successfully retrieved category</response>
    /// <response code="404">Category not found</response>
    // GET: Get single category by ID
    [Function("GetCategory")]
    [OpenApiOperation(operationId: "GetCategory", tags: new[] { "Categories" }, Summary = "Get category by ID", Description = "Retrieves a specific category by its ID")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Category ID")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(MenuCategory), Description = "Successfully retrieved category")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.NotFound, Description = "Category not found")]
    public async Task<HttpResponseData> GetCategory([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "categories/{id}")] HttpRequestData req, string id)
    {
        try
        {
            var category = await _mongo.GetCategoryAsync(id);
            if (category == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Category not found" });
                return notFound;
            }

            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(category);
            return res;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting category {Id}", id);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = ex.Message });
            return res;
        }
    }

    // POST: Create new category (Admin only)
    [Function("CreateCategory")]
    [OpenApiOperation(operationId: "CreateCategory", tags: new[] { "Categories" }, Summary = "Create a new category", Description = "Creates a new menu category (Admin only)")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(MenuCategory), Required = true, Description = "Category details")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Created, contentType: "application/json", bodyType: typeof(MenuCategory), Description = "Category successfully created")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.BadRequest, Description = "Invalid category data")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "User not authenticated")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Forbidden, Description = "User not authorized")]
    public async Task<HttpResponseData> CreateCategory([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "categories")] HttpRequestData req)
    {
        try
        {
            // Validate admin authorization
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var category = await req.ReadFromJsonAsync<MenuCategory>();
            if (category == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid category data" });
                return badRequest;
            }

            var created = await _mongo.CreateCategoryAsync(category);
            var res = req.CreateResponse(HttpStatusCode.Created);
            await res.WriteAsJsonAsync(created);
            return res;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error creating category");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = ex.Message });
            return res;
        }
    }

    // PUT: Update category (Admin only)
    [Function("UpdateCategory")]
    public async Task<HttpResponseData> UpdateCategory([HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "categories/{id}")] HttpRequestData req, string id)
    {
        try
        {
            // Validate admin authorization
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var category = await req.ReadFromJsonAsync<MenuCategory>();
            if (category == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid category data" });
                return badRequest;
            }

            category.Id = id;
            var success = await _mongo.UpdateCategoryAsync(id, category);
            
            if (!success)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Category not found" });
                return notFound;
            }

            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(category);
            return res;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error updating category {Id}", id);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = ex.Message });
            return res;
        }
    }

    // DELETE: Delete category (Admin only)
    [Function("DeleteCategory")]
    public async Task<HttpResponseData> DeleteCategory([HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "categories/{id}")] HttpRequestData req, string id)
    {
        try
        {
            // Validate admin authorization
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var success = await _mongo.DeleteCategoryAsync(id);
            
            if (!success)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Category not found" });
                return notFound;
            }

            var res = req.CreateResponse(HttpStatusCode.NoContent);
            return res;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error deleting category {Id}", id);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = ex.Message });
            return res;
        }
    }
}
