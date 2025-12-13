using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Models;
using Cafe.Api.Helpers;
using System.Net;

namespace Cafe.Api.Functions;

public class SubCategoryFunction
{
    private readonly MongoService _mongo;
    private readonly AuthService _auth;
    private readonly ILogger _log;

    public SubCategoryFunction(MongoService mongo, AuthService auth, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _auth = auth;
        _log = loggerFactory.CreateLogger<SubCategoryFunction>();
    }

    // GET: Get all subcategories
    [Function("GetSubCategories")]
    public async Task<HttpResponseData> GetSubCategories([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "subcategories")] HttpRequestData req)
    {
        try
        {
            var subcategories = await _mongo.GetSubCategoriesAsync();
            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(subcategories);
            return res;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting subcategories");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = ex.Message });
            return res;
        }
    }

    // GET: Get subcategories by category ID
    [Function("GetSubCategoriesByCategory")]
    public async Task<HttpResponseData> GetSubCategoriesByCategory([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "categories/{categoryId}/subcategories")] HttpRequestData req, string categoryId)
    {
        try
        {
            var subcategories = await _mongo.GetSubCategoriesByCategoryAsync(categoryId);
            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(subcategories);
            return res;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting subcategories for category {CategoryId}", categoryId);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = ex.Message });
            return res;
        }
    }

    // GET: Get single subcategory by ID
    [Function("GetSubCategory")]
    public async Task<HttpResponseData> GetSubCategory([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "subcategories/{id}")] HttpRequestData req, string id)
    {
        try
        {
            var subcategory = await _mongo.GetSubCategoryAsync(id);
            if (subcategory == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "SubCategory not found" });
                return notFound;
            }

            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(subcategory);
            return res;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting subcategory {Id}", id);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = ex.Message });
            return res;
        }
    }

    // POST: Create new subcategory (Admin only)
    [Function("CreateSubCategory")]
    public async Task<HttpResponseData> CreateSubCategory([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "subcategories")] HttpRequestData req)
    {
        try
        {
            // Validate admin authorization
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var subcategory = await req.ReadFromJsonAsync<MenuSubCategory>();
            if (subcategory == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid subcategory data" });
                return badRequest;
            }

            var created = await _mongo.CreateSubCategoryAsync(subcategory);
            var res = req.CreateResponse(HttpStatusCode.Created);
            await res.WriteAsJsonAsync(created);
            return res;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error creating subcategory");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = ex.Message });
            return res;
        }
    }

    // PUT: Update subcategory (Admin only)
    [Function("UpdateSubCategory")]
    public async Task<HttpResponseData> UpdateSubCategory([HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "subcategories/{id}")] HttpRequestData req, string id)
    {
        try
        {
            // Validate admin authorization
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var subcategory = await req.ReadFromJsonAsync<MenuSubCategory>();
            if (subcategory == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid subcategory data" });
                return badRequest;
            }

            subcategory.Id = id;
            var success = await _mongo.UpdateSubCategoryAsync(id, subcategory);
            
            if (!success)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "SubCategory not found" });
                return notFound;
            }

            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(subcategory);
            return res;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error updating subcategory {Id}", id);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = ex.Message });
            return res;
        }
    }

    // DELETE: Delete subcategory (Admin only)
    [Function("DeleteSubCategory")]
    public async Task<HttpResponseData> DeleteSubCategory([HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "subcategories/{id}")] HttpRequestData req, string id)
    {
        try
        {
            // Validate admin authorization
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var success = await _mongo.DeleteSubCategoryAsync(id);
            
            if (!success)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "SubCategory not found" });
                return notFound;
            }

            var res = req.CreateResponse(HttpStatusCode.NoContent);
            return res;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error deleting subcategory {Id}", id);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = ex.Message });
            return res;
        }
    }
}
