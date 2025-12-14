using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Models;
using Cafe.Api.Helpers;
using System.Net;

namespace Cafe.Api.Functions;

public class OnlineExpenseTypeFunction
{
    private readonly MongoService _mongo;
    private readonly AuthService _authService;
    private readonly ILogger _log;

    public OnlineExpenseTypeFunction(MongoService mongo, AuthService authService, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _authService = authService;
        _log = loggerFactory.CreateLogger<OnlineExpenseTypeFunction>();
    }

    [Function("GetAllOnlineExpenseTypes")]
    public async Task<HttpResponseData> GetAllOnlineExpenseTypes(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "onlineexpensetypes")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _authService);
            
            if (!isAuthorized)
                return errorResponse!;

            var expenseTypes = await _mongo.GetAllOnlineExpenseTypesAsync();
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(expenseTypes);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting online expense types");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = ex.Message });
            return errorResponse;
        }
    }

    [Function("GetActiveOnlineExpenseTypes")]
    public async Task<HttpResponseData> GetActiveOnlineExpenseTypes(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "onlineexpensetypes/active")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _authService);
            
            if (!isAuthorized)
                return errorResponse!;

            var expenseTypes = await _mongo.GetActiveOnlineExpenseTypesAsync();
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(expenseTypes);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting active online expense types");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = ex.Message });
            return errorResponse;
        }
    }

    [Function("CreateOnlineExpenseType")]
    public async Task<HttpResponseData> CreateOnlineExpenseType(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "onlineexpensetypes")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _authService);
            
            if (!isAuthorized)
                return errorResponse!;

            var request = await req.ReadFromJsonAsync<CreateOnlineExpenseTypeRequest>();
            if (request == null || string.IsNullOrWhiteSpace(request.ExpenseType))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid request" });
                return badRequest;
            }

            var expenseType = await _mongo.CreateOnlineExpenseTypeAsync(request);
            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(expenseType);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error creating online expense type");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = ex.Message });
            return errorResponse;
        }
    }

    [Function("UpdateOnlineExpenseType")]
    public async Task<HttpResponseData> UpdateOnlineExpenseType(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "onlineexpensetypes/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _authService);
            
            if (!isAuthorized)
                return errorResponse!;

            var request = await req.ReadFromJsonAsync<CreateOnlineExpenseTypeRequest>();
            if (request == null || string.IsNullOrWhiteSpace(request.ExpenseType))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid request" });
                return badRequest;
            }

            await _mongo.UpdateOnlineExpenseTypeAsync(id, request);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Online expense type updated successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error updating online expense type");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = ex.Message });
            return errorResponse;
        }
    }

    [Function("DeleteOnlineExpenseType")]
    public async Task<HttpResponseData> DeleteOnlineExpenseType(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "onlineexpensetypes/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _authService);
            
            if (!isAuthorized)
                return errorResponse!;

            await _mongo.DeleteOnlineExpenseTypeAsync(id);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Online expense type deleted successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error deleting online expense type");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = ex.Message });
            return errorResponse;
        }
    }

    [Function("InitializeOnlineExpenseTypes")]
    public async Task<HttpResponseData> InitializeOnlineExpenseTypes(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "onlineexpensetypes/initialize")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = 
                await AuthorizationHelper.ValidateAdminRole(req, _authService);
            
            if (!isAuthorized)
                return errorResponse!;

            await _mongo.InitializeDefaultOnlineExpenseTypesAsync();
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Online expense types initialized successfully with 27 default types" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error initializing online expense types");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = ex.Message });
            return errorResponse;
        }
    }
}
