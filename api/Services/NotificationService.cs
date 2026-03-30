using Cafe.Api.Models;
using Cafe.Api.Repositories;
using Microsoft.Extensions.Logging;

namespace Cafe.Api.Services;

/// <summary>
/// Service for creating and sending in-app notifications.
/// Checks user notification preferences before creating notifications.
/// </summary>
public class NotificationService
{
    private readonly INotificationRepository _mongo;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(INotificationRepository mongo, ILogger<NotificationService> logger)
    {
        _mongo = mongo;
        _logger = logger;
    }

    /// <summary>
    /// Send a notification to a specific user, respecting their preferences.
    /// </summary>
    public async Task<bool> SendAsync(string userId, string type, string title, string message,
        Dictionary<string, string>? data = null, string? actionUrl = null, string? imageUrl = null)
    {
        try
        {
            // Check user preferences
            var prefs = await _mongo.GetNotificationPreferencesAsync(userId);
            if (!ShouldNotify(prefs, type))
            {
                _logger.LogDebug("Notification suppressed for user {UserId} (type: {Type}, preference off)", userId, type);
                return false;
            }

            var notification = new AppNotification
            {
                UserId = userId,
                Type = type,
                Title = title,
                Message = message,
                Data = data,
                ActionUrl = actionUrl,
                ImageUrl = imageUrl
            };

            await _mongo.CreateNotificationAsync(notification);
            _logger.LogInformation("Notification created for user {UserId}: {Type} - {Title}", userId, type, title);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create notification for user {UserId}", userId);
            return false;
        }
    }

    /// <summary>
    /// Send a notification to multiple users at once.
    /// </summary>
    public async Task<int> SendToManyAsync(IEnumerable<string> userIds, string type, string title, string message,
        Dictionary<string, string>? data = null, string? actionUrl = null, string? imageUrl = null)
    {
        var count = 0;
        foreach (var userId in userIds)
        {
            if (await SendAsync(userId, type, title, message, data, actionUrl, imageUrl))
                count++;
        }
        return count;
    }

    /// <summary>
    /// Send order status notification to the customer.
    /// </summary>
    public async Task SendOrderStatusNotificationAsync(Order order, string newStatus)
    {
        if (string.IsNullOrEmpty(order.UserId)) return;

        var (title, message) = newStatus.ToLower() switch
        {
            "confirmed" => ("Order Confirmed ✅", $"Your order #{order.Id?[^6..]} has been confirmed and is being prepared."),
            "preparing" => ("Order Being Prepared 👨‍🍳", $"Your order #{order.Id?[^6..]} is now being prepared."),
            "ready" => ("Order Ready! 🎉", $"Your order #{order.Id?[^6..]} is ready for pickup!"),
            "delivered" => ("Order Delivered ✅", $"Your order #{order.Id?[^6..]} has been delivered. Enjoy!"),
            "cancelled" => ("Order Cancelled ❌", $"Your order #{order.Id?[^6..]} has been cancelled."),
            _ => ("Order Update", $"Your order #{order.Id?[^6..]} status changed to {newStatus}.")
        };

        await SendAsync(
            order.UserId,
            "order_status",
            title,
            message,
            new Dictionary<string, string>
            {
                { "orderId", order.Id ?? "" },
                { "status", newStatus }
            },
            actionUrl: "/orders"
        );
    }

    /// <summary>
    /// Send loyalty points notification.
    /// </summary>
    public async Task SendLoyaltyPointsNotificationAsync(string userId, int pointsAwarded, int totalPoints, string reason)
    {
        await SendAsync(
            userId,
            "loyalty_points",
            $"+{pointsAwarded} Loyalty Points! ⭐",
            $"You earned {pointsAwarded} points for {reason}. Total: {totalPoints} points.",
            new Dictionary<string, string>
            {
                { "pointsAwarded", pointsAwarded.ToString() },
                { "totalPoints", totalPoints.ToString() }
            },
            actionUrl: "/loyalty"
        );
    }

    /// <summary>
    /// Send new offer notification to all users (or a list).
    /// </summary>
    public async Task SendOfferNotificationAsync(IEnumerable<string> userIds, string offerTitle, string offerDescription, string? offerId = null)
    {
        await SendToManyAsync(
            userIds,
            "offer",
            $"New Offer: {offerTitle} 🎁",
            offerDescription,
            offerId != null ? new Dictionary<string, string> { { "offerId", offerId } } : null,
            actionUrl: "/offers"
        );
    }

    /// <summary>
    /// Send system notification.
    /// </summary>
    public async Task SendSystemNotificationAsync(string userId, string title, string message, string? actionUrl = null)
    {
        await SendAsync(userId, "system", title, message, actionUrl: actionUrl);
    }

    /// <summary>
    /// Notify all admin users about a new order placed by a customer.
    /// </summary>
    public async Task SendNewOrderNotificationToAdminsAsync(Order order, decimal total)
    {
        try
        {
            var adminIds = await _mongo.GetAdminUserIdsAsync();
            if (adminIds.Count == 0) return;

            var orderId = order.Id?[^6..] ?? "N/A";
            await SendToManyAsync(
                adminIds,
                "order_status",
                "New Order Received! 🛒",
                $"Order #{orderId} placed — ₹{total:N2} ({order.Items?.Count ?? 0} items)",
                new Dictionary<string, string>
                {
                    { "orderId", order.Id ?? "" },
                    { "status", "pending" }
                },
                actionUrl: "/admin/orders"
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send new order notification to admins");
        }
    }

    /// <summary>
    /// Notify all admin users about an order cancellation.
    /// </summary>
    public async Task SendOrderCancellationToAdminsAsync(Order order)
    {
        try
        {
            var adminIds = await _mongo.GetAdminUserIdsAsync();
            if (adminIds.Count == 0) return;

            var orderId = order.Id?[^6..] ?? "N/A";
            await SendToManyAsync(
                adminIds,
                "order_status",
                "Order Cancelled ❌",
                $"Order #{orderId} has been cancelled by the customer.",
                new Dictionary<string, string>
                {
                    { "orderId", order.Id ?? "" },
                    { "status", "cancelled" }
                },
                actionUrl: "/admin/orders"
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send order cancellation notification to admins");
        }
    }

    private static bool ShouldNotify(NotificationPreferences prefs, string type)
    {
        if (!prefs.PushNotifications) return false;

        return type.ToLower() switch
        {
            "order_status" => prefs.OrderUpdates,
            "loyalty_points" => prefs.LoyaltyPoints,
            "offer" => prefs.Offers,
            "system" => prefs.SystemNotifications,
            "stock_alert" => prefs.SystemNotifications,
            _ => true
        };
    }
}
