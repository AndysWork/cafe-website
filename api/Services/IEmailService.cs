namespace Cafe.Api.Services;

public interface IEmailService
{
    /// <summary>
    /// Sends a password reset email with a reset link
    /// </summary>
    Task<bool> SendPasswordResetEmailAsync(string toEmail, string userName, string resetToken);

    /// <summary>
    /// Sends a notification that the password was changed
    /// </summary>
    Task<bool> SendPasswordChangedNotificationAsync(string toEmail, string userName);

    /// <summary>
    /// Sends a notification that the profile was updated
    /// </summary>
    Task<bool> SendProfileUpdatedNotificationAsync(string toEmail, string userName);

    /// <summary>
    /// Sends an order confirmation email
    /// </summary>
    Task<bool> SendOrderConfirmationEmailAsync(string toEmail, string userName, string orderId, decimal total);

    /// <summary>
    /// Sends an order status update email
    /// </summary>
    Task<bool> SendOrderStatusUpdateEmailAsync(string toEmail, string userName, string orderId, string status);

    /// <summary>
    /// Sends a price alert notification email
    /// </summary>
    Task<bool> SendPriceAlertEmailAsync(string toEmail, string subject, string htmlContent);

    /// <summary>
    /// Sends a welcome email to new users
    /// </summary>
    Task<bool> SendWelcomeEmailAsync(string toEmail, string userName);

    /// <summary>
    /// Sends a promotional email
    /// </summary>
    Task<bool> SendPromotionalEmailAsync(string toEmail, string userName, string subject, string content);
}
