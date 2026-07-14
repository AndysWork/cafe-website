using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Repositories;
using Cafe.Api.Models;
using Cafe.Api.Helpers;
using System.Net;
using System.Globalization;
using MongoDB.Bson;

namespace Cafe.Api.Functions;

public class KitchenDisplayFunction
{
    private static readonly string[] KitchenChecklistTemplate =
    {
        "Item quantity rechecked",
        "Plating and garnish completed",
        "Temperature and freshness verified",
        "Packaging/sealing verified",
        "Special instructions verified"
    };

    private readonly MongoService _mongo;
    private readonly AuthService _auth;
    private readonly OutboxService _outbox;
    private readonly ILogger _log;

    public KitchenDisplayFunction(MongoService mongo, AuthService auth, OutboxService outbox, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _auth = auth;
        _outbox = outbox;
        _log = loggerFactory.CreateLogger<KitchenDisplayFunction>();
    }

    [Function("GetKitchenOrders")]
    public async Task<HttpResponseData> GetKitchenOrders(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "kitchen/orders")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, role, errorResponse) = await AuthorizationHelper.ValidateKitchenAccessRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);
            var orders = await _mongo.GetOrdersByStatusAsync(new[] { "pending", "confirmed", "preparing", "ready", "out-for-delivery" }, outletId);

            if (IsKitchenOpsRole(role))
            {
                var (staff, _, _) = await ResolveStaffForCurrentUser(req, userId);
                if (staff?.Id != null)
                {
                    orders = orders
                        .Where(o =>
                            ((o.Status == "pending" || o.Status == "confirmed") && string.IsNullOrWhiteSpace(o.KitchenAssignedStaffId)) ||
                            (!string.IsNullOrWhiteSpace(o.KitchenAssignedStaffId) && o.KitchenAssignedStaffId == staff.Id))
                        .ToList();
                }
                else
                {
                    orders = new List<Order>();
                }
            }

            var kitchenItems = orders.Select(o => new
            {
                o.Id,
                CustomerName = o.Username,
                o.OrderType,
                o.TableNumber,
                o.Status,
                o.KitchenPrepStartedAt,
                o.KitchenReadyAt,
                o.KptMinutes,
                o.KitchenAssignedStaffId,
                o.KitchenAssignedStaffName,
                o.KitchenAssignedRole,
                o.KitchenAssignedAt,
                KitchenChecklist = BuildChecklistForResponse(o),
                Items = o.Items.Select(i => new
                {
                    i.Name,
                    i.Quantity,
                    Category = i.CategoryName
                }),
                PreparationNotes = o.PreparationNotes,
                SpecialInstructions = string.IsNullOrWhiteSpace(o.PreparationNotes) ? o.Notes : o.PreparationNotes,
                Notes = o.Notes,
                o.CreatedAt,
                WaitTime = (int)(MongoService.GetIstNow() - o.CreatedAt).TotalMinutes,
                IsScheduled = o.IsScheduled,
                ScheduledFor = o.ScheduledFor
            }).OrderBy(o => o.CreatedAt).ToList();

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(kitchenItems);
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
            var (isAuthorized, userId, role, errorResponse) = await AuthorizationHelper.ValidateKitchenAccessRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var (request, validationError) = await ValidationHelper.ValidateBody<KitchenStatusUpdateRequest>(req);
            if (validationError != null) return validationError;

            if (request == null)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Invalid request body" });
                return badReq;
            }

            var validStatuses = new[] { "confirmed", "preparing", "ready", "delivered" };
            var requestedStatus = request.Status.ToLowerInvariant();
            if (!validStatuses.Contains(requestedStatus))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = $"Status must be one of: {string.Join(", ", validStatuses)}" });
                return badReq;
            }

            if (requestedStatus == "out-for-delivery")
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Delivery partner pickup is required before out-for-delivery" });
                return badReq;
            }

            var order = await _mongo.GetOrderByIdAsync(id);
            if (order == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Order not found" });
                return notFound;
            }

            if (requestedStatus == "confirmed" && order.Status != "pending" && order.Status != "confirmed")
            {
                var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                await conflict.WriteAsJsonAsync(new { error = "Only pending orders can be confirmed" });
                return conflict;
            }

            if (requestedStatus == "preparing" && !order.KitchenPrepStartedAt.HasValue)
            {
                order.KitchenPrepStartedAt = MongoService.GetIstNow();
            }

            if (IsKitchenOpsRole(role) && requestedStatus == "preparing")
            {
                var (staff, _, staffResolveError) = await ResolveStaffForCurrentUser(req, userId);
                if (staff?.Id == null)
                {
                    var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                    await forbidden.WriteAsJsonAsync(new { error = staffResolveError ?? "Staff profile required to approve kitchen orders" });
                    return forbidden;
                }

                if (!string.IsNullOrWhiteSpace(order.KitchenAssignedStaffId) && order.KitchenAssignedStaffId != staff.Id)
                {
                    var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                    await conflict.WriteAsJsonAsync(new { error = "Order already assigned to another kitchen staff" });
                    return conflict;
                }

                if (order.Status != "confirmed" && string.IsNullOrWhiteSpace(order.KitchenAssignedStaffId))
                {
                    var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                    await conflict.WriteAsJsonAsync(new { error = "Only confirmed orders can be claimed" });
                    return conflict;
                }

                order.KitchenAssignedStaffId = staff.Id;
                order.KitchenAssignedStaffName = $"{staff.FirstName} {staff.LastName}".Trim();
                order.KitchenAssignedRole = role;
                order.KitchenAssignedAt ??= MongoService.GetIstNow();
            }

            if (IsKitchenOpsRole(role) && requestedStatus != "preparing" && requestedStatus != "confirmed")
            {
                var (staff, _, staffResolveError) = await ResolveStaffForCurrentUser(req, userId);
                if (staff?.Id == null)
                {
                    var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                    await forbidden.WriteAsJsonAsync(new { error = staffResolveError ?? "Staff profile required for kitchen order updates" });
                    return forbidden;
                }

                if (!string.IsNullOrWhiteSpace(order.KitchenAssignedStaffId) && order.KitchenAssignedStaffId != staff.Id)
                {
                    var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                    await forbidden.WriteAsJsonAsync(new { error = "Only assigned kitchen staff can update this order" });
                    return forbidden;
                }
            }

            if (requestedStatus == "ready")
            {
                var suppliedChecklist = request.ChecklistItems ?? new List<KitchenChecklistUpdateItem>();
                if (!suppliedChecklist.Any())
                {
                    var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badReq.WriteAsJsonAsync(new { error = "Checklist is required before marking order as ready" });
                    return badReq;
                }

                var normalized = NormalizeChecklist(suppliedChecklist, userId ?? "kitchen");
                if (normalized.Any(c => !c.IsCompleted))
                {
                    var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badReq.WriteAsJsonAsync(new { error = "Complete all checklist items before marking order as ready" });
                    return badReq;
                }

                order.KitchenChecklist = normalized;
                var readyAt = MongoService.GetIstNow();
                order.KitchenPrepStartedAt ??= order.KitchenAssignedAt ?? readyAt;
                order.KitchenReadyAt = readyAt;

                var prepStartAt = order.KitchenPrepStartedAt.Value;
                if (prepStartAt > readyAt)
                {
                    prepStartAt = readyAt;
                    order.KitchenPrepStartedAt = prepStartAt;
                }

                var kptMinutes = Math.Max((readyAt - prepStartAt).TotalMinutes, 0);
                order.KptMinutes = Math.Round((decimal)kptMinutes, 2);
            }

            if (requestedStatus == "delivered" && !string.Equals(order.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Order can be delivered only after payment is marked paid" });
                return badReq;
            }

            order.Status = requestedStatus;
            order.KitchenHandledByUserId = userId;
            order.KitchenHandledByRole = role;
            order.UpdatedAt = MongoService.GetIstNow();

            if (requestedStatus == "delivered")
            {
                order.CompletedAt = MongoService.GetIstNow();
            }

            var success = await _mongo.UpdateOrderAsync(order);
            if (!success)
            {
                var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                await conflict.WriteAsJsonAsync(new { error = "Failed to update order status" });
                return conflict;
            }

            var deliveryNotificationQueued = false;
            if (requestedStatus == "ready"
                && string.Equals(order.OrderType, "delivery", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(order.OutletId)
                && !string.IsNullOrWhiteSpace(order.Id))
            {
                await _outbox.EnqueueAsync("DeliveryPartnerStatusAlert", "Order", order.Id,
                    new
                    {
                        OrderId = order.Id,
                        OutletId = order.OutletId,
                        Status = "ready",
                        DeliveryPartnerId = order.DeliveryPartnerId
                    });
                deliveryNotificationQueued = true;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                message = $"Order status updated to {request.Status}",
                kptMinutes = order.KptMinutes,
                checklistCompleted = order.KitchenChecklist.Count > 0 && order.KitchenChecklist.All(c => c.IsCompleted),
                deliveryNotificationQueued
            });
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
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateKitchenAccessRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);
            var now = MongoService.GetIstNow();
            var todayStart = now.Date;

            var pendingOrders = await _mongo.GetOrdersByStatusAsync(new[] { "pending" }, outletId);
            var preparingOrders = await _mongo.GetOrdersByStatusAsync(new[] { "preparing" }, outletId);
            var completedToday = await _mongo.GetOrdersByStatusAsync(new[] { "delivered" }, outletId);
            var todayCompleted = completedToday.Where(o => o.CreatedAt >= todayStart).ToList();

            double avgPrepTime = 0;
            if (todayCompleted.Any())
            {
                avgPrepTime = todayCompleted
                    .Select(o => o.KptMinutes.HasValue
                        ? (double)o.KptMinutes.Value
                        : (o.KitchenReadyAt.HasValue && o.KitchenPrepStartedAt.HasValue
                            ? Math.Max((o.KitchenReadyAt.Value - o.KitchenPrepStartedAt.Value).TotalMinutes, 0)
                            : 0))
                    .DefaultIfEmpty(0)
                    .Average();
            }

            var todayReady = await _mongo.GetOrdersByStatusAsync(new[] { "ready" }, outletId);
            var avgKpt = todayReady
                .Where(o => o.KptMinutes.HasValue && o.UpdatedAt >= todayStart)
                .Select(o => (double)o.KptMinutes!.Value)
                .DefaultIfEmpty(0)
                .Average();

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                pendingOrders = pendingOrders.Count,
                preparingOrders = preparingOrders.Count,
                readyOrders = todayReady.Count,
                completedToday = todayCompleted.Count,
                avgPrepTime = Math.Round(avgPrepTime, 1),
                avgKptMinutes = Math.Round(avgKpt, 1)
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

    [Function("GetKitchenStaffDashboard")]
    public async Task<HttpResponseData> GetKitchenStaffDashboard(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "kitchen/staff/dashboard")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, role, errorResponse) = await AuthorizationHelper.ValidateKitchenAccessRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var period = (req.Query["period"] ?? "day").ToLowerInvariant();
            var now = MongoService.GetIstNow();
            var (start, end) = ResolvePeriodRange(period, now);

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);
            var reviews = await _mongo.GetAllReviewsAsync(outletId, 1, 1000);
            var periodReviews = reviews.Where(r => r.CreatedAt >= start && r.CreatedAt <= end).ToList();
            var avgRating = periodReviews.Any() ? Math.Round(periodReviews.Average(r => r.Rating), 2) : 0;

            var (staff, resolvedOutletId, _) = await ResolveStaffForCurrentUser(req, userId);
            var todayAttendance = staff?.Id != null
                ? await _mongo.GetTodayAttendanceAsync(staff.Id, resolvedOutletId ?? outletId ?? "default")
                : null;

            var totalOrdersPrepared = 0;
            var goodOrdersPrepared = 0;
            var badOrdersPrepared = 0;
            var avgKitchenPreparationTimeMinutes = 0d;

            if (staff?.Id != null)
            {
                var startDate = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var endDate = end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var dailyPerformanceEntries = await _mongo.GetDailyPerformanceByStaffAsync(staff.Id, startDate, endDate);

                totalOrdersPrepared = dailyPerformanceEntries.Sum(e =>
                    e.Shifts != null && e.Shifts.Count > 0
                        ? e.Shifts.Sum(s => s.TotalOrdersPrepared)
                        : e.TotalOrdersPrepared);

                goodOrdersPrepared = dailyPerformanceEntries.Sum(e =>
                    e.Shifts != null && e.Shifts.Count > 0
                        ? e.Shifts.Sum(s => s.GoodOrdersCount)
                        : e.GoodOrdersCount);

                badOrdersPrepared = dailyPerformanceEntries.Sum(e =>
                    e.Shifts != null && e.Shifts.Count > 0
                        ? e.Shifts.Sum(s => s.BadOrdersCount)
                        : e.BadOrdersCount);

                var completedStatuses = new[] { "ready", "delivered" };
                var staffCompletedOrders = (await _mongo.GetOrdersByStatusAsync(completedStatuses, resolvedOutletId ?? outletId))
                    .Where(o => o.KitchenAssignedStaffId == staff.Id)
                    .Where(o =>
                    {
                        var preparedAt = o.KitchenReadyAt ?? o.UpdatedAt;
                        return preparedAt >= start && preparedAt <= end;
                    })
                    .ToList();

                if (totalOrdersPrepared == 0)
                {
                    totalOrdersPrepared = staffCompletedOrders.Count;
                }

                avgKitchenPreparationTimeMinutes = staffCompletedOrders
                    .Select(o => o.KptMinutes.HasValue
                        ? (double)o.KptMinutes.Value
                        : (o.KitchenReadyAt.HasValue && o.KitchenPrepStartedAt.HasValue
                            ? Math.Max((o.KitchenReadyAt.Value - o.KitchenPrepStartedAt.Value).TotalMinutes, 0)
                            : 0))
                    .DefaultIfEmpty(0)
                    .Average();
            }

            var activeSession = todayAttendance?.Sessions?
                .Where(s => s.ClockIn.HasValue && !s.ClockOut.HasValue)
                .OrderByDescending(s => s.ClockIn)
                .FirstOrDefault();

            var hasActiveSession = activeSession != null;
            var isInShift = hasActiveSession || (todayAttendance?.ClockIn.HasValue == true && !todayAttendance.ClockOut.HasValue);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                role,
                period,
                ratings = new
                {
                    average = avgRating,
                    reviewsCount = periodReviews.Count,
                    start,
                    end
                },
                shift = new
                {
                    isInShift,
                    clockIn = hasActiveSession ? activeSession!.ClockIn : todayAttendance?.ClockIn,
                    clockOut = hasActiveSession ? null : todayAttendance?.ClockOut,
                    hoursWorked = hasActiveSession ? activeSession!.HoursWorked : todayAttendance?.HoursWorked,
                    shiftName = hasActiveSession ? activeSession!.ShiftName : null
                },
                attendance = todayAttendance,
                kitchenPerformance = new
                {
                    totalOrdersPrepared,
                    goodOrdersPrepared,
                    badOrdersPrepared,
                    avgKitchenPreparationTimeMinutes = Math.Round(avgKitchenPreparationTimeMinutes, 1)
                },
                payslip = new
                {
                    route = "/staff/attendance",
                    label = "Open attendance and shift summary"
                }
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting kitchen staff dashboard");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("KitchenShiftIn")]
    public async Task<HttpResponseData> KitchenShiftIn(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "kitchen/staff/shift-in")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateKitchenAccessRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var (staff, outletId, error) = await ResolveStaffForCurrentUser(req, userId);
            if (!string.IsNullOrWhiteSpace(error))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error });
                return badReq;
            }

            var clientNow = ResolveClientNow(req);
            var attendance = await _mongo.ClockInAsync(staff!.Id!, $"{staff.FirstName} {staff.LastName}".Trim(), outletId!, clockInAt: clientNow);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Shift started", attendance });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error starting kitchen shift");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("KitchenShiftOut")]
    public async Task<HttpResponseData> KitchenShiftOut(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "kitchen/staff/shift-out")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateKitchenAccessRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var (staff, outletId, error) = await ResolveStaffForCurrentUser(req, userId);
            if (!string.IsNullOrWhiteSpace(error))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error });
                return badReq;
            }

            var clientNow = ResolveClientNow(req);
            var attendance = await _mongo.ClockOutAsync(staff!.Id!, outletId!, clockOutAt: clientNow);
            if (attendance == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "No active shift found for today" });
                return notFound;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Shift ended", attendance });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error ending kitchen shift");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("KitchenMarkAttendance")]
    public async Task<HttpResponseData> KitchenMarkAttendance(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "kitchen/staff/attendance/mark")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateKitchenAccessRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var (staff, outletId, error) = await ResolveStaffForCurrentUser(req, userId);
            if (!string.IsNullOrWhiteSpace(error))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error });
                return badReq;
            }

            var clientNow = ResolveClientNow(req);
            var attendance = await _mongo.GetTodayAttendanceAsync(staff!.Id!, outletId!, clientNow);
            if (attendance == null)
            {
                attendance = await _mongo.ClockInAsync(staff.Id!, $"{staff.FirstName} {staff.LastName}".Trim(), outletId!, clockInAt: clientNow);
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Attendance marked", attendance });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error marking kitchen attendance");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    private static List<KitchenChecklistItem> BuildChecklistForResponse(Order order)
    {
        if (order.KitchenChecklist.Any())
            return order.KitchenChecklist;

        return KitchenChecklistTemplate.Select(label => new KitchenChecklistItem
        {
            Label = label,
            IsCompleted = false
        }).ToList();
    }

    private static List<KitchenChecklistItem> NormalizeChecklist(List<KitchenChecklistUpdateItem> suppliedChecklist, string completedBy)
    {
        var now = MongoService.GetIstNow();
        return suppliedChecklist
            .Where(i => !string.IsNullOrWhiteSpace(i.Label))
            .Select(i => new KitchenChecklistItem
            {
                Id = string.IsNullOrWhiteSpace(i.Id) ? Guid.NewGuid().ToString("N") : i.Id,
                Label = i.Label.Trim(),
                IsCompleted = i.IsCompleted,
                CompletedAt = i.IsCompleted ? now : null,
                CompletedBy = i.IsCompleted ? completedBy : null
            })
            .ToList();
    }

    private static (DateTime start, DateTime end) ResolvePeriodRange(string period, DateTime now)
    {
        return period switch
        {
            "week" => (now.Date.AddDays(-(int)now.DayOfWeek), now),
            "month" => (new DateTime(now.Year, now.Month, 1), now),
            "year" => (new DateTime(now.Year, 1, 1), now),
            _ => (now.Date, now)
        };
    }

    private static bool IsKitchenOpsRole(string? role)
    {
        return role == "cook"
            || role == "chef"
            || role == "sous-chef"
            || role == "kitchen"
            || role == "kitchen-staff"
            || role == "kitchen-helper";
    }

    private static bool IsValidObjectId(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && ObjectId.TryParse(value, out _);
    }

    private async Task<(Staff? staff, string? outletId, string? error)> ResolveStaffForCurrentUser(HttpRequestData req, string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return (null, null, "User identity missing");

        var user = await _mongo.GetUserByIdAsync(userId);
        var staffByUser = await _mongo.GetStaffByUserIdAsync(userId);
        if (staffByUser?.Id != null)
        {
            var outletIdByUser = OutletHelper.GetOutletIdForAdmin(req, _auth) ?? user?.DefaultOutletId;
            if (!IsValidObjectId(outletIdByUser))
            {
                outletIdByUser = staffByUser.OutletIds?.FirstOrDefault(IsValidObjectId);
            }
            if (!IsValidObjectId(outletIdByUser))
            {
                return (staffByUser, null, "No valid outlet is assigned to this staff account");
            }
            return (staffByUser, outletIdByUser, null);
        }

        if (user == null)
            return (null, null, "User account not found");

        var staff = await _mongo.GetStaffByEmailAsync(user.Email);
        if (staff?.Id == null)
            return (null, null, "No staff profile mapped to this account email");

        if (string.IsNullOrWhiteSpace(staff.UserId))
        {
            await _mongo.LinkStaffToUserAsync(staff.Id, userId);
            staff.UserId = userId;
        }

        var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth) ?? user.DefaultOutletId;
        if (!IsValidObjectId(outletId))
        {
            outletId = staff.OutletIds?.FirstOrDefault(IsValidObjectId);
        }
        if (!IsValidObjectId(outletId))
        {
            return (staff, null, "No valid outlet is assigned to this staff account");
        }
        return (staff, outletId, null);
    }

    private static DateTime ResolveClientNow(HttpRequestData req)
    {
        var headers = req.Headers;

        if (!TryGetSingleHeader(headers, "x-client-epoch-ms", out var epochRaw)
            || !long.TryParse(epochRaw, out var epochMs))
        {
            return MongoService.GetIstNow();
        }

        try
        {
            var utc = DateTimeOffset.FromUnixTimeMilliseconds(epochMs).UtcDateTime;

            if (TryGetSingleHeader(headers, "x-client-timezone-offset-minutes", out var offsetRaw)
                && int.TryParse(offsetRaw, out var offsetMinutes)
                && offsetMinutes >= -840
                && offsetMinutes <= 840)
            {
                var local = utc - TimeSpan.FromMinutes(offsetMinutes);
                return DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            }

            return DateTime.SpecifyKind(utc, DateTimeKind.Unspecified);
        }
        catch
        {
            return MongoService.GetIstNow();
        }
    }

    private static bool TryGetSingleHeader(HttpHeadersCollection headers, string key, out string value)
    {
        value = string.Empty;
        if (!headers.TryGetValues(key, out var values))
        {
            return false;
        }

        value = values.FirstOrDefault() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }
}

public class KitchenStatusUpdateRequest
{
    public string Status { get; set; } = string.Empty;
    public List<KitchenChecklistUpdateItem>? ChecklistItems { get; set; }
}

public class KitchenChecklistUpdateItem
{
    public string? Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
}
