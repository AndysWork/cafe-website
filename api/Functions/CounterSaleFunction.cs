using System.Net;
using Cafe.Api.Helpers;
using Cafe.Api.Models;
using Cafe.Api.Repositories;
using Cafe.Api.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Cafe.Api.Functions;

public class CounterSaleFunction
{
    private readonly IOrderRepository _orderRepo;
    private readonly IMenuRepository _menuRepo;
    private readonly MongoService _mongo;
    private readonly AuthService _auth;
    private readonly OutboxService _outbox;
    private readonly InvoiceRenderService _invoiceRender;
    private readonly ThermalPrinterService _thermalPrinter;
    private readonly ILogger _log;

    public CounterSaleFunction(
        IOrderRepository orderRepo,
        IMenuRepository menuRepo,
        MongoService mongo,
        AuthService auth,
        OutboxService outbox,
        InvoiceRenderService invoiceRender,
        ThermalPrinterService thermalPrinter,
        ILoggerFactory loggerFactory)
    {
        _orderRepo = orderRepo;
        _menuRepo = menuRepo;
        _mongo = mongo;
        _auth = auth;
        _outbox = outbox;
        _invoiceRender = invoiceRender;
        _thermalPrinter = thermalPrinter;
        _log = loggerFactory.CreateLogger<CounterSaleFunction>();
    }

    [Function("CreateCounterSale")]
    public async Task<HttpResponseData> CreateCounterSale(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "counter-sales")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!isAuthorized || string.IsNullOrWhiteSpace(userId))
            {
                return errorResponse!;
            }

            var (request, validationError) = await ValidationHelper.ValidateBody<CreateCounterSaleRequest>(req);
            if (validationError != null) return validationError;

            var (hasOutletAccess, outletId, outletError) = await OutletHelper.ValidateOutletAccess(req, _auth, _mongo, request.OutletId);
            if (!hasOutletAccess || string.IsNullOrWhiteSpace(outletId))
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = outletError ?? "A valid outlet is required for counter sale" });
                return bad;
            }

            var paymentMethod = (request.PaymentMethod ?? "cash_at_counter").Trim().ToLowerInvariant();
            if (paymentMethod != "cash_at_counter" && paymentMethod != "upi-qr")
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "Invalid payment method for counter sale" });
                return bad;
            }

            if (paymentMethod == "upi-qr" && string.IsNullOrWhiteSpace(request.UpiReference))
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "UPI reference is required for UPI counter sales" });
                return bad;
            }

            var customerName = string.IsNullOrWhiteSpace(request.CustomerName)
                ? "Walk-in Customer"
                : request.CustomerName.Trim();

            var menuItemIds = request.Items.Select(i => i.MenuItemId).Distinct().ToList();
            var allMenuItems = await _menuRepo.GetMenuItemsByIdsAsync(menuItemIds, outletId);
            var menuItemMap = allMenuItems
                .Where(m => m.Id != null)
                .ToDictionary(m => m.Id!, m => m);

            var categoryIds = allMenuItems
                .Where(m => !string.IsNullOrEmpty(m.CategoryId))
                .Select(m => m.CategoryId!)
                .Distinct()
                .ToList();
            var categories = categoryIds.Count > 0
                ? await _menuRepo.GetCategoriesByIdsAsync(categoryIds)
                : new Dictionary<string, string>();

            var orderItems = new List<OrderItem>();
            decimal subtotal = 0;

            foreach (var item in request.Items)
            {
                if (!menuItemMap.TryGetValue(item.MenuItemId, out var menuItem))
                {
                    var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                    await bad.WriteAsJsonAsync(new { error = $"Menu item {item.MenuItemId} not found" });
                    return bad;
                }

                if (!menuItem.IsAvailable)
                {
                    var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                    await bad.WriteAsJsonAsync(new { error = $"{menuItem.Name} is currently unavailable" });
                    return bad;
                }

                string? categoryName = null;
                if (!string.IsNullOrEmpty(menuItem.CategoryId))
                {
                    categories.TryGetValue(menuItem.CategoryId, out categoryName);
                }

                var baseUnitPrice = menuItem.ShopSellingPrice > 0
                    ? menuItem.ShopSellingPrice
                    : (menuItem.WebPrice > 0 ? menuItem.WebPrice : menuItem.OnlinePrice);

                string? selectedVariantName = null;
                decimal? selectedVariantPrice = null;
                if (!string.IsNullOrWhiteSpace(item.SelectedVariantName))
                {
                    var matchedVariant = (menuItem.Variants ?? new List<MenuItemVariant>()).FirstOrDefault(v =>
                        v.VariantName.Equals(item.SelectedVariantName.Trim(), StringComparison.OrdinalIgnoreCase));

                    if (matchedVariant == null)
                    {
                        var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                        await bad.WriteAsJsonAsync(new { error = $"Selected variant '{item.SelectedVariantName}' is not available for {menuItem.Name}" });
                        return bad;
                    }

                    selectedVariantName = matchedVariant.VariantName;
                    selectedVariantPrice = matchedVariant.Price;
                    baseUnitPrice = matchedVariant.Price;
                }

                var selectedAddOns = new List<OrderItemAddOn>();
                if (item.SelectedAddOnNames != null && item.SelectedAddOnNames.Count > 0)
                {
                    var addOnMap = (menuItem.AddOns ?? new List<MenuItemAddOn>())
                        .Where(a => a.IsActive)
                        .ToDictionary(a => a.Name.Trim().ToLowerInvariant(), a => a);

                    foreach (var addOnName in item.SelectedAddOnNames)
                    {
                        var normalizedAddOn = (addOnName ?? string.Empty).Trim().ToLowerInvariant();
                        if (string.IsNullOrWhiteSpace(normalizedAddOn)) continue;

                        if (!addOnMap.TryGetValue(normalizedAddOn, out var matchedAddOn))
                        {
                            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                            await bad.WriteAsJsonAsync(new { error = $"Selected add-on '{addOnName}' is not available for {menuItem.Name}" });
                            return bad;
                        }

                        selectedAddOns.Add(new OrderItemAddOn
                        {
                            Name = matchedAddOn.Name,
                            Price = matchedAddOn.Price
                        });
                    }
                }

                var addOnTotal = selectedAddOns.Sum(a => a.Price);
                var unitPrice = baseUnitPrice + addOnTotal;
                var itemTotal = unitPrice * item.Quantity;
                subtotal += itemTotal;

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
                    SelectedVariantName = selectedVariantName,
                    SelectedVariantPrice = selectedVariantPrice,
                    SelectedAddOns = selectedAddOns,
                    AddOnTotal = addOnTotal,
                    Total = itemTotal
                });
            }

            var now = MongoService.GetIstNow();
            var order = new Order
            {
                OutletId = outletId,
                UserId = userId,
                Username = customerName,
                Items = orderItems,
                Subtotal = subtotal,
                Tax = 0,
                PlatformCharge = 0,
                DiscountAmount = 0,
                LoyaltyDiscountAmount = 0,
                LoyaltyPointsUsed = 0,
                DeliveryFee = 0,
                Total = subtotal,
                Status = "delivered",
                PaymentStatus = "paid",
                PaymentMethod = paymentMethod,
                UpiReference = paymentMethod == "upi-qr" ? request.UpiReference?.Trim() : null,
                PhoneNumber = request.PhoneNumber?.Trim(),
                Notes = request.Notes,
                OrderType = "pickup",
                Channel = "shop",
                CreatedAt = now,
                UpdatedAt = now,
                CompletedAt = now
            };

            var createdOrder = await _orderRepo.CreateOrderAsync(order);
            var invoiceNumber = BuildInvoiceNumber(createdOrder);

            var printQueued = false;
            if (request.PrintInvoice && !string.IsNullOrWhiteSpace(createdOrder.Id))
            {
                await _outbox.EnqueueAsync(
                    "ThermalInvoicePrint",
                    "Order",
                    createdOrder.Id,
                    new OutboxProcessorFunction.ThermalInvoicePrintPayload(createdOrder.Id, "CounterSaleCreate"));
                printQueued = true;
            }

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(new CounterSaleInvoiceResponse
            {
                Success = true,
                Message = "Counter sale invoice created successfully",
                OrderId = createdOrder.Id ?? string.Empty,
                InvoiceNumber = invoiceNumber,
                PaymentMethod = createdOrder.PaymentMethod,
                Subtotal = createdOrder.Subtotal,
                Total = createdOrder.Total,
                ReceiptPdfPath = $"/api/orders/{createdOrder.Id}/receipt-pdf",
                PrintQueued = printQueued
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error creating counter sale invoice");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "An error occurred while creating counter sale invoice" });
            return error;
        }
    }

    [Function("PrintCounterSaleInvoice")]
    public async Task<HttpResponseData> PrintCounterSaleInvoice(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "counter-sales/{orderId}/print")] HttpRequestData req,
        string orderId)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!isAuthorized)
            {
                return errorResponse!;
            }

            if (!_thermalPrinter.IsInvoiceConfigured)
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new
                {
                    error = "Thermal invoice printer is not configured",
                    required = "Set ThermalPrinter__InvoiceEndpoint (or fallback ThermalPrinter__KotEndpoint)"
                });
                return bad;
            }

            var order = await _orderRepo.GetOrderByIdAsync(orderId);
            if (order == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Order not found" });
                return notFound;
            }

            var invoiceText = _invoiceRender.Render(order, 48);
            var printed = await _thermalPrinter.TryPrintInvoiceAsync(order, invoiceText, "CounterSaleManualReprint");
            if (!printed)
            {
                var bad = req.CreateResponse(HttpStatusCode.BadGateway);
                await bad.WriteAsJsonAsync(new { error = "Invoice print dispatch failed" });
                return bad;
            }

            var ok = req.CreateResponse(HttpStatusCode.OK);
            await ok.WriteAsJsonAsync(new { success = true, message = "Invoice print dispatched" });
            return ok;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error printing counter sale invoice for {OrderId}", orderId);
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "An error occurred while printing counter sale invoice" });
            return error;
        }
    }

    private static string BuildInvoiceNumber(Order order)
    {
        var suffix = order.Id?.Length >= 6 ? order.Id[^6..].ToUpperInvariant() : (order.Id ?? "NA").ToUpperInvariant();
        return $"CS-{order.CreatedAt:yyyyMMdd}-{suffix}";
    }
}
