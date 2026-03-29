using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Models;
using Cafe.Api.Helpers;
using System.Net;

namespace Cafe.Api.Functions;

public class CustomerSegmentFunction
{
    private readonly MongoService _mongo;
    private readonly AuthService _auth;
    private readonly ILogger _log;

    public CustomerSegmentFunction(MongoService mongo, AuthService auth, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _auth = auth;
        _log = loggerFactory.CreateLogger<CustomerSegmentFunction>();
    }

    [Function("GetCustomerSegments")]
    public async Task<HttpResponseData> GetCustomerSegments(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/customer-segments")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var segment = req.Query["segment"];
            var (page, pageSize) = PaginationHelper.ParsePagination(req);

            var segments = await _mongo.GetCustomerSegmentsAsync(segment, page ?? 1, pageSize ?? 50);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(segments);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting customer segments");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("GetSegmentSummary")]
    public async Task<HttpResponseData> GetSegmentSummary(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/customer-segments/summary")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var summary = await _mongo.GetSegmentSummaryAsync();

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(summary);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting segment summary");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("RefreshCustomerSegments")]
    public async Task<HttpResponseData> RefreshCustomerSegments(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/customer-segments/refresh")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var count = await _mongo.RefreshCustomerSegmentsAsync();

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                message = $"Customer segments refreshed. {count} customers processed.",
                customersProcessed = count
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error refreshing customer segments");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }
}
