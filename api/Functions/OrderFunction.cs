using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Models;
using Cafe.Api.Helpers;
using System.Net;
using System.Security.Claims;

namespace Cafe.Api.Functions;

public class OrderFunction
{
    private readonly MongoService _mongo;
    private readonly AuthService _auth;
    private readonly ILogger _log;

    public OrderFunction(MongoService mongo, AuthService auth, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _auth = auth;
        _log = loggerFactory.CreateLogger<OrderFunction>();
    }

    // POST: Create new order (Authenticated users only)
    [Function("CreateOrder")]
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
            if (orderRequest == null || !orderRequest.Items.Any())
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Order must contain at least one item" });
                return badRequest;
            }

            // Validate and build order items
            var orderItems = new List<OrderItem>();
            decimal subtotal = 0;

            foreach (var item in orderRequest.Items)
            {
                if (item.Quantity <= 0)
                {
                    var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequest.WriteAsJsonAsync(new { error = "Quantity must be greater than 0" });
                    return badRequest;
                }

                // Get menu item details
                var menuItem = await _mongo.GetMenuItemAsync(item.MenuItemId);
                if (menuItem == null)
                {
                    var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequest.WriteAsJsonAsync(new { error = $"Menu item {item.MenuItemId} not found" });
                    return badRequest;
                }

                // Get category name if available
                string? categoryName = null;
                if (!string.IsNullOrEmpty(menuItem.CategoryId))
                {
                    var category = await _mongo.GetCategoryAsync(menuItem.CategoryId);
                    categoryName = category?.Name;
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

            // Calculate tax (10%)
            var tax = Math.Round(subtotal * 0.10m, 2);
            var total = subtotal + tax;

            // Get user email
            var user = await _mongo.GetUserByIdAsync(userId);

            // Create order
            var order = new Order
            {
                UserId = userId,
                Username = username,
                UserEmail = user?.Email,
                Items = orderItems,
                Subtotal = subtotal,
                Tax = tax,
                Total = total,
                Status = "pending",
                PaymentStatus = "pending",
                DeliveryAddress = orderRequest.DeliveryAddress,
                PhoneNumber = orderRequest.PhoneNumber,
                Notes = orderRequest.Notes,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var createdOrder = await _mongo.CreateOrderAsync(order);

            _log.LogInformation($"Order {createdOrder.Id} created by user {username}");

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

            var orders = await _mongo.GetUserOrdersAsync(userId);
            var orderResponses = orders.Select(MapToOrderResponse).ToList();

            var response = req.CreateResponse(HttpStatusCode.OK);
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

            var orders = await _mongo.GetAllOrdersAsync();
            var orderResponses = orders.Select(MapToOrderResponse).ToList();

            var response = req.CreateResponse(HttpStatusCode.OK);
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

            // Award loyalty points when order is delivered
            if (statusRequest.Status.ToLower() == "delivered")
            {
                var order = await _mongo.GetOrderByIdAsync(id);
                if (order != null)
                {
                    // Award 1 point per ₹10 spent
                    int pointsToAward = (int)(order.Total / 10);
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
                        }
                        catch (Exception pointsEx)
                        {
                            _log.LogWarning($"Failed to award points for order {id}: {pointsEx.Message}");
                            // Don't fail the order status update if points award fails
                        }
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
            DeliveryAddress = order.DeliveryAddress,
            PhoneNumber = order.PhoneNumber,
            Notes = order.Notes,
            CreatedAt = order.CreatedAt,
            UpdatedAt = order.UpdatedAt,
            CompletedAt = order.CompletedAt
        };
    }
}
