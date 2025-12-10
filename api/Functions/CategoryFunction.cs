using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Models;
using System.Net;

namespace Cafe.Api.Functions;

public class CategoryFunction
{
    private readonly MongoService _mongo;
    private readonly ILogger _log;

    public CategoryFunction(MongoService mongo, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _log = loggerFactory.CreateLogger<CategoryFunction>();
    }

    // GET: Get all categories
    [Function("GetCategories")]
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

    // GET: Get single category by ID
    [Function("GetCategory")]
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

    // POST: Create new category
    [Function("CreateCategory")]
    public async Task<HttpResponseData> CreateCategory([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "categories")] HttpRequestData req)
    {
        try
        {
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

    // PUT: Update category
    [Function("UpdateCategory")]
    public async Task<HttpResponseData> UpdateCategory([HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "categories/{id}")] HttpRequestData req, string id)
    {
        try
        {
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

    // DELETE: Delete category
    [Function("DeleteCategory")]
    public async Task<HttpResponseData> DeleteCategory([HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "categories/{id}")] HttpRequestData req, string id)
    {
        try
        {
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
