using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Models;
using Cafe.Api.Helpers;
using System.Net;
using System.Security.Claims;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;

namespace Cafe.Api.Functions;

public class OrderFunction
{
    private readonly MongoService _mongo;
    private readonly AuthService _auth;
    private readonly ILogger _log;
    private readonly IWhatsAppService _whatsApp;
    private readonly IEmailService _emailService;
    private readonly NotificationService _notificationService;

    public OrderFunction(MongoService mongo, AuthService auth, IWhatsAppService whatsApp, IEmailService emailService, NotificationService notificationService, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _auth = auth;
        _whatsApp = whatsApp;
        _emailService = emailService;
        _notificationService = notificationService;
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
            var orderRequest = await req.ReadFromJsonAsync<CreateOrderRequest>();
            if (orderRequest == null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { success = false, error = "Invalid request" });
                return badRequest;
            }

            // Validate request
            if (!ValidationHelper.TryValidate(orderRequest, out var validationError))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(validationError!.Value);
                return badRequest;
            }

            // Validate outlet access
            var (hasAccess, outletId, accessError) = await OutletHelper.ValidateOutletAccess(req, _auth, _mongo);
            if (!hasAccess)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = accessError });
                return forbidden;
            }

            // Validate and build order items
            var orderItems = new List<OrderItem>();
            decimal subtotal = 0;

            // Batch fetch all menu items at once (fixes N+1 query)
            var menuItemIds = orderRequest.Items.Select(i => i.MenuItemId).Distinct().ToList();
            var allMenuItems = await _mongo.GetMenuItemsByIdsAsync(menuItemIds, outletId);
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
                ? await _mongo.GetCategoriesByIdsAsync(categoryIds)
                : new Dictionary<string, string>();

            foreach (var item in orderRequest.Items)
            {
                if (!menuItemMap.TryGetValue(item.MenuItemId, out var menuItem))
                {
                    var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequest.WriteAsJsonAsync(new { success = false, error = $"Menu item {item.MenuItemId} not found" });
                    return badRequest;
                }

                string? categoryName = null;
                if (!string.IsNullOrEmpty(menuItem.CategoryId))
                {
                    categories.TryGetValue(menuItem.CategoryId, out categoryName);
                }

                var itemTotal = menuItem.OnlinePrice * item.Quantity;
                subtotal += itemTotal;

                orderItems.Add(new OrderItem
                {
                    MenuItemId = menuItem.Id!,
                    Name = menuItem.Name,
                    Description = menuItem.Description,
                    CategoryId = menuItem.CategoryId,
                    CategoryName = categoryName,
                    Quantity = item.Quantity,
                    Price = menuItem.OnlinePrice,
                    Total = itemTotal
                });
            }

            // Calculate tax (2.5%) and platform charge (2.5%)
            var tax = Math.Round(subtotal * 0.025m, 2);
            var platformCharge = Math.Round(subtotal * 0.025m, 2);

            // Apply coupon discount
            decimal discountAmount = 0;
            string? couponCode = null;
            if (!string.IsNullOrWhiteSpace(orderRequest.CouponCode))
            {
                couponCode = orderRequest.CouponCode.Trim().ToUpper();
                var offer = await _mongo.GetOfferByCodeAsync(couponCode);
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
                        await _mongo.IncrementOfferUsageAsync(offer.Id!);
                    }
                }
            }

            // Apply loyalty points discount
            int loyaltyPointsUsed = 0;
            decimal loyaltyDiscountAmount = 0;
            if (orderRequest.LoyaltyPointsUsed > 0)
            {
                var loyaltyAccount = await _mongo.GetLoyaltyAccountAsync(userId);
                if (loyaltyAccount != null && loyaltyAccount.CurrentPoints >= orderRequest.LoyaltyPointsUsed)
                {
                    loyaltyPointsUsed = orderRequest.LoyaltyPointsUsed;
                    // 1 point = ₹0.25
                    loyaltyDiscountAmount = Math.Round(loyaltyPointsUsed * 0.25m, 2);
                    // Deduct loyalty points
                    await _mongo.DeductLoyaltyPointsAsync(userId, loyaltyPointsUsed, $"Used for order");
                }
            }

            var total = Math.Max(0, subtotal + tax + platformCharge - discountAmount - loyaltyDiscountAmount);

            // Get user email
            var user = await _mongo.GetUserByIdAsync(userId);

            // Determine payment method and status
            var paymentMethod = orderRequest.PaymentMethod?.ToLower() ?? "cod";
            var paymentStatus = "pending";
            string? razorpayOrderId = null;
            string? razorpayPaymentId = null;
            string? razorpaySignature = null;

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

            // Create order
            var order = new Order
            {
                OutletId = outletId,
                UserId = userId,
                Username = username,
                UserEmail = user?.Email,
                Items = orderItems,
                Subtotal = subtotal,
                Tax = tax,
                PlatformCharge = platformCharge,
                Total = total,
                Status = "pending",
                PaymentStatus = paymentStatus,
                PaymentMethod = paymentMethod,
                RazorpayOrderId = razorpayOrderId,
                RazorpayPaymentId = razorpayPaymentId,
                RazorpaySignature = razorpaySignature,
                DeliveryAddress = orderRequest.DeliveryAddress,
                PhoneNumber = orderRequest.PhoneNumber,
                Notes = orderRequest.Notes,
                CouponCode = couponCode,
                DiscountAmount = discountAmount,
                LoyaltyPointsUsed = loyaltyPointsUsed,
                LoyaltyDiscountAmount = loyaltyDiscountAmount,
                CreatedAt = MongoService.GetIstNow(),
                UpdatedAt = MongoService.GetIstNow()
            };

            var createdOrder = await _mongo.CreateOrderAsync(order);

            _log.LogInformation($"Order {createdOrder.Id} created by user {username}");

            // Fire-and-forget: Send notifications without blocking the response
            _ = Task.Run(async () =>
            {
                // Send WhatsApp notification to customer
                if (!string.IsNullOrEmpty(orderRequest.PhoneNumber))
                {
                    try
                    {
                        var orderDetails = string.Join("\n", orderItems.Select(item => $"- {item.Name} x{item.Quantity} (₹{item.Total:N2})"));
                        await _whatsApp.SendOrderConfirmationAsync(
                            orderRequest.PhoneNumber,
                            username,
                            createdOrder.Id!,
                            total,
                            orderDetails
                        );
                    }
                    catch (Exception whatsAppEx)
                    {
                        _log.LogWarning(whatsAppEx, "Failed to send WhatsApp notification for order {OrderId}", createdOrder.Id);
                    }
                }

                // Send order confirmation email to customer
                if (!string.IsNullOrEmpty(user?.Email))
                {
                    try
                    {
                        var customerName = user.FirstName ?? username;
                        await _emailService.SendOrderConfirmationEmailAsync(
                            user.Email,
                            customerName,
                            createdOrder.Id!,
                            total,
                            orderItems
                        );
                    }
                    catch (Exception emailEx)
                    {
                        _log.LogWarning(emailEx, "Failed to send order confirmation email for order {OrderId}", createdOrder.Id);
                    }
                }

                // Send order notification email to admin
                try
                {
                    await _emailService.SendOrderConfirmationEmailAsync(
                        "maataracafekpa@gmail.com",
                        $"Admin (Order by {username})",
                        createdOrder.Id!,
                        total,
                        orderItems
                    );
                }
                catch (Exception adminEmailEx)
                {
                    _log.LogWarning(adminEmailEx, "Failed to send admin order notification email for order {OrderId}", createdOrder.Id);
                }

                // Send in-app notification to customer
                try
                {
                    await _notificationService.SendAsync(
                        userId,
                        "order_status",
                        "Order Placed Successfully! 🎉",
                        $"Your order #{createdOrder.Id?[^6..]} has been placed. Total: ₹{total:N2}",
                        new Dictionary<string, string>
                        {
                            { "orderId", createdOrder.Id ?? "" },
                            { "status", "pending" }
                        },
                        actionUrl: "/orders"
                    );
                }
                catch (Exception notifEx)
                {
                    _log.LogWarning(notifEx, "Failed to send in-app notification for order {OrderId}", createdOrder.Id);
                }

                // Notify admin(s) about the new order
                try
                {
                    await _notificationService.SendNewOrderNotificationToAdminsAsync(createdOrder, total);
                }
                catch (Exception adminNotifEx)
                {
                    _log.LogWarning(adminNotifEx, "Failed to send admin in-app notification for order {OrderId}", createdOrder.Id);
                }
            });

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
            var orders = await _mongo.GetUserOrdersAsync(userId, page, pageSize);
            var orderResponses = orders.Select(MapToOrderResponse).ToList();

            var response = req.CreateResponse(HttpStatusCode.OK);
            if (page.HasValue && pageSize.HasValue)
            {
                var totalCount = await _mongo.GetUserOrdersCountAsync(userId);
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
            var orders = await _mongo.GetAllOrdersAsync(outletId, page, pageSize);
            var orderResponses = orders.Select(MapToOrderResponse).ToList();

            var response = req.CreateResponse(HttpStatusCode.OK);
            if (page.HasValue && pageSize.HasValue)
            {
                var totalCount = await _mongo.GetAllOrdersCountAsync(outletId);
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

            var order = await _mongo.GetOrderByIdAsync(id);

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

    /// <summary>
    /// Updates the status of an order (Admin only)
    /// </summary>
    /// <param name="req">HTTP request with new status (pending, confirmed, preparing, ready, delivered, cancelled)</param>
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

            var statusRequest = await req.ReadFromJsonAsync<UpdateOrderStatusRequest>();
            if (statusRequest == null || string.IsNullOrWhiteSpace(statusRequest.Status))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Status is required" });
                return badRequest;
            }

            // Validate status value
            var validStatuses = new[] { "pending", "confirmed", "preparing", "ready", "delivered", "cancelled" };
            if (!validStatuses.Contains(statusRequest.Status.ToLower()))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = $"Invalid status. Valid values: {string.Join(", ", validStatuses)}" });
                return badRequest;
            }

            var success = await _mongo.UpdateOrderStatusAsync(id, statusRequest.Status.ToLower());

            if (!success)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Order not found" });
                return notFound;
            }

            // Get order details for notifications
            var order = await _mongo.GetOrderByIdAsync(id);

            // Send WhatsApp notification to customer about status update
            if (order != null && !string.IsNullOrEmpty(order.PhoneNumber))
            {
                try
                {
                    await _whatsApp.SendOrderStatusUpdateAsync(
                        order.PhoneNumber,
                        order.Username ?? "Customer",
                        order.Id!,
                        statusRequest.Status
                    );
                }
                catch (Exception whatsAppEx)
                {
                    _log.LogWarning(whatsAppEx, "Failed to send WhatsApp notification for order {OrderId} status update", id);
                    // Don't fail the status update if WhatsApp sending fails
                }
            }

            // Send email notification to customer about status update
            if (order != null && !string.IsNullOrEmpty(order.UserEmail))
            {
                try
                {
                    await _emailService.SendOrderStatusUpdateEmailAsync(
                        order.UserEmail,
                        order.Username ?? "Customer",
                        order.Id!,
                        statusRequest.Status
                    );
                }
                catch (Exception emailEx)
                {
                    _log.LogWarning(emailEx, "Failed to send order status email for order {OrderId}", id);
                }
            }

            // Send in-app notification to customer
            if (order != null && !string.IsNullOrEmpty(order.UserId))
            {
                try
                {
                    await _notificationService.SendOrderStatusNotificationAsync(order, statusRequest.Status);
                }
                catch (Exception notifEx)
                {
                    _log.LogWarning(notifEx, "Failed to send in-app notification for order {OrderId}", id);
                }
            }

            // Award loyalty points when order is delivered
            if (statusRequest.Status.ToLower() == "delivered" && order != null)
            {
                // Award 80% of the amount paid as loyalty points
                int pointsToAward = (int)Math.Floor(order.Total * 0.80m);
                if (pointsToAward > 0)
                {
                    try
                    {
                        await _mongo.AwardPointsAsync(
                            order.UserId,
                            pointsToAward,
                            $"Order #{order.Id}",
                            order.Id
                        );
                        _log.LogInformation($"Awarded {pointsToAward} points to user {order.UserId} for order {order.Id}");

                        // Send WhatsApp notification about loyalty points
                        if (!string.IsNullOrEmpty(order.PhoneNumber))
                        {
                            try
                            {
                                var totalPoints = pointsToAward;
                                await _whatsApp.SendLoyaltyNotificationAsync(
                                    order.PhoneNumber,
                                    order.Username ?? "Customer",
                                    pointsToAward,
                                    totalPoints
                                );
                            }
                            catch (Exception whatsAppEx)
                            {
                                _log.LogWarning(whatsAppEx, "Failed to send WhatsApp loyalty notification for order {OrderId}", id);
                            }
                        }

                        // Send in-app loyalty points notification
                        try
                        {
                            var loyaltyAccount = await _mongo.GetLoyaltyAccountByUserIdAsync(order.UserId);
                            var totalPts = loyaltyAccount?.CurrentPoints ?? pointsToAward;
                            await _notificationService.SendLoyaltyPointsNotificationAsync(
                                order.UserId, pointsToAward, totalPts, $"Order #{order.Id?[^6..]}");
                        }
                        catch (Exception notifEx)
                        {
                            _log.LogWarning(notifEx, "Failed to send loyalty in-app notification for order {OrderId}", id);
                        }
                    }
                    catch (Exception pointsEx)
                    {
                        _log.LogWarning($"Failed to award points for order {id}: {pointsEx.Message}");
                        // Don't fail the order status update if points award fails
                    }
                }
            }

            _log.LogInformation($"Order {id} status updated to {statusRequest.Status}");

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

            var order = await _mongo.GetOrderByIdAsync(id);

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
            if (order.Status != "pending" && order.Status != "confirmed")
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = $"Cannot cancel order with status '{order.Status}'" });
                return badRequest;
            }

            var success = await _mongo.UpdateOrderStatusAsync(id, "cancelled");

            if (!success)
            {
                var error = req.CreateResponse(HttpStatusCode.InternalServerError);
                await error.WriteAsJsonAsync(new { error = "Failed to cancel order" });
                return error;
            }

            _log.LogInformation($"Order {id} cancelled by user {userId}");

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
            Total = order.Total,
            Status = order.Status,
            PaymentStatus = order.PaymentStatus,
            PaymentMethod = order.PaymentMethod,
            RazorpayOrderId = order.RazorpayOrderId,
            RazorpayPaymentId = order.RazorpayPaymentId,
            DeliveryAddress = order.DeliveryAddress,
            PhoneNumber = order.PhoneNumber,
            Notes = order.Notes,
            CreatedAt = order.CreatedAt,
            UpdatedAt = order.UpdatedAt,
            CompletedAt = order.CompletedAt,
            ReceiptImageUrl = order.ReceiptImageUrl
        };
    }
}
