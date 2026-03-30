using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Repositories;
using Cafe.Api.Models;
using Cafe.Api.Helpers;
using System.Net;

namespace Cafe.Api.Functions;

public class KitchenDisplayFunction
{
    private readonly IOrderRepository _mongo;
    private readonly AuthService _auth;
    private readonly ILogger _log;

    public KitchenDisplayFunction(IOrderRepository mongo, AuthService auth, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _auth = auth;
        _log = loggerFactory.CreateLogger<KitchenDisplayFunction>();
    }

    [Function("GetKitchenOrders")]
    public async Task<HttpResponseData> GetKitchenOrders(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "kitchen/orders")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);
            var orders = await _mongo.GetOrdersByStatusAsync(new[] { "confirmed", "preparing" }, outletId);

            var kitchenItems = orders.Select(o => new
            {
                o.Id,
                CustomerName = o.Username,
                o.OrderType,
                o.TableNumber,
                o.Status,
                Items = o.Items.Select(i => new
                {
                    i.Name,
                    i.Quantity,
                    Category = i.CategoryName
                }),
                SpecialInstructions = o.Notes,
                o.CreatedAt,
                WaitTime = (int)(MongoService.GetIstNow() - o.CreatedAt).TotalMinutes,
                IsScheduled = o.IsScheduled,
                ScheduledFor = o.ScheduledFor
            }).OrderBy(o => o.CreatedAt).ToList();

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                totalOrders = kitchenItems.Count,
                orders = kitchenItems
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting kitchen orders");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("UpdateKitchenOrderStatus")]
    public async Task<HttpResponseData> UpdateKitchenOrderStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "kitchen/orders/{id}/status")] HttpRequestData req, string id)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var request = await req.ReadFromJsonAsync<KitchenStatusUpdateRequest>();
            if (request == null || string.IsNullOrWhiteSpace(request.Status))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Status is required" });
                return badReq;
            }

            var validStatuses = new[] { "preparing", "ready", "completed" };
            if (!validStatuses.Contains(request.Status.ToLower()))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = $"Status must be one of: {string.Join(", ", validStatuses)}" });
                return badReq;
            }

            var success = await _mongo.UpdateOrderStatusAsync(id, request.Status.ToLower());
            if (!success)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Order not found" });
                return notFound;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = $"Order status updated to {request.Status}" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error updating kitchen order status");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("GetKitchenStats")]
    public async Task<HttpResponseData> GetKitchenStats(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "kitchen/stats")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);
            var now = MongoService.GetIstNow();
            var todayStart = now.Date;

            var pendingOrders = await _mongo.GetOrdersByStatusAsync(new[] { "confirmed" }, outletId);
            var preparingOrders = await _mongo.GetOrdersByStatusAsync(new[] { "preparing" }, outletId);
            var completedToday = await _mongo.GetOrdersByStatusAsync(new[] { "completed", "delivered" }, outletId);
            var todayCompleted = completedToday.Where(o => o.CreatedAt >= todayStart).ToList();

            double avgPrepTime = 0;
            if (todayCompleted.Any())
            {
                avgPrepTime = todayCompleted
                    .Select(o => (o.UpdatedAt - o.CreatedAt).TotalMinutes)
                    .DefaultIfEmpty(0)
                    .Average();
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                pendingCount = pendingOrders.Count,
                preparingCount = preparingOrders.Count,
                completedToday = todayCompleted.Count,
                averagePrepTimeMinutes = Math.Round(avgPrepTime, 1)
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting kitchen stats");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }
}

public class KitchenStatusUpdateRequest
{
    public string Status { get; set; } = string.Empty;
}
