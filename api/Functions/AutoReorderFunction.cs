using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Repositories;
using Cafe.Api.Models;
using Cafe.Api.Helpers;
using System.Net;

namespace Cafe.Api.Functions;

public class AutoReorderFunction
{
    private readonly IInventoryRepository _mongo;
    private readonly AuthService _auth;
    private readonly ILogger _log;

    public AutoReorderFunction(IInventoryRepository mongo, AuthService auth, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _auth = auth;
        _log = loggerFactory.CreateLogger<AutoReorderFunction>();
    }

    [Function("TriggerAutoReorder")]
    public async Task<HttpResponseData> TriggerAutoReorder(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/purchase-orders/auto-reorder")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);
            var generated = await _mongo.GenerateAutoReorderPurchaseOrdersAsync(outletId ?? "default");

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                message = $"Generated {generated.Count} purchase orders for low-stock items",
                purchaseOrders = generated
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error triggering auto reorder");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("GetPurchaseOrders")]
    public async Task<HttpResponseData> GetPurchaseOrders(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/purchase-orders")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);
            var status = req.Query["status"];
            var orders = await _mongo.GetPurchaseOrdersAsync(outletId ?? "default", status);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(orders);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting purchase orders");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("UpdatePurchaseOrderStatus")]
    public async Task<HttpResponseData> UpdatePurchaseOrderStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "manage/purchase-orders/{id}/status")] HttpRequestData req, string id)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var request = await req.ReadFromJsonAsync<UpdatePurchaseOrderStatusRequest>();
            if (request == null || string.IsNullOrWhiteSpace(request.Status))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Status is required" });
                return badReq;
            }

            var validStatuses = new[] { "approved", "ordered", "received", "cancelled" };
            if (!validStatuses.Contains(request.Status.ToLower()))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = $"Status must be one of: {string.Join(", ", validStatuses)}" });
                return badReq;
            }

            var success = await _mongo.UpdatePurchaseOrderStatusAsync(id, request.Status.ToLower());

            if (!success)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Purchase order not found" });
                return notFound;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = $"Purchase order status updated to {request.Status}" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error updating purchase order status");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }
}
