using System.Threading.Tasks;

namespace Cafe.Api.Services;

/// <summary>
/// Interface for WhatsApp messaging service using WhatsApp Business Platform API
/// </summary>
public interface IWhatsAppService
{
    /// <summary>
    /// Send order confirmation message to customer
    /// </summary>
    Task<bool> SendOrderConfirmationAsync(string phoneNumber, string customerName, string orderId, decimal totalAmount, string orderDetails);

    /// <summary>
    /// Send order status update message to customer
    /// </summary>
    Task<bool> SendOrderStatusUpdateAsync(string phoneNumber, string customerName, string orderId, string status);

    /// <summary>
    /// Send loyalty points notification to customer
    /// </summary>
    Task<bool> SendLoyaltyNotificationAsync(string phoneNumber, string customerName, int pointsEarned, int totalPoints, string? rewardUnlocked = null);

    /// <summary>
    /// Send promotional offer message to customer
    /// </summary>
    Task<bool> SendPromotionalOfferAsync(string phoneNumber, string customerName, string offerTitle, string offerDescription, string? offerCode = null, DateTime? expiryDate = null);

    /// <summary>
    /// Send staff notification (shift reminders, performance alerts, etc.)
    /// </summary>
    Task<bool> SendStaffNotificationAsync(string phoneNumber, string staffName, string subject, string message);

    /// <summary>
    /// Send custom template message
    /// </summary>
    Task<bool> SendTemplateMessageAsync(string phoneNumber, string templateName, Dictionary<string, string> parameters);

    /// <summary>
    /// Send simple text message (for non-template messages)
    /// </summary>
    Task<bool> SendTextMessageAsync(string phoneNumber, string message);

    /// <summary>
    /// Check if WhatsApp service is properly configured and enabled
    /// </summary>
    bool IsEnabled { get; }
}
