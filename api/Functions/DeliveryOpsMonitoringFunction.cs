using System.Net;
using Cafe.Api.Helpers;
using Cafe.Api.Repositories;
using Cafe.Api.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Cafe.Api.Functions;

public class DeliveryOpsMonitoringFunction
{
    private readonly IOrderRepository _orderRepo;
    private readonly OutboxService _outbox;
    private readonly AuthService _auth;
    private readonly ILogger<DeliveryOpsMonitoringFunction> _logger;

    public DeliveryOpsMonitoringFunction(
        IOrderRepository orderRepo,
        OutboxService outbox,
        AuthService auth,
        ILoggerFactory loggerFactory)
    {
        _orderRepo = orderRepo;
        _outbox = outbox;
        _auth = auth;
        _logger = loggerFactory.CreateLogger<DeliveryOpsMonitoringFunction>();
    }

    [Function("GetDeliveryDispatchHealth")]
    public async Task<HttpResponseData> GetDeliveryDispatchHealth(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/ops/delivery-dispatch-health")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);
            var orders = await _orderRepo.GetAllOrdersAsync(outletId);
            var now = MongoService.GetIstNow();

            var deliveryOrders = orders.Where(o => string.Equals(o.OrderType, "delivery", StringComparison.OrdinalIgnoreCase)).ToList();
            var assignableStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "pending", "confirmed", "preparing", "ready" };

            var unassignedQueue = deliveryOrders
                .Where(o => assignableStatuses.Contains(o.Status) && string.IsNullOrWhiteSpace(o.DeliveryPartnerId))
                .OrderByDescending(o => o.CreatedAt)
                .ToList();

            var stuckOutForDelivery = deliveryOrders
                .Where(o => string.Equals(o.Status, "out-for-delivery", StringComparison.OrdinalIgnoreCase)
                            && o.UpdatedAt <= now.AddMinutes(-45))
                .OrderBy(o => o.UpdatedAt)
                .ToList();

            var stuckReady = deliveryOrders
                .Where(o => string.Equals(o.Status, "ready", StringComparison.OrdinalIgnoreCase)
                            && string.IsNullOrWhiteSpace(o.DeliveryPartnerId)
                            && o.UpdatedAt <= now.AddMinutes(-15))
                .OrderBy(o => o.UpdatedAt)
                .ToList();

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                capturedAt = now,
                outletId = outletId ?? "default",
                totalDeliveryOrders = deliveryOrders.Count,
                unassignedQueueCount = unassignedQueue.Count,
                stuckOutForDeliveryCount = stuckOutForDelivery.Count,
                stuckReadyUnassignedCount = stuckReady.Count,
                unassignedQueue = unassignedQueue.Take(25).Select(MapOrderCard),
                stuckOutForDelivery = stuckOutForDelivery.Take(25).Select(MapOrderCard),
                stuckReadyUnassigned = stuckReady.Take(25).Select(MapOrderCard)
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating delivery dispatch health report");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("GetNotificationDeadLetters")]
    public async Task<HttpResponseData> GetNotificationDeadLetters(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/ops/notification-dead-letters")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var deadLetters = await _outbox.GetDeadLetterMessagesAsync(200);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                count = deadLetters.Count,
                items = deadLetters.Select(m => new
                {
                    m.Id,
                    m.EventType,
                    m.AggregateType,
                    m.AggregateId,
                    m.RetryCount,
                    m.MaxRetries,
                    m.Error,
                    m.CreatedAt,
                    m.LastAttemptAt,
                    m.DeadLetteredAt
                })
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting notification dead letters");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("GetOutboxHealth")]
    public async Task<HttpResponseData> GetOutboxHealth(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/ops/outbox-health")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var health = await _outbox.GetHealthSummaryAsync();
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(health);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting outbox health");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    private static object MapOrderCard(Models.Order order)
    {
        return new
        {
            id = order.Id,
            status = order.Status,
            total = order.Total,
            deliveryPartnerId = order.DeliveryPartnerId,
            deliveryPartnerName = order.DeliveryPartnerName,
            deliveryAddress = order.DeliveryAddress,
            phoneNumber = order.PhoneNumber,
            createdAt = order.CreatedAt,
            updatedAt = order.UpdatedAt
        };
    }
}
