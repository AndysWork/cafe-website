using System.Globalization;
using System.Net;
using System.Security.Claims;
using Cafe.Api.Helpers;
using Cafe.Api.Models;
using Cafe.Api.Repositories;
using Cafe.Api.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Cafe.Api.Functions;

public class ManagerOperationsFunction
{
    private const int KitchenSlaMinutes = 20;
    private const int ParcelAcceptSlaMinutes = 10;
    private const int HighPendingQueueThreshold = 20;
    private const decimal ExpectedPayoutRatePerKm = 8m;

    private readonly IOperationsRepository _ops;
    private readonly IOrderRepository _orders;
    private readonly AuthService _auth;
    private readonly NotificationService _notification;
    private readonly ILogger<ManagerOperationsFunction> _log;

    public ManagerOperationsFunction(
        IOperationsRepository ops,
        IOrderRepository orders,
        AuthService auth,
        NotificationService notification,
        ILoggerFactory loggerFactory)
    {
        _ops = ops;
        _orders = orders;
        _auth = auth;
        _notification = notification;
        _log = loggerFactory.CreateLogger<ManagerOperationsFunction>();
    }

    [Function("GetManagerOperationsBoard")]
    public async Task<HttpResponseData> GetManagerOperationsBoard(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/ops/manager/board")] HttpRequestData req)
    {
        try
        {
            var (ok, _, _, authError) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!ok) return authError!;

            var outletId = ResolveOutletId(req);
            if (string.IsNullOrWhiteSpace(outletId))
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "Outlet context not found" });
                return bad;
            }

            var now = MongoService.GetIstNow();
            var kitchenQueue = await _ops.GetKitchenQueueOrdersAsync(outletId, 100);
            var deliveryQueue = await _ops.GetDeliveryQueueOrdersAsync(outletId, 100);
            var parcelTasks = await _ops.GetParcelTasksByOutletAsync(outletId, null, 200);
            var partners = await _ops.GetDeliveryPartnersAsync(outletId);

            var kitchenBreaches = kitchenQueue
                .Where(o => !string.Equals(o.Status, "ready", StringComparison.OrdinalIgnoreCase)
                            && o.CreatedAt <= now.AddMinutes(-KitchenSlaMinutes))
                .OrderBy(o => o.CreatedAt)
                .Take(50)
                .Select(o => new
                {
                    type = "kitchen_sla",
                    orderId = o.Id,
                    status = o.Status,
                    waitedMinutes = Math.Max(0, (int)(now - o.CreatedAt).TotalMinutes),
                    createdAt = o.CreatedAt
                });

            var parcelAcceptBreaches = parcelTasks
                .Where(t => string.Equals(t.Status, "assigned", StringComparison.OrdinalIgnoreCase)
                            && t.CreatedAt <= now.AddMinutes(-ParcelAcceptSlaMinutes))
                .OrderBy(t => t.CreatedAt)
                .Take(50)
                .Select(t => new
                {
                    type = "parcel_accept_sla",
                    taskId = t.Id,
                    partnerId = t.PartnerId,
                    partnerName = t.PartnerName,
                    waitedMinutes = Math.Max(0, (int)(now - t.CreatedAt).TotalMinutes),
                    createdAt = t.CreatedAt
                });

            var alerts = new List<object>();
            var pendingQueueCount = kitchenQueue.Count + parcelTasks.Count(t => string.Equals(t.Status, "assigned", StringComparison.OrdinalIgnoreCase));
            if (pendingQueueCount >= HighPendingQueueThreshold)
            {
                alerts.Add(new
                {
                    type = "high_pending_queue",
                    severity = "high",
                    message = $"Pending queue is high ({pendingQueueCount} items)",
                    value = pendingQueueCount
                });
            }

            var availablePartners = partners.Count(p => string.Equals(p.Status, "available", StringComparison.OrdinalIgnoreCase));
            if (availablePartners <= 1)
            {
                alerts.Add(new
                {
                    type = "low_partner_availability",
                    severity = "medium",
                    message = $"Low partner availability ({availablePartners} available)",
                    value = availablePartners
                });
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                capturedAt = now,
                outletId,
                kitchenQueue = kitchenQueue.Select(MapOrderCard),
                deliveryQueue = deliveryQueue.Select(MapOrderCard),
                parcelTasks = parcelTasks.Select(MapParcelCard),
                escalations = kitchenBreaches.Cast<object>().Concat(parcelAcceptBreaches.Cast<object>()),
                alerts,
                summary = new
                {
                    kitchenQueueCount = kitchenQueue.Count,
                    deliveryQueueCount = deliveryQueue.Count,
                    parcelAssignedCount = parcelTasks.Count(t => string.Equals(t.Status, "assigned", StringComparison.OrdinalIgnoreCase)),
                    parcelAcceptedCount = parcelTasks.Count(t => string.Equals(t.Status, "accepted", StringComparison.OrdinalIgnoreCase)),
                    availablePartners,
                    partnersOnDelivery = partners.Count(p => string.Equals(p.Status, "on-delivery", StringComparison.OrdinalIgnoreCase))
                }
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting manager operations board");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("GetManagerParcelTasks")]
    public async Task<HttpResponseData> GetManagerParcelTasks(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/ops/manager/parcel-tasks")] HttpRequestData req)
    {
        try
        {
            var (ok, _, _, authError) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!ok) return authError!;

            var outletId = ResolveOutletId(req);
            if (string.IsNullOrWhiteSpace(outletId))
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "Outlet context not found" });
                return bad;
            }

            var status = ParseQuery(req, "status");
            var tasks = await _ops.GetParcelTasksByOutletAsync(outletId, status, 300);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                count = tasks.Count,
                items = tasks.Select(MapParcelCard)
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting manager parcel tasks");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("ReassignOrderPartnerManager")]
    public async Task<HttpResponseData> ReassignOrderPartnerManager(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/ops/manager/reassign-partner")] HttpRequestData req)
    {
        try
        {
            var (ok, userId, _, authError) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!ok) return authError!;

            var outletId = ResolveOutletId(req);
            if (string.IsNullOrWhiteSpace(outletId))
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "Outlet context not found" });
                return bad;
            }

            var body = await req.ReadFromJsonAsync<ReassignPartnerRequest>();
            if (body == null || string.IsNullOrWhiteSpace(body.OrderId) || string.IsNullOrWhiteSpace(body.PartnerId))
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "orderId and partnerId are required" });
                return bad;
            }

            var success = await _ops.ReassignOrderPartnerAsync(body.OrderId, body.PartnerId);
            if (!success)
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "Order could not be reassigned" });
                return bad;
            }

            var partner = await _ops.GetDeliveryPartnerByIdAsync(body.PartnerId);
            if (!string.IsNullOrWhiteSpace(partner?.UserId))
            {
                await _notification.SendSystemNotificationAsync(partner!.UserId!, "Order reassigned", $"You have been assigned order {body.OrderId}.", "/partner/delivery");
            }

            await _ops.CreateManagerOpsAuditEntryAsync(new ManagerOpsAuditEntry
            {
                OutletId = outletId,
                EventType = "reassign_partner",
                EntityType = "order",
                EntityId = body.OrderId,
                Summary = $"Order {body.OrderId} reassigned to partner {body.PartnerId}",
                CreatedByUserId = userId,
                Metadata = new Dictionary<string, string>
                {
                    ["orderId"] = body.OrderId,
                    ["partnerId"] = body.PartnerId
                }
            });

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Order reassigned" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error reassigning order partner");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("MarkOrderUrgentManager")]
    public async Task<HttpResponseData> MarkOrderUrgentManager(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/ops/manager/mark-urgent")] HttpRequestData req)
    {
        try
        {
            var (ok, userId, _, authError) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!ok) return authError!;

            var outletId = ResolveOutletId(req);
            if (string.IsNullOrWhiteSpace(outletId))
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "Outlet context not found" });
                return bad;
            }

            var body = await req.ReadFromJsonAsync<MarkUrgentRequest>();
            if (body == null || string.IsNullOrWhiteSpace(body.OrderId))
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "orderId is required" });
                return bad;
            }

            var success = await _ops.MarkOrderUrgentAsync(body.OrderId, true, body.Reason);
            if (!success)
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "Order could not be marked urgent" });
                return bad;
            }

            await _ops.CreateManagerOpsAuditEntryAsync(new ManagerOpsAuditEntry
            {
                OutletId = outletId,
                EventType = "mark_urgent",
                EntityType = "order",
                EntityId = body.OrderId,
                Summary = $"Order {body.OrderId} marked urgent",
                CreatedByUserId = userId,
                Metadata = new Dictionary<string, string>
                {
                    ["orderId"] = body.OrderId,
                    ["reason"] = body.Reason ?? string.Empty
                }
            });

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Order marked urgent" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error marking order urgent");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("ResendOrderNotificationManager")]
    public async Task<HttpResponseData> ResendOrderNotificationManager(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/ops/manager/resend-notification")] HttpRequestData req)
    {
        try
        {
            var (ok, userId, _, authError) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!ok) return authError!;

            var outletId = ResolveOutletId(req);
            if (string.IsNullOrWhiteSpace(outletId))
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "Outlet context not found" });
                return bad;
            }

            var body = await req.ReadFromJsonAsync<ResendNotificationRequest>();
            if (body == null || string.IsNullOrWhiteSpace(body.OrderId))
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "orderId is required" });
                return bad;
            }

            var order = await _orders.GetOrderByIdAsync(body.OrderId);
            if (order == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Order not found" });
                return notFound;
            }

            await _notification.SendOrderStatusNotificationAsync(order, order.Status);

            await _ops.CreateManagerOpsAuditEntryAsync(new ManagerOpsAuditEntry
            {
                OutletId = outletId,
                EventType = "resend_notification",
                EntityType = "order",
                EntityId = body.OrderId,
                Summary = $"Order notification resent for {body.OrderId}",
                CreatedByUserId = userId,
                Metadata = new Dictionary<string, string>
                {
                    ["orderId"] = body.OrderId,
                    ["status"] = order.Status
                }
            });

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Notification resent" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error resending order notification");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("GetManagerAuditAndReconciliation")]
    public async Task<HttpResponseData> GetManagerAuditAndReconciliation(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/ops/manager/audit-reconciliation")] HttpRequestData req)
    {
        try
        {
            var (ok, _, _, authError) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!ok) return authError!;

            var outletId = ResolveOutletId(req);
            if (string.IsNullOrWhiteSpace(outletId))
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "Outlet context not found" });
                return bad;
            }

            var dateRaw = ParseQuery(req, "date");
            var refDate = ParseDate(dateRaw) ?? MongoService.GetIstNow().Date;
            var start = refDate.Date;
            var end = start.AddDays(1);

            var completedParcels = await _ops.GetParcelTasksByOutletAsync(outletId, "completed", 1000);
            var expectedPayout = completedParcels
                .Where(t => t.CompletedAt.HasValue && t.CompletedAt.Value >= start && t.CompletedAt.Value < end)
                .Sum(t => Convert.ToDecimal(t.BillableDistanceKm) * ExpectedPayoutRatePerKm);

            var actualPayout = await _ops.GetPayoutLedgerTotalAsync(outletId, start, end);
            var auditEntries = await _ops.GetManagerOpsAuditEntriesAsync(outletId, start, end, 300);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                date = start,
                expectedPayout,
                actualPayout,
                variance = actualPayout - expectedPayout,
                completedParcelTasks = completedParcels.Count(t => t.CompletedAt.HasValue && t.CompletedAt.Value >= start && t.CompletedAt.Value < end),
                auditEntries = auditEntries.Select(a => new
                {
                    a.Id,
                    a.EventType,
                    a.EntityType,
                    a.EntityId,
                    a.Summary,
                    a.Metadata,
                    a.CreatedByUserId,
                    a.CreatedAt
                })
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting manager audit and reconciliation");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    private static object MapOrderCard(Order order)
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
            updatedAt = order.UpdatedAt,
            isUrgent = order.IsUrgent,
            urgentReason = order.UrgentReason,
            urgentMarkedAt = order.UrgentMarkedAt
        };
    }

    private static object MapParcelCard(ParcelDeliveryTask task)
    {
        return new
        {
            id = task.Id,
            partnerId = task.PartnerId,
            partnerName = task.PartnerName,
            startPoint = task.StartPoint,
            endPoint = task.EndPoint,
            distanceKm = task.DistanceKm,
            billableDistanceKm = task.BillableDistanceKm,
            isRoundTrip = task.IsRoundTrip,
            etaMinutes = task.EtaMinutes,
            status = task.Status,
            createdAt = task.CreatedAt,
            acceptedAt = task.AcceptedAt,
            completedAt = task.CompletedAt,
            payoutImpact = Convert.ToDecimal(task.BillableDistanceKm) * ExpectedPayoutRatePerKm
        };
    }

    private static string? ParseQuery(HttpRequestData req, string name)
    {
        var rawQuery = req.Url.Query;
        if (string.IsNullOrWhiteSpace(rawQuery))
        {
            return null;
        }

        var trimmed = rawQuery.StartsWith('?') ? rawQuery.Substring(1) : rawQuery;
        var pairs = trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in pairs)
        {
            var idx = pair.IndexOf('=');
            if (idx <= 0)
            {
                continue;
            }

            var key = Uri.UnescapeDataString(pair.Substring(0, idx));
            if (!string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = Uri.UnescapeDataString(pair.Substring(idx + 1));
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        return null;
    }

    private static DateTime? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return DateTime.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.Date
            : null;
    }

    private string? ResolveOutletId(HttpRequestData req)
    {
        var fromRequest = OutletHelper.GetOutletIdForAdmin(req, _auth);
        if (IsUsableOutletId(fromRequest))
        {
            return fromRequest;
        }

        if (!req.Headers.TryGetValues("Authorization", out var authHeaders))
        {
            return null;
        }

        var authHeader = authHeaders.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = authHeader.Substring("Bearer ".Length).Trim();
        var principal = _auth.ValidateToken(token);
        if (principal == null)
        {
            return null;
        }

        var defaultOutletId = principal.FindFirst("DefaultOutletId")?.Value;
        if (IsUsableOutletId(defaultOutletId))
        {
            return defaultOutletId!.Trim();
        }

        var assignedOutletsRaw = principal.FindFirst("AssignedOutlets")?.Value;
        if (string.IsNullOrWhiteSpace(assignedOutletsRaw))
        {
            return null;
        }

        return assignedOutletsRaw
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(v => v.Trim())
            .FirstOrDefault(IsUsableOutletId);
    }

    private static bool IsUsableOutletId(string? outletId)
    {
        return !string.IsNullOrWhiteSpace(outletId);
    }
}

public class ReassignPartnerRequest
{
    public string OrderId { get; set; } = string.Empty;
    public string PartnerId { get; set; } = string.Empty;
}

public class MarkUrgentRequest
{
    public string OrderId { get; set; } = string.Empty;
    public string? Reason { get; set; }
}

public class ResendNotificationRequest
{
    public string OrderId { get; set; } = string.Empty;
}
