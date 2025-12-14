using System.Net;
using Cafe.Api.Helpers;
using Cafe.Api.Models;
using Cafe.Api.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Cafe.Api.Functions;

public class SalesItemTypeFunction
{
    private readonly MongoService _mongo;
    private readonly AuthService _authService;
    private readonly ILogger<SalesItemTypeFunction> _log;

    public SalesItemTypeFunction(MongoService mongo, AuthService authService, ILogger<SalesItemTypeFunction> log)
    {
        _mongo = mongo;
        _authService = authService;
        _log = log;
    }

    [Function("GetAllSalesItemTypes")]
    public async Task<HttpResponseData> GetAllSalesItemTypes(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "salesitemtypes")] HttpRequestData req)
    {
        try
        {
            var user = await AuthorizationHelper.ValidateAdminRole(req, _authService);

            var itemTypes = await _mongo.GetAllSalesItemTypesAsync();
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(itemTypes);
            return response;
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
            await errorResponse.WriteAsJsonAsync(new { error = ex.Message });
            return errorResponse;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting sales item types");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = ex.Message });
            return errorResponse;
        }
    }

    [Function("GetActiveSalesItemTypes")]
    public async Task<HttpResponseData> GetActiveSalesItemTypes(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "salesitemtypes/active")] HttpRequestData req)
    {
        try
        {
            // Restricted to admin only
            var user = await AuthorizationHelper.ValidateAdminRole(req, _authService);

            var itemTypes = await _mongo.GetActiveSalesItemTypesAsync();
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(itemTypes);
            return response;
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
            await errorResponse.WriteAsJsonAsync(new { error = ex.Message });
            return errorResponse;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting active sales item types");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = ex.Message });
            return errorResponse;
        }
    }

    [Function("CreateSalesItemType")]
    public async Task<HttpResponseData> CreateSalesItemType(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "salesitemtypes")] HttpRequestData req)
    {
        try
        {
            var user = await AuthorizationHelper.ValidateAdminRole(req, _authService);

            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var itemTypeRequest = JsonSerializer.Deserialize<CreateSalesItemTypeRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (itemTypeRequest == null || string.IsNullOrEmpty(itemTypeRequest.ItemName))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid item type data" });
                return badRequest;
            }

            var itemType = new SalesItemType
            {
                ItemName = itemTypeRequest.ItemName,
                DefaultPrice = itemTypeRequest.DefaultPrice,
                IsActive = true
            };

            var created = await _mongo.CreateSalesItemTypeAsync(itemType);
            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(created);
            return response;
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
            await errorResponse.WriteAsJsonAsync(new { error = ex.Message });
            return errorResponse;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error creating sales item type");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = ex.Message });
            return errorResponse;
        }
    }

    [Function("UpdateSalesItemType")]
    public async Task<HttpResponseData> UpdateSalesItemType(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "salesitemtypes/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var user = await AuthorizationHelper.ValidateAdminRole(req, _authService);

            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var itemTypeRequest = JsonSerializer.Deserialize<SalesItemType>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (itemTypeRequest == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid item type data" });
                return badRequest;
            }

            itemTypeRequest.Id = id;
            var updated = await _mongo.UpdateSalesItemTypeAsync(id, itemTypeRequest);

            if (updated == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Sales item type not found" });
                return notFound;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(updated);
            return response;
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
            await errorResponse.WriteAsJsonAsync(new { error = ex.Message });
            return errorResponse;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error updating sales item type");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = ex.Message });
            return errorResponse;
        }
    }

    [Function("DeleteSalesItemType")]
    public async Task<HttpResponseData> DeleteSalesItemType(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "salesitemtypes/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var user = await AuthorizationHelper.ValidateAdminRole(req, _authService);

            var success = await _mongo.DeleteSalesItemTypeAsync(id);

            if (!success)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Sales item type not found" });
                return notFound;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Sales item type deleted successfully" });
            return response;
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
            await errorResponse.WriteAsJsonAsync(new { error = ex.Message });
            return errorResponse;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error deleting sales item type");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = ex.Message });
            return errorResponse;
        }
    }

    [Function("InitializeSalesItemTypes")]
    public async Task<HttpResponseData> InitializeSalesItemTypes(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "salesitemtypes/initialize")] HttpRequestData req)
    {
        try
        {
            var user = await AuthorizationHelper.ValidateAdminRole(req, _authService);

            await _mongo.InitializeDefaultSalesItemTypesAsync();

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Default sales item types initialized successfully" });
            return response;
        }
        catch (UnauthorizedAccessException ex)
        {
            var errorResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
            await errorResponse.WriteAsJsonAsync(new { error = ex.Message });
            return errorResponse;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error initializing sales item types");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = ex.Message });
            return errorResponse;
        }
    }
}
