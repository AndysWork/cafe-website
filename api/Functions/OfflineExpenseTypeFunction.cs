using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Models;
using Cafe.Api.Helpers;
using System.Net;

namespace Cafe.Api.Functions;

public class OfflineExpenseTypeFunction
{
    private readonly MongoService _mongo;
    private readonly AuthService _authService;
    private readonly ILogger _log;

    public OfflineExpenseTypeFunction(MongoService mongo, AuthService authService, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _authService = authService;
        _log = loggerFactory.CreateLogger<OfflineExpenseTypeFunction>();
    }

    [Function("GetAllOfflineExpenseTypes")]
    public async Task<HttpResponseData> GetAllOfflineExpenseTypes(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "offlineexpensetypes")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _authService);
            
            if (!isAuthorized)
                return errorResponse!;

            var expenseTypes = await _mongo.GetAllOfflineExpenseTypesAsync();
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(expenseTypes);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting offline expense types");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = ex.Message });
            return errorResponse;
        }
    }

    [Function("GetActiveOfflineExpenseTypes")]
    public async Task<HttpResponseData> GetActiveOfflineExpenseTypes(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "offlineexpensetypes/active")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _authService);
            
            if (!isAuthorized)
                return errorResponse!;

            var expenseTypes = await _mongo.GetActiveOfflineExpenseTypesAsync();
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(expenseTypes);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting active offline expense types");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = ex.Message });
            return errorResponse;
        }
    }

    [Function("CreateOfflineExpenseType")]
    public async Task<HttpResponseData> CreateOfflineExpenseType(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "offlineexpensetypes")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _authService);
            
            if (!isAuthorized)
                return errorResponse!;

            var request = await req.ReadFromJsonAsync<CreateOfflineExpenseTypeRequest>();
            if (request == null || string.IsNullOrWhiteSpace(request.ExpenseType))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid request" });
                return badRequest;
            }

            var expenseType = await _mongo.CreateOfflineExpenseTypeAsync(request);
            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(expenseType);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error creating offline expense type");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = ex.Message });
            return errorResponse;
        }
    }

    [Function("UpdateOfflineExpenseType")]
    public async Task<HttpResponseData> UpdateOfflineExpenseType(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "offlineexpensetypes/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _authService);
            
            if (!isAuthorized)
                return errorResponse!;

            var request = await req.ReadFromJsonAsync<CreateOfflineExpenseTypeRequest>();
            if (request == null || string.IsNullOrWhiteSpace(request.ExpenseType))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid request" });
                return badRequest;
            }

            var success = await _mongo.UpdateOfflineExpenseTypeAsync(id, request);
            if (!success)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Offline expense type not found" });
                return notFound;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Offline expense type updated successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error updating offline expense type");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = ex.Message });
            return errorResponse;
        }
    }

    [Function("DeleteOfflineExpenseType")]
    public async Task<HttpResponseData> DeleteOfflineExpenseType(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "offlineexpensetypes/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _authService);
            
            if (!isAuthorized)
                return errorResponse!;

            var success = await _mongo.DeleteOfflineExpenseTypeAsync(id);
            if (!success)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Offline expense type not found" });
                return notFound;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Offline expense type deleted successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error deleting offline expense type");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = ex.Message });
            return errorResponse;
        }
    }

    [Function("InitializeOfflineExpenseTypes")]
    public async Task<HttpResponseData> InitializeOfflineExpenseTypes(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "offlineexpensetypes/initialize")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _authService);
            
            if (!isAuthorized)
                return errorResponse!;

            await _mongo.InitializeDefaultOfflineExpenseTypesAsync();

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Default offline expense types initialized successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error initializing offline expense types");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = ex.Message });
            return errorResponse;
        }
    }
}
