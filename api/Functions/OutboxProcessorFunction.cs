using System.Text.Json;
using Cafe.Api.Models;
using Cafe.Api.Repositories;
using Cafe.Api.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Cafe.Api.Functions;

/// <summary>
/// Timer-triggered function that processes outbox messages for reliable side-effect delivery.
/// Ticks on a short schedule and processes messages at a configurable interval
/// (default 90 seconds) to control cost and load.
/// Replaces fire-and-forget Task.Run patterns with guaranteed delivery and retry.
/// </summary>
public class OutboxProcessorFunction
{
    private static readonly object IntervalLock = new();
    private static DateTime _lastProcessedAtUtc = DateTime.MinValue;

    private readonly OutboxService _outbox;
    private readonly IWhatsAppService _whatsApp;
    private readonly IEmailService _email;
    private readonly NotificationService _notification;
    private readonly INotificationRepository _notificationRepo;
    private readonly WebPushService _webPush;
    private readonly IOrderRepository _orderRepo;
    private readonly IOperationsRepository _operationsRepo;
    private readonly ILoyaltyRepository _loyaltyRepo;
    private readonly ILogger<OutboxProcessorFunction> _logger;

    public OutboxProcessorFunction(
        OutboxService outbox,
        IWhatsAppService whatsApp,
        IEmailService email,
        NotificationService notification,
        INotificationRepository notificationRepo,
        WebPushService webPush,
        IOrderRepository orderRepo,
        IOperationsRepository operationsRepo,
        ILoyaltyRepository loyaltyRepo,
        ILogger<OutboxProcessorFunction> logger)
    {
        _outbox = outbox;
        _whatsApp = whatsApp;
        _email = email;
        _notification = notification;
        _notificationRepo = notificationRepo;
        _webPush = webPush;
        _orderRepo = orderRepo;
        _operationsRepo = operationsRepo;
        _loyaltyRepo = loyaltyRepo;
        _logger = logger;
    }

    [Function("ProcessOutboxMessages")]
    public async Task Run([TimerTrigger("%OutboxProcessorSchedule%")] TimerInfo timerInfo)
    {
        if (!ShouldProcessNow()) return;

        var messages = await _outbox.GetPendingMessagesAsync();
        if (messages.Count == 0) return;

        _logger.LogInformation("Processing {Count} outbox messages", messages.Count);

        foreach (var message in messages)
        {
            try
            {
                var acquired = await _outbox.MarkProcessingAsync(message.Id!);
                if (!acquired)
                {
                    continue;
                }

                await ProcessMessageAsync(message);
                await _outbox.MarkCompletedAsync(message.Id!);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process outbox message {MessageId} ({EventType})",
                    message.Id, message.EventType);
                await _outbox.MarkFailedAsync(message.Id!, ex.Message);
            }
        }

        // Periodic cleanup on the half-hour
        if (timerInfo.ScheduleStatus?.Last.Minute % 30 == 0)
        {
            var cleaned = await _outbox.CleanupOldMessagesAsync();
            if (cleaned > 0)
                _logger.LogInformation("Cleaned up {Count} old outbox messages", cleaned);
        }
    }

    private static bool ShouldProcessNow()
    {
        var intervalSeconds = 90;
        var raw = Environment.GetEnvironmentVariable("OutboxProcessorIntervalSeconds");
        if (int.TryParse(raw, out var parsed) && parsed > 0)
        {
            intervalSeconds = parsed;
        }

        var now = DateTime.UtcNow;
        lock (IntervalLock)
        {
            if (_lastProcessedAtUtc != DateTime.MinValue && (now - _lastProcessedAtUtc).TotalSeconds < intervalSeconds)
            {
                return false;
            }

            _lastProcessedAtUtc = now;
            return true;
        }
    }

    private async Task ProcessMessageAsync(OutboxMessage message)
    {
        switch (message.EventType)
        {
            case "OrderWhatsApp":
                var whatsApp = JsonSerializer.Deserialize<OrderWhatsAppPayload>(message.Payload);
                if (whatsApp != null)
                    await _whatsApp.SendOrderConfirmationAsync(
                        whatsApp.PhoneNumber, whatsApp.Username, whatsApp.OrderId, whatsApp.Total, whatsApp.OrderDetails);
                break;

            case "OrderEmailCustomer":
            case "OrderEmailAdmin":
                var emailPayload = JsonSerializer.Deserialize<OrderEmailPayload>(message.Payload);
                if (emailPayload != null)
                {
                    var items = JsonSerializer.Deserialize<List<OrderItem>>(emailPayload.OrderItemsJson ?? "[]") ?? new();
                    await _email.SendOrderConfirmationEmailAsync(
                        emailPayload.Email, emailPayload.CustomerName, emailPayload.OrderId, emailPayload.Total, items);
                }
                break;

            case "OrderNotificationUser":
                var notif = JsonSerializer.Deserialize<NotificationPayload>(message.Payload);
                if (notif != null)
                    await _notification.SendAsync(
                        notif.UserId, notif.Type, notif.Title, notif.Message, notif.Data, actionUrl: notif.ActionUrl);
                break;

            case "OrderNotificationAdmin":
                var adminNotif = JsonSerializer.Deserialize<AdminOrderNotificationPayload>(message.Payload);
                if (adminNotif != null)
                {
                    var order = await _orderRepo.GetOrderByIdAsync(adminNotif.OrderId);
                    if (order != null)
                        await _notification.SendNewOrderNotificationToAdminsAsync(order, adminNotif.Total);
                }
                break;

            case "OrderNotificationKitchen":
                var kitchenNotif = JsonSerializer.Deserialize<AdminOrderNotificationPayload>(message.Payload);
                if (kitchenNotif != null)
                {
                    var order = await _orderRepo.GetOrderByIdAsync(kitchenNotif.OrderId);
                    if (order != null)
                        await _notification.SendNewOrderNotificationToKitchenRolesAsync(order, kitchenNotif.Total);
                }
                break;

            case "LoyaltyPointsAward":
                var loyalty = JsonSerializer.Deserialize<LoyaltyPointsPayload>(message.Payload);
                if (loyalty != null)
                    await _loyaltyRepo.AwardPointsAsync(loyalty.UserId, loyalty.Points, loyalty.Reason, loyalty.OrderId);
                break;

            case "LoyaltyPointsAwardExact":
                var loyaltyExact = JsonSerializer.Deserialize<LoyaltyPointsPayload>(message.Payload);
                if (loyaltyExact != null)
                    await _loyaltyRepo.AwardExactPointsAsync(loyaltyExact.UserId, loyaltyExact.Points, loyaltyExact.Reason, loyaltyExact.OrderId);
                break;

            case "LoyaltyWhatsApp":
                var loyaltyWa = JsonSerializer.Deserialize<LoyaltyWhatsAppPayload>(message.Payload);
                if (loyaltyWa != null)
                    await _whatsApp.SendLoyaltyNotificationAsync(
                        loyaltyWa.PhoneNumber, loyaltyWa.Username, loyaltyWa.PointsEarned, loyaltyWa.TotalPoints);
                break;

            case "LoyaltyNotification":
                var loyaltyNotif = JsonSerializer.Deserialize<LoyaltyNotificationPayload>(message.Payload);
                if (loyaltyNotif != null)
                    await _notification.SendLoyaltyPointsNotificationAsync(
                        loyaltyNotif.UserId, loyaltyNotif.PointsEarned, loyaltyNotif.TotalPoints, loyaltyNotif.Reason);
                break;

            case "StatusUpdateWhatsApp":
                var statusWa = JsonSerializer.Deserialize<StatusUpdateWhatsAppPayload>(message.Payload);
                if (statusWa != null)
                    await _whatsApp.SendOrderStatusUpdateAsync(
                        statusWa.PhoneNumber, statusWa.Username, statusWa.OrderId, statusWa.Status);
                break;

            case "StatusUpdateEmail":
                var statusEmail = JsonSerializer.Deserialize<StatusUpdateEmailPayload>(message.Payload);
                if (statusEmail != null)
                    await _email.SendOrderStatusUpdateEmailAsync(
                        statusEmail.Email, statusEmail.Username, statusEmail.OrderId, statusEmail.Status);
                break;

            case "StatusUpdateNotification":
                var statusNotif = JsonSerializer.Deserialize<StatusUpdateNotificationPayload>(message.Payload);
                if (statusNotif != null)
                {
                    var statusOrder = await _orderRepo.GetOrderByIdAsync(statusNotif.OrderId);
                    if (statusOrder != null)
                        await _notification.SendOrderStatusNotificationAsync(statusOrder, statusNotif.Status);
                }
                break;

            case "DeliveryPartnerBroadcast":
                var broadcast = JsonSerializer.Deserialize<DeliveryPartnerBroadcastPayload>(message.Payload);
                if (broadcast != null)
                {
                    var broadcastOrder = await _orderRepo.GetOrderByIdAsync(broadcast.OrderId);
                    if (broadcastOrder != null && string.Equals(broadcastOrder.OrderType, "delivery", StringComparison.OrdinalIgnoreCase))
                    {
                        var partners = await _operationsRepo.GetDeliveryPartnersAsync(broadcast.OutletId);
                        var shortOrderId = broadcastOrder.Id?.Length >= 6 ? broadcastOrder.Id[^6..] : broadcastOrder.Id;
                        var notifiedTargets = 0;
                        foreach (var partner in partners)
                        {
                            if (string.IsNullOrWhiteSpace(partner.UserId) || !string.Equals(partner.Status, "available", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            notifiedTargets++;

                            await _notification.SendSystemNotificationAsync(
                                partner.UserId,
                                "New Delivery Request",
                                $"Order #{shortOrderId} is available for pickup. Accept it from your console.",
                                actionUrl: "/partner/delivery");

                            await _webPush.SendToUsersAsync(new[] { partner.UserId }, new WebPushPayload
                            {
                                Title = "New Delivery Request",
                                Body = $"Order #{shortOrderId} is available for pickup.",
                                Data = new
                                {
                                    orderId = broadcastOrder.Id,
                                    deliveryAddress = broadcastOrder.DeliveryAddress,
                                    phoneNumber = broadcastOrder.PhoneNumber
                                },
                                Actions = new[]
                                {
                                    new { action = "accept", title = "Accept" },
                                    new { action = "navigate", title = "Navigate" },
                                    new { action = "call", title = "Call" }
                                }
                            });
                        }

                        await AuditOrderNotificationAsync(broadcastOrder, notifiedTargets, "inapp+webpush");
                    }
                }
                break;

            case "DeliveryPartnerStatusAlert":
                var alert = JsonSerializer.Deserialize<DeliveryPartnerStatusAlertPayload>(message.Payload);
                if (alert != null)
                {
                    var alertOrder = await _orderRepo.GetOrderByIdAsync(alert.OrderId);
                    if (alertOrder != null && string.Equals(alertOrder.OrderType, "delivery", StringComparison.OrdinalIgnoreCase))
                    {
                        var shortOrderId = alertOrder.Id?.Length >= 6 ? alertOrder.Id[^6..] : alertOrder.Id;
                        var statusLabel = alert.Status.Replace("-", " ");

                        if (!string.IsNullOrWhiteSpace(alert.DeliveryPartnerId))
                        {
                            var assigned = await _operationsRepo.GetDeliveryPartnerByIdAsync(alert.DeliveryPartnerId);
                            if (!string.IsNullOrWhiteSpace(assigned?.UserId))
                            {
                                await _notification.SendSystemNotificationAsync(
                                    assigned.UserId,
                                    "Delivery Status Update",
                                    $"Order #{shortOrderId} is now {statusLabel}.",
                                    actionUrl: "/partner/delivery");

                                await _webPush.SendToUsersAsync(new[] { assigned.UserId }, new WebPushPayload
                                {
                                    Title = "Delivery Status Update",
                                    Body = $"Order #{shortOrderId} is now {statusLabel}.",
                                    Data = new
                                    {
                                        orderId = alertOrder.Id,
                                        deliveryAddress = alertOrder.DeliveryAddress,
                                        phoneNumber = alertOrder.PhoneNumber
                                    },
                                    Actions = new[]
                                    {
                                        new { action = "navigate", title = "Navigate" },
                                        new { action = "call", title = "Call" }
                                    }
                                });

                                await AuditOrderNotificationAsync(alertOrder, 1, "inapp+webpush");
                            }
                        }
                        else if (string.Equals(alert.Status, "ready", StringComparison.OrdinalIgnoreCase)
                                 || string.Equals(alert.Status, "confirmed", StringComparison.OrdinalIgnoreCase))
                        {
                            var partners = await _operationsRepo.GetDeliveryPartnersAsync(alert.OutletId);
                            var notifiedTargets = 0;
                            foreach (var partner in partners)
                            {
                                if (string.IsNullOrWhiteSpace(partner.UserId) || !string.Equals(partner.Status, "available", StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;
                                }

                                notifiedTargets++;

                                await _notification.SendSystemNotificationAsync(
                                    partner.UserId,
                                    "Delivery Request Update",
                                    $"Order #{shortOrderId} is now {statusLabel} and waiting for acceptance.",
                                    actionUrl: "/partner/delivery");

                                await _webPush.SendToUsersAsync(new[] { partner.UserId }, new WebPushPayload
                                {
                                    Title = "Delivery Request Update",
                                    Body = $"Order #{shortOrderId} is now {statusLabel} and waiting for acceptance.",
                                    Data = new
                                    {
                                        orderId = alertOrder.Id,
                                        deliveryAddress = alertOrder.DeliveryAddress,
                                        phoneNumber = alertOrder.PhoneNumber
                                    },
                                    Actions = new[]
                                    {
                                        new { action = "accept", title = "Accept" },
                                        new { action = "navigate", title = "Navigate" },
                                        new { action = "call", title = "Call" }
                                    }
                                });
                            }

                            await AuditOrderNotificationAsync(alertOrder, notifiedTargets, "inapp+webpush");
                        }
                    }
                }
                break;

            case "KitchenVoiceStockRequestAdminNotification":
                var stockReq = JsonSerializer.Deserialize<KitchenVoiceStockRequestAdminNotificationPayload>(message.Payload);
                if (stockReq != null)
                {
                    var adminIds = await _notificationRepo.GetAdminUserIdsAsync();
                    if (adminIds.Count > 0)
                    {
                        var requestedItems = stockReq.RequestedItems?.Any() == true
                            ? string.Join(", ", stockReq.RequestedItems.Take(5))
                            : "inventory items";

                        await _notification.SendToManyAsync(
                            adminIds,
                            "system",
                            "Kitchen Stock Request Pending",
                            $"{stockReq.RequestedByName} requested: {requestedItems}",
                            new Dictionary<string, string>
                            {
                                { "requestId", stockReq.RequestId },
                                { "outletId", stockReq.OutletId },
                                { "source", "kitchen_voice_request" }
                            },
                            actionUrl: "/admin/kitchen-stock-requests");
                    }
                }
                break;

            default:
                _logger.LogWarning("Unknown outbox event type: {EventType}", message.EventType);
                break;
        }
    }

    private async Task AuditOrderNotificationAsync(Order order, int targets, string channel)
    {
        if (targets <= 0)
        {
            return;
        }

        order.NotifiedAt = DateTime.UtcNow;
        order.NotifiedTargetsCount = targets;
        order.LastNotificationChannel = channel;
        order.UpdatedAt = MongoService.GetIstNow();
        await _orderRepo.UpdateOrderAsync(order);
    }

    // ── Payload DTOs ────────────────────────────────────────────────────
    public record OrderWhatsAppPayload(string PhoneNumber, string Username, string OrderId, decimal Total, string OrderDetails);
    public record OrderEmailPayload(string Email, string CustomerName, string OrderId, decimal Total, string? OrderItemsJson);
    public record NotificationPayload(string UserId, string Type, string Title, string Message, Dictionary<string, string>? Data, string? ActionUrl);
    public record AdminOrderNotificationPayload(string OrderId, decimal Total);
    public record LoyaltyPointsPayload(string UserId, int Points, string Reason, string? OrderId);
    public record LoyaltyWhatsAppPayload(string PhoneNumber, string Username, int PointsEarned, int TotalPoints);
    public record LoyaltyNotificationPayload(string UserId, int PointsEarned, int TotalPoints, string Reason);
    public record StatusUpdateWhatsAppPayload(string PhoneNumber, string Username, string OrderId, string Status);
    public record StatusUpdateEmailPayload(string Email, string Username, string OrderId, string Status);
    public record StatusUpdateNotificationPayload(string OrderId, string Status);
    public record DeliveryPartnerBroadcastPayload(string OrderId, string OutletId, string Trigger);
    public record DeliveryPartnerStatusAlertPayload(string OrderId, string OutletId, string Status, string? DeliveryPartnerId);
    public record KitchenVoiceStockRequestAdminNotificationPayload(string RequestId, string OutletId, string RequestedByName, List<string> RequestedItems, string TranscriptText);
}
