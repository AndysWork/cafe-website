using System.Net;
using System.Text.Json;
using Cafe.Api.Helpers;
using Cafe.Api.Models;
using Cafe.Api.Repositories;
using Cafe.Api.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Cafe.Api.Functions;

public sealed class PhoneDeliveryOrderFunction
{
    private readonly IOrderRepository _orders;
    private readonly IMenuRepository _menu;
    private readonly IUserRepository _users;
    private readonly IStaffRepository _staff;
    private readonly IOutletRepository _outlets;
    private readonly IOperationsRepository _operations;
    private readonly IPhoneDeliveryOrderRepository _phone;
    private readonly DeliveryRoutingService? _deliveryRoutingService;
    private readonly AuthService _auth;
    private readonly OutboxService _outbox;
    private readonly ILogger _log;

    public PhoneDeliveryOrderFunction(IOrderRepository orders, IMenuRepository menu, IUserRepository users,
        IStaffRepository staff, IOutletRepository outlets, IOperationsRepository operations,
        IPhoneDeliveryOrderRepository phone, AuthService auth, OutboxService outbox, ILoggerFactory loggerFactory,
        DeliveryRoutingService? deliveryRoutingService = null)
    {
        _orders = orders;
        _menu = menu;
        _users = users;
        _staff = staff;
        _outlets = outlets;
        _operations = operations;
        _phone = phone;
        _deliveryRoutingService = deliveryRoutingService;
        _auth = auth;
        _outbox = outbox;
        _log = loggerFactory.CreateLogger<PhoneDeliveryOrderFunction>();
    }

    [Function("CreatePhoneDeliveryOrder")]
    public Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "phone-orders")] HttpRequestData req) =>
        PriceOrCreate(req, false);

    [Function("QuotePhoneDeliveryOrder")]
    public Task<HttpResponseData> Quote(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "phone-orders/quote")] HttpRequestData req) =>
        PriceOrCreate(req, true);

    private async Task<HttpResponseData> PriceOrCreate(HttpRequestData req, bool quoteOnly)
    {
        var (authorized, userId, _, authError) = await AuthorizationHelper.ValidateCashierRole(req, _auth);
        if (!authorized) return authError!;
        if (string.IsNullOrWhiteSpace(userId))
            return await Error(req, HttpStatusCode.Unauthorized, "Authenticated staff identity is required");
        try
        {
            var (request, validationError) = await ValidationHelper.ValidateBody<CreatePhoneDeliveryOrderRequest>(req);
            if (validationError != null) return validationError;
            request = request with
            {
                DeliveryLocationUrl = WebUtility.HtmlDecode(request.DeliveryLocationUrl),
                PaymentMethod = request.PaymentMethod.Trim().ToLowerInvariant()
            };
            var (outletId, outletError) = await ResolveOutlet(req, request.OutletId);
            if (outletError != null) return outletError;
            if (!PhoneDeliveryOrderRules.IsValidMapsUrl(request.DeliveryLocationUrl))
                return await Error(req, HttpStatusCode.BadRequest, "A valid HTTPS Google Maps location URL is required");
            if (!quoteOnly && request.PaymentMethod == "upi-qr" && string.IsNullOrWhiteSpace(request.UpiReference))
                return await Error(req, HttpStatusCode.BadRequest, "UPI reference is required for prepaid UPI");
            var hasManualFee = request.ManualDeliveryFee.HasValue;
            var hasManualDistance = request.ManualApproximateDistanceKm.HasValue;
            if (hasManualFee != hasManualDistance)
            {
                return await Error(req, HttpStatusCode.BadRequest,
                    "Provide both manual delivery fee and manual distance to override automatic calculation");
            }

            string? orderId = null;
            string? requestHash = null;
            if (!quoteOnly)
            {
                var key = req.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.FirstOrDefault() : null;
                if (string.IsNullOrWhiteSpace(key) || key.Length > 128 || key.Any(char.IsControl))
                    return await Error(req, HttpStatusCode.BadRequest, "Idempotency-Key header is required (1-128 characters)");
                orderId = PhoneDeliveryOrderRules.IdempotentOrderId(userId, outletId!, key);
                requestHash = IdempotencyService.ComputeRequestHash(JsonSerializer.Serialize(request));
                var existing = await _orders.GetOrderByIdAsync(orderId);
                if (existing != null)
                    return await Replay(req, existing, requestHash);
            }

            var outlet = await _outlets.GetOutletByIdAsync(outletId!, bypassCache: true);
            var unavailable = OutletHelper.GetOrderingUnavailableReason(outlet, "delivery");
            if (unavailable != null) return await Error(req, HttpStatusCode.Conflict, unavailable);
            var (items, subtotal, itemError) = await ShopOrderItemBuilder.BuildAsync(_menu, request.Items, outletId!);
            if (itemError != null) return await Error(req, HttpStatusCode.BadRequest, itemError);
            var (deliveryFee, estimatedMinutes, routeDistanceKm, feeZoneName, manualOverrideApplied) = await ResolveDeliveryFeeAsync(
                outletId!,
                subtotal,
                request.DeliveryAddress,
                request.DeliveryLocationUrl,
                request.ManualDeliveryFee,
                request.ManualApproximateDistanceKm);
            var total = subtotal + deliveryFee;
            if (quoteOnly)
                return await Json(req, HttpStatusCode.OK, new
                {
                    subtotal,
                    tax = 0m,
                    platformCharge = 0m,
                    deliveryFee,
                    total,
                    estimatedMinutes,
                    approximateDistanceKm = routeDistanceKm,
                    zone = feeZoneName,
                    manualOverrideApplied
                });
            if (request.ExpectedTotal.HasValue && request.ExpectedTotal.Value != total)
                return await Error(req, HttpStatusCode.Conflict, "Order total changed; refresh the quote before recording payment");

            var staff = await _users.GetUserByIdAsync(userId);
            var staffName = string.Join(" ", new[] { staff?.FirstName, staff?.LastName }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
            var now = MongoService.GetIstNow();
            var paid = request.PaymentMethod != "cod";
            var order = new Order
            {
                Id = orderId, OutletId = outletId, OrderSource = "phone", OrderType = "delivery", Channel = "shop",
                UserId = PhoneDeliveryOrderRules.GuestUserId,
                Username = request.CustomerName.Trim(), CustomerName = request.CustomerName.Trim(),
                PhoneNumber = request.PhoneNumber.Trim(), DeliveryAddress = request.DeliveryAddress.Trim(),
                DeliveryLocationUrl = request.DeliveryLocationUrl.Trim(),
                RecordedByUserId = userId, RecordedByName = string.IsNullOrWhiteSpace(staffName) ? staff?.Username : staffName,
                Items = items, Subtotal = subtotal, Tax = 0, PlatformCharge = 0, DeliveryFee = deliveryFee, Total = total,
                PaymentMethod = request.PaymentMethod, PaymentStatus = paid ? "paid" : "pending",
                PaymentRecordedByUserId = paid ? userId : null, PaymentRecordedAt = paid ? now : null,
                UpiReference = request.PaymentMethod == "upi-qr" ? request.UpiReference?.Trim() : null,
                UpiConfirmedBy = request.PaymentMethod == "upi-qr" ? userId : null,
                UpiConfirmedAt = request.PaymentMethod == "upi-qr" ? now : null,
                PreparationNotes = request.PreparationNotes?.Trim(), Notes = request.Notes?.Trim(),
                KotNumber = await _orders.GetNextDineInKotNumberAsync(outletId!),
                ApprovalStatus = "approved", ApprovedByUserId = userId, ApprovedAt = now,
                Status = "pending", CreatedAt = now, UpdatedAt = now,
                DeliveryDistanceKm = routeDistanceKm,
                DeliveryEtaMinutes = estimatedMinutes,
                ManualDeliveryFeeOverride = manualOverrideApplied ? deliveryFee : null,
                ManualDeliveryDistanceKmOverride = manualOverrideApplied ? routeDistanceKm : null,
                PhoneTrackingToken = PhoneDeliveryOrderRules.NewTrackingToken(), PhoneRequestHash = requestHash
            };
            try
            {
                await _orders.CreateOrderAsync(order);
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                var existing = await _orders.GetOrderByIdAsync(orderId!);
                if (existing == null) throw;
                return await Replay(req, existing, requestHash!);
            }
            return await Created(req, order);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Phone delivery operation failed");
            return await Error(req, HttpStatusCode.InternalServerError, "Unable to process phone delivery order; retry creation with the same Idempotency-Key");
        }
    }

    private async Task<(decimal deliveryFee, int? estimatedMinutes, double? distanceKm, string? zoneName, bool manualOverrideApplied)> ResolveDeliveryFeeAsync(
        string outletId,
        decimal subtotal,
        string deliveryAddress,
        string deliveryLocationUrl,
        decimal? manualDeliveryFee,
        double? manualDistanceKm)
    {
        var zones = await _operations.GetActiveDeliveryZonesAsync(outletId);
        zones ??= new List<DeliveryZone>();
        if (manualDeliveryFee.HasValue && manualDistanceKm.HasValue)
        {
            var roundedDistance = Math.Round(manualDistanceKm.Value, 1, MidpointRounding.AwayFromZero);
            var roundedFee = Math.Round(manualDeliveryFee.Value, 2, MidpointRounding.AwayFromZero);
            var zoneForDistance = zones.Count > 0
                ? DeliveryFeeCalculator.SelectZoneForDistance(zones, roundedDistance).Zone
                : null;
            return (roundedFee, zoneForDistance?.EstimatedMinutes, roundedDistance, zoneForDistance?.ZoneName, true);
        }

        double? routeDistanceKm = null;
        int? routeEtaMinutes = null;

        if (_deliveryRoutingService != null)
        {
            var route = await _deliveryRoutingService.ResolveDistanceForDeliveryFeeAsync(outletId, deliveryAddress, deliveryLocationUrl);
            routeDistanceKm = route.distanceKm;
            routeEtaMinutes = route.etaMinutes;
        }

        if (zones.Count == 0)
        {
            var fallbackFee = await _operations.CalculateDeliveryFeeAsync(outletId, subtotal);
            return (fallbackFee, routeEtaMinutes, routeDistanceKm, null, false);
        }

        var (zone, resolvedDistanceKm, _) = DeliveryFeeCalculator.SelectZoneForDistance(zones, routeDistanceKm);
        var fee = DeliveryFeeCalculator.ResolveDeliveryFee(subtotal, zone);
        return (fee, routeEtaMinutes ?? zone.EstimatedMinutes, resolvedDistanceKm, zone.ZoneName, false);
    }

    [Function("GetPhoneDeliveryOrders")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "phone-orders")] HttpRequestData req)
    {
        var (authorized, _, _, authError) = await AuthorizationHelper.ValidateCashierRole(req, _auth);
        if (!authorized) return authError!;
        var (outletId, outletError) = await ResolveOutlet(req, req.Query["outletId"]);
        if (outletError != null) return outletError;
        var orders = await _phone.GetPhoneDeliveryOrdersAsync(outletId!, req.Query["status"]);
        return await Json(req, HttpStatusCode.OK, new { orders = orders.Select(ToResponse).ToList() });
    }

    [Function("GetPublicPhoneDeliveryTracking")]
    public async Task<HttpResponseData> Track(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "public/phone-orders/{id}/tracking")] HttpRequestData req, string id)
    {
        var token = req.Query["token"];
        if (!ObjectId.TryParse(id, out _) || token?.Length != 43)
            return await Error(req, HttpStatusCode.NotFound, "Tracking link not found");
        try
        {
            var order = await _orders.GetOrderByIdAsync(id);
            if (order == null || !PhoneDeliveryOrderRules.IsPhoneOrder(order)
                || !PhoneDeliveryOrderRules.IsValidTrackingToken(order.PhoneTrackingToken, token))
                return await Error(req, HttpStatusCode.NotFound, "Tracking link not found");
            return await Json(req, HttpStatusCode.OK, PhoneDeliveryOrderRules.PublicTracking(order));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Phone tracking lookup failed");
            return await Error(req, HttpStatusCode.ServiceUnavailable, "Tracking is temporarily unavailable");
        }
    }

    private async Task<HttpResponseData> Replay(HttpRequestData req, Order order, string requestHash)
    {
        if (!PhoneDeliveryOrderRules.IsPhoneOrder(order) || order.PhoneRequestHash != requestHash)
            return await Error(req, HttpStatusCode.Conflict, "Idempotency key reused with a different request");
        return await Created(req, order);
    }

    private async Task<HttpResponseData> Created(HttpRequestData req, Order order)
    {
        if (!order.PhoneEffectsQueued)
        {
            await _outbox.EnqueueOnceAsync("OrderNotificationKitchen", order.Id!, new { OrderId = order.Id, order.Total });
            await _outbox.EnqueueOnceAsync("ThermalKotPrint", order.Id!, new { OrderId = order.Id, Trigger = "PhoneDeliveryOrder" });
            await _phone.MarkPhoneEffectsQueuedAsync(order.Id!);
        }
        var response = await Json(req, HttpStatusCode.Created, new { order = ToResponse(order), message = "Phone delivery order sent to kitchen" });
        response.Headers.Add("Location", $"/api/phone-orders?outletId={order.OutletId}");
        return response;
    }

    private static OrderResponse ToResponse(Order order)
    {
        var response = OrderFunction.MapToOrderResponse(order);
        response.TrackingPath = $"/track-order/{order.Id}?token={order.PhoneTrackingToken}";
        return response;
    }

    private async Task<(string? OutletId, HttpResponseData? Error)> ResolveOutlet(HttpRequestData req, string? requested)
    {
        var (access, outletId, error) = await OutletHelper.ValidateOutletAccess(req, _auth, _users, _staff, _outlets,
            string.IsNullOrWhiteSpace(requested) ? null : requested.Trim());
        if (!access) return (null, await Error(req, HttpStatusCode.Forbidden, error ?? "Outlet access denied"));
        return string.IsNullOrWhiteSpace(outletId)
            ? (null, await Error(req, HttpStatusCode.BadRequest, "Select an outlet for phone orders"))
            : (outletId, null);
    }

    private static Task<HttpResponseData> Error(HttpRequestData req, HttpStatusCode status, string message) =>
        Json(req, status, new { error = message });

    private static async Task<HttpResponseData> Json(HttpRequestData req, HttpStatusCode status, object body)
    {
        var response = req.CreateResponse(status);
        response.Headers.Add("Cache-Control", "no-store, private");
        response.Headers.Add("Referrer-Policy", "no-referrer");
        await response.WriteAsJsonAsync(body);
        return response;
    }
}
