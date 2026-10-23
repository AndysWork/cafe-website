using System.Net;
using Cafe.Api.Helpers;
using Cafe.Api.Models;
using Cafe.Api.Repositories;
using Cafe.Api.Services;
using System.ComponentModel.DataAnnotations;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Cafe.Api.Functions;

public class DineInOrderWorkflowFunction
{
    private readonly IOrderRepository _orderRepo;
    private readonly IMenuRepository _menuRepo;
    private readonly IOutletRepository _outletRepo;
    private readonly AuthService _auth;
    private readonly OutboxService _outbox;
    private readonly ILogger _log;

    public DineInOrderWorkflowFunction(
        IOrderRepository orderRepo,
        IMenuRepository menuRepo,
        IOutletRepository outletRepo,
        AuthService auth,
        OutboxService outbox,
        ILoggerFactory loggerFactory)
    {
        _orderRepo = orderRepo;
        _menuRepo = menuRepo;
        _outletRepo = outletRepo;
        _auth = auth;
        _outbox = outbox;
        _log = loggerFactory.CreateLogger<DineInOrderWorkflowFunction>();
    }

    [Function("GetQrDineInOrdersAwaitingApproval")]
    public async Task<HttpResponseData> GetAwaitingApproval(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "dine-in/orders/qr-pending")] HttpRequestData req)
    {
        var (authorized, _, _, error) = await AuthorizationHelper.ValidateWaiterRole(req, _auth);
        if (!authorized) return error!;

        try
        {
            var outletId = await ResolveOutletIdAsync(req, req.Query["outletId"]);
            if (string.IsNullOrWhiteSpace(outletId))
                return await Error(req, HttpStatusCode.BadRequest, "Unable to resolve outlet");

            var orders = await _orderRepo.GetOrdersByStatusAsync(
                new[] { "awaiting_waiter_approval" }, outletId);
            var pending = orders
                .Where(o => o.OrderType == "dine-in"
                    && o.DineInOrderSource == "qr"
                    && o.ApprovalStatus == "pending")
                .OrderBy(o => o.CreatedAt)
                .Select(o => new
                {
                    orderId = o.Id,
                    sessionId = o.DineInSessionId,
                    tableNumber = o.TableNumber,
                    roundNumber = o.RoundNumber,
                    createdAt = o.CreatedAt,
                    subtotal = o.Subtotal,
                    notes = o.Notes,
                    preparationNotes = o.PreparationNotes,
                    kotNumber = o.KotNumber,
                    items = o.Items.Select(i => new
                    {
                        menuItemId = i.MenuItemId,
                        name = i.Name,
                        quantity = i.Quantity,
                        unitPrice = i.Price,
                        total = i.Total,
                        variant = i.SelectedVariantName,
                        addOns = (i.SelectedAddOns ?? new List<OrderItemAddOn>()).Select(a => a.Name).ToList()
                    }).ToList()
                })
                .ToList();

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { orders = pending });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to list QR dine-in orders awaiting waiter approval");
            return await Error(req, HttpStatusCode.InternalServerError, "Failed to load pending QR orders");
        }
    }

    [Function("EditPendingQrDineInOrder")]
    public async Task<HttpResponseData> EditPendingQrOrder(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "dine-in/orders/{orderId}/edit")] HttpRequestData req,
        string orderId)
    {
        var (authorized, userId, _, error) = await AuthorizationHelper.ValidateWaiterRole(req, _auth);
        if (!authorized) return error!;
        if (string.IsNullOrWhiteSpace(userId))
            return await Error(req, HttpStatusCode.Unauthorized, "Authenticated staff identity is required");

        try
        {
            var (request, validationError) = await ValidationHelper.ValidateBody<EditQrDineInOrderRequest>(req);
            if (validationError != null) return validationError;

            var outletId = await ResolveOutletIdAsync(req, req.Query["outletId"]);
            if (string.IsNullOrWhiteSpace(outletId))
                return await Error(req, HttpStatusCode.BadRequest, "Unable to resolve outlet");

            var order = await GetPendingQrOrderAsync(orderId, outletId);
            if (order == null)
                return await Error(req, HttpStatusCode.NotFound, "Pending QR dine-in order not found");

            var menuItemIds = request.Items.Select(item => item.MenuItemId).Distinct().ToList();
            var menuItems = await _menuRepo.GetMenuItemsByIdsAsync(menuItemIds, outletId);
            var menuMap = menuItems.Where(item => !string.IsNullOrWhiteSpace(item.Id))
                .ToDictionary(item => item.Id, item => item);
            if (menuMap.Count != menuItemIds.Count)
                return await Error(req, HttpStatusCode.BadRequest, "One or more menu items are unavailable");

            var categoryIds = menuItems
                .Where(item => !string.IsNullOrWhiteSpace(item.CategoryId))
                .Select(item => item.CategoryId!)
                .Distinct()
                .ToList();
            var categories = categoryIds.Count > 0
                ? await _menuRepo.GetCategoriesByIdsAsync(categoryIds)
                : new Dictionary<string, string>();

            var updatedItems = new List<OrderItem>();
            decimal subtotal = 0;
            foreach (var requestedItem in request.Items)
            {
                var menuItem = menuMap[requestedItem.MenuItemId];
                if (!menuItem.IsAvailable)
                    return await Error(req, HttpStatusCode.BadRequest, $"{menuItem.Name} is currently unavailable");

                categories.TryGetValue(menuItem.CategoryId ?? string.Empty, out var categoryName);
                var baseUnitPrice = menuItem.DineInPrice > 0
                    ? menuItem.DineInPrice
                    : menuItem.WebPrice > 0 ? menuItem.WebPrice : menuItem.ShopSellingPrice;

                string? variantName = null;
                decimal? variantPrice = null;
                if (!string.IsNullOrWhiteSpace(requestedItem.SelectedVariantName))
                {
                    var variant = menuItem.Variants?.FirstOrDefault(item =>
                        item.VariantName.Equals(requestedItem.SelectedVariantName, StringComparison.OrdinalIgnoreCase));
                    if (variant == null)
                        return await Error(req, HttpStatusCode.BadRequest, $"Variant is unavailable for {menuItem.Name}");
                    variantName = variant.VariantName;
                    variantPrice = variant.Price;
                    baseUnitPrice = variant.Price;
                }

                var addOns = new List<OrderItemAddOn>();
                decimal addOnTotal = 0;
                var selectedAddOnNames = requestedItem.SelectedAddOnNames ?? new List<string>();
                if (selectedAddOnNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() != selectedAddOnNames.Count)
                    return await Error(req, HttpStatusCode.BadRequest, $"An add-on was selected more than once for {menuItem.Name}");

                foreach (var addOnName in selectedAddOnNames)
                {
                    var addOn = menuItem.AddOns?.FirstOrDefault(item =>
                        item.IsActive && item.Name.Equals(addOnName, StringComparison.OrdinalIgnoreCase));
                    if (addOn == null)
                        return await Error(req, HttpStatusCode.BadRequest, $"Add-on is unavailable for {menuItem.Name}");
                    addOns.Add(new OrderItemAddOn { Name = addOn.Name, Price = addOn.Price });
                    addOnTotal += addOn.Price;
                }

                var unitPrice = baseUnitPrice + addOnTotal;
                var itemTotal = unitPrice * requestedItem.Quantity;
                subtotal += itemTotal;
                updatedItems.Add(new OrderItem
                {
                    MenuItemId = menuItem.Id,
                    Name = menuItem.Name,
                    Description = menuItem.Description,
                    CategoryId = menuItem.CategoryId,
                    CategoryName = categoryName,
                    Quantity = requestedItem.Quantity,
                    Price = unitPrice,
                    BaseUnitPrice = baseUnitPrice,
                    SelectedVariantName = variantName,
                    SelectedVariantPrice = variantPrice,
                    SelectedAddOns = addOns,
                    AddOnTotal = addOnTotal,
                    Total = itemTotal
                });
            }

            order.Items = updatedItems;
            order.Subtotal = subtotal;
            order.Tax = 0;
            order.Total = subtotal;
            order.Notes = request.Notes?.Trim();
            order.PreparationNotes = request.PreparationNotes?.Trim();
            order.UpdatedAt = MongoService.GetIstNow();

            if (!await _orderRepo.TryEditPendingQrDineInOrderAsync(order))
                return await Error(req, HttpStatusCode.Conflict, "Order was already reviewed; refresh and try again");

            await AddSessionEventAsync(
                order, "qr_order_edited", $"QR order round #{order.RoundNumber} edited by waiter", userId);
            await RecalculateSessionTotalsAsync(order.DineInSessionId);

            return await Ok(req, new { message = "QR order updated", orderId, subtotal });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to edit pending QR dine-in order {OrderId}", orderId);
            return await Error(req, HttpStatusCode.InternalServerError, "Failed to update QR order");
        }
    }

    [Function("ApproveQrDineInOrder")]
    public async Task<HttpResponseData> Approve(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "dine-in/orders/{orderId}/approve")] HttpRequestData req,
        string orderId)
    {
        var (authorized, userId, _, error) = await AuthorizationHelper.ValidateWaiterRole(req, _auth);
        if (!authorized) return error!;
        if (string.IsNullOrWhiteSpace(userId))
            return await Error(req, HttpStatusCode.Unauthorized, "Authenticated staff identity is required");

        try
        {
            var outletId = await ResolveOutletIdAsync(req, req.Query["outletId"]);
            if (string.IsNullOrWhiteSpace(outletId))
                return await Error(req, HttpStatusCode.BadRequest, "Unable to resolve outlet");

            var order = await GetPendingQrOrderAsync(orderId, outletId);
            if (order == null)
                return await Error(req, HttpStatusCode.NotFound, "Pending QR dine-in order not found");

            var kotNumber = await _orderRepo.GetNextDineInKotNumberAsync(outletId);
            if (!await _orderRepo.TryReviewQrDineInOrderAsync(
                    orderId, outletId, "approved", userId, null, kotNumber))
                return await Error(req, HttpStatusCode.Conflict, "Order was already reviewed");

            var now = MongoService.GetIstNow();
            order.Status = "pending";
            order.ApprovalStatus = "approved";
            order.ApprovedByUserId = userId;
            order.ApprovedAt = now;
            order.KotNumber = kotNumber;
            order.UpdatedAt = now;

            await AddSessionEventAsync(
                order,
                "qr_order_approved",
                $"QR order round #{order.RoundNumber} approved and sent to kitchen",
                userId);

            await _outbox.EnqueueAsync("OrderNotificationKitchen", "Order", order.Id!,
                new { OrderId = order.Id!, Total = order.Total, TableNumber = order.TableNumber, RoundNumber = order.RoundNumber });
            await _outbox.EnqueueAsync("ThermalKotPrint", "Order", order.Id!,
                new { OrderId = order.Id!, Trigger = "WaiterApprovedQrOrder" });

            return await Ok(req, new { message = "QR order approved and sent to kitchen", orderId });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to approve QR dine-in order {OrderId}", orderId);
            return await Error(req, HttpStatusCode.InternalServerError, "Failed to approve QR order");
        }
    }

    [Function("RejectQrDineInOrder")]
    public async Task<HttpResponseData> Reject(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "dine-in/orders/{orderId}/reject")] HttpRequestData req,
        string orderId)
    {
        var (authorized, userId, _, error) = await AuthorizationHelper.ValidateWaiterRole(req, _auth);
        if (!authorized) return error!;
        if (string.IsNullOrWhiteSpace(userId))
            return await Error(req, HttpStatusCode.Unauthorized, "Authenticated staff identity is required");

        try
        {
            var (request, validationError) = await ValidationHelper.ValidateBody<RejectQrDineInOrderRequest>(req);
            if (validationError != null) return validationError;

            var outletId = await ResolveOutletIdAsync(req, req.Query["outletId"]);
            if (string.IsNullOrWhiteSpace(outletId))
                return await Error(req, HttpStatusCode.BadRequest, "Unable to resolve outlet");

            var order = await GetPendingQrOrderAsync(orderId, outletId);
            if (order == null)
                return await Error(req, HttpStatusCode.NotFound, "Pending QR dine-in order not found");

            if (!await _orderRepo.TryReviewQrDineInOrderAsync(
                    orderId, outletId, "rejected", userId, request.Reason.Trim(), null))
                return await Error(req, HttpStatusCode.Conflict, "Order was already reviewed");

            var now = MongoService.GetIstNow();
            order.Status = "cancelled";
            order.ApprovalStatus = "rejected";
            order.RejectedByUserId = userId;
            order.RejectedAt = now;
            order.RejectionReason = request.Reason.Trim();
            order.UpdatedAt = now;

            await AddSessionEventAsync(
                order,
                "qr_order_rejected",
                $"QR order round #{order.RoundNumber} rejected: {order.RejectionReason}",
                userId);
            await RecalculateSessionTotalsAsync(order.DineInSessionId);

            return await Ok(req, new { message = "QR order rejected", orderId });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to reject QR dine-in order {OrderId}", orderId);
            return await Error(req, HttpStatusCode.InternalServerError, "Failed to reject QR order");
        }
    }

    [Function("GetDineInSessionOrders")]
    public async Task<HttpResponseData> GetSessionOrders(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "dine-in/session/{sessionId}/orders")] HttpRequestData req,
        string sessionId)
    {
        var (authorized, _, _, error) = await AuthorizationHelper.ValidateWaiterRole(req, _auth);
        if (!authorized) return error!;

        try
        {
            var outletId = await ResolveOutletIdAsync(req, req.Query["outletId"]);
            if (string.IsNullOrWhiteSpace(outletId))
                return await Error(req, HttpStatusCode.BadRequest, "Unable to resolve outlet");
            var session = await _orderRepo.GetDineInSessionByIdAsync(sessionId);
            if (session == null || session.OutletId != outletId)
                return await Error(req, HttpStatusCode.NotFound, "Dine-in session not found");

            var orders = new List<object>();
            foreach (var orderId in session.OrderIds)
            {
                var order = await _orderRepo.GetOrderByIdAsync(orderId);
                if (order == null || order.OrderType != "dine-in" || order.OutletId != outletId) continue;
                orders.Add(new
                {
                    orderId = order.Id,
                    tableNumber = order.TableNumber,
                    roundNumber = order.RoundNumber,
                    status = order.Status,
                    kotNumber = order.KotNumber,
                    kotReprintCount = order.KotReprintCount,
                    createdAt = order.CreatedAt,
                    total = order.Total,
                    itemCount = order.Items.Sum(item => item.Quantity),
                    items = order.Items.Select((item, index) => new
                    {
                        index,
                        item.Name,
                        item.Quantity,
                        kitchenStatus = ResolveDineInItemStatus(order, item.KitchenStatus)
                    }),
                    canCancelKot = order.KotNumber != null && (order.Status == "pending" || order.Status == "confirmed")
                });
            }

            return await Ok(req, new { orders });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to load orders for dine-in session {SessionId}", sessionId);
            return await Error(req, HttpStatusCode.InternalServerError, "Failed to load table orders");
        }
    }

    [Function("ReprintDineInKot")]
    public async Task<HttpResponseData> ReprintKot(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "dine-in/orders/{orderId}/kot/reprint")] HttpRequestData req,
        string orderId)
    {
        var (authorized, userId, _, error) = await AuthorizationHelper.ValidateWaiterRole(req, _auth);
        if (!authorized) return error!;
        if (string.IsNullOrWhiteSpace(userId))
            return await Error(req, HttpStatusCode.Unauthorized, "Authenticated staff identity is required");

        try
        {
            var outletId = await ResolveOutletIdAsync(req, req.Query["outletId"]);
            if (string.IsNullOrWhiteSpace(outletId))
                return await Error(req, HttpStatusCode.BadRequest, "Unable to resolve outlet");
            var order = await GetDineInOrderAsync(orderId, outletId);
            if (order == null || string.IsNullOrWhiteSpace(order.KotNumber))
                return await Error(req, HttpStatusCode.NotFound, "Printed dine-in KOT not found");
            if (order.Status == "cancelled")
                return await Error(req, HttpStatusCode.Conflict, "Cancelled KOTs cannot be reprinted");
            if (!await _orderRepo.TryQueueDineInKotReprintAsync(orderId, outletId, userId))
                return await Error(req, HttpStatusCode.Conflict, "KOT changed state; refresh and try again");

            await _outbox.EnqueueAsync("ThermalKotPrint", "Order", orderId,
                new { OrderId = orderId, Trigger = "WaiterDuplicateReprint", IsDuplicate = true });
            await AddSessionEventAsync(order, "kot_reprinted", $"KOT {order.KotNumber} reprint requested", userId);
            return await Ok(req, new { message = "Duplicate KOT queued for printing", orderId, order.KotNumber });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to queue KOT reprint for order {OrderId}", orderId);
            return await Error(req, HttpStatusCode.InternalServerError, "Failed to queue KOT reprint");
        }
    }

    [Function("CancelDineInKot")]
    public async Task<HttpResponseData> CancelKot(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "dine-in/orders/{orderId}/kot/cancel")] HttpRequestData req,
        string orderId)
    {
        var (authorized, userId, _, error) = await AuthorizationHelper.ValidateWaiterRole(req, _auth);
        if (!authorized) return error!;
        if (string.IsNullOrWhiteSpace(userId))
            return await Error(req, HttpStatusCode.Unauthorized, "Authenticated staff identity is required");

        try
        {
            var (request, validationError) = await ValidationHelper.ValidateBody<CancelDineInKotRequest>(req);
            if (validationError != null) return validationError;
            var outletId = await ResolveOutletIdAsync(req, req.Query["outletId"]);
            if (string.IsNullOrWhiteSpace(outletId))
                return await Error(req, HttpStatusCode.BadRequest, "Unable to resolve outlet");
            var order = await GetDineInOrderAsync(orderId, outletId);
            if (order == null || string.IsNullOrWhiteSpace(order.KotNumber))
                return await Error(req, HttpStatusCode.NotFound, "Dine-in KOT not found");

            const string expectedStatus = "pending";
            var reason = request.Reason.Trim();
            if (!await _orderRepo.TryCancelDineInKotAsync(
                    orderId, outletId, new[] { expectedStatus, "confirmed" }, userId, reason))
                return await Error(req, HttpStatusCode.Conflict, "KOT can only be cancelled before kitchen preparation starts");

            order.Status = "cancelled";
            order.KotCancelledAt = MongoService.GetIstNow();
            order.KotCancelledByUserId = userId;
            order.KotCancellationReason = reason;
            order.UpdatedAt = order.KotCancelledAt.Value;
            await AddSessionEventAsync(
                order, "kot_cancelled", $"KOT {order.KotNumber} cancelled: {reason}", userId);
            await RecalculateSessionTotalsAsync(order.DineInSessionId);
            await _outbox.EnqueueAsync("ThermalKotCancellationPrint", "Order", orderId,
                new { OrderId = orderId, Reason = reason });

            return await Ok(req, new { message = "KOT cancelled; kitchen cancellation slip queued", orderId });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to cancel KOT for order {OrderId}", orderId);
            return await Error(req, HttpStatusCode.InternalServerError, "Failed to cancel KOT");
        }
    }

    private async Task<Order?> GetDineInOrderAsync(string orderId, string outletId)
    {
        var order = await _orderRepo.GetOrderByIdAsync(orderId);
        return order != null && order.OutletId == outletId && order.OrderType == "dine-in"
            && !string.IsNullOrWhiteSpace(order.DineInSessionId)
            ? order
            : null;
    }

    private async Task<Order?> GetPendingQrOrderAsync(string orderId, string outletId)
    {
        var order = await _orderRepo.GetOrderByIdAsync(orderId);
        return order != null
            && order.OutletId == outletId
            && order.OrderType == "dine-in"
            && order.DineInOrderSource == "qr"
            && order.ApprovalStatus == "pending"
            && order.Status == "awaiting_waiter_approval"
            && !string.IsNullOrWhiteSpace(order.DineInSessionId)
            ? order
            : null;
    }

    private async Task AddSessionEventAsync(Order order, string eventType, string message, string? actor)
    {
        if (string.IsNullOrWhiteSpace(order.DineInSessionId))
            throw new InvalidOperationException($"QR order {order.Id} has no dine-in session");
        var session = await _orderRepo.GetDineInSessionByIdAsync(order.DineInSessionId);
        if (session == null)
            throw new InvalidOperationException($"Dine-in session {order.DineInSessionId} was not found");

        session.Timeline ??= new List<DineInSessionEvent>();
        session.Timeline.Add(new DineInSessionEvent
        {
            EventType = eventType,
            Message = message,
            Actor = actor,
            CreatedAt = MongoService.GetIstNow(),
            Metadata = new Dictionary<string, string>
            {
                ["orderId"] = order.Id ?? string.Empty,
                ["roundNumber"] = order.RoundNumber.ToString()
            }
        });
        session.UpdatedAt = MongoService.GetIstNow();
        if (!await _orderRepo.UpdateDineInSessionAsync(session))
            throw new InvalidOperationException($"Could not record QR order event for session {session.Id}");
    }

    private async Task RecalculateSessionTotalsAsync(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new InvalidOperationException("Cannot recalculate totals without a dine-in session");
        var session = await _orderRepo.GetDineInSessionByIdAsync(sessionId);
        if (session == null)
            throw new InvalidOperationException($"Dine-in session {sessionId} was not found");

        decimal subtotal = 0;
        foreach (var id in session.OrderIds)
        {
            var order = await _orderRepo.GetOrderByIdAsync(id);
            if (order != null && order.Status != "cancelled") subtotal += order.Subtotal;
        }

        session.Subtotal = subtotal;
        session.TaxAmount = 0;
        session.GrandTotal = Math.Max(0, subtotal - session.DiscountAmount - session.LoyaltyDiscountAmount);
        session.UpdatedAt = MongoService.GetIstNow();
        if (!await _orderRepo.UpdateDineInSessionAsync(session))
            throw new InvalidOperationException($"Could not update totals for session {sessionId}");
    }

    private async Task<string?> ResolveOutletIdAsync(HttpRequestData req, string? requestedId)
    {
        if (!string.IsNullOrWhiteSpace(requestedId)) return requestedId.Trim();
        var headerId = req.Headers.TryGetValues("X-Outlet-Id", out var values) ? values.FirstOrDefault() : null;
        if (!string.IsNullOrWhiteSpace(headerId)) return headerId.Trim();
        return (await _outletRepo.GetAllOutletsAsync()).FirstOrDefault()?.Id;
    }

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

    private static string ResolveDineInItemStatus(Order order, string itemStatus)
    {
        if (!string.Equals(itemStatus, "pending", StringComparison.OrdinalIgnoreCase)) return itemStatus;
        return order.Status switch
        {
            "preparing" => "preparing",
            "ready" => "ready",
            "served" => "served",
            _ => itemStatus
        };
    }
}

public class EditQrDineInOrderRequest
{
    [Required]
    [MinLength(1)]
    public List<OrderItemRequest> Items { get; set; } = new();

    [StringLength(500)]
    public string? Notes { get; set; }

    [StringLength(500)]
    public string? PreparationNotes { get; set; }
}

public class RejectQrDineInOrderRequest
{
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.StringLength(500)]
    public string Reason { get; set; } = string.Empty;
}

public class CancelDineInKotRequest
{
    [Required]
    [StringLength(500, MinimumLength = 1)]
    public string Reason { get; set; } = string.Empty;
}
