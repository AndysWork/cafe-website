using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using Cafe.Api.Services;

namespace Cafe.Api.Functions;

public class PublicStatsFunction
{
    private readonly ILogger<PublicStatsFunction> _log;
    private readonly MongoService _mongo;

    public PublicStatsFunction(ILogger<PublicStatsFunction> log, MongoService mongo)
    {
        _log = log;
        _mongo = mongo;
    }

    // GET /api/public/stats - Public landing page stats (no auth required)
    [Function("GetPublicStats")]
    public async Task<HttpResponseData> GetPublicStats(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "public/stats")] HttpRequestData req)
    {
        try
        {
            var stats = await _mongo.GetPublicStatsAsync();

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, data = stats });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting public stats");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { success = false, message = "Unable to load stats" });
            return errorResponse;
        }
    }
}
