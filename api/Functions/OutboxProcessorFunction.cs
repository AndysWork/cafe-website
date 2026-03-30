using System.Text.Json;
using Cafe.Api.Models;
using Cafe.Api.Repositories;
using Cafe.Api.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Cafe.Api.Functions;

/// <summary>
/// Timer-triggered function that processes outbox messages for reliable side-effect delivery.
/// Runs every 30 seconds to pick up pending notifications, emails, loyalty point awards, etc.
/// Replaces fire-and-forget Task.Run patterns with guaranteed delivery and retry.
/// </summary>
public class OutboxProcessorFunction
{
    private readonly OutboxService _outbox;
    private readonly IWhatsAppService _whatsApp;
    private readonly IEmailService _email;
    private readonly NotificationService _notification;
    private readonly IOrderRepository _orderRepo;
    private readonly ILoyaltyRepository _loyaltyRepo;
    private readonly ILogger<OutboxProcessorFunction> _logger;

    public OutboxProcessorFunction(
        OutboxService outbox,
        IWhatsAppService whatsApp,
        IEmailService email,
        NotificationService notification,
        IOrderRepository orderRepo,
        ILoyaltyRepository loyaltyRepo,
        ILogger<OutboxProcessorFunction> logger)
    {
        _outbox = outbox;
        _whatsApp = whatsApp;
        _email = email;
        _notification = notification;
        _orderRepo = orderRepo;
        _loyaltyRepo = loyaltyRepo;
        _logger = logger;
    }

    [Function("ProcessOutboxMessages")]
    public async Task Run([TimerTrigger("*/30 * * * * *")] TimerInfo timerInfo)
    {
        var messages = await _outbox.GetPendingMessagesAsync();
        if (messages.Count == 0) return;

        _logger.LogInformation("Processing {Count} outbox messages", messages.Count);

        foreach (var message in messages)
        {
            try
            {
                await _outbox.MarkProcessingAsync(message.Id!);
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

            case "LoyaltyPointsAward":
                var loyalty = JsonSerializer.Deserialize<LoyaltyPointsPayload>(message.Payload);
                if (loyalty != null)
                    await _loyaltyRepo.AwardPointsAsync(loyalty.UserId, loyalty.Points, loyalty.Reason, loyalty.OrderId);
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

            default:
                _logger.LogWarning("Unknown outbox event type: {EventType}", message.EventType);
                break;
        }
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
}
