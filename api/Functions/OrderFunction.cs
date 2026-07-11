using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Models;
using Cafe.Api.Helpers;
using Cafe.Api.Repositories;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Globalization;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;
using MongoDB.Bson;

namespace Cafe.Api.Functions;

public class OrderFunction
{
    private const decimal BillToPointRate = 0.10m; // ₹1 = 0.1 loyalty points
    private const int ReviewWithItemRatingsBonusPoints = 3;

    private readonly IOrderRepository _orderRepo;
    private readonly IMenuRepository _menuRepo;
    private readonly IOfferRepository _offerRepo;
    private readonly ILoyaltyRepository _loyaltyRepo;
    private readonly IUserRepository _userRepo;
    private readonly IOperationsRepository _operationsRepo;
    private readonly MongoService _mongo;  // retained for OutletHelper compatibility and static helpers
    private readonly AuthService _auth;
    private readonly NotificationService _notificationService;
    private readonly ILogger _log;
    private readonly EventLogService _eventLog;
    private readonly OutboxService _outbox;

    public OrderFunction(
        IOrderRepository orderRepo,
        IMenuRepository menuRepo,
        IOfferRepository offerRepo,
        ILoyaltyRepository loyaltyRepo,
        IUserRepository userRepo,
        IOperationsRepository operationsRepo,
        MongoService mongo,
        AuthService auth,
        NotificationService notificationService,
        EventLogService eventLog,
        OutboxService outbox,
        ILoggerFactory loggerFactory)
    {
        _orderRepo = orderRepo;
        _menuRepo = menuRepo;
        _offerRepo = offerRepo;
        _loyaltyRepo = loyaltyRepo;
        _userRepo = userRepo;
        _operationsRepo = operationsRepo;
        _mongo = mongo;
        _auth = auth;
        _notificationService = notificationService;
        _eventLog = eventLog;
        _outbox = outbox;
        _log = loggerFactory.CreateLogger<OrderFunction>();
    }

    /// <summary>
    /// Creates a new order for the authenticated user
    /// </summary>
    /// <param name="req">HTTP request containing order details including items, delivery method, and payment info</param>
    /// <returns>Created order with order ID and confirmation details</returns>
    /// <response code="201">Order successfully created</response>
    /// <response code="400">Invalid request data or menu item not found</response>
    /// <response code="401">User not authenticated</response>
    [Function("CreateOrder")]
    [OpenApiOperation(operationId: "CreateOrder", tags: new[] { "Orders" }, Summary = "Create a new order", Description = "Creates a new order for the authenticated user")]
    [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(CreateOrderRequest), Required = true, Description = "Order details including items and delivery information")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Created, contentType: "application/json", bodyType: typeof(Order), Description = "Order successfully created")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.BadRequest, Description = "Invalid request data")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "User not authenticated")]
    public async Task<HttpResponseData> CreateOrder(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "orders")] HttpRequestData req)
    {
        bool orderPersisted = false;

        try
        {
            // Validate user authorization (any authenticated user can create orders)
            var authHeader = req.Headers.GetValues("Authorization").FirstOrDefault();
            if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Authentication required" });
                return unauthorized;
            }

            var token = authHeader.Substring("Bearer ".Length).Trim();
            var principal = _auth.ValidateToken(token);

            if (principal == null)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Invalid or expired token" });
                return unauthorized;
            }

            var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var username = principal.FindFirst(ClaimTypes.Name)?.Value;

            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(username))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid user information" });
                return badRequest;
            }

            // Parse request
            var (orderRequest, validationError) = await ValidationHelper.ValidateBody<CreateOrderRequest>(req);
            if (validationError != null) return validationError;

            // Resolve outlet for this order.
            // Admins keep strict access behavior while customers are not blocked by assigned outlet lists.
            var role = principal.FindFirst(ClaimTypes.Role)?.Value;
            var (outletResolved, outletId, outletError, suggestions) = await ResolveOrderOutletAsync(req, orderRequest, role);
            if (!outletResolved || string.IsNullOrWhiteSpace(outletId))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new
                {
                    error = outletError ?? "Unable to resolve outlet for this order",
                    suggestions = suggestions ?? new List<OutletSuggestionResponse>()
                });
                return badRequest;
            }

            var orderType = string.IsNullOrWhiteSpace(orderRequest.OrderType)
                ? "delivery"
                : orderRequest.OrderType.Trim().ToLowerInvariant();
            var channel = NormalizeOrderChannel(orderRequest.Channel, orderType);

            if (orderType != "delivery" && orderType != "pickup" && orderType != "dine-in")
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid order type" });
                return badRequest;
            }

            var normalizedPhone = orderRequest.PhoneNumber?.Trim() ?? string.Empty;
            if (orderType == "delivery")
            {
                if (string.IsNullOrWhiteSpace(orderRequest.DeliveryAddress))
                {
                    var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequest.WriteAsJsonAsync(new { error = "Delivery address is required for delivery orders" });
                    return badRequest;
                }

                if (string.IsNullOrWhiteSpace(normalizedPhone))
                {
                    var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequest.WriteAsJsonAsync(new { error = "Phone number is required for delivery orders" });
                    return badRequest;
                }
            }

            if (orderType == "dine-in" && string.IsNullOrWhiteSpace(orderRequest.TableNumber))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Table number is required for dine-in orders" });
                return badRequest;
            }

            // Validate and build order items
            var orderItems = new List<OrderItem>();
            decimal subtotal = 0;

            // Batch fetch all menu items at once (fixes N+1 query)
            var menuItemIds = orderRequest.Items.Select(i => i.MenuItemId).Distinct().ToList();
            var allMenuItems = await _menuRepo.GetMenuItemsByIdsAsync(menuItemIds, outletId);
            var menuItemMap = allMenuItems
                .Where(m => m.Id != null)
                .ToDictionary(m => m.Id!, m => m);

            // Batch fetch all categories at once
            var categoryIds = allMenuItems
                .Where(m => !string.IsNullOrEmpty(m.CategoryId))
                .Select(m => m.CategoryId!)
                .Distinct()
                .ToList();
            var categories = categoryIds.Count > 0
                ? await _menuRepo.GetCategoriesByIdsAsync(categoryIds)
                : new Dictionary<string, string>();

            foreach (var item in orderRequest.Items)
            {
                if (!menuItemMap.TryGetValue(item.MenuItemId, out var menuItem))
                {
                    var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequest.WriteAsJsonAsync(new { error = $"Menu item {item.MenuItemId} not found" });
                    return badRequest;
                }

                if (!menuItem.IsAvailable)
                {
                    var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequest.WriteAsJsonAsync(new { error = $"{menuItem.Name} is currently unavailable" });
                    return badRequest;
                }

                string? categoryName = null;
                if (!string.IsNullOrEmpty(menuItem.CategoryId))
                {
                    categories.TryGetValue(menuItem.CategoryId, out categoryName);
                }

                var baseUnitPrice = menuItem.WebPrice > 0
                    ? menuItem.WebPrice
                    : (menuItem.ShopSellingPrice > 0 ? menuItem.ShopSellingPrice : menuItem.OnlinePrice);

                string? selectedVariantName = null;
                decimal? selectedVariantPrice = null;
                if (!string.IsNullOrWhiteSpace(item.SelectedVariantName))
                {
                    var matchedVariant = (menuItem.Variants ?? new List<MenuItemVariant>()).FirstOrDefault(v =>
                        v.VariantName.Equals(item.SelectedVariantName.Trim(), StringComparison.OrdinalIgnoreCase));

                    if (matchedVariant == null)
                    {
                        var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                        await badRequest.WriteAsJsonAsync(new { error = $"Selected variant '{item.SelectedVariantName}' is not available for {menuItem.Name}" });
                        return badRequest;
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
                            var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                            await badRequest.WriteAsJsonAsync(new { error = $"Selected add-on '{addOnName}' is not available for {menuItem.Name}" });
                            return badRequest;
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

            // Tax is temporarily disabled until GSTIN onboarding is completed.
            // Platform charge is a flat ₹2 per order for now.
            var tax = 0m;
            var platformCharge = subtotal > 0 ? 2m : 0m;

            // Apply coupon discount
            decimal discountAmount = 0;
            string? couponCode = null;
            if (!string.IsNullOrWhiteSpace(orderRequest.CouponCode))
            {
                couponCode = orderRequest.CouponCode.Trim().ToUpper();
                var offer = await _offerRepo.GetOfferByCodeAsync(couponCode);
                if (offer != null && offer.IsActive && offer.ValidFrom <= MongoService.GetIstNow() && offer.ValidTill >= MongoService.GetIstNow())
                {
                    if (offer.UsageLimit.HasValue && offer.UsageCount >= offer.UsageLimit.Value)
                    {
                        // Coupon exhausted — ignore silently
                    }
                    else if (offer.MinOrderAmount.HasValue && subtotal < offer.MinOrderAmount.Value)
                    {
                        // Minimum order not met — ignore silently
                    }
                    else
                    {
                        if (offer.DiscountType == "percentage")
                            discountAmount = Math.Round(subtotal * (offer.DiscountValue / 100m), 2);
                        else if (offer.DiscountType == "flat")
                            discountAmount = Math.Round(offer.DiscountValue, 2);

                        if (offer.MaxDiscount.HasValue && discountAmount > offer.MaxDiscount.Value)
                            discountAmount = offer.MaxDiscount.Value;

                        // Increment usage count
                        await _offerRepo.IncrementOfferUsageAsync(offer.Id!);
                    }
                }
            }

            // Apply loyalty points discount
            int loyaltyPointsUsed = 0;
            decimal loyaltyDiscountAmount = 0;
            if (orderRequest.LoyaltyPointsUsed > 0)
            {
                var loyaltyAccount = await _loyaltyRepo.GetLoyaltyAccountAsync(userId);
                if (loyaltyAccount != null && loyaltyAccount.CurrentPoints >= orderRequest.LoyaltyPointsUsed)
                {
                    loyaltyPointsUsed = orderRequest.LoyaltyPointsUsed;
                    // 1 point = ₹0.25
                    loyaltyDiscountAmount = Math.Round(loyaltyPointsUsed * 0.25m, 2);
                    // Deduct loyalty points
                    await _loyaltyRepo.DeductLoyaltyPointsAsync(userId, loyaltyPointsUsed, $"Used for order");
                }
            }

            var deliveryFee = orderType == "delivery" ? Math.Max(0, orderRequest.DeliveryFee) : 0;
            var payableBeforeWallet = Math.Max(0, subtotal + tax + platformCharge + deliveryFee - discountAmount - loyaltyDiscountAmount);
            var total = payableBeforeWallet;

            var plannedOrderId = ObjectId.GenerateNewId().ToString();

            // Get user email
            var user = await _userRepo.GetUserByIdAsync(userId);

            // Determine payment method and status
            var paymentMethod = orderRequest.PaymentMethod?.ToLower() ?? "cod";
            var paymentStatus = "pending";
            string? razorpayOrderId = null;
            string? razorpayPaymentId = null;
            string? razorpaySignature = null;
            var upiReference = string.IsNullOrWhiteSpace(orderRequest.UpiReference)
                ? null
                : orderRequest.UpiReference.Trim();

            if (paymentMethod == "razorpay")
            {
                // Validate Razorpay payment details
                if (string.IsNullOrEmpty(orderRequest.RazorpayPaymentId) ||
                    string.IsNullOrEmpty(orderRequest.RazorpayOrderId) ||
                    string.IsNullOrEmpty(orderRequest.RazorpaySignature))
                {
                    var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequest.WriteAsJsonAsync(new { error = "Razorpay payment details are required for online payment" });
                    return badRequest;
                }

                razorpayOrderId = orderRequest.RazorpayOrderId;
                razorpayPaymentId = orderRequest.RazorpayPaymentId;
                razorpaySignature = orderRequest.RazorpaySignature;
                paymentStatus = "paid";
            }

            // Parse and validate scheduled order
            DateTime? scheduledFor = null;
            bool isScheduled = false;
            if (orderRequest.ScheduledFor.HasValue)
            {
                scheduledFor = orderRequest.ScheduledFor.Value;
                // Ensure scheduled time is at least 30 minutes in the future
                var minScheduleTime = MongoService.GetIstNow().AddMinutes(30);
                if (scheduledFor.Value < minScheduleTime)
                {
                    return req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
                }
                // Ensure scheduled time is not more than 7 days in the future
                var maxScheduleTime = MongoService.GetIstNow().AddDays(7);
                if (scheduledFor.Value > maxScheduleTime)
                {
                    return req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
                }
                isScheduled = true;
            }

            // Create order
            var order = new Order
            {
                Id = plannedOrderId,
                OutletId = outletId,
                UserId = userId,
                Username = username,
                UserEmail = user?.Email,
                Items = orderItems,
                Subtotal = subtotal,
                Tax = tax,
                PlatformCharge = platformCharge,
                Total = total,
                Status = isScheduled ? "scheduled" : "pending",
                PaymentStatus = paymentStatus,
                PaymentMethod = paymentMethod,
                RazorpayOrderId = razorpayOrderId,
                RazorpayPaymentId = razorpayPaymentId,
                RazorpaySignature = razorpaySignature,
                UpiReference = paymentMethod == "upi-qr" ? upiReference : null,
                DeliveryAddress = orderType == "delivery" ? orderRequest.DeliveryAddress?.Trim() : null,
                PhoneNumber = string.IsNullOrWhiteSpace(normalizedPhone) ? null : normalizedPhone,
                PreparationNotes = string.IsNullOrWhiteSpace(orderRequest.PreparationNotes) ? null : orderRequest.PreparationNotes.Trim(),
                Notes = orderRequest.Notes,
                CouponCode = couponCode,
                DiscountAmount = discountAmount,
                LoyaltyPointsUsed = loyaltyPointsUsed,
                LoyaltyDiscountAmount = loyaltyDiscountAmount,
                DeliveryFee = deliveryFee,
                OrderType = orderType,
                Channel = channel,
                TableNumber = orderType == "dine-in" ? orderRequest.TableNumber?.Trim() : null,
                WalletAmountUsed = 0,
                ScheduledFor = scheduledFor,
                IsScheduled = isScheduled,
                CreatedAt = MongoService.GetIstNow(),
                UpdatedAt = MongoService.GetIstNow()
            };

            var createdOrder = await _orderRepo.CreateOrderAsync(order);
            orderPersisted = true;

            _log.LogInformation("Order {OrderId} created by user {Username}", createdOrder.Id, username);

            // Event sourcing: record order creation (FLAW 15)
            _ = _eventLog.LogEventAsync("Order", createdOrder.Id ?? "", "Created",
                actorId: userId, actorRole: "user",
                newState: new { createdOrder.Status, createdOrder.Total, createdOrder.PaymentMethod, ItemCount = orderItems.Count },
                outletId: outletId);

            // Outbox pattern: enqueue side effects for reliable delivery (FLAW 17)
            var orderItemsJson = JsonSerializer.Serialize(orderItems);

            if (!string.IsNullOrEmpty(orderRequest.PhoneNumber))
            {
                var orderDetails = string.Join("\n", orderItems.Select(item => $"- {item.Name} x{item.Quantity} (₹{item.Total:N2})"));
                await _outbox.EnqueueAsync("OrderWhatsApp", "Order", createdOrder.Id!,
                    new { PhoneNumber = orderRequest.PhoneNumber, Username = username, OrderId = createdOrder.Id!, Total = total, OrderDetails = orderDetails });
            }

            if (!string.IsNullOrEmpty(user?.Email))
            {
                await _outbox.EnqueueAsync("OrderEmailCustomer", "Order", createdOrder.Id!,
                    new { Email = user.Email, CustomerName = user.FirstName ?? username, OrderId = createdOrder.Id!, Total = total, OrderItemsJson = orderItemsJson });
            }

            await _outbox.EnqueueAsync("OrderEmailAdmin", "Order", createdOrder.Id!,
                new { Email = "maataracafekpa@gmail.com", CustomerName = $"Admin (Order by {username})", OrderId = createdOrder.Id!, Total = total, OrderItemsJson = orderItemsJson });

            await _outbox.EnqueueAsync("OrderNotificationUser", "Order", createdOrder.Id!,
                new { UserId = userId, Type = "order_status", Title = "Order Placed Successfully! 🎉",
                    Message = $"Your order #{createdOrder.Id?[^6..]} has been placed. Total: ₹{total:N2}",
                    Data = new Dictionary<string, string> { { "orderId", createdOrder.Id ?? "" }, { "status", "pending" } },
                    ActionUrl = "/orders" });

            await _outbox.EnqueueAsync("OrderNotificationAdmin", "Order", createdOrder.Id!,
                new { OrderId = createdOrder.Id!, Total = total });

            await _outbox.EnqueueAsync("OrderNotificationKitchen", "Order", createdOrder.Id!,
                new { OrderId = createdOrder.Id!, Total = total });

            if (string.Equals(createdOrder.OrderType, "delivery", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(createdOrder.OutletId))
            {
                await _outbox.EnqueueAsync("DeliveryPartnerBroadcast", "Order", createdOrder.Id!,
                    new { OrderId = createdOrder.Id!, OutletId = createdOrder.OutletId, Trigger = "placed" });
            }

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(MapToOrderResponse(createdOrder));
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error creating order");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while creating the order" });
            return res;
        }
    }

    [Function("GetOrderOutletSuggestions")]
    public async Task<HttpResponseData> GetOrderOutletSuggestions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "order-outlet-suggestions")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var orderType = (req.Query["orderType"] ?? "delivery").Trim().ToLowerInvariant();
            var deliveryAddress = req.Query["deliveryAddress"]?.Trim() ?? string.Empty;

            decimal subtotal = 0;
            var subtotalRaw = req.Query["subtotal"];
            if (!string.IsNullOrWhiteSpace(subtotalRaw))
            {
                decimal.TryParse(subtotalRaw, NumberStyles.Number, CultureInfo.InvariantCulture, out subtotal);
            }

            var suggestions = await BuildOutletSuggestionsAsync(orderType, deliveryAddress, subtotal);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(suggestions);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting outlet suggestions for order");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while generating outlet suggestions" });
            return res;
        }
    }

    /// <summary>
    /// Retrieves all orders for the authenticated user
    /// </summary>
    /// <param name="req">HTTP request with authorization header</param>
    /// <returns>List of user's orders with order details</returns>
    /// <response code="200">Successfully retrieved user's orders</response>
    /// <response code="401">User not authenticated</response>
    // GET: Get user's orders (Authenticated users only)
    [Function("GetMyOrders")]
    public async Task<HttpResponseData> GetMyOrders(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "orders/my")] HttpRequestData req)
    {
        try
        {
            // Validate user authorization
            var authHeader = req.Headers.GetValues("Authorization").FirstOrDefault();
            if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Authentication required" });
                return unauthorized;
            }

            var token = authHeader.Substring("Bearer ".Length).Trim();
            var principal = _auth.ValidateToken(token);

            if (principal == null)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Invalid or expired token" });
                return unauthorized;
            }

            var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid user information" });
                return badRequest;
            }

            var (page, pageSize) = PaginationHelper.ParsePagination(req);
            var orders = await _orderRepo.GetUserOrdersAsync(userId, page, pageSize);
            var orderResponses = orders.Select(MapToOrderResponse).ToList();

            var response = req.CreateResponse(HttpStatusCode.OK);
            if (page.HasValue && pageSize.HasValue)
            {
                var totalCount = await _orderRepo.GetUserOrdersCountAsync(userId);
                PaginationHelper.AddPaginationHeaders(response, totalCount, page.Value, pageSize.Value);
            }
            await response.WriteAsJsonAsync(orderResponses);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting user orders");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while retrieving orders" });
            return res;
        }
    }

    /// <summary>
    /// Retrieves all orders in the system (Admin only)
    /// </summary>
    /// <param name="req">HTTP request with authorization header</param>
    /// <returns>List of all orders</returns>
    /// <response code="200">Successfully retrieved all orders</response>
    /// <response code="401">User not authenticated</response>
    /// <response code="403">User not authorized (admin role required)</response>
    // GET: Get all orders (Admin only)
    [Function("GetAllOrders")]
    public async Task<HttpResponseData> GetAllOrders(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "orders")] HttpRequestData req)
    {
        try
        {
            // Validate admin authorization
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);
            var (page, pageSize) = PaginationHelper.ParsePagination(req);
            var channel = ParseChannelQuery(req.Url.Query);

            List<Order> orders;
            if (!string.IsNullOrWhiteSpace(channel))
            {
                var allOrders = await _orderRepo.GetAllOrdersAsync(outletId, null, null);
                orders = allOrders
                    .Where(o => IsChannelMatch(o.Channel, channel!))
                    .ToList();
            }
            else
            {
                orders = await _orderRepo.GetAllOrdersAsync(outletId, page, pageSize);
            }

            var orderResponses = orders.Select(MapToOrderResponse).ToList();

            var response = req.CreateResponse(HttpStatusCode.OK);
            if (string.IsNullOrWhiteSpace(channel) && page.HasValue && pageSize.HasValue)
            {
                var totalCount = await _orderRepo.GetAllOrdersCountAsync(outletId);
                PaginationHelper.AddPaginationHeaders(response, totalCount, page.Value, pageSize.Value);
            }
            await response.WriteAsJsonAsync(orderResponses);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting all orders");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while retrieving orders" });
            return res;
        }
    }

    /// <summary>
    /// Retrieves a specific order by ID
    /// </summary>
    /// <param name="req">HTTP request with authorization header</param>
    /// <param name="id">The order ID</param>
    /// <returns>Order details</returns>
    /// <response code="200">Successfully retrieved order</response>
    /// <response code="401">User not authenticated</response>
    /// <response code="403">Access denied (users can only view their own orders)</response>
    /// <response code="404">Order not found</response>
    // GET: Get order by ID
    [Function("GetOrder")]
    public async Task<HttpResponseData> GetOrder(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "orders/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            // Validate user authorization
            var authHeader = req.Headers.GetValues("Authorization").FirstOrDefault();
            if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Authentication required" });
                return unauthorized;
            }

            var token = authHeader.Substring("Bearer ".Length).Trim();
            var principal = _auth.ValidateToken(token);

            if (principal == null)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Invalid or expired token" });
                return unauthorized;
            }

            var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var role = principal.FindFirst(ClaimTypes.Role)?.Value;

            var order = await _orderRepo.GetOrderByIdAsync(id);

            if (order == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Order not found" });
                return notFound;
            }

            // Users can only view their own orders, admins can view all
            if (role != "admin" && order.UserId != userId)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Access denied" });
                return forbidden;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(MapToOrderResponse(order));
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting order {OrderId}", id);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while retrieving the order" });
            return res;
        }
    }

    [Function("GetOrderTracking")]
    public async Task<HttpResponseData> GetOrderTracking(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "orders/{id}/tracking")] HttpRequestData req,
        string id)
    {
        try
        {
            var authHeader = req.Headers.GetValues("Authorization").FirstOrDefault();
            if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Authentication required" });
                return unauthorized;
            }

            var token = authHeader.Substring("Bearer ".Length).Trim();
            var principal = _auth.ValidateToken(token);
            if (principal == null)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Invalid or expired token" });
                return unauthorized;
            }

            var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var role = principal.FindFirst(ClaimTypes.Role)?.Value;

            var order = await _orderRepo.GetOrderByIdAsync(id);
            if (order == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Order not found" });
                return notFound;
            }

            if (role != "admin" && order.UserId != userId)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Access denied" });
                return forbidden;
            }

            var estimatedAt = EstimateDeliveryTime(order);
            int? etaMinutes = estimatedAt.HasValue
                ? Math.Max(0, (int)Math.Ceiling((estimatedAt.Value - MongoService.GetIstNow()).TotalMinutes))
                : null;

            DeliveryTrackingPartnerInfo? partnerInfo = null;
            if (!string.IsNullOrWhiteSpace(order.DeliveryPartnerId))
            {
                var partner = await _operationsRepo.GetDeliveryPartnerByIdAsync(order.DeliveryPartnerId);
                if (partner != null)
                {
                    partnerInfo = new DeliveryTrackingPartnerInfo
                    {
                        Id = partner.Id,
                        Name = partner.Name,
                        Phone = partner.Phone,
                        VehicleType = partner.VehicleType,
                        VehicleNumber = partner.VehicleNumber,
                        Status = partner.Status,
                        CurrentLatitude = partner.CurrentLatitude,
                        CurrentLongitude = partner.CurrentLongitude,
                        LastLocationUpdatedAt = partner.LastLocationUpdatedAt
                    };
                }
            }

            var hasLocation = partnerInfo?.CurrentLatitude.HasValue == true && partnerInfo?.CurrentLongitude.HasValue == true;

            var payload = new OrderTrackingResponse
            {
                OrderId = order.Id ?? id,
                Status = order.Status,
                OrderType = order.OrderType,
                IsScheduled = order.IsScheduled,
                ScheduledFor = order.ScheduledFor,
                EstimatedDeliveryAt = estimatedAt,
                EtaMinutes = etaMinutes,
                EtaLabel = BuildEtaLabel(order.Status, etaMinutes, estimatedAt),
                LiveLocationAvailable = hasLocation,
                LiveLocationMapUrl = hasLocation
                    ? $"https://maps.google.com/?q={partnerInfo!.CurrentLatitude!.Value},{partnerInfo.CurrentLongitude!.Value}"
                    : null,
                DeliveryPartner = partnerInfo,
                SupportPhone = "+91-9876543210",
                SupportEmail = "support@cafemanagement.com"
            };

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(payload);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting tracking for order {OrderId}", id);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while retrieving tracking" });
            return res;
        }
    }

    [Function("CreateOrderIssue")]
    public async Task<HttpResponseData> CreateOrderIssue(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "orders/{id}/issues")] HttpRequestData req,
        string id)
    {
        try
        {
            var authHeader = req.Headers.GetValues("Authorization").FirstOrDefault();
            if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Authentication required" });
                return unauthorized;
            }

            var token = authHeader.Substring("Bearer ".Length).Trim();
            var principal = _auth.ValidateToken(token);
            if (principal == null)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Invalid or expired token" });
                return unauthorized;
            }

            var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var username = principal.FindFirst(ClaimTypes.Name)?.Value;
            var role = principal.FindFirst(ClaimTypes.Role)?.Value;

            var order = await _orderRepo.GetOrderByIdAsync(id);
            if (order == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Order not found" });
                return notFound;
            }

            if (role != "admin" && order.UserId != userId)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Access denied" });
                return forbidden;
            }

            var (issueRequest, validationError) = await ValidationHelper.ValidateBody<CreateOrderIssueRequest>(req);
            if (validationError != null) return validationError;

            var validCategories = new[] { "missing-item", "wrong-item", "damaged-item", "delay", "quality", "other" };
            var category = issueRequest.Category.Trim().ToLowerInvariant();
            if (!validCategories.Contains(category))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid issue category" });
                return badRequest;
            }

            var issue = new OrderIssue
            {
                OrderId = id,
                OutletId = order.OutletId,
                UserId = userId ?? order.UserId,
                Username = username ?? order.Username,
                Category = category,
                Description = issueRequest.Description.Trim(),
                Status = "open",
                CreatedAt = MongoService.GetIstNow(),
                UpdatedAt = MongoService.GetIstNow()
            };

            var created = await _orderRepo.CreateOrderIssueAsync(issue);

            await _outbox.EnqueueAsync("OrderIssueNotification", "Order", id,
                new { OrderId = id, Category = category, Description = issue.Description, Username = issue.Username });

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(created);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error creating issue for order {OrderId}", id);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while creating issue" });
            return res;
        }
    }

    [Function("GetOrderIssues")]
    public async Task<HttpResponseData> GetOrderIssues(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "orders/{id}/issues")] HttpRequestData req,
        string id)
    {
        try
        {
            var authHeader = req.Headers.GetValues("Authorization").FirstOrDefault();
            if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Authentication required" });
                return unauthorized;
            }

            var token = authHeader.Substring("Bearer ".Length).Trim();
            var principal = _auth.ValidateToken(token);
            if (principal == null)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Invalid or expired token" });
                return unauthorized;
            }

            var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var role = principal.FindFirst(ClaimTypes.Role)?.Value;

            var order = await _orderRepo.GetOrderByIdAsync(id);
            if (order == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Order not found" });
                return notFound;
            }

            if (role != "admin" && order.UserId != userId)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Access denied" });
                return forbidden;
            }

            var issues = await _orderRepo.GetOrderIssuesAsync(id);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(issues);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting issues for order {OrderId}", id);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while retrieving issues" });
            return res;
        }
    }

    [Function("UpdateOrderIssueStatus")]
    public async Task<HttpResponseData> UpdateOrderIssueStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "orders/{id}/issues/{issueId}")] HttpRequestData req,
        string id,
        string issueId)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var order = await _orderRepo.GetOrderByIdAsync(id);
            if (order == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Order not found" });
                return notFound;
            }

            var (updateRequest, validationError) = await ValidationHelper.ValidateBody<UpdateOrderIssueStatusRequest>(req);
            if (validationError != null) return validationError;

            var nextStatus = updateRequest.Status.Trim().ToLowerInvariant();
            var validStatuses = new[] { "open", "in-progress", "resolved", "closed" };
            if (!validStatuses.Contains(nextStatus))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Invalid issue status" });
                return badRequest;
            }

            var updated = await _orderRepo.UpdateOrderIssueStatusAsync(id, issueId, nextStatus, updateRequest.ResolutionNotes, updateRequest.RefundProcessed);
            if (!updated)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Issue not found" });
                return notFound;
            }

            await TryAwardWorkflowLoyaltyPointsAsync(order);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, message = "Issue updated" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error updating issue {IssueId} for order {OrderId}", issueId, id);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while updating issue" });
            return res;
        }
    }

    [Function("CancelOrderItem")]
    public async Task<HttpResponseData> CancelOrderItem(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "orders/{id}/items/{menuItemId}/cancel")] HttpRequestData req,
        string id,
        string menuItemId)
    {
        try
        {
            var authHeader = req.Headers.GetValues("Authorization").FirstOrDefault();
            if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Authentication required" });
                return unauthorized;
            }

            var token = authHeader.Substring("Bearer ".Length).Trim();
            var principal = _auth.ValidateToken(token);
            if (principal == null)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Invalid or expired token" });
                return unauthorized;
            }

            var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var role = principal.FindFirst(ClaimTypes.Role)?.Value;

            var order = await _orderRepo.GetOrderByIdAsync(id);
            if (order == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Order not found" });
                return notFound;
            }

            if (role != "admin" && order.UserId != userId)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Access denied" });
                return forbidden;
            }

            if (order.Status != "pending" && order.Status != "confirmed" && order.Status != "scheduled")
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = $"Cannot modify items when order is '{order.Status}'" });
                return badRequest;
            }

            var (cancelRequest, validationError) = await ValidationHelper.ValidateBody<CancelOrderItemRequest>(req);
            if (validationError != null) return validationError;

            var item = order.Items.FirstOrDefault(i => i.MenuItemId == menuItemId);
            if (item == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Item is not part of this order" });
                return badRequest;
            }

            var qtyToCancel = Math.Min(item.Quantity, Math.Max(1, cancelRequest.Quantity));
            if (qtyToCancel >= item.Quantity)
            {
                order.Items.Remove(item);
            }
            else
            {
                item.Quantity -= qtyToCancel;
                item.Total = Math.Round(item.Price * item.Quantity, 2);
            }

            if (order.Items.Count == 0)
            {
                order.Status = "cancelled";
            }

            order.Subtotal = Math.Round(order.Items.Sum(i => i.Total), 2);
            order.Tax = 0m;
            order.PlatformCharge = order.Subtotal > 0 ? 2m : 0m;
            order.DiscountAmount = Math.Min(order.DiscountAmount, order.Subtotal);

            var loyaltyCap = Math.Max(0, order.Subtotal + order.Tax + order.PlatformCharge + order.DeliveryFee - order.DiscountAmount);
            order.LoyaltyDiscountAmount = Math.Min(order.LoyaltyDiscountAmount, loyaltyCap);
            order.Total = Math.Max(0, order.Subtotal + order.Tax + order.PlatformCharge + order.DeliveryFee - order.DiscountAmount - order.LoyaltyDiscountAmount);
            order.UpdatedAt = MongoService.GetIstNow();

            var updated = await _orderRepo.UpdateOrderAsync(order);
            if (!updated)
            {
                var error = req.CreateResponse(HttpStatusCode.InternalServerError);
                await error.WriteAsJsonAsync(new { error = "Failed to update order" });
                return error;
            }

            _ = _eventLog.LogEventAsync("Order", id, "ItemCancelled",
                actorId: userId,
                actorRole: role,
                newState: new { MenuItemId = menuItemId, QuantityCancelled = qtyToCancel, Subtotal = order.Subtotal, Total = order.Total },
                outletId: order.OutletId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(MapToOrderResponse(order));
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error cancelling order item {MenuItemId} for order {OrderId}", menuItemId, id);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while cancelling item" });
            return res;
        }
    }

    /// <summary>
    /// Updates the status of an order (Admin only)
    /// </summary>
    /// <param name="req">HTTP request with new status (pending, confirmed, preparing, ready, out-for-delivery, delivered, cancelled)</param>
    /// <param name="id">The order ID</param>
    /// <returns>Updated order details</returns>
    /// <response code="200">Order status successfully updated</response>
    /// <response code="400">Invalid status or order not found</response>
    /// <response code="401">User not authenticated</response>
    /// <response code="403">User not authorized (admin role required)</response>
    // PUT: Update order status (Admin only)
    [Function("UpdateOrderStatus")]
    public async Task<HttpResponseData> UpdateOrderStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "orders/{id}/status")] HttpRequestData req,
        string id)
    {
        try
        {
            // Validate admin authorization
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var (statusRequest, validationError) = await ValidationHelper.ValidateBody<UpdateOrderStatusRequest>(req);
            if (validationError != null) return validationError;

            // Validate status value
            var validStatuses = new[] { "pending", "confirmed", "preparing", "ready", "out-for-delivery", "delivered", "cancelled" };
            if (!validStatuses.Contains(statusRequest.Status.ToLower()))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = $"Invalid status. Valid values: {string.Join(", ", validStatuses)}" });
                return badRequest;
            }

            var oldOrder = await _orderRepo.GetOrderByIdAsync(id);
            if (oldOrder == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Order not found" });
                return notFound;
            }

            var requiredChannel = ParseChannelQuery(req.Url.Query);
            if (!string.IsNullOrWhiteSpace(requiredChannel) && !IsChannelMatch(oldOrder.Channel, requiredChannel))
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = $"Order channel does not match required '{requiredChannel}'" });
                return forbidden;
            }

            var oldStatus = oldOrder.Status;
            var nextStatus = statusRequest.Status.ToLowerInvariant();

            if (!IsValidStatusTransition(oldStatus, nextStatus))
            {
                var badTransition = req.CreateResponse(HttpStatusCode.BadRequest);
                await badTransition.WriteAsJsonAsync(new { error = $"Invalid status transition from '{oldStatus}' to '{nextStatus}'" });
                return badTransition;
            }

            if (RequiresPaymentConfirmationBeforeProgress(oldOrder, nextStatus))
            {
                var paymentRequired = req.CreateResponse(HttpStatusCode.BadRequest);
                await paymentRequired.WriteAsJsonAsync(new { error = "Payment is pending. Confirm payment before moving this order to next workflow step." });
                return paymentRequired;
            }

            if (nextStatus == "out-for-delivery" && oldOrder.OrderType == "delivery")
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Delivery partner must pick up the ready order to move it to out-for-delivery" });
                return badRequest;
            }

            var success = await _orderRepo.UpdateOrderStatusAsync(id, nextStatus);

            if (!success)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Order not found" });
                return notFound;
            }

            var order = await _orderRepo.GetOrderByIdAsync(id);

            // Event sourcing: record status transition (FLAW 15)
            _ = _eventLog.LogEventAsync("Order", id, "StatusChanged",
                actorId: null, actorRole: "admin",
                oldState: new { Status = oldStatus },
                newState: new { Status = nextStatus },
                outletId: order?.OutletId);

            // Outbox: enqueue status update notifications (FLAW 17)
            if (order != null && !string.IsNullOrEmpty(order.PhoneNumber))
            {
                await _outbox.EnqueueAsync("StatusUpdateWhatsApp", "Order", id,
                    new { PhoneNumber = order.PhoneNumber, Username = order.Username ?? "Customer", OrderId = order.Id!, Status = statusRequest.Status });
            }

            var shouldSendStatusEmail = string.Equals(nextStatus, "confirmed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(nextStatus, "delivered", StringComparison.OrdinalIgnoreCase);

            if (order != null && !string.IsNullOrEmpty(order.UserEmail) && shouldSendStatusEmail)
            {
                await _outbox.EnqueueAsync("StatusUpdateEmail", "Order", id,
                    new { Email = order.UserEmail, Username = order.Username ?? "Customer", OrderId = order.Id!, Status = statusRequest.Status });
            }

            if (order != null && !string.IsNullOrEmpty(order.UserId))
            {
                await _outbox.EnqueueAsync("StatusUpdateNotification", "Order", id,
                    new { OrderId = order.Id!, Status = statusRequest.Status });
            }

            if (order != null
                && string.Equals(order.OrderType, "delivery", StringComparison.OrdinalIgnoreCase)
                && (nextStatus == "confirmed" || nextStatus == "ready"))
            {
                await _outbox.EnqueueAsync("DeliveryPartnerStatusAlert", "Order", order.Id!,
                    new { OrderId = order.Id!, OutletId = order.OutletId, Status = nextStatus, DeliveryPartnerId = order.DeliveryPartnerId });
            }

            if (nextStatus == "delivered" && order != null)
            {
                order.CompletedAt = MongoService.GetIstNow();
                order.UpdatedAt = MongoService.GetIstNow();
                await _orderRepo.UpdateOrderAsync(order);
                await TryAwardWorkflowLoyaltyPointsAsync(order);
            }

            _log.LogInformation("Order {OrderId} status updated to {Status}", id, statusRequest.Status);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Order status updated successfully", status = statusRequest.Status });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error updating order status {OrderId}", id);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while updating the order status" });
            return res;
        }
    }

    /// <summary>
    /// Confirms payment for an order (Admin bypass for non-integrated payment rails)
    /// </summary>
    [Function("AdminConfirmOrderPayment")]
    public async Task<HttpResponseData> AdminConfirmOrderPayment(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "orders/{id}/payment/confirm")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var (confirmRequest, validationError) = await ValidationHelper.ValidateBody<AdminConfirmPaymentRequest>(req);
            if (validationError != null) return validationError;

            var order = await _orderRepo.GetOrderByIdAsync(id);
            if (order == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Order not found" });
                return notFound;
            }

            if (order.PaymentStatus == "paid")
            {
                var ok = req.CreateResponse(HttpStatusCode.OK);
                await ok.WriteAsJsonAsync(new { success = true, message = "Payment already confirmed", paymentStatus = order.PaymentStatus });
                return ok;
            }

            if (order.PaymentStatus == "refunded")
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Cannot confirm payment for a refunded order" });
                return badRequest;
            }

            var adminAuditNote = $"Admin payment confirmation by {userId ?? "admin"}";
            if (!string.IsNullOrWhiteSpace(confirmRequest.PaymentReference))
            {
                adminAuditNote += $" | Ref: {confirmRequest.PaymentReference.Trim()}";
            }
            if (!string.IsNullOrWhiteSpace(confirmRequest.AdminNote))
            {
                adminAuditNote += $" | Note: {confirmRequest.AdminNote.Trim()}";
            }

            var updated = await _orderRepo.UpdatePaymentStatusAsync(
                id,
                "paid",
                razorpayPaymentId: string.IsNullOrWhiteSpace(confirmRequest.PaymentReference) ? null : confirmRequest.PaymentReference.Trim(),
                razorpaySignature: "admin-bypass-confirmed",
                razorpayOrderId: order.RazorpayOrderId);

            if (!updated)
            {
                var error = req.CreateResponse(HttpStatusCode.InternalServerError);
                await error.WriteAsJsonAsync(new { error = "Failed to confirm payment" });
                return error;
            }

            var isUpiPayment = string.Equals(order.PaymentMethod, "upi-qr", StringComparison.OrdinalIgnoreCase)
                || string.Equals(order.PaymentMethod, "upi", StringComparison.OrdinalIgnoreCase);

            if (isUpiPayment)
            {
                if (!string.IsNullOrWhiteSpace(confirmRequest.PaymentReference))
                {
                    order.UpiReference = confirmRequest.PaymentReference.Trim();
                }

                order.UpiConfirmedBy = userId ?? "admin";
                order.UpiConfirmedAt = MongoService.GetIstNow();
            }

            if (!string.IsNullOrWhiteSpace(adminAuditNote))
            {
                order.Notes = string.IsNullOrWhiteSpace(order.Notes)
                    ? adminAuditNote
                    : $"{order.Notes} | {adminAuditNote}";
            }

            order.UpdatedAt = MongoService.GetIstNow();
            await _orderRepo.UpdateOrderAsync(order);

            _ = _eventLog.LogEventAsync("Order", id, "PaymentConfirmedByAdmin",
                actorId: userId,
                actorRole: "admin",
                oldState: new { PaymentStatus = order.PaymentStatus },
                newState: new { PaymentStatus = "paid", PaymentReference = confirmRequest.PaymentReference },
                outletId: order.OutletId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { success = true, message = "Payment confirmed. Order can now continue in workflow.", paymentStatus = "paid" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error confirming payment for order {OrderId}", id);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while confirming payment" });
            return res;
        }
    }

    // DELETE: Cancel order
    [Function("CancelOrder")]
    public async Task<HttpResponseData> CancelOrder(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "orders/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            // Validate user authorization
            var authHeader = req.Headers.GetValues("Authorization").FirstOrDefault();
            if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Authentication required" });
                return unauthorized;
            }

            var token = authHeader.Substring("Bearer ".Length).Trim();
            var principal = _auth.ValidateToken(token);

            if (principal == null)
            {
                var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
                await unauthorized.WriteAsJsonAsync(new { error = "Invalid or expired token" });
                return unauthorized;
            }

            var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var role = principal.FindFirst(ClaimTypes.Role)?.Value;

            var order = await _orderRepo.GetOrderByIdAsync(id);

            if (order == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Order not found" });
                return notFound;
            }

            // Users can only cancel their own orders, admins can cancel any
            if (role != "admin" && order.UserId != userId)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "Access denied" });
                return forbidden;
            }

            // Can only cancel pending or confirmed orders
            if (order.Status != "pending" && order.Status != "confirmed" && order.Status != "scheduled")
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = $"Cannot cancel order with status '{order.Status}'" });
                return badRequest;
            }

            var success = await _orderRepo.UpdateOrderStatusAsync(id, "cancelled");

            if (!success)
            {
                var error = req.CreateResponse(HttpStatusCode.InternalServerError);
                await error.WriteAsJsonAsync(new { error = "Failed to cancel order" });
                return error;
            }

            // Event sourcing: record cancellation (FLAW 15)
            _ = _eventLog.LogEventAsync("Order", id, "Cancelled",
                actorId: userId, actorRole: role,
                oldState: new { order.Status },
                newState: new { Status = "cancelled" },
                outletId: order.OutletId);

            _log.LogInformation("Order {OrderId} cancelled by user {UserId}", id, userId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Order cancelled successfully" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error cancelling order {OrderId}", id);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while cancelling the order" });
            return res;
        }
    }

    [Function("GetUpiPaymentReconciliationReport")]
    public async Task<HttpResponseData> GetUpiPaymentReconciliationReport(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/reports/upi-reconciliation")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);
            var from = ParseDateQuery(req, "from");
            var to = ParseDateQuery(req, "to");
            var toExclusive = to?.Date.AddDays(1);

            var orders = await _orderRepo.GetAllOrdersAsync(outletId);
            var upiOrders = orders
                .Where(o => string.Equals(o.PaymentMethod, "upi-qr", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(o.PaymentMethod, "upi", StringComparison.OrdinalIgnoreCase))
                .Where(o => !from.HasValue || o.CreatedAt >= from.Value)
                .Where(o => !toExclusive.HasValue || o.CreatedAt < toExclusive.Value)
                .OrderByDescending(o => o.CreatedAt)
                .ToList();

            var pending = upiOrders.Count(o => string.Equals(o.PaymentStatus, "pending", StringComparison.OrdinalIgnoreCase));
            var confirmed = upiOrders.Count(o => string.Equals(o.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase));
            var refunded = upiOrders.Count(o => string.Equals(o.PaymentStatus, "refunded", StringComparison.OrdinalIgnoreCase));
            var proofSubmitted = upiOrders.Count(o => !string.IsNullOrWhiteSpace(o.UpiReference) || !string.IsNullOrWhiteSpace(o.UpiProofUrl));

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                generatedAt = MongoService.GetIstNow(),
                outletId,
                dateRange = new { from, to },
                summary = new
                {
                    totalUpiOrders = upiOrders.Count,
                    pending,
                    confirmed,
                    refunded,
                    proofSubmitted,
                    pendingWithoutProof = upiOrders.Count(o => string.Equals(o.PaymentStatus, "pending", StringComparison.OrdinalIgnoreCase)
                        && string.IsNullOrWhiteSpace(o.UpiReference)
                        && string.IsNullOrWhiteSpace(o.UpiProofUrl)),
                    confirmedWithoutProof = upiOrders.Count(o => string.Equals(o.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase)
                        && string.IsNullOrWhiteSpace(o.UpiReference)
                        && string.IsNullOrWhiteSpace(o.UpiProofUrl))
                },
                items = upiOrders.Select(o => new
                {
                    id = o.Id,
                    userId = o.UserId,
                    username = o.Username,
                    total = o.Total,
                    paymentStatus = o.PaymentStatus,
                    upiReference = o.UpiReference,
                    upiProofUrl = o.UpiProofUrl,
                    upiConfirmedBy = o.UpiConfirmedBy,
                    upiConfirmedAt = o.UpiConfirmedAt,
                    createdAt = o.CreatedAt,
                    updatedAt = o.UpdatedAt
                })
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error generating UPI reconciliation report");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = "An error occurred while generating UPI reconciliation report" });
            return error;
        }
    }

    // Helper method to map Order to OrderResponse
    private static OrderResponse MapToOrderResponse(Order order)
    {
        return new OrderResponse
        {
            Id = order.Id!,
            UserId = order.UserId,
            Username = order.Username,
            UserEmail = order.UserEmail,
            Items = order.Items,
            Subtotal = order.Subtotal,
            Tax = order.Tax,
            PlatformCharge = order.PlatformCharge,
            Total = order.Total,
            CouponCode = order.CouponCode,
            DiscountAmount = order.DiscountAmount,
            LoyaltyPointsUsed = order.LoyaltyPointsUsed,
            LoyaltyDiscountAmount = order.LoyaltyDiscountAmount,
            Status = order.Status,
            PaymentStatus = order.PaymentStatus,
            PaymentMethod = order.PaymentMethod,
            RazorpayOrderId = order.RazorpayOrderId,
            RazorpayPaymentId = order.RazorpayPaymentId,
            UpiReference = order.UpiReference,
            UpiConfirmedBy = order.UpiConfirmedBy,
            UpiConfirmedAt = order.UpiConfirmedAt,
            UpiProofUrl = order.UpiProofUrl,
            DeliveryAddress = order.DeliveryAddress,
            PhoneNumber = order.PhoneNumber,
            PreparationNotes = order.PreparationNotes,
            Notes = order.Notes,
            CreatedAt = order.CreatedAt,
            UpdatedAt = order.UpdatedAt,
            CompletedAt = order.CompletedAt,
            ReceiptImageUrl = order.ReceiptImageUrl,
            DeliveryFee = order.DeliveryFee,
            OutletId = order.OutletId,
            OrderType = order.OrderType,
            Channel = string.IsNullOrWhiteSpace(order.Channel) ? "web" : order.Channel,
            ScheduledFor = order.ScheduledFor,
            IsScheduled = order.IsScheduled,
            DeliveryPartnerId = order.DeliveryPartnerId,
            DeliveryPartnerName = order.DeliveryPartnerName,
            DeliveryRouteUrl = order.DeliveryRouteUrl,
            DeliveryRouteShortCode = order.DeliveryRouteShortCode,
            DeliveryRouteShortUrl = order.DeliveryRouteShortUrl,
            DeliveryDistanceKm = order.DeliveryDistanceKm,
            DeliveryEtaMinutes = order.DeliveryEtaMinutes,
            DeliveryRouteUpdatedAt = order.DeliveryRouteUpdatedAt,
            TableNumber = order.TableNumber,
            LoyaltyPointsAwarded = order.LoyaltyPointsAwarded,
            LoyaltyPointsAwardedValue = order.LoyaltyPointsAwardedValue
        };
    }

    private async Task<(bool resolved, string? outletId, string? error, List<OutletSuggestionResponse>? suggestions)> ResolveOrderOutletAsync(
        HttpRequestData req,
        CreateOrderRequest orderRequest,
        string? role)
    {
        var requestedOutletId = orderRequest.OutletId?.Trim();
        if (string.IsNullOrWhiteSpace(requestedOutletId))
        {
            requestedOutletId = OutletHelper.GetOutletIdFromRequest(req, _auth);
        }

        // Preserve strict behavior for admin/staff operational flows.
        if (string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase))
        {
            var (hasAccess, outletId, accessError) = await OutletHelper.ValidateOutletAccess(req, _auth, _mongo, requestedOutletId);
            if (!hasAccess)
            {
                return (false, null, accessError ?? "Unable to validate outlet access", null);
            }

            if (!string.IsNullOrWhiteSpace(outletId))
            {
                return (true, outletId, null, null);
            }

            var firstActiveOutlet = (await _mongo.GetActiveOutletsAsync()).FirstOrDefault(o => !string.IsNullOrWhiteSpace(o.Id));
            if (firstActiveOutlet?.Id != null)
            {
                return (true, firstActiveOutlet.Id, null, null);
            }

            return (false, null, "No active outlets available to place this order", null);
        }

        if (!string.IsNullOrWhiteSpace(requestedOutletId))
        {
            var outlet = await _mongo.GetOutletByIdAsync(requestedOutletId);
            if (outlet != null && outlet.IsActive)
            {
                return (true, requestedOutletId, null, null);
            }

            var fallbackSuggestions = await BuildOutletSuggestionsAsync(orderRequest.OrderType, orderRequest.DeliveryAddress, 0);
            return (false, null, "Selected outlet is unavailable. Please choose a suggested outlet.", fallbackSuggestions);
        }

        var suggestions = await BuildOutletSuggestionsAsync(orderRequest.OrderType, orderRequest.DeliveryAddress, 0);
        return (false, null, "Please choose an outlet before placing the order.", suggestions);
    }

    private async Task<List<OutletSuggestionResponse>> BuildOutletSuggestionsAsync(string? orderType, string? deliveryAddress, decimal subtotal)
    {
        var normalizedOrderType = string.IsNullOrWhiteSpace(orderType) ? "delivery" : orderType.Trim().ToLowerInvariant();
        var addressText = deliveryAddress?.Trim().ToLowerInvariant() ?? string.Empty;

        var activeOutlets = await _mongo.GetActiveOutletsAsync();
        var suggestions = new List<OutletSuggestionResponse>();

        foreach (var outlet in activeOutlets)
        {
            if (string.IsNullOrWhiteSpace(outlet.Id)) continue;

            var acceptsOrderType = normalizedOrderType switch
            {
                "delivery" => outlet.Settings.AcceptsOnlineOrders,
                "pickup" => outlet.Settings.AcceptsTakeaway,
                "dine-in" => outlet.Settings.AcceptsDineIn,
                _ => outlet.Settings.AcceptsOnlineOrders
            };

            if (!acceptsOrderType) continue;

            var zones = await _mongo.GetActiveDeliveryZonesAsync(outlet.Id);
            var closestZone = zones.OrderBy(z => z.MinDistance).FirstOrDefault();

            var estimatedEta = normalizedOrderType == "delivery"
                ? (closestZone?.EstimatedMinutes ?? 35)
                : 10;

            var estimatedDistance = normalizedOrderType == "delivery"
                ? (closestZone?.MinDistance ?? 0)
                : 0;

            var estimatedFee = normalizedOrderType == "delivery"
                ? await _mongo.CalculateDeliveryFeeAsync(outlet.Id, subtotal)
                : 0;

            var rating = await _mongo.GetAverageRatingAsync(outlet.Id);

            var locationMatch = 0d;
            if (!string.IsNullOrWhiteSpace(addressText))
            {
                if (!string.IsNullOrWhiteSpace(outlet.City) && addressText.Contains(outlet.City.ToLowerInvariant())) locationMatch += 20;
                if (!string.IsNullOrWhiteSpace(outlet.State) && addressText.Contains(outlet.State.ToLowerInvariant())) locationMatch += 10;
                if (!string.IsNullOrWhiteSpace(outlet.Address) && addressText.Contains(outlet.Address.ToLowerInvariant())) locationMatch += 5;
            }

            var ratingScore = Math.Min(5d, Math.Max(0, rating)) * 10;
            var etaScore = Math.Max(0, 30 - Math.Min(30, estimatedEta));
            var feeScore = Math.Max(0, 25 - Math.Min(25, (double)estimatedFee));
            var score = Math.Round(ratingScore + etaScore + feeScore + locationMatch, 2);

            var reasons = new List<string>
            {
                $"Rating {Math.Round(rating, 1):0.0}/5",
                normalizedOrderType == "delivery"
                    ? $"Estimated ETA {estimatedEta} min"
                    : "Best for quick pickup/dine-in",
                normalizedOrderType == "delivery"
                    ? $"Approx. delivery fee ₹{estimatedFee:0.##}"
                    : "No delivery fee"
            };

            if (locationMatch > 0)
            {
                reasons.Add("Address match confidence boosted");
            }

            suggestions.Add(new OutletSuggestionResponse
            {
                OutletId = outlet.Id,
                OutletName = outlet.OutletName,
                OutletCode = outlet.OutletCode,
                Address = outlet.Address,
                City = outlet.City,
                State = outlet.State,
                Rating = Math.Round(rating, 1),
                EstimatedEtaMinutes = estimatedEta,
                EstimatedDistanceKm = Math.Round(estimatedDistance, 1),
                EstimatedDeliveryFee = Math.Round(estimatedFee, 2),
                Score = score,
                Reasons = reasons
            });
        }

        return suggestions
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.EstimatedEtaMinutes)
            .Take(5)
            .ToList();
    }

    private static DateTime? EstimateDeliveryTime(Order order)
    {
        var now = MongoService.GetIstNow();

        if (order.Status == "delivered" || order.Status == "cancelled") return now;

        if (order.IsScheduled && order.ScheduledFor.HasValue && order.ScheduledFor.Value > now)
        {
            return order.ScheduledFor.Value.AddMinutes(35);
        }

        var baseMinutes = order.Status switch
        {
            "pending" => 35,
            "confirmed" => 28,
            "preparing" => 18,
            "ready" => order.OrderType == "delivery" ? 10 : 0,
            "out-for-delivery" => 8,
            _ => 25
        };

        return now.AddMinutes(baseMinutes);
    }

    private static string BuildEtaLabel(string status, int? etaMinutes, DateTime? estimatedAt)
    {
        if (status == "delivered") return "Delivered";
        if (status == "cancelled") return "Cancelled";
        if (!etaMinutes.HasValue || !estimatedAt.HasValue) return "ETA unavailable";
        if (etaMinutes.Value <= 1) return "Arriving now";
        return $"Arriving in {etaMinutes.Value} min";
    }

    private async Task TryAwardWorkflowLoyaltyPointsAsync(Order order)
    {
        if (order.LoyaltyPointsAwarded || order.Status != "delivered") return;

        var issues = await _orderRepo.GetOrderIssuesAsync(order.Id!);
        var hasOpenIssue = issues.Any(i => i.Status == "open" || i.Status == "in-progress");
        if (hasOpenIssue) return;

        var review = await _orderRepo.GetReviewByOrderIdAsync(order.Id!);
        if (review == null) return;

        var paidBillValue = GetEligiblePaidBillValue(order);
        var pointsToAward = (int)Math.Floor(paidBillValue * BillToPointRate);
        if (HasOrderAndItemRatings(order, review))
        {
            pointsToAward += ReviewWithItemRatingsBonusPoints;
        }
        if (pointsToAward <= 0) return;

        await _outbox.EnqueueAsync("LoyaltyPointsAwardExact", "Order", order.Id!,
            new { UserId = order.UserId, Points = pointsToAward, Reason = $"Order #{order.Id} completion", OrderId = order.Id });

        if (!string.IsNullOrEmpty(order.PhoneNumber))
        {
            await _outbox.EnqueueAsync("LoyaltyWhatsApp", "Order", order.Id!,
                new { PhoneNumber = order.PhoneNumber, Username = order.Username ?? "Customer", PointsEarned = pointsToAward, TotalPoints = pointsToAward });
        }

        await _outbox.EnqueueAsync("LoyaltyNotification", "Order", order.Id!,
            new { UserId = order.UserId, PointsEarned = pointsToAward, TotalPoints = pointsToAward, Reason = $"Order #{order.Id?[^6..]}" });

        order.LoyaltyPointsAwarded = true;
        order.LoyaltyPointsAwardedValue = pointsToAward;
        order.UpdatedAt = MongoService.GetIstNow();
        await _orderRepo.UpdateOrderAsync(order);

        _ = _eventLog.LogEventAsync("Loyalty", order.UserId, "PointsAwarded",
            actorId: null, actorRole: "system",
            newState: new { Points = pointsToAward, Reason = $"Order #{order.Id} completion" },
            outletId: order.OutletId);
    }

    private static decimal GetEligiblePaidBillValue(Order order)
    {
        // The payable amount is already net of loyalty/wallet/coupon redemption.
        // Award points only on this paid amount.
        return Math.Max(0m, order.Total);
    }

    private static bool HasOrderAndItemRatings(Order order, CustomerReview review)
    {
        if (review.Rating < 1) return false;

        var uniqueOrderItems = order.Items
            .Select(i => i.MenuItemId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (uniqueOrderItems.Count == 0) return false;

        var ratedItemIds = (review.ItemRatings ?? new List<ItemRating>())
            .Where(r => !string.IsNullOrWhiteSpace(r.MenuItemId) && r.Rating >= 1)
            .Select(r => r.MenuItemId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return uniqueOrderItems.All(id => ratedItemIds.Contains(id));
    }

    private static bool IsValidStatusTransition(string current, string next)
    {
        if (current == next) return true;

        if (next == "cancelled")
        {
            return current == "scheduled" || current == "pending" || current == "confirmed" || current == "preparing" || current == "ready" || current == "out-for-delivery";
        }

        var transitions = new Dictionary<string, string[]>
        {
            ["scheduled"] = new[] { "pending", "confirmed" },
            ["pending"] = new[] { "confirmed" },
            ["confirmed"] = new[] { "preparing" },
            ["preparing"] = new[] { "ready" },
            ["ready"] = new[] { "out-for-delivery", "delivered" },
            ["out-for-delivery"] = new[] { "delivered" },
            ["delivered"] = Array.Empty<string>(),
            ["cancelled"] = Array.Empty<string>()
        };

        return transitions.TryGetValue(current, out var allowed) && allowed.Contains(next);
    }

    private static string NormalizeOrderChannel(string? requestedChannel, string orderType)
    {
        if (!string.IsNullOrWhiteSpace(requestedChannel))
        {
            var value = requestedChannel.Trim().ToLowerInvariant();
            if (value == "web" || value == "shop" || value == "partner")
            {
                return value;
            }
        }

        return orderType == "dine-in" ? "shop" : "web";
    }

    private static string? ParseChannelQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;
        var trimmed = query.TrimStart('?');
        if (string.IsNullOrWhiteSpace(trimmed)) return null;

        foreach (var segment in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = segment.Split('=', 2);
            if (parts.Length == 0) continue;

            var key = Uri.UnescapeDataString(parts[0]);
            if (!string.Equals(key, "channel", StringComparison.OrdinalIgnoreCase)) continue;

            var raw = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
            var value = raw.Trim().ToLowerInvariant();
            if (value == "web" || value == "shop" || value == "partner")
            {
                return value;
            }
        }

        return null;
    }

    private static bool IsChannelMatch(string? orderChannel, string targetChannel)
    {
        return string.Equals((orderChannel ?? "web").Trim(), targetChannel, StringComparison.OrdinalIgnoreCase);
    }

    private static bool RequiresPaymentConfirmationBeforeProgress(Order order, string nextStatus)
    {
        if (nextStatus == "pending" || nextStatus == "cancelled")
        {
            return false;
        }

        var paymentMethod = (order.PaymentMethod ?? "").Trim().ToLowerInvariant();
        var paymentPending = !string.Equals(order.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase);

        // Delivered state is only valid after successful payment for all payment methods.
        if (nextStatus == "delivered")
        {
            return paymentPending;
        }

        // UPI orders must be verified by admin while accepting (pending -> confirmed).
        if (nextStatus == "confirmed")
        {
            var isUpiPayment = paymentMethod == "upi" || paymentMethod == "upi-qr";
            return isUpiPayment && paymentPending;
        }

        var needsOnlinePayment = paymentMethod == "razorpay" || paymentMethod == "upi-qr";
        return needsOnlinePayment && paymentPending;
    }

    private static DateTime? ParseDateQuery(HttpRequestData req, string key)
    {
        var value = req.Query[key];
        if (string.IsNullOrWhiteSpace(value)) return null;

        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed)
            ? parsed
            : null;
    }
}
