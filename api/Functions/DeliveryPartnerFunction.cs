using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Repositories;
using Cafe.Api.Models;
using Cafe.Api.Helpers;
using System.Net;
using System.Text.Json;

namespace Cafe.Api.Functions;

public class DeliveryPartnerFunction
{
    private readonly IOperationsRepository _mongo;
    private readonly IOrderRepository _orderRepo;
    private readonly NotificationService _notificationService;
    private readonly DeliveryRoutingService _deliveryRoutingService;
    private readonly AuthService _auth;
    private readonly ILogger _log;
    private readonly IdempotencyService _idempotency;

    public DeliveryPartnerFunction(
        IOperationsRepository mongo,
        IOrderRepository orderRepo,
        NotificationService notificationService,
        DeliveryRoutingService deliveryRoutingService,
        IdempotencyService idempotency,
        AuthService auth,
        ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _orderRepo = orderRepo;
        _notificationService = notificationService;
        _deliveryRoutingService = deliveryRoutingService;
        _idempotency = idempotency;
        _auth = auth;
        _log = loggerFactory.CreateLogger<DeliveryPartnerFunction>();
    }

    [Function("GetDeliveryPartners")]
    public async Task<HttpResponseData> GetDeliveryPartners(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/delivery-partners")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);
            var partners = await _mongo.GetDeliveryPartnersAsync(outletId ?? "default");

            var includeTest = string.Equals(req.Query["includeTest"], "true", StringComparison.OrdinalIgnoreCase);
            if (!includeTest)
            {
                partners = partners.Where(p => !IsLikelyTestPartner(p)).ToList();
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(partners);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting delivery partners");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    private static bool IsLikelyTestPartner(DeliveryPartner partner)
    {
        var name = (partner.Name ?? string.Empty).Trim().ToLowerInvariant();
        var phone = (partner.Phone ?? string.Empty).Trim();
        var vehicle = (partner.VehicleNumber ?? string.Empty).Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(name)) return true;

        var testNameTokens = new[] { "test", "dummy", "sample", "demo", "trial", "qa" };
        if (testNameTokens.Any(token => name.Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var obviousDummyPhones = new HashSet<string>(StringComparer.Ordinal)
        {
            "0000000000",
            "1111111111",
            "9999999999",
            "1234567890",
            "9876543210"
        };

        if (obviousDummyPhones.Contains(phone))
        {
            return true;
        }

        if (vehicle.Contains("test", StringComparison.OrdinalIgnoreCase) || vehicle.Contains("dummy", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    [Function("CreateDeliveryPartner")]
    public async Task<HttpResponseData> CreateDeliveryPartner(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/delivery-partners")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var (request, validationError) = await ValidationHelper.ValidateBody<CreateDeliveryPartnerRequest>(req);
            if (validationError != null) return validationError;

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);

            var partner = new DeliveryPartner
            {
                OutletId = outletId ?? "default",
                Name = InputSanitizer.Sanitize(request.Name),
                Phone = InputSanitizer.Sanitize(request.Phone),
                VehicleType = InputSanitizer.Sanitize(request.VehicleType),
                VehicleNumber = request.VehicleNumber != null ? InputSanitizer.Sanitize(request.VehicleNumber) : null,
                UserId = string.IsNullOrWhiteSpace(request.UserId) ? null : InputSanitizer.Sanitize(request.UserId),
                MileageKmpl = request.MileageKmpl is > 0 ? request.MileageKmpl.Value : 40,
                CodAllowed = request.CodAllowed ?? true,
                PayoutEnabled = request.PayoutEnabled ?? true,
                LicenseNumber = string.IsNullOrWhiteSpace(request.LicenseNumber) ? null : InputSanitizer.Sanitize(request.LicenseNumber),
                EmergencyContactName = string.IsNullOrWhiteSpace(request.EmergencyContactName) ? null : InputSanitizer.Sanitize(request.EmergencyContactName),
                EmergencyContactPhone = string.IsNullOrWhiteSpace(request.EmergencyContactPhone) ? null : InputSanitizer.Sanitize(request.EmergencyContactPhone),
                BankOrUpi = string.IsNullOrWhiteSpace(request.BankOrUpi) ? null : InputSanitizer.Sanitize(request.BankOrUpi),
                Status = "available",
                TotalDeliveries = 0,
                Rating = 5.0,
                CreatedAt = MongoService.GetIstNow()
            };

            await _mongo.CreateDeliveryPartnerAsync(partner);

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(partner);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error creating delivery partner");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("AssignDeliveryPartner")]
    public async Task<HttpResponseData> AssignDeliveryPartner(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/delivery-partners/assign")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var (request, validationError) = await ValidationHelper.ValidateBody<AssignDeliveryRequest>(req);
            if (validationError != null) return validationError;

            var order = await _orderRepo.GetOrderByIdAsync(request.OrderId);
            if (order == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Order not found" });
                return notFound;
            }

            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var requiredChannel = query["channel"]?.Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(requiredChannel) && (requiredChannel == "web" || requiredChannel == "shop" || requiredChannel == "partner"))
            {
                var orderChannel = string.IsNullOrWhiteSpace(order.Channel)
                    ? (order.OrderType == "dine-in" ? "shop" : "web")
                    : order.Channel.Trim().ToLowerInvariant();

                if (!string.Equals(orderChannel, requiredChannel, StringComparison.OrdinalIgnoreCase))
                {
                    var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                    await forbidden.WriteAsJsonAsync(new { error = $"Order channel does not match required '{requiredChannel}'" });
                    return forbidden;
                }
            }

            string? partnerId = request.DeliveryPartnerId;
            string? partnerName = null;

            if (string.IsNullOrWhiteSpace(partnerId))
            {
                // Auto-assign available partner
                var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);
                var available = await _mongo.GetAvailableDeliveryPartnerAsync(outletId ?? "default");
                if (available == null)
                {
                    var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                    await conflict.WriteAsJsonAsync(new { error = "No delivery partners available" });
                    return conflict;
                }
                partnerId = available.Id!;
                partnerName = available.Name;
            }

            var success = await _mongo.AssignDeliveryPartnerAsync(partnerId!, request.OrderId);
            if (!success)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Order or delivery partner not found" });
                return notFound;
            }

            var assignedOrder = await _orderRepo.GetOrderByIdAsync(request.OrderId);
            var assignedPartner = await _mongo.GetDeliveryPartnerByIdAsync(partnerId!);

            string? routeShortUrl = null;
            string? routeCode = null;
            double? routeDistanceKm = null;
            int? routeEtaMinutes = null;

            if (assignedOrder != null
                && string.Equals(assignedOrder.OrderType, "delivery", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(assignedOrder.DeliveryAddress)
                && !string.IsNullOrWhiteSpace(assignedOrder.OutletId))
            {
                var quote = await _deliveryRoutingService.BuildRouteQuoteAsync(assignedOrder.OutletId!, assignedOrder.DeliveryAddress!);
                if (quote != null)
                {
                    var routeLink = await _deliveryRoutingService.CreateOrReuseShortLinkAsync(
                        assignedOrder.OutletId!,
                        assignedOrder.DeliveryAddress!,
                        quote.MapUrl,
                        assignedOrder.Id,
                        quote.DistanceKm,
                        quote.EtaMinutes);

                    assignedOrder.DeliveryRouteUrl = routeLink.FullMapUrl;
                    assignedOrder.DeliveryRouteShortCode = routeLink.Code;
                    assignedOrder.DeliveryRouteShortUrl = routeLink.ShortUrl;
                    assignedOrder.DeliveryDistanceKm = routeLink.DistanceKm;
                    assignedOrder.DeliveryEtaMinutes = routeLink.EtaMinutes;
                    assignedOrder.DeliveryRouteUpdatedAt = MongoService.GetIstNow();
                    assignedOrder.UpdatedAt = MongoService.GetIstNow();
                    await _orderRepo.UpdateOrderAsync(assignedOrder);

                    routeShortUrl = routeLink.ShortUrl;
                    routeCode = routeLink.Code;
                    routeDistanceKm = routeLink.DistanceKm;
                    routeEtaMinutes = routeLink.EtaMinutes;
                }
            }

            if (assignedOrder != null && !string.IsNullOrWhiteSpace(assignedPartner?.UserId))
            {
                var shortOrderId = assignedOrder.Id?.Length >= 6 ? assignedOrder.Id[^6..] : assignedOrder.Id;
                var pickupMessage = string.Equals(assignedOrder.Status, "ready", StringComparison.OrdinalIgnoreCase)
                    ? $"Order #{shortOrderId} is ready. Please pick it up now."
                    : $"Order #{shortOrderId} has been assigned to you. Current status: {assignedOrder.Status}.";

                if (!string.IsNullOrWhiteSpace(routeShortUrl))
                {
                    pickupMessage = $"{pickupMessage} Route: {routeShortUrl}";
                }

                await _notificationService.SendSystemNotificationAsync(
                    assignedPartner.UserId,
                    "New Delivery Assignment",
                    pickupMessage,
                    actionUrl: "/partner/delivery");
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                message = $"Delivery partner {partnerName ?? partnerId} assigned to order",
                partnerId,
                orderId = request.OrderId,
                routeShortUrl,
                routeCode,
                routeDistanceKm,
                routeEtaMinutes
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error assigning delivery partner");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("GetParcelTaskRouteQuote")]
    public async Task<HttpResponseData> GetParcelTaskRouteQuote(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/delivery-partners/parcel-tasks/route-quote")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var (request, validationError) = await ValidationHelper.ValidateBody<ParcelRouteQuoteRequest>(req);
            if (validationError != null) return validationError;

            var startPoint = request.StartPoint.Trim();
            var endPoint = request.EndPoint.Trim();

            var route = await _deliveryRoutingService.BuildPointToPointRouteQuoteAsync(startPoint, endPoint);
            if (route == null || !route.DistanceKm.HasValue || route.DistanceKm.Value <= 0)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Unable to calculate route distance for parcel task" });
                return badReq;
            }

            var distance = Math.Round((decimal)route.DistanceKm.Value, 2, MidpointRounding.AwayFromZero);
            var billableDistance = request.IsRoundTrip ? distance * 2 : distance;

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new ParcelTaskRouteQuoteResponse
            {
                DistanceKm = distance,
                BillableDistanceKm = billableDistance,
                IsRoundTrip = request.IsRoundTrip,
                EtaMinutes = route.EtaMinutes,
                MapUrl = route.MapUrl
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting parcel route quote");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("CreateParcelDeliveryTask")]
    public async Task<HttpResponseData> CreateParcelDeliveryTask(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/delivery-partners/parcel-tasks")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var (request, validationError) = await ValidationHelper.ValidateBody<CreateParcelTaskRequest>(req);
            if (validationError != null) return validationError;

            var partner = await _mongo.GetDeliveryPartnerByIdAsync(request.PartnerId);
            if (partner == null || string.IsNullOrWhiteSpace(partner.Id))
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Delivery partner not found" });
                return notFound;
            }

            var route = await _deliveryRoutingService.BuildPointToPointRouteQuoteAsync(request.StartPoint, request.EndPoint);
            if (route == null || !route.DistanceKm.HasValue || route.DistanceKm.Value <= 0)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Unable to calculate route distance for parcel task" });
                return badReq;
            }

            var link = await _deliveryRoutingService.CreateOrReuseShortLinkAsync(
                partner.OutletId,
                request.EndPoint,
                route.MapUrl,
                null,
                route.DistanceKm,
                route.EtaMinutes);

            var now = MongoService.GetIstNow();
            var distance = Math.Round((decimal)route.DistanceKm.Value, 2, MidpointRounding.AwayFromZero);
            var billableDistance = request.IsRoundTrip ? distance * 2 : distance;

            var task = new ParcelDeliveryTask
            {
                OutletId = partner.OutletId,
                PartnerId = partner.Id,
                PartnerName = partner.Name,
                AssignedByUserId = userId,
                StartPoint = InputSanitizer.Sanitize(request.StartPoint),
                EndPoint = InputSanitizer.Sanitize(request.EndPoint),
                DistanceKm = distance,
                IsRoundTrip = request.IsRoundTrip,
                BillableDistanceKm = billableDistance,
                EtaMinutes = route.EtaMinutes,
                RouteMapUrl = route.MapUrl,
                RouteShortCode = link.Code,
                RouteShortUrl = link.ShortUrl,
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : InputSanitizer.Sanitize(request.Notes),
                Status = "assigned",
                CreatedAt = now,
                UpdatedAt = now
            };

            await _mongo.CreateParcelTaskAsync(task);

            if (!string.IsNullOrWhiteSpace(partner.UserId))
            {
                var roundTripText = request.IsRoundTrip ? "Round-trip" : "One-way";
                var routeShareUrl = !string.IsNullOrWhiteSpace(link.ShortUrl) ? link.ShortUrl : route.MapUrl;
                var notificationMessage =
                    $"{roundTripText} parcel task assigned: {request.StartPoint} to {request.EndPoint} ({billableDistance:0.##} km). Route: {routeShareUrl}";

                await _notificationService.SendSystemNotificationAsync(
                    partner.UserId,
                    "New Parcel Delivery Task",
                    notificationMessage,
                    actionUrl: $"/partner/delivery?action=accept&parcelTaskId={task.Id}");
            }

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(task);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error creating parcel delivery task");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("AcceptParcelDeliveryTask")]
    public async Task<HttpResponseData> AcceptParcelDeliveryTask(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "partner/delivery/parcel-tasks/{taskId}/accept")] HttpRequestData req,
        string taskId)
    {
        try
        {
            var (isAuthorized, userId, role, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthorized) return errorResponse!;

            if (role != "partner" && role != "delivery-partner")
            {
                var forbiddenRole = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbiddenRole.WriteAsJsonAsync(new { error = "Partner access required" });
                return forbiddenRole;
            }

            var partner = await _mongo.GetDeliveryPartnerByUserIdAsync(userId!);
            if (partner == null || string.IsNullOrWhiteSpace(partner.Id))
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Delivery partner profile not found" });
                return notFound;
            }

            var accepted = await _mongo.AcceptParcelTaskAsync(taskId, partner.Id);
            if (!accepted)
            {
                var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                await conflict.WriteAsJsonAsync(new { error = "Parcel task already processed or not assigned to you" });
                return conflict;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Parcel task accepted", status = "accepted" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error accepting parcel delivery task {TaskId}", taskId);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("CompleteParcelDeliveryTask")]
    public async Task<HttpResponseData> CompleteParcelDeliveryTask(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "partner/delivery/parcel-tasks/{taskId}/complete")] HttpRequestData req,
        string taskId)
    {
        try
        {
            var (isAuthorized, userId, role, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthorized) return errorResponse!;

            if (role != "partner" && role != "delivery-partner")
            {
                var forbiddenRole = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbiddenRole.WriteAsJsonAsync(new { error = "Partner access required" });
                return forbiddenRole;
            }

            var partner = await _mongo.GetDeliveryPartnerByUserIdAsync(userId!);
            if (partner == null || string.IsNullOrWhiteSpace(partner.Id))
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Delivery partner profile not found" });
                return notFound;
            }

            var task = await _mongo.GetParcelTaskByIdAsync(taskId);
            if (task == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Parcel task not found" });
                return notFound;
            }

            var completed = await _mongo.CompleteParcelTaskAsync(taskId, partner.Id);
            if (!completed)
            {
                var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                await conflict.WriteAsJsonAsync(new { error = "Parcel task already completed or not assigned to you" });
                return conflict;
            }

            var activeShift = await _mongo.GetActiveShiftForPartnerAsync(partner.Id);
            var now = MongoService.GetIstNow();
            var trip = new PartnerTripLog
            {
                ShiftId = activeShift?.Id ?? "000000000000000000000000",
                PartnerId = partner.Id,
                OutletId = partner.OutletId,
                TripType = "delivery",
                StartPointLabel = task.StartPoint,
                EndPointLabel = task.EndPoint,
                StartOdometerKm = 0,
                EndOdometerKm = task.BillableDistanceKm,
                DistanceKm = task.BillableDistanceKm,
                StartedAt = now,
                EndedAt = now,
                Notes = task.IsRoundTrip
                    ? $"Parcel task completed (round-trip). Base distance: {task.DistanceKm:0.##} km"
                    : "Parcel task completed",
                CreatedAt = now
            };

            await _mongo.CreatePartnerTripAsync(trip);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Parcel task completed", status = "completed", tripDistanceKm = task.BillableDistanceKm });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error completing parcel delivery task {TaskId}", taskId);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("PickupAssignedOrder")]
    public async Task<HttpResponseData> PickupAssignedOrder(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "partner/delivery/orders/{orderId}/pickup")] HttpRequestData req,
        string orderId)
    {
        try
        {
            var (isAuthorized, userId, role, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthorized) return errorResponse!;

            if (role != "partner" && role != "delivery-partner")
            {
                var forbiddenRole = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbiddenRole.WriteAsJsonAsync(new { error = "Partner access required" });
                return forbiddenRole;
            }

            var idempotencyKey = GetIdempotencyKey(req);
            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                var requestHash = IdempotencyService.ComputeRequestHash($"pickup:{userId}:{orderId}");
                var start = await _idempotency.TryBeginAsync(idempotencyKey, "partner.pickup-order", userId ?? string.Empty, requestHash);
                if (start.IsConflict)
                {
                    var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                    await conflict.WriteAsJsonAsync(new { error = "Idempotency key reused with different request payload" });
                    return conflict;
                }

                if (start.IsInProgress)
                {
                    var accepted = req.CreateResponse(HttpStatusCode.Accepted);
                    await accepted.WriteAsJsonAsync(new { message = "Request already in progress" });
                    return accepted;
                }

                if (start.ReplayStatusCode.HasValue)
                {
                    return await BuildReplayResponseAsync(req, start.ReplayStatusCode.Value, start.ReplayBody);
                }
            }

            var partner = await _mongo.GetDeliveryPartnerByUserIdAsync(userId!);
            if (partner == null || string.IsNullOrWhiteSpace(partner.Id))
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Delivery partner profile not found" });
                return notFound;
            }

            var order = await _orderRepo.GetOrderByIdAsync(orderId);
            if (order == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Order not found" });
                return notFound;
            }

            if (!string.Equals(order.OrderType, "delivery", StringComparison.OrdinalIgnoreCase))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Pickup is only valid for delivery orders" });
                return badReq;
            }

            if (!string.Equals(order.DeliveryPartnerId, partner.Id, StringComparison.OrdinalIgnoreCase))
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "This order is not assigned to you" });
                return forbidden;
            }

            if (!string.Equals(order.Status, "ready", StringComparison.OrdinalIgnoreCase))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Order must be in ready state before pickup" });
                return badReq;
            }

            order.Status = "out-for-delivery";
            order.UpdatedAt = MongoService.GetIstNow();

            var updated = await _orderRepo.UpdateOrderAsync(order);
            if (!updated)
            {
                var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                await conflict.WriteAsJsonAsync(new { error = "Failed to update order status" });
                return conflict;
            }

            if (!string.IsNullOrWhiteSpace(order.UserId))
            {
                await _notificationService.SendOrderStatusNotificationAsync(order, "out-for-delivery");
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            var payload = new { message = "Order picked up and marked out-for-delivery", status = "out-for-delivery" };
            await response.WriteAsJsonAsync(payload);

            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                await _idempotency.MarkCompletedAsync(idempotencyKey, "partner.pickup-order", userId ?? string.Empty, (int)HttpStatusCode.OK, JsonSerializer.Serialize(payload));
            }
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error picking up assigned order {OrderId}", orderId);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("AcceptDeliveryOrder")]
    public async Task<HttpResponseData> AcceptDeliveryOrder(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "partner/delivery/orders/{orderId}/accept")] HttpRequestData req,
        string orderId)
    {
        try
        {
            var (isAuthorized, userId, role, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthorized) return errorResponse!;

            if (role != "partner" && role != "delivery-partner")
            {
                var forbiddenRole = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbiddenRole.WriteAsJsonAsync(new { error = "Partner access required" });
                return forbiddenRole;
            }

            var idempotencyKey = GetIdempotencyKey(req);
            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                var requestHash = IdempotencyService.ComputeRequestHash($"accept:{userId}:{orderId}");
                var start = await _idempotency.TryBeginAsync(idempotencyKey, "partner.accept-order", userId ?? string.Empty, requestHash);
                if (start.IsConflict)
                {
                    var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                    await conflict.WriteAsJsonAsync(new { error = "Idempotency key reused with different request payload" });
                    return conflict;
                }

                if (start.IsInProgress)
                {
                    var accepted = req.CreateResponse(HttpStatusCode.Accepted);
                    await accepted.WriteAsJsonAsync(new { message = "Request already in progress" });
                    return accepted;
                }

                if (start.ReplayStatusCode.HasValue)
                {
                    return await BuildReplayResponseAsync(req, start.ReplayStatusCode.Value, start.ReplayBody);
                }
            }

            if (string.IsNullOrWhiteSpace(userId))
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Invalid user context" });
                return unauthorized;
            }

            var partner = await _mongo.GetDeliveryPartnerByUserIdAsync(userId);
            if (partner == null || string.IsNullOrWhiteSpace(partner.Id))
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Delivery partner profile not found" });
                return notFound;
            }

            var order = await _orderRepo.GetOrderByIdAsync(orderId);
            if (order == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Order not found" });
                return notFound;
            }

            if (!string.Equals(order.OrderType, "delivery", StringComparison.OrdinalIgnoreCase))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Only delivery orders can be accepted" });
                return badReq;
            }

            if (!string.IsNullOrWhiteSpace(order.DeliveryPartnerId))
            {
                if (string.Equals(order.DeliveryPartnerId, partner.Id, StringComparison.OrdinalIgnoreCase))
                {
                    var alreadyMine = req.CreateResponse(HttpStatusCode.OK);
                    await alreadyMine.WriteAsJsonAsync(new { message = "Order already assigned to you", assigned = true });
                    return alreadyMine;
                }

                var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                await conflict.WriteAsJsonAsync(new { error = "Order already accepted by another delivery partner" });
                return conflict;
            }

            var success = await _mongo.TryAssignUnassignedDeliveryPartnerAsync(partner.Id, orderId);
            if (!success)
            {
                var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                await conflict.WriteAsJsonAsync(new { error = "Order was already claimed or is no longer eligible" });
                return conflict;
            }

            var assignedOrder = await _orderRepo.GetOrderByIdAsync(orderId);
            if (assignedOrder != null && !string.IsNullOrWhiteSpace(assignedOrder.UserId))
            {
                await _notificationService.SendOrderStatusNotificationAsync(assignedOrder, assignedOrder.Status);
            }

            if (!string.IsNullOrWhiteSpace(order.OutletId))
            {
                var peers = await _mongo.GetDeliveryPartnersAsync(order.OutletId);
                var shortOrderId = order.Id?.Length >= 6 ? order.Id[^6..] : order.Id;
                foreach (var peer in peers)
                {
                    if (string.IsNullOrWhiteSpace(peer.UserId) || string.Equals(peer.Id, partner.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    await _notificationService.SendSystemNotificationAsync(
                        peer.UserId,
                        "Delivery Request Claimed",
                        $"Order #{shortOrderId} has been accepted by another partner.",
                        actionUrl: "/partner/delivery");
                }
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            var payload = new { message = "Order accepted successfully", assigned = true, orderId, partnerId = partner.Id };
            await response.WriteAsJsonAsync(payload);

            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                await _idempotency.MarkCompletedAsync(idempotencyKey, "partner.accept-order", userId ?? string.Empty, (int)HttpStatusCode.OK, JsonSerializer.Serialize(payload));
            }
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error accepting delivery order {OrderId}", orderId);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("CompleteDelivery")]
    public async Task<HttpResponseData> CompleteDelivery(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/delivery-partners/{partnerId}/complete")] HttpRequestData req, string partnerId)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var success = await _mongo.CompleteDeliveryAsync(partnerId);
            if (!success)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Delivery partner not found" });
                return notFound;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Delivery completed" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error completing delivery");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("UpdateDeliveryPartnerStatus")]
    public async Task<HttpResponseData> UpdateDeliveryPartnerStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "manage/delivery-partners/{partnerId}/status")] HttpRequestData req, string partnerId)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var (request, validationError) = await ValidationHelper.ValidateBody<UpdateDeliveryPartnerStatusRequest>(req);
            if (validationError != null) return validationError;

            var validStatuses = new[] { "available", "offline" };
            if (!validStatuses.Contains(request.Status.ToLower()))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Status must be 'available' or 'offline'" });
                return badReq;
            }

            var partner = await _mongo.GetDeliveryPartnerByIdAsync(partnerId);
            if (partner == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Delivery partner not found" });
                return notFound;
            }

            partner.Status = request.Status.ToLower();
            await _mongo.UpdateDeliveryPartnerAsync(partnerId, partner);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = $"Status updated to {request.Status}" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error updating status");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("UpdateDeliveryPartner")]
    public async Task<HttpResponseData> UpdateDeliveryPartner(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "manage/delivery-partners/{partnerId}")] HttpRequestData req, string partnerId)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var (request, validationError) = await ValidationHelper.ValidateBody<CreateDeliveryPartnerRequest>(req);
            if (validationError != null) return validationError;

            var partner = await _mongo.GetDeliveryPartnerByIdAsync(partnerId);
            if (partner == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Delivery partner not found" });
                return notFound;
            }

            partner.Name = InputSanitizer.Sanitize(request.Name);
            partner.Phone = InputSanitizer.Sanitize(request.Phone);
            partner.VehicleType = InputSanitizer.Sanitize(request.VehicleType);
            partner.VehicleNumber = request.VehicleNumber != null ? InputSanitizer.Sanitize(request.VehicleNumber) : null;
            partner.UserId = string.IsNullOrWhiteSpace(request.UserId) ? null : InputSanitizer.Sanitize(request.UserId);
            partner.MileageKmpl = request.MileageKmpl is > 0 ? request.MileageKmpl.Value : partner.MileageKmpl;
            partner.CodAllowed = request.CodAllowed ?? partner.CodAllowed;
            partner.PayoutEnabled = request.PayoutEnabled ?? partner.PayoutEnabled;
            partner.LicenseNumber = string.IsNullOrWhiteSpace(request.LicenseNumber) ? partner.LicenseNumber : InputSanitizer.Sanitize(request.LicenseNumber);
            partner.EmergencyContactName = string.IsNullOrWhiteSpace(request.EmergencyContactName) ? partner.EmergencyContactName : InputSanitizer.Sanitize(request.EmergencyContactName);
            partner.EmergencyContactPhone = string.IsNullOrWhiteSpace(request.EmergencyContactPhone) ? partner.EmergencyContactPhone : InputSanitizer.Sanitize(request.EmergencyContactPhone);
            partner.BankOrUpi = string.IsNullOrWhiteSpace(request.BankOrUpi) ? partner.BankOrUpi : InputSanitizer.Sanitize(request.BankOrUpi);

            await _mongo.UpdateDeliveryPartnerAsync(partnerId, partner);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Delivery partner updated successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error updating delivery partner");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("UpdateDeliveryPartnerLocation")]
    public async Task<HttpResponseData> UpdateDeliveryPartnerLocation(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "manage/delivery-partners/{partnerId}/location")] HttpRequestData req, string partnerId)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var (request, validationError) = await ValidationHelper.ValidateBody<UpdateDeliveryPartnerLocationRequest>(req);
            if (validationError != null) return validationError;

            var success = await _mongo.UpdateDeliveryPartnerLocationAsync(partnerId, request.Latitude, request.Longitude);
            if (!success)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Delivery partner not found" });
                return notFound;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Location updated" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error updating delivery partner location");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("DeleteDeliveryPartner")]
    public async Task<HttpResponseData> DeleteDeliveryPartner(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "manage/delivery-partners/{partnerId}")] HttpRequestData req, string partnerId)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var success = await _mongo.DeleteDeliveryPartnerAsync(partnerId);
            if (!success)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Delivery partner not found" });
                return notFound;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Delivery partner deleted" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error deleting delivery partner");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("DeleteTestDeliveryPartners")]
    public async Task<HttpResponseData> DeleteTestDeliveryPartners(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "manage/delivery-partners/test-data")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth) ?? "default";
            var partners = await _mongo.GetDeliveryPartnersAsync(outletId);
            var testPartners = partners.Where(IsLikelyTestPartner).ToList();

            var deletedIds = new List<string>();
            foreach (var partner in testPartners)
            {
                if (string.IsNullOrWhiteSpace(partner.Id))
                {
                    continue;
                }

                var deleted = await _mongo.DeleteDeliveryPartnerAsync(partner.Id);
                if (deleted)
                {
                    deletedIds.Add(partner.Id);
                }
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                message = "Test delivery partner records removed",
                outletId,
                matched = testPartners.Count,
                deleted = deletedIds.Count,
                deletedIds
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error deleting test delivery partner records");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("GetDeliveryPartnerDashboard")]
    public async Task<HttpResponseData> GetDeliveryPartnerDashboard(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "partner/delivery/dashboard")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, role, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthorized) return errorResponse!;

            DeliveryPartner? partner = null;
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var requestedPartnerId = query["partnerId"];

            if ((role == "admin" || role == "manager") && !string.IsNullOrWhiteSpace(requestedPartnerId))
            {
                partner = await _mongo.GetDeliveryPartnerByIdAsync(requestedPartnerId);
            }
            else if (!string.IsNullOrWhiteSpace(userId))
            {
                partner = await _mongo.GetDeliveryPartnerByUserIdAsync(userId);
            }

            if (partner == null || string.IsNullOrWhiteSpace(partner.Id))
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Delivery partner profile not found" });
                return notFound;
            }

            var now = MongoService.GetIstNow();
            var dayStart = now.Date;
            var dayEnd = dayStart.AddDays(1);

            var activeShift = await _mongo.GetActiveShiftForPartnerAsync(partner.Id);
            var activeOrders = await _mongo.GetActiveOrdersForPartnerAsync(partner.Id, partner.OutletId);
            var parcelTasks = await _mongo.GetParcelTasksForPartnerAsync(partner.Id, null, 100);
            var outletOrders = await _orderRepo.GetAllOrdersAsync(partner.OutletId);
            var pendingRequests = outletOrders
                .Where(o =>
                    string.Equals(o.OrderType, "delivery", StringComparison.OrdinalIgnoreCase)
                    && string.IsNullOrWhiteSpace(o.DeliveryPartnerId)
                    && (string.Equals(o.Status, "pending", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(o.Status, "confirmed", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(o.Status, "preparing", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(o.Status, "ready", StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(o => o.CreatedAt)
                .Take(20)
                .ToList();
            var todayDistance = await _mongo.GetPartnerDistanceAsync(partner.Id, dayStart, dayEnd);
            var fuelPrice = await _mongo.GetFuelPriceAsync(partner.OutletId, dayStart);
            var codOutstanding = await _mongo.GetOutstandingCodAmountAsync(partner.Id);
            var (avgRating, reviewsCount) = await _mongo.GetDeliveryPartnerRatingSummaryAsync(partner.Id);
            var recentReviews = await _mongo.GetDeliveryPartnerReviewsAsync(partner.Id, 10);

            var mileage = partner.MileageKmpl <= 0 ? 40 : partner.MileageKmpl;
            var litres = mileage > 0 ? todayDistance / mileage : 0;
            var todayPayout = Math.Round(litres * (fuelPrice?.PetrolPricePerLitre ?? 0), 2, MidpointRounding.AwayFromZero);

            var responseModel = new PartnerDashboardResponse
            {
                Profile = partner,
                ActiveShift = activeShift,
                ActiveOrders = activeOrders,
                PendingRequests = pendingRequests,
                ActiveParcelTasks = parcelTasks.Where(t => t.Status == "accepted").OrderByDescending(t => t.CreatedAt).ToList(),
                PendingParcelTasks = parcelTasks.Where(t => t.Status == "assigned").OrderByDescending(t => t.CreatedAt).ToList(),
                TodayDistanceKm = Math.Round(todayDistance, 2, MidpointRounding.AwayFromZero),
                TodayPayout = todayPayout,
                CodOutstanding = Math.Round(codOutstanding, 2, MidpointRounding.AwayFromZero),
                AverageRating = avgRating,
                ReviewsCount = reviewsCount,
                RecentReviews = recentReviews.Select(r => new PartnerReviewSummary
                {
                    OrderId = r.OrderId,
                    Rating = r.Rating,
                    Review = r.Review,
                    CreatedAt = r.CreatedAt
                }).ToList()
            };

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(responseModel);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting delivery partner dashboard");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("StartDeliveryPartnerShift")]
    public async Task<HttpResponseData> StartDeliveryPartnerShift(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/delivery-partners/{partnerId}/shift/start")] HttpRequestData req,
        string partnerId)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var (request, validationError) = await ValidationHelper.ValidateBody<StartPartnerShiftRequest>(req);
            if (validationError != null) return validationError;

            var partner = await _mongo.GetDeliveryPartnerByIdAsync(partnerId);
            if (partner == null || string.IsNullOrWhiteSpace(partner.Id))
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Delivery partner not found" });
                return notFound;
            }

            var existingShift = await _mongo.GetActiveShiftForPartnerAsync(partner.Id);
            if (existingShift != null)
            {
                var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                await conflict.WriteAsJsonAsync(new { error = "Partner already has an active shift" });
                return conflict;
            }

            var now = MongoService.GetIstNow();
            var shift = new DeliveryShift
            {
                PartnerId = partner.Id,
                OutletId = partner.OutletId,
                ShiftDate = now.Date,
                StartedAt = now,
                StartOdometerKm = request.StartOdometerKm,
                StartLatitude = request.StartLatitude,
                StartLongitude = request.StartLongitude,
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : InputSanitizer.Sanitize(request.Notes),
                Status = "active",
                CreatedAt = now,
                UpdatedAt = now
            };

            await _mongo.StartPartnerShiftAsync(shift);

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(shift);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error starting partner shift");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("EndDeliveryPartnerShift")]
    public async Task<HttpResponseData> EndDeliveryPartnerShift(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/delivery-partners/{partnerId}/shift/{shiftId}/end")] HttpRequestData req,
        string partnerId,
        string shiftId)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var (request, validationError) = await ValidationHelper.ValidateBody<EndPartnerShiftRequest>(req);
            if (validationError != null) return validationError;

            var partner = await _mongo.GetDeliveryPartnerByIdAsync(partnerId);
            if (partner == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Delivery partner not found" });
                return notFound;
            }

            var activeShift = await _mongo.GetActiveShiftForPartnerAsync(partnerId);
            if (activeShift == null || activeShift.Id != shiftId)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "No matching active shift found" });
                return badReq;
            }

            if (request.EndOdometerKm < activeShift.StartOdometerKm)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "End odometer cannot be less than start odometer" });
                return badReq;
            }

            var success = await _mongo.EndPartnerShiftAsync(
                shiftId,
                request.EndOdometerKm,
                request.EndLatitude,
                request.EndLongitude,
                string.IsNullOrWhiteSpace(request.Notes) ? null : InputSanitizer.Sanitize(request.Notes));

            if (!success)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Could not end shift" });
                return badReq;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Shift ended successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error ending partner shift");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("CreateDeliveryPartnerTrip")]
    public async Task<HttpResponseData> CreateDeliveryPartnerTrip(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/delivery-partners/{partnerId}/trips")] HttpRequestData req,
        string partnerId)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var (request, validationError) = await ValidationHelper.ValidateBody<CreatePartnerTripRequest>(req);
            if (validationError != null) return validationError;

            var partner = await _mongo.GetDeliveryPartnerByIdAsync(partnerId);
            if (partner == null || string.IsNullOrWhiteSpace(partner.Id))
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Delivery partner not found" });
                return notFound;
            }

            var activeShift = await _mongo.GetActiveShiftForPartnerAsync(partner.Id);
            if (activeShift == null || activeShift.Id != request.ShiftId)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Trip can be created only for active shift" });
                return badReq;
            }

            if (request.EndOdometerKm < request.StartOdometerKm)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "End odometer cannot be less than start odometer" });
                return badReq;
            }

            var now = MongoService.GetIstNow();
            var trip = new PartnerTripLog
            {
                ShiftId = request.ShiftId,
                PartnerId = partner.Id,
                OutletId = partner.OutletId,
                TripType = InputSanitizer.Sanitize(request.TripType).ToLowerInvariant(),
                OrderId = string.IsNullOrWhiteSpace(request.OrderId) ? null : request.OrderId,
                StartPointLabel = string.IsNullOrWhiteSpace(request.StartPointLabel) ? null : InputSanitizer.Sanitize(request.StartPointLabel),
                EndPointLabel = string.IsNullOrWhiteSpace(request.EndPointLabel) ? null : InputSanitizer.Sanitize(request.EndPointLabel),
                StartLatitude = request.StartLatitude,
                StartLongitude = request.StartLongitude,
                EndLatitude = request.EndLatitude,
                EndLongitude = request.EndLongitude,
                StartOdometerKm = request.StartOdometerKm,
                EndOdometerKm = request.EndOdometerKm,
                DistanceKm = Math.Max(0, request.EndOdometerKm - request.StartOdometerKm),
                StartedAt = now,
                EndedAt = now,
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : InputSanitizer.Sanitize(request.Notes),
                CreatedAt = now
            };

            await _mongo.CreatePartnerTripAsync(trip);

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(trip);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error creating delivery partner trip");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("UpsertDeliveryFuelPrice")]
    public async Task<HttpResponseData> UpsertDeliveryFuelPrice(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "manage/delivery-partners/fuel-price")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var (request, validationError) = await ValidationHelper.ValidateBody<UpsertFuelPriceRequest>(req);
            if (validationError != null) return validationError;

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth) ?? "default";
            var price = await _mongo.UpsertFuelPriceAsync(outletId, request.Date.Date, request.PetrolPricePerLitre);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(price);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error upserting delivery fuel price");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("ConfirmCodCollection")]
    public async Task<HttpResponseData> ConfirmCodCollection(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/delivery-partners/{partnerId}/cod/confirm")] HttpRequestData req,
        string partnerId)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var (request, validationError) = await ValidationHelper.ValidateBody<ConfirmCodCollectionRequest>(req);
            if (validationError != null) return validationError;

            var partner = await _mongo.GetDeliveryPartnerByIdAsync(partnerId);
            if (partner == null || string.IsNullOrWhiteSpace(partner.Id))
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Delivery partner not found" });
                return notFound;
            }

            var order = await _orderRepo.GetOrderByIdAsync(request.OrderId);
            if (order == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Order not found" });
                return notFound;
            }

            if (!string.Equals(order.PaymentMethod, "cod", StringComparison.OrdinalIgnoreCase))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Order is not COD" });
                return badReq;
            }

            if (!string.Equals(order.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "COD must be confirmed by assigned delivery partner in partner dashboard" });
                return badReq;
            }

            if (!string.Equals(order.DeliveryPartnerId, partnerId, StringComparison.OrdinalIgnoreCase))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Order is not assigned to this delivery partner" });
                return badReq;
            }

            var now = MongoService.GetIstNow();
            var codLog = new CODCollectionLog
            {
                OrderId = order.Id!,
                PartnerId = partnerId,
                Amount = request.Amount,
                Collected = true,
                CollectionReference = string.IsNullOrWhiteSpace(request.CollectionReference) ? null : InputSanitizer.Sanitize(request.CollectionReference),
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : InputSanitizer.Sanitize(request.Notes),
                CollectedAt = now,
                ConfirmedByAdmin = true,
                CreatedAt = now
            };

            await _mongo.UpsertCodCollectionAsync(codLog);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "COD collection verified" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error confirming COD collection");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("ConfirmMyCodCollection")]
    public async Task<HttpResponseData> ConfirmMyCodCollection(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "partner/delivery/orders/{orderId}/cod/confirm")] HttpRequestData req,
        string orderId)
    {
        try
        {
            var (isAuthorized, userId, role, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthorized) return errorResponse!;

            if (role != "partner" && role != "delivery-partner" && role != "admin" && role != "manager")
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Partner access required" });
                return forbidden;
            }

            if (string.IsNullOrWhiteSpace(userId))
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Invalid user context" });
                return unauthorized;
            }

            var idempotencyKey = GetIdempotencyKey(req);
            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                var requestHash = IdempotencyService.ComputeRequestHash($"cod-confirm:{userId}:{orderId}");
                var start = await _idempotency.TryBeginAsync(idempotencyKey, "partner.confirm-cod", userId, requestHash);
                if (start.IsConflict)
                {
                    var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                    await conflict.WriteAsJsonAsync(new { error = "Idempotency key reused with different request payload" });
                    return conflict;
                }

                if (start.IsInProgress)
                {
                    var accepted = req.CreateResponse(HttpStatusCode.Accepted);
                    await accepted.WriteAsJsonAsync(new { message = "Request already in progress" });
                    return accepted;
                }

                if (start.ReplayStatusCode.HasValue)
                {
                    return await BuildReplayResponseAsync(req, start.ReplayStatusCode.Value, start.ReplayBody);
                }
            }

            var partner = await _mongo.GetDeliveryPartnerByUserIdAsync(userId);
            if (partner == null || string.IsNullOrWhiteSpace(partner.Id))
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Delivery partner profile not found" });
                return notFound;
            }

            var (request, validationError) = await ValidationHelper.ValidateBody<ConfirmCodCollectionRequest>(req);
            if (validationError != null) return validationError;

            var order = await _orderRepo.GetOrderByIdAsync(orderId);
            if (order == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Order not found" });
                return notFound;
            }

            if (!string.Equals(order.PaymentMethod, "cod", StringComparison.OrdinalIgnoreCase))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Order is not COD" });
                return badReq;
            }

            if (!string.Equals(order.DeliveryPartnerId, partner.Id, StringComparison.OrdinalIgnoreCase))
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "This COD order is not assigned to you" });
                return forbidden;
            }

            if (string.Equals(order.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase))
            {
                var ok = req.CreateResponse(HttpStatusCode.OK);
                await ok.WriteAsJsonAsync(new { message = "COD already confirmed", paymentStatus = "paid" });

                if (!string.IsNullOrWhiteSpace(idempotencyKey))
                {
                    await _idempotency.MarkCompletedAsync(idempotencyKey, "partner.confirm-cod", userId, (int)HttpStatusCode.OK, JsonSerializer.Serialize(new { message = "COD already confirmed", paymentStatus = "paid" }));
                }
                return ok;
            }

            var now = MongoService.GetIstNow();
            var codLog = new CODCollectionLog
            {
                OrderId = order.Id!,
                PartnerId = partner.Id,
                Amount = request.Amount,
                Collected = true,
                CollectionReference = string.IsNullOrWhiteSpace(request.CollectionReference) ? null : InputSanitizer.Sanitize(request.CollectionReference),
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : InputSanitizer.Sanitize(request.Notes),
                CollectedAt = now,
                ConfirmedByAdmin = false,
                CreatedAt = now
            };

            await _mongo.UpsertCodCollectionAsync(codLog);

            order.PaymentStatus = "paid";
            order.UpdatedAt = now;
            await _orderRepo.UpdateOrderAsync(order);

            var payload = new { message = "COD payment confirmed", paymentStatus = "paid" };
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(payload);

            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                await _idempotency.MarkCompletedAsync(idempotencyKey, "partner.confirm-cod", userId, (int)HttpStatusCode.OK, JsonSerializer.Serialize(payload));
            }
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error confirming COD collection from partner dashboard");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("AddDeliveryPartnerReview")]
    public async Task<HttpResponseData> AddDeliveryPartnerReview(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "delivery-partners/reviews")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var (request, validationError) = await ValidationHelper.ValidateBody<AddDeliveryPartnerReviewRequest>(req);
            if (validationError != null) return validationError;

            var order = await _orderRepo.GetOrderByIdAsync(request.OrderId);
            if (order == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Order not found" });
                return notFound;
            }

            if (order.UserId != userId)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "You can only review your own orders" });
                return forbidden;
            }

            if (!string.Equals(order.Status, "delivered", StringComparison.OrdinalIgnoreCase))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Only delivered orders can be reviewed" });
                return badReq;
            }

            if (!string.Equals(order.DeliveryPartnerId, request.PartnerId, StringComparison.OrdinalIgnoreCase))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Partner does not match order assignment" });
                return badReq;
            }

            var review = new DeliveryPartnerReview
            {
                OrderId = request.OrderId,
                PartnerId = request.PartnerId,
                UserId = userId!,
                Rating = request.Rating,
                Review = string.IsNullOrWhiteSpace(request.Review) ? null : InputSanitizer.Sanitize(request.Review),
                CreatedAt = MongoService.GetIstNow()
            };

            await _mongo.AddDeliveryPartnerReviewAsync(review);

            var partner = await _mongo.GetDeliveryPartnerByIdAsync(request.PartnerId);
            if (!string.IsNullOrWhiteSpace(partner?.UserId))
            {
                await _notificationService.SendSystemNotificationAsync(
                    partner.UserId,
                    "New Delivery Rating",
                    $"You received a new rating of {request.Rating}/5 for order #{request.OrderId}");
            }

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(review);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error adding delivery partner review");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("GetDeliveryPartnerPayoutSummary")]
    public async Task<HttpResponseData> GetDeliveryPartnerPayoutSummary(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/delivery-partners/{partnerId}/payout")] HttpRequestData req,
        string partnerId)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var partner = await _mongo.GetDeliveryPartnerByIdAsync(partnerId);
            if (partner == null || string.IsNullOrWhiteSpace(partner.Id))
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Delivery partner not found" });
                return notFound;
            }

            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var periodType = string.IsNullOrWhiteSpace(query["periodType"]) ? "day" : query["periodType"]!.Trim().ToLowerInvariant();
            var referenceDate = DateTime.TryParse(query["date"], out var parsedDate)
                ? parsedDate
                : MongoService.GetIstNow();

            var (periodStart, periodEnd) = GetPeriodRange(periodType, referenceDate);

            var totalDistance = await _mongo.GetPartnerDistanceAsync(partner.Id, periodStart, periodEnd);
            var trips = await _mongo.GetPartnerTripsAsync(partner.Id, periodStart, periodEnd);
            var totalDeliveries = trips.Count(t => t.TripType == "delivery");

            var fuel = await _mongo.GetFuelPriceAsync(partner.OutletId, periodStart.Date)
                ?? await _mongo.UpsertFuelPriceAsync(partner.OutletId, periodStart.Date, 105);

            var mileage = partner.MileageKmpl <= 0 ? 40 : partner.MileageKmpl;
            var litresConsumed = mileage > 0 ? totalDistance / mileage : 0;
            var payoutAmount = Math.Round(litresConsumed * fuel.PetrolPricePerLitre, 2, MidpointRounding.AwayFromZero);

            var responseModel = new PartnerPayoutSummaryResponse
            {
                PeriodType = periodType,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                TotalDistanceKm = Math.Round(totalDistance, 2, MidpointRounding.AwayFromZero),
                TotalDeliveries = totalDeliveries,
                MileageKmpl = mileage,
                FuelPricePerLitre = fuel.PetrolPricePerLitre,
                LitresConsumed = Math.Round(litresConsumed, 3, MidpointRounding.AwayFromZero),
                PayoutAmount = payoutAmount
            };

            var ledger = new PartnerPayoutLedger
            {
                PartnerId = partner.Id,
                OutletId = partner.OutletId,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                PeriodType = periodType,
                TotalDistanceKm = responseModel.TotalDistanceKm,
                MileageKmpl = responseModel.MileageKmpl,
                FuelPricePerLitre = responseModel.FuelPricePerLitre,
                LitresConsumed = responseModel.LitresConsumed,
                PayoutAmount = responseModel.PayoutAmount,
                TotalDeliveries = responseModel.TotalDeliveries,
                IsFinalized = false
            };

            await _mongo.CreatePartnerPayoutLedgerAsync(ledger);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(responseModel);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting delivery partner payout summary");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("StartMyDeliveryShift")]
    public async Task<HttpResponseData> StartMyDeliveryShift(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "partner/delivery/shift/start")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, role, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthorized) return errorResponse!;

            if (role != "partner" && role != "delivery-partner" && role != "admin" && role != "manager")
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Partner access required" });
                return forbidden;
            }

            if (string.IsNullOrWhiteSpace(userId))
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Invalid user context" });
                return unauthorized;
            }

            var partner = await _mongo.GetDeliveryPartnerByUserIdAsync(userId);
            if (partner == null || string.IsNullOrWhiteSpace(partner.Id))
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Delivery partner profile not found" });
                return notFound;
            }

            var (request, validationError) = await ValidationHelper.ValidateBody<StartPartnerShiftRequest>(req);
            if (validationError != null) return validationError;

            var existingShift = await _mongo.GetActiveShiftForPartnerAsync(partner.Id);
            if (existingShift != null)
            {
                var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                await conflict.WriteAsJsonAsync(new { error = "You already have an active shift" });
                return conflict;
            }

            var now = MongoService.GetIstNow();
            var shift = new DeliveryShift
            {
                PartnerId = partner.Id,
                OutletId = partner.OutletId,
                ShiftDate = now.Date,
                StartedAt = now,
                StartOdometerKm = request.StartOdometerKm,
                StartLatitude = request.StartLatitude,
                StartLongitude = request.StartLongitude,
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : InputSanitizer.Sanitize(request.Notes),
                Status = "active",
                CreatedAt = now,
                UpdatedAt = now
            };

            await _mongo.StartPartnerShiftAsync(shift);

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(shift);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error starting self-service partner shift");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("EndMyDeliveryShift")]
    public async Task<HttpResponseData> EndMyDeliveryShift(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "partner/delivery/shift/{shiftId}/end")] HttpRequestData req,
        string shiftId)
    {
        try
        {
            var (isAuthorized, userId, role, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthorized) return errorResponse!;

            if (role != "partner" && role != "delivery-partner" && role != "admin" && role != "manager")
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Partner access required" });
                return forbidden;
            }

            if (string.IsNullOrWhiteSpace(userId))
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Invalid user context" });
                return unauthorized;
            }

            var partner = await _mongo.GetDeliveryPartnerByUserIdAsync(userId);
            if (partner == null || string.IsNullOrWhiteSpace(partner.Id))
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Delivery partner profile not found" });
                return notFound;
            }

            var (request, validationError) = await ValidationHelper.ValidateBody<EndPartnerShiftRequest>(req);
            if (validationError != null) return validationError;

            var activeShift = await _mongo.GetActiveShiftForPartnerAsync(partner.Id);
            if (activeShift == null || activeShift.Id != shiftId)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "No matching active shift found" });
                return badReq;
            }

            if (request.EndOdometerKm < activeShift.StartOdometerKm)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "End odometer cannot be less than start odometer" });
                return badReq;
            }

            var success = await _mongo.EndPartnerShiftAsync(
                shiftId,
                request.EndOdometerKm,
                request.EndLatitude,
                request.EndLongitude,
                string.IsNullOrWhiteSpace(request.Notes) ? null : InputSanitizer.Sanitize(request.Notes));

            if (!success)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Could not end shift" });
                return badReq;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Shift ended successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error ending self-service partner shift");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("CreateMyDeliveryTrip")]
    public async Task<HttpResponseData> CreateMyDeliveryTrip(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "partner/delivery/trips")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, role, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthorized) return errorResponse!;

            if (role != "partner" && role != "delivery-partner" && role != "admin" && role != "manager")
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Partner access required" });
                return forbidden;
            }

            if (string.IsNullOrWhiteSpace(userId))
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Invalid user context" });
                return unauthorized;
            }

            var partner = await _mongo.GetDeliveryPartnerByUserIdAsync(userId);
            if (partner == null || string.IsNullOrWhiteSpace(partner.Id))
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Delivery partner profile not found" });
                return notFound;
            }

            var (request, validationError) = await ValidationHelper.ValidateBody<CreatePartnerTripRequest>(req);
            if (validationError != null) return validationError;

            var activeShift = await _mongo.GetActiveShiftForPartnerAsync(partner.Id);
            if (activeShift == null || activeShift.Id != request.ShiftId)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Trip can be created only for active shift" });
                return badReq;
            }

            if (request.EndOdometerKm < request.StartOdometerKm)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "End odometer cannot be less than start odometer" });
                return badReq;
            }

            var now = MongoService.GetIstNow();
            var trip = new PartnerTripLog
            {
                ShiftId = request.ShiftId,
                PartnerId = partner.Id,
                OutletId = partner.OutletId,
                TripType = InputSanitizer.Sanitize(request.TripType).ToLowerInvariant(),
                OrderId = string.IsNullOrWhiteSpace(request.OrderId) ? null : request.OrderId,
                StartPointLabel = string.IsNullOrWhiteSpace(request.StartPointLabel) ? null : InputSanitizer.Sanitize(request.StartPointLabel),
                EndPointLabel = string.IsNullOrWhiteSpace(request.EndPointLabel) ? null : InputSanitizer.Sanitize(request.EndPointLabel),
                StartLatitude = request.StartLatitude,
                StartLongitude = request.StartLongitude,
                EndLatitude = request.EndLatitude,
                EndLongitude = request.EndLongitude,
                StartOdometerKm = request.StartOdometerKm,
                EndOdometerKm = request.EndOdometerKm,
                DistanceKm = Math.Max(0, request.EndOdometerKm - request.StartOdometerKm),
                StartedAt = now,
                EndedAt = now,
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : InputSanitizer.Sanitize(request.Notes),
                CreatedAt = now
            };

            await _mongo.CreatePartnerTripAsync(trip);

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(trip);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error creating self-service partner trip");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("GetMyDeliveryPayoutSummary")]
    public async Task<HttpResponseData> GetMyDeliveryPayoutSummary(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "partner/delivery/payout")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, role, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthorized) return errorResponse!;

            if (role != "partner" && role != "delivery-partner" && role != "admin" && role != "manager")
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Partner access required" });
                return forbidden;
            }

            if (string.IsNullOrWhiteSpace(userId))
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Invalid user context" });
                return unauthorized;
            }

            var partner = await _mongo.GetDeliveryPartnerByUserIdAsync(userId);
            if (partner == null || string.IsNullOrWhiteSpace(partner.Id))
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Delivery partner profile not found" });
                return notFound;
            }

            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var periodType = string.IsNullOrWhiteSpace(query["periodType"]) ? "day" : query["periodType"]!.Trim().ToLowerInvariant();
            var referenceDate = DateTime.TryParse(query["date"], out var parsedDate)
                ? parsedDate
                : MongoService.GetIstNow();

            var (periodStart, periodEnd) = GetPeriodRange(periodType, referenceDate);

            var totalDistance = await _mongo.GetPartnerDistanceAsync(partner.Id, periodStart, periodEnd);
            var trips = await _mongo.GetPartnerTripsAsync(partner.Id, periodStart, periodEnd);
            var totalDeliveries = trips.Count(t => t.TripType == "delivery");

            var fuel = await _mongo.GetFuelPriceAsync(partner.OutletId, periodStart.Date)
                ?? await _mongo.UpsertFuelPriceAsync(partner.OutletId, periodStart.Date, 105);

            var mileage = partner.MileageKmpl <= 0 ? 40 : partner.MileageKmpl;
            var litresConsumed = mileage > 0 ? totalDistance / mileage : 0;
            var payoutAmount = Math.Round(litresConsumed * fuel.PetrolPricePerLitre, 2, MidpointRounding.AwayFromZero);

            var responseModel = new PartnerPayoutSummaryResponse
            {
                PeriodType = periodType,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                TotalDistanceKm = Math.Round(totalDistance, 2, MidpointRounding.AwayFromZero),
                TotalDeliveries = totalDeliveries,
                MileageKmpl = mileage,
                FuelPricePerLitre = fuel.PetrolPricePerLitre,
                LitresConsumed = Math.Round(litresConsumed, 3, MidpointRounding.AwayFromZero),
                PayoutAmount = payoutAmount
            };

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(responseModel);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting self-service partner payout summary");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    private static (DateTime periodStart, DateTime periodEnd) GetPeriodRange(string periodType, DateTime referenceDate)
    {
        var d = referenceDate.Date;
        return periodType switch
        {
            "week" => (d.AddDays(-(int)d.DayOfWeek), d.AddDays(-(int)d.DayOfWeek).AddDays(7)),
            "month" => (new DateTime(d.Year, d.Month, 1), new DateTime(d.Year, d.Month, 1).AddMonths(1)),
            "year" => (new DateTime(d.Year, 1, 1), new DateTime(d.Year + 1, 1, 1)),
            _ => (d, d.AddDays(1))
        };
    }

    private static string? GetIdempotencyKey(HttpRequestData req)
    {
        if (!req.Headers.TryGetValues("X-Idempotency-Key", out var values))
        {
            return null;
        }

        var raw = values.FirstOrDefault();
        return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
    }

    private static async Task<HttpResponseData> BuildReplayResponseAsync(HttpRequestData req, int statusCode, string? body)
    {
        var response = req.CreateResponse((HttpStatusCode)statusCode);
        if (!string.IsNullOrWhiteSpace(body))
        {
            await response.WriteStringAsync(body);
        }

        return response;
    }
}

public class UpdateDeliveryPartnerStatusRequest
{
    public string Status { get; set; } = string.Empty;
}
