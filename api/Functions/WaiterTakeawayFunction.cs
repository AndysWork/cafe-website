using System.ComponentModel.DataAnnotations;
using System.Net;
using Cafe.Api.Helpers;
using Cafe.Api.Models;
using Cafe.Api.Repositories;
using Cafe.Api.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;

namespace Cafe.Api.Functions;

/// <summary>
/// Waiter-placed takeaway (parcel) orders: kitchen packs them, the waiter collects payment and hands them over.
/// </summary>
public class WaiterTakeawayFunction
{
    private readonly IOrderRepository _orderRepo;
    private readonly IMenuRepository _menuRepo;
    private readonly IOutletRepository _outletRepo;
    private readonly IUserRepository _userRepo;
    private readonly MongoService _mongo;
    private readonly AuthService _auth;
    private readonly OutboxService _outbox;
    private readonly ILogger _log;

    public WaiterTakeawayFunction(
        IOrderRepository orderRepo,
        IMenuRepository menuRepo,
        IOutletRepository outletRepo,
        IUserRepository userRepo,
        MongoService mongo,
        AuthService auth,
        OutboxService outbox,
        ILoggerFactory loggerFactory)
    {
        _orderRepo = orderRepo;
        _menuRepo = menuRepo;
        _outletRepo = outletRepo;
        _userRepo = userRepo;
        _mongo = mongo;
        _auth = auth;
        _outbox = outbox;
        _log = loggerFactory.CreateLogger<WaiterTakeawayFunction>();
    }

    [Function("CreateWaiterTakeawayOrder")]
    public async Task<HttpResponseData> CreateTakeawayOrder(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "takeaway/orders")] HttpRequestData req)
    {
        var (authorized, userId, _, authError) = await AuthorizationHelper.ValidateWaiterRole(req, _auth);
        if (!authorized) return authError!;
        if (string.IsNullOrWhiteSpace(userId))
            return await Error(req, HttpStatusCode.Unauthorized, "Authenticated staff identity is required");

        try
        {
            var (request, validationError) = await ValidationHelper.ValidateBody<CreateWaiterTakeawayOrderRequest>(req);
            if (validationError != null) return validationError;

            var (outletId, outletError) = await ResolveAccessibleOutletAsync(req, request.OutletId);
            if (outletError != null) return outletError;

            var outlet = await _outletRepo.GetOutletByIdAsync(outletId!, bypassCache: true);
            var orderingError = OutletHelper.GetOrderingUnavailableReason(outlet, "pickup");
            if (orderingError != null)
                return await Error(req, HttpStatusCode.Conflict, orderingError);

            var paymentTiming = TakeawayOrderRules.NormalizePaymentTiming(request.PaymentTiming);
            if (paymentTiming == null)
                return await Error(req, HttpStatusCode.BadRequest, "Payment timing must be upfront or handover");

            string? paymentMethod = null;
            if (paymentTiming == "upfront")
            {
                var (method, paymentError) = TakeawayOrderRules.ValidatePayment(request.PaymentMethod, request.UpiReference);
                if (paymentError != null) return await Error(req, HttpStatusCode.BadRequest, paymentError);
                paymentMethod = method;
            }

            var (orderItems, subtotal, itemError) = await BuildOrderItemsAsync(request.Items, outletId!);
            if (itemError != null) return await Error(req, HttpStatusCode.BadRequest, itemError);

            var now = MongoService.GetIstNow();
            var packagingCharge = TakeawayOrderRules.GetPackagingCharge(outlet);
            var waiterName = await ResolveWaiterNameAsync(userId, req);
            var customerName = request.CustomerName.Trim();
            var isPaid = paymentMethod != null;

            var order = new Order
            {
                Id = ObjectId.GenerateNewId().ToString(),
                OutletId = outletId,
                UserId = TakeawayOrderRules.GuestUserId,
                Username = customerName,
                CustomerName = customerName,
                PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim(),
                WaiterUserId = userId,
                WaiterName = waiterName,
                Items = orderItems,
                Subtotal = subtotal,
                Tax = 0,
                PlatformCharge = 0,
                DeliveryFee = 0,
                PackagingCharge = packagingCharge,
                Total = subtotal + packagingCharge,
                OrderType = "pickup",
                Channel = "shop",
                TakeawaySource = TakeawayOrderRules.WaiterSource,
                TakeawayPaymentTiming = paymentTiming,
                PaymentMethod = paymentMethod ?? "cash_at_counter",
                PaymentStatus = isPaid ? "paid" : "unpaid",
                UpiReference = paymentMethod == "upi-qr" ? request.UpiReference?.Trim() : null,
                UpiConfirmedBy = paymentMethod == "upi-qr" ? userId : null,
                UpiConfirmedAt = paymentMethod == "upi-qr" ? now : null,
                PaymentRecordedByUserId = isPaid ? userId : null,
                PaymentRecordedAt = isPaid ? now : null,
                KotNumber = await _orderRepo.GetNextDineInKotNumberAsync(outletId!),
                ApprovalStatus = "approved",
                ApprovedByUserId = userId,
                ApprovedAt = now,
                Notes = request.Notes?.Trim(),
                PreparationNotes = request.PreparationNotes?.Trim(),
                Status = "pending",
                CreatedAt = now,
                UpdatedAt = now
            };

            await _orderRepo.CreateOrderAsync(order);

            await _outbox.EnqueueAsync("OrderNotificationKitchen", "Order", order.Id!,
                new { OrderId = order.Id!, Total = order.Total });
            await _outbox.EnqueueAsync("ThermalKotPrint", "Order", order.Id!,
                new { OrderId = order.Id!, Trigger = "WaiterTakeawayOrder" });

            _log.LogInformation("Waiter takeaway order {OrderId} ({Token}) created by {UserId}",
                order.Id, order.KotNumber, userId);

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(new
            {
                message = $"Takeaway {order.KotNumber} sent to kitchen",
                order = ToResponse(order)
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to create waiter takeaway order");
            return await Error(req, HttpStatusCode.InternalServerError, "Failed to create takeaway order");
        }
    }

    [Function("GetWaiterTakeawayOrders")]
    public async Task<HttpResponseData> GetTakeawayOrders(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "takeaway/orders")] HttpRequestData req)
    {
        var (authorized, _, _, authError) = await AuthorizationHelper.ValidateWaiterRole(req, _auth);
        if (!authorized) return authError!;

        try
        {
            var (outletId, outletError) = await ResolveAccessibleOutletAsync(req, req.Query["outletId"]);
            if (outletError != null) return outletError;

            var orders = await _orderRepo.GetOrdersByStatusAsync(TakeawayOrderRules.ActiveStatuses, outletId);
            var takeaways = orders
                .Where(TakeawayOrderRules.IsWaiterTakeaway)
                .OrderBy(o => o.CreatedAt)
                .Select(ToResponse)
                .ToList();
            return await Ok(req, new { orders = takeaways });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to list waiter takeaway orders");
            return await Error(req, HttpStatusCode.InternalServerError, "Failed to load takeaway orders");
        }
    }

    [Function("RecordWaiterTakeawayPayment")]
    public async Task<HttpResponseData> RecordPayment(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "takeaway/orders/{orderId}/payment")] HttpRequestData req,
        string orderId)
    {
        var (authorized, userId, _, authError) = await AuthorizationHelper.ValidateWaiterRole(req, _auth);
        if (!authorized) return authError!;
        if (string.IsNullOrWhiteSpace(userId))
            return await Error(req, HttpStatusCode.Unauthorized, "Authenticated staff identity is required");

        try
        {
            var (request, validationError) = await ValidationHelper.ValidateBody<RecordTakeawayPaymentRequest>(req);
            if (validationError != null) return validationError;

            var (order, orderError) = await LoadOrderAsync(req, orderId);
            if (orderError != null) return orderError;

            var blockReason = TakeawayOrderRules.GetPaymentBlockReason(order!);
            if (blockReason != null) return await Error(req, HttpStatusCode.Conflict, blockReason);

            var (method, paymentError) = TakeawayOrderRules.ValidatePayment(request.PaymentMethod, request.UpiReference);
            if (paymentError != null) return await Error(req, HttpStatusCode.BadRequest, paymentError);

            var now = MongoService.GetIstNow();
            order!.PaymentMethod = method!;
            order.PaymentStatus = "paid";
            order.UpiReference = method == "upi-qr" ? request.UpiReference?.Trim() : null;
            order.UpiConfirmedBy = method == "upi-qr" ? userId : null;
            order.UpiConfirmedAt = method == "upi-qr" ? now : null;
            order.PaymentRecordedByUserId = userId;
            order.PaymentRecordedAt = now;
            order.UpdatedAt = now;

            if (!await _orderRepo.UpdateOrderAsync(order))
                return await Error(req, HttpStatusCode.Conflict, "Takeaway order changed; refresh and try again");

            return await Ok(req, new { message = "Payment recorded", order = ToResponse(order) });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to record payment for takeaway order {OrderId}", orderId);
            return await Error(req, HttpStatusCode.InternalServerError, "Failed to record payment");
        }
    }

    [Function("HandOverWaiterTakeawayOrder")]
    public async Task<HttpResponseData> HandOver(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "takeaway/orders/{orderId}/handover")] HttpRequestData req,
        string orderId)
    {
        var (authorized, userId, _, authError) = await AuthorizationHelper.ValidateWaiterRole(req, _auth);
        if (!authorized) return authError!;
        if (string.IsNullOrWhiteSpace(userId))
            return await Error(req, HttpStatusCode.Unauthorized, "Authenticated staff identity is required");

        try
        {
            var (order, orderError) = await LoadOrderAsync(req, orderId);
            if (orderError != null) return orderError;

            var blockReason = TakeawayOrderRules.GetHandoverBlockReason(order!);
            if (blockReason != null) return await Error(req, HttpStatusCode.Conflict, blockReason);

            var now = MongoService.GetIstNow();
            order!.Status = "delivered";
            order.CompletedAt = now;
            order.HandedOverAt = now;
            order.HandedOverByUserId = userId;
            order.HandedOverByName = await ResolveWaiterNameAsync(userId, req);
            order.UpdatedAt = now;

            if (!await _orderRepo.UpdateOrderAsync(order))
                return await Error(req, HttpStatusCode.Conflict, "Takeaway order changed; refresh and try again");

            return await Ok(req, new { message = $"Takeaway {order.KotNumber} handed over", order = ToResponse(order) });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to hand over takeaway order {OrderId}", orderId);
            return await Error(req, HttpStatusCode.InternalServerError, "Failed to hand over takeaway order");
        }
    }

    [Function("ReprintWaiterTakeawayKot")]
    public async Task<HttpResponseData> ReprintKot(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "takeaway/orders/{orderId}/kot/reprint")] HttpRequestData req,
        string orderId)
    {
        var (authorized, userId, _, authError) = await AuthorizationHelper.ValidateWaiterRole(req, _auth);
        if (!authorized) return authError!;

        try
        {
            var (order, orderError) = await LoadOrderAsync(req, orderId);
            if (orderError != null) return orderError;
            if (!TakeawayOrderRules.IsWaiterTakeaway(order) || !TakeawayOrderRules.ActiveStatuses.Contains(order!.Status))
                return await Error(req, HttpStatusCode.Conflict, "Only active takeaway KOTs can be reprinted");

            var now = MongoService.GetIstNow();
            order.KotReprintCount++;
            order.LastKotReprintedAt = now;
            order.LastKotReprintedByUserId = userId;
            order.UpdatedAt = now;
            await _orderRepo.UpdateOrderAsync(order);

            await _outbox.EnqueueAsync("ThermalKotPrint", "Order", orderId,
                new { OrderId = orderId, Trigger = "WaiterTakeawayReprint", IsDuplicate = true });
            return await Ok(req, new { message = "Duplicate KOT queued for printing", orderId, order.KotNumber });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to reprint takeaway KOT {OrderId}", orderId);
            return await Error(req, HttpStatusCode.InternalServerError, "Failed to reprint KOT");
        }
    }

    [Function("CancelWaiterTakeawayOrder")]
    public async Task<HttpResponseData> Cancel(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "takeaway/orders/{orderId}/cancel")] HttpRequestData req,
        string orderId)
    {
        var (authorized, userId, _, authError) = await AuthorizationHelper.ValidateWaiterRole(req, _auth);
        if (!authorized) return authError!;
        if (string.IsNullOrWhiteSpace(userId))
            return await Error(req, HttpStatusCode.Unauthorized, "Authenticated staff identity is required");

        try
        {
            var (request, validationError) = await ValidationHelper.ValidateBody<CancelDineInKotRequest>(req);
            if (validationError != null) return validationError;

            var (order, orderError) = await LoadOrderAsync(req, orderId);
            if (orderError != null) return orderError;

            var blockReason = TakeawayOrderRules.GetCancelBlockReason(order!);
            if (blockReason != null) return await Error(req, HttpStatusCode.Conflict, blockReason);

            var reason = request.Reason.Trim();
            var now = MongoService.GetIstNow();
            order!.Status = "cancelled";
            order.KotCancelledAt = now;
            order.KotCancelledByUserId = userId;
            order.KotCancellationReason = reason;
            order.UpdatedAt = now;

            if (!await _orderRepo.UpdateOrderAsync(order))
                return await Error(req, HttpStatusCode.Conflict, "Takeaway order changed; refresh and try again");

            await _outbox.EnqueueAsync("ThermalKotCancellationPrint", "Order", orderId,
                new { OrderId = orderId, Reason = reason });
            return await Ok(req, new { message = "Takeaway cancelled; kitchen cancellation slip queued", orderId });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to cancel takeaway order {OrderId}", orderId);
            return await Error(req, HttpStatusCode.InternalServerError, "Failed to cancel takeaway order");
        }
    }

    private async Task<(List<OrderItem> Items, decimal Subtotal, string? Error)> BuildOrderItemsAsync(
        List<OrderItemRequest> requestedItems, string outletId)
    {
        var menuItemIds = requestedItems.Select(i => i.MenuItemId).Distinct().ToList();
        var menuItems = await _menuRepo.GetMenuItemsByIdsAsync(menuItemIds, outletId);
        var menuMap = menuItems.Where(m => m.Id != null).ToDictionary(m => m.Id!, m => m);
        var categoryIds = menuItems
            .Where(m => !string.IsNullOrEmpty(m.CategoryId))
            .Select(m => m.CategoryId!)
            .Distinct()
            .ToList();
        var categories = categoryIds.Count > 0
            ? await _menuRepo.GetCategoriesByIdsAsync(categoryIds)
            : new Dictionary<string, string>();

        var orderItems = new List<OrderItem>();
        decimal subtotal = 0;
        foreach (var item in requestedItems)
        {
            if (!menuMap.TryGetValue(item.MenuItemId, out var menuItem))
                return (orderItems, 0, $"Menu item {item.MenuItemId} not found");
            if (!menuItem.IsAvailable)
                return (orderItems, 0, $"{menuItem.Name} is currently unavailable");

            // Takeaway is a shop sale, so it uses counter pricing.
            var baseUnitPrice = TakeawayUnitPrice(menuItem);
            string? variantName = null;
            decimal? variantPrice = null;
            if (!string.IsNullOrWhiteSpace(item.SelectedVariantName))
            {
                var variant = (menuItem.Variants ?? new List<MenuItemVariant>()).FirstOrDefault(v =>
                    v.VariantName.Equals(item.SelectedVariantName.Trim(), StringComparison.OrdinalIgnoreCase));
                if (variant == null)
                    return (orderItems, 0, $"Selected variant '{item.SelectedVariantName}' is not available for {menuItem.Name}");
                variantName = variant.VariantName;
                variantPrice = variant.Price;
                baseUnitPrice = variant.Price;
            }

            var addOns = new List<OrderItemAddOn>();
            foreach (var addOnName in item.SelectedAddOnNames ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(addOnName)) continue;
                var addOn = (menuItem.AddOns ?? new List<MenuItemAddOn>()).FirstOrDefault(a =>
                    a.IsActive && a.Name.Equals(addOnName.Trim(), StringComparison.OrdinalIgnoreCase));
                if (addOn == null)
                    return (orderItems, 0, $"Selected add-on '{addOnName}' is not available for {menuItem.Name}");
                addOns.Add(new OrderItemAddOn { Name = addOn.Name, Price = addOn.Price });
            }

            var addOnTotal = addOns.Sum(a => a.Price);
            var unitPrice = baseUnitPrice + addOnTotal;
            var itemTotal = unitPrice * item.Quantity;
            subtotal += itemTotal;
            categories.TryGetValue(menuItem.CategoryId ?? string.Empty, out var categoryName);

            orderItems.Add(new OrderItem
            {
                MenuItemId = menuItem.Id!,
                Name = menuItem.Name,
                Description = menuItem.Description,
                CategoryId = menuItem.CategoryId,
                CategoryName = categoryName,
                Quantity = item.Quantity,
                Price = unitPrice,
                BaseUnitPrice = baseUnitPrice,
                SelectedVariantName = variantName,
                SelectedVariantPrice = variantPrice,
                SelectedAddOns = addOns,
                AddOnTotal = addOnTotal,
                Total = itemTotal
            });
        }

        return (orderItems, subtotal, null);
    }

    public static decimal TakeawayUnitPrice(CafeMenuItem menuItem) =>
        menuItem.ShopSellingPrice > 0
            ? menuItem.ShopSellingPrice
            : menuItem.WebPrice > 0 ? menuItem.WebPrice : menuItem.OnlinePrice;

    private async Task<(Order? Order, HttpResponseData? Error)> LoadOrderAsync(HttpRequestData req, string orderId)
    {
        var (outletId, outletError) = await ResolveAccessibleOutletAsync(req, req.Query["outletId"]);
        if (outletError != null) return (null, outletError);

        var order = await _orderRepo.GetOrderByIdAsync(orderId);
        if (order == null || order.OutletId != outletId || !TakeawayOrderRules.IsWaiterTakeaway(order))
            return (null, await Error(req, HttpStatusCode.NotFound, "Waiter takeaway order not found"));
        return (order, null);
    }

    private async Task<(string? OutletId, HttpResponseData? Error)> ResolveAccessibleOutletAsync(
        HttpRequestData req, string? requestedOutletId)
    {
        var (hasAccess, outletId, accessError) = await OutletHelper.ValidateOutletAccess(
            req, _auth, _mongo, string.IsNullOrWhiteSpace(requestedOutletId) ? null : requestedOutletId.Trim());
        if (!hasAccess)
            return (null, await Error(req, HttpStatusCode.Forbidden, accessError ?? "Outlet access denied"));
        if (string.IsNullOrWhiteSpace(outletId))
            return (null, await Error(req, HttpStatusCode.BadRequest, "Select an outlet for takeaway orders"));
        return (outletId, null);
    }

    private async Task<string?> ResolveWaiterNameAsync(string? userId, HttpRequestData req)
    {
        var user = !string.IsNullOrWhiteSpace(userId) ? await _userRepo.GetUserByIdAsync(userId) : null;
        var name = string.Join(" ", new[] { user?.FirstName, user?.LastName }
            .Where(part => !string.IsNullOrWhiteSpace(part))).Trim();
        if (!string.IsNullOrWhiteSpace(name)) return name;
        if (!string.IsNullOrWhiteSpace(user?.Username)) return user.Username;
        var authHeader = req.Headers.TryGetValues("Authorization", out var values) ? values.FirstOrDefault() : null;
        return authHeader != null && authHeader.StartsWith("Bearer ")
            ? _auth.ValidateToken(authHeader["Bearer ".Length..].Trim())?.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
            : null;
    }

    private static object ToResponse(Order order) => new
    {
        orderId = order.Id,
        token = order.KotNumber,
        status = order.Status,
        customerName = order.CustomerName,
        phoneNumber = order.PhoneNumber,
        waiterUserId = order.WaiterUserId,
        waiterName = order.WaiterName,
        subtotal = order.Subtotal,
        packagingCharge = order.PackagingCharge,
        total = order.Total,
        paymentStatus = order.PaymentStatus,
        paymentMethod = order.PaymentMethod,
        paymentTiming = order.TakeawayPaymentTiming,
        upiReference = order.UpiReference,
        notes = order.Notes,
        preparationNotes = order.PreparationNotes,
        createdAt = order.CreatedAt,
        kitchenReadyAt = order.KitchenReadyAt,
        handedOverAt = order.HandedOverAt,
        items = order.Items.Select(i => new
        {
            name = i.Name,
            quantity = i.Quantity,
            unitPrice = i.Price,
            total = i.Total,
            variant = i.SelectedVariantName,
            addOns = (i.SelectedAddOns ?? new List<OrderItemAddOn>()).Select(a => a.Name).ToList()
        }).ToList()
    };

    private static async Task<HttpResponseData> Ok(HttpRequestData req, object data)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(data);
        return response;
    }

    private static async Task<HttpResponseData> Error(HttpRequestData req, HttpStatusCode status, string message)
    {
        var response = req.CreateResponse(status);
        await response.WriteAsJsonAsync(new { error = message });
        return response;
    }
}

public class CreateWaiterTakeawayOrderRequest
{
    public string? OutletId { get; set; }

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string CustomerName { get; set; } = string.Empty;

    [StringLength(20)]
    public string? PhoneNumber { get; set; }

    [Required]
    [MinLength(1, ErrorMessage = "Order must contain at least one item")]
    public List<OrderItemRequest> Items { get; set; } = new();

    [StringLength(500)]
    public string? Notes { get; set; }

    [StringLength(500)]
    public string? PreparationNotes { get; set; }

    [StringLength(20)]
    public string? PaymentTiming { get; set; }

    [StringLength(30)]
    public string? PaymentMethod { get; set; }

    [StringLength(100)]
    public string? UpiReference { get; set; }
}

public class RecordTakeawayPaymentRequest
{
    [Required]
    [StringLength(30)]
    public string PaymentMethod { get; set; } = string.Empty;

    [StringLength(100)]
    public string? UpiReference { get; set; }
}
