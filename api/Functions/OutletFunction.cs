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

public class OutletFunction
{
    private readonly MongoService _mongo;
    private readonly AuthService _auth;
    private readonly ILogger _log;

    public OutletFunction(MongoService mongo, AuthService auth, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _auth = auth;
        _log = loggerFactory.CreateLogger<OutletFunction>();
    }

    /// <summary>
    /// Retrieves all outlets (Admin only)
    /// </summary>
    [Function("GetAllOutlets")]
    [OpenApiOperation(operationId: "GetAllOutlets", tags: new[] { "Outlets" }, Summary = "Get all outlets", Description = "Retrieves all outlets (Admin only)")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(List<Outlet>), Description = "Successfully retrieved outlets")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "User not authenticated")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Forbidden, Description = "User not authorized")]
    public async Task<HttpResponseData> GetAllOutlets(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "outlets")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            var outlets = await _mongo.GetAllOutletsAsync();
            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(outlets);
            return res;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting all outlets");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = ex.Message });
            return res;
        }
    }

    /// <summary>
    /// Retrieves active outlets only
    /// </summary>
    [Function("GetActiveOutlets")]
    [OpenApiOperation(operationId: "GetActiveOutlets", tags: new[] { "Outlets" }, Summary = "Get active outlets", Description = "Retrieves all active outlets")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(List<Outlet>), Description = "Successfully retrieved active outlets")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "User not authenticated")]
    public async Task<HttpResponseData> GetActiveOutlets(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "outlets/active")] HttpRequestData req)
    {
        try
        {
            // Allow authenticated users to see active outlets
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            var outlets = await _mongo.GetActiveOutletsAsync();
            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(outlets);
            return res;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting active outlets");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = ex.Message });
            return res;
        }
    }

    /// <summary>
    /// Retrieves a specific outlet by ID
    /// </summary>
    [Function("GetOutlet")]
    [OpenApiOperation(operationId: "GetOutlet", tags: new[] { "Outlets" }, Summary = "Get outlet by ID", Description = "Retrieves a specific outlet by its ID")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Outlet ID")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(Outlet), Description = "Successfully retrieved outlet")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.NotFound, Description = "Outlet not found")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "User not authenticated")]
    public async Task<HttpResponseData> GetOutlet(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "outlets/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            var outlet = await _mongo.GetOutletByIdAsync(id);
            if (outlet == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Outlet not found" });
                return notFound;
            }

            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(outlet);
            return res;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting outlet {Id}", id);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = ex.Message });
            return res;
        }
    }

    /// <summary>
    /// Retrieves outlet by code
    /// </summary>
    [Function("GetOutletByCode")]
    public async Task<HttpResponseData> GetOutletByCode(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "outlets/code/{code}")] HttpRequestData req,
        string code)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            var outlet = await _mongo.GetOutletByCodeAsync(code);
            if (outlet == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Outlet not found" });
                return notFound;
            }

            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(outlet);
            return res;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting outlet by code {Code}", code);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = ex.Message });
            return res;
        }
    }

    /// <summary>
    /// Creates a new outlet (Admin only)
    /// </summary>
    [Function("CreateOutlet")]
    public async Task<HttpResponseData> CreateOutlet(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "outlets")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            var request = await req.ReadFromJsonAsync<CreateOutletRequest>();
            if (request == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid request body" });
                return badRequest;
            }

            // Validate request
            if (string.IsNullOrWhiteSpace(request.OutletName) || string.IsNullOrWhiteSpace(request.OutletCode))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Outlet name and code are required" });
                return badRequest;
            }

            var outlet = await _mongo.CreateOutletAsync(request, userId!);
            var res = req.CreateResponse(HttpStatusCode.Created);
            await res.WriteAsJsonAsync(outlet);
            return res;
        }
        catch (InvalidOperationException ex)
        {
            _log.LogWarning(ex, "Outlet creation validation error");
            var res = req.CreateResponse(HttpStatusCode.BadRequest);
            await res.WriteAsJsonAsync(new { error = ex.Message });
            return res;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error creating outlet");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = ex.Message });
            return res;
        }
    }

    /// <summary>
    /// Updates an existing outlet (Admin only)
    /// </summary>
    [Function("UpdateOutlet")]
    public async Task<HttpResponseData> UpdateOutlet(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "outlets/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthorized, userId, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            var request = await req.ReadFromJsonAsync<UpdateOutletRequest>();
            if (request == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid request body" });
                return badRequest;
            }

            var success = await _mongo.UpdateOutletAsync(id, request, userId!);
            if (!success)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Outlet not found or no changes made" });
                return notFound;
            }

            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(new { message = "Outlet updated successfully" });
            return res;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error updating outlet {Id}", id);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = ex.Message });
            return res;
        }
    }

    /// <summary>
    /// Deletes an outlet (Admin only)
    /// </summary>
    [Function("DeleteOutlet")]
    public async Task<HttpResponseData> DeleteOutlet(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "outlets/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            var success = await _mongo.DeleteOutletAsync(id);
            if (!success)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Outlet not found" });
                return notFound;
            }

            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(new { message = "Outlet deleted successfully" });
            return res;
        }
        catch (InvalidOperationException ex)
        {
            _log.LogWarning(ex, "Cannot delete outlet with data");
            var res = req.CreateResponse(HttpStatusCode.BadRequest);
            await res.WriteAsJsonAsync(new { error = ex.Message });
            return res;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error deleting outlet {Id}", id);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = ex.Message });
            return res;
        }
    }

    /// <summary>
    /// Toggle outlet active status (Admin only)
    /// </summary>
    [Function("ToggleOutletStatus")]
    public async Task<HttpResponseData> ToggleOutletStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "outlets/{id}/toggle-status")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _auth);
            
            if (!isAuthorized)
                return errorResponse!;

            var success = await _mongo.ToggleOutletStatusAsync(id);
            if (!success)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Outlet not found" });
                return notFound;
            }

            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(new { message = "Outlet status toggled successfully" });
            return res;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error toggling outlet status {Id}", id);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = ex.Message });
            return res;
        }
    }
}
