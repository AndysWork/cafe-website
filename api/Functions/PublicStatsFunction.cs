using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using Cafe.Api.Services;
using Cafe.Api.Repositories;

namespace Cafe.Api.Functions;

public class PublicStatsFunction
{
    private readonly ILogger<PublicStatsFunction> _log;
    private readonly IOutletRepository _mongo;

    public PublicStatsFunction(ILogger<PublicStatsFunction> log, IOutletRepository mongo)
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

    // GET /api/public/outlets - Public outlet info for landing page (no auth required)
    [Function("GetPublicOutlets")]
    public async Task<HttpResponseData> GetPublicOutlets(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "public/outlets")] HttpRequestData req)
    {
        try
        {
            var outlets = await _mongo.GetActiveOutletsAsync();

            // Return only public-safe fields (no email, manager, internal dates)
            var publicOutlets = outlets.Select(o => new
            {
                o.Id,
                o.OutletName,
                o.OutletCode,
                o.Address,
                o.City,
                o.State,
                o.PhoneNumber,
                Settings = new
                {
                    o.Settings.OpeningTime,
                    o.Settings.ClosingTime,
                    o.Settings.AcceptsDineIn,
                    o.Settings.AcceptsTakeaway,
                    o.Settings.AcceptsOnlineOrders
                }
            });

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(publicOutlets);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting public outlets");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "An internal error occurred" });
            return errorResponse;
        }
    }
}
