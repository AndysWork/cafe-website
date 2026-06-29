using System.Net;
using Cafe.Api.Helpers;
using Cafe.Api.Models;
using Cafe.Api.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Cafe.Api.Functions;

public class HomeContentFunction
{
    private readonly MongoService _mongo;
    private readonly AuthService _auth;
    private readonly ILogger<HomeContentFunction> _log;

    public HomeContentFunction(MongoService mongo, AuthService auth, ILogger<HomeContentFunction> log)
    {
        _mongo = mongo;
        _auth = auth;
        _log = log;
    }

    [Function("GetPublicHomeContentConfig")]
    public async Task<HttpResponseData> GetPublicHomeContentConfig(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "public/home-content")] HttpRequestData req)
    {
        try
        {
            var outletId = OutletHelper.GetOutletIdFromRequest(req, _auth);
            var config = await _mongo.GetHomeContentConfigAsync(outletId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, data = config });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error retrieving public home content config");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { success = false, message = "Failed to load home content config" });
            return error;
        }
    }

    [Function("GetAdminHomeContentConfig")]
    public async Task<HttpResponseData> GetAdminHomeContentConfig(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "home-content-config")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);
            var config = await _mongo.GetHomeContentConfigAsync(outletId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, data = config });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error retrieving admin home content config");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { success = false, message = "Failed to load home content config" });
            return error;
        }
    }

    [Function("UpsertHomeContentConfig")]
    public async Task<HttpResponseData> UpsertHomeContentConfig(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "home-content-config")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var body = await req.ReadAsStringAsync();
            var request = System.Text.Json.JsonSerializer.Deserialize<UpdateHomeContentConfigRequest>(body, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (request == null)
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { success = false, message = "Invalid request body" });
                return bad;
            }

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);
            var updated = await _mongo.UpsertHomeContentConfigAsync(outletId, request, userId ?? "admin");

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, data = updated, message = "Home content updated successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error updating home content config");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { success = false, message = "Failed to update home content config" });
            return error;
        }
    }
}
