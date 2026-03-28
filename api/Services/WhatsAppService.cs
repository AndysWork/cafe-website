using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Cafe.Api.Services;

/// <summary>
/// WhatsApp messaging service using Twilio WhatsApp API
/// </summary>
public class WhatsAppService : IWhatsAppService
{
    private readonly IConfiguration _config;
    private readonly ILogger<WhatsAppService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _accountSid;
    private readonly string _authToken;
    private readonly string _fromNumber;
    private readonly bool _isEnabled;

    public bool IsEnabled => _isEnabled;

    public WhatsAppService(IConfiguration config, ILogger<WhatsAppService> logger, IHttpClientFactory httpClientFactory)
    {
        _config = config;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("WhatsApp");

        // Load Twilio configuration
        _accountSid = _config["WhatsAppService:TwilioAccountSid"] ?? string.Empty;
        _authToken = _config["WhatsAppService:TwilioAuthToken"] ?? string.Empty;
        _fromNumber = _config["WhatsAppService:TwilioFromNumber"] ?? string.Empty;

        // Check if service is enabled (all required configs are present)
        _isEnabled = !string.IsNullOrEmpty(_accountSid) &&
                     !string.IsNullOrEmpty(_authToken) &&
                     !string.IsNullOrEmpty(_fromNumber) &&
                     _accountSid != "your-account-sid-here" &&
                     _authToken != "your-auth-token-here" &&
                     _fromNumber != "whatsapp:+14155238886";

        if (!_isEnabled)
        {
            _logger.LogWarning("WhatsApp service is not enabled. Please configure Twilio AccountSid, AuthToken, and FromNumber in application settings.");
        }

        // Setup HTTP client with Basic Authentication
        var authBytes = Encoding.ASCII.GetBytes($"{_accountSid}:{_authToken}");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
    }

    public async Task<bool> SendOrderConfirmationAsync(string phoneNumber, string customerName, string orderId, decimal totalAmount, string orderDetails)
    {
        if (!IsEnabled)
        {
            _logger.LogWarning("WhatsApp service is disabled. Cannot send order confirmation.");
            return false;
        }

        try
        {
            var message = $"Hi {customerName}! 🎉\n\n" +
                         $"Your order has been confirmed!\n\n" +
                         $"📝 Order ID: {orderId}\n" +
                         $"💰 Total Amount: ₹{totalAmount:N2}\n\n" +
                         $"📦 Order Details:\n{orderDetails}\n\n" +
                         $"Thank you for choosing Maa Tara Cafe! ☕";

            return await SendTextMessageAsync(phoneNumber, message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending order confirmation WhatsApp message to {PhoneNumber}", phoneNumber);
            return false;
        }
    }

    public async Task<bool> SendOrderStatusUpdateAsync(string phoneNumber, string customerName, string orderId, string status)
    {
        if (!IsEnabled)
        {
            _logger.LogWarning("WhatsApp service is disabled. Cannot send order status update.");
            return false;
        }

        try
        {
            var statusEmoji = status.ToLower() switch
            {
                "preparing" => "👨‍🍳",
                "ready" => "✅",
                "delivered" => "🚀",
                "cancelled" => "❌",
                _ => "📋"
            };

            var message = $"Hi {customerName}! {statusEmoji}\n\n" +
                         $"Your order status has been updated:\n\n" +
                         $"📝 Order ID: {orderId}\n" +
                         $"📊 Status: {status}\n\n" +
                         $"Thank you for your patience! ☕";

            return await SendTextMessageAsync(phoneNumber, message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending order status update WhatsApp message to {PhoneNumber}", phoneNumber);
            return false;
        }
    }

    public async Task<bool> SendLoyaltyNotificationAsync(string phoneNumber, string customerName, int pointsEarned, int totalPoints, string? rewardUnlocked = null)
    {
        if (!IsEnabled)
        {
            _logger.LogWarning("WhatsApp service is disabled. Cannot send loyalty notification.");
            return false;
        }

        try
        {
            var message = $"Hi {customerName}! 🎁\n\n" +
                         $"You've earned {pointsEarned} loyalty points!\n\n" +
                         $"⭐ Total Points: {totalPoints}\n";

            if (!string.IsNullOrEmpty(rewardUnlocked))
            {
                message += $"\n🎉 Congratulations! You've unlocked: {rewardUnlocked}\n";
            }

            message += "\nKeep collecting points for more rewards! ☕";

            return await SendTextMessageAsync(phoneNumber, message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending loyalty notification WhatsApp message to {PhoneNumber}", phoneNumber);
            return false;
        }
    }

    public async Task<bool> SendPromotionalOfferAsync(string phoneNumber, string customerName, string offerTitle, string offerDescription, string? offerCode = null, DateTime? expiryDate = null)
    {
        if (!IsEnabled)
        {
            _logger.LogWarning("WhatsApp service is disabled. Cannot send promotional offer.");
            return false;
        }

        try
        {
            var message = $"Hi {customerName}! 🎉\n\n" +
                         $"🌟 Special Offer: {offerTitle}\n\n" +
                         $"{offerDescription}\n";

            if (!string.IsNullOrEmpty(offerCode))
            {
                message += $"\n🎫 Use Code: {offerCode}\n";
            }

            if (expiryDate.HasValue)
            {
                message += $"\n⏰ Valid Until: {expiryDate.Value:dd MMM yyyy}\n";
            }

            message += "\nVisit us today! ☕";

            return await SendTextMessageAsync(phoneNumber, message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending promotional offer WhatsApp message to {PhoneNumber}", phoneNumber);
            return false;
        }
    }

    public async Task<bool> SendStaffNotificationAsync(string phoneNumber, string staffName, string subject, string message)
    {
        if (!IsEnabled)
        {
            _logger.LogWarning("WhatsApp service is disabled. Cannot send staff notification.");
            return false;
        }

        try
        {
            var fullMessage = $"Hi {staffName}! 👋\n\n" +
                            $"📢 {subject}\n\n" +
                            $"{message}\n\n" +
                            $"- Maa Tara Cafe Management";

            return await SendTextMessageAsync(phoneNumber, fullMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending staff notification WhatsApp message to {PhoneNumber}", phoneNumber);
            return false;
        }
    }

    public async Task<bool> SendTemplateMessageAsync(string phoneNumber, string templateName, Dictionary<string, string> parameters)
    {
        if (!IsEnabled)
        {
            _logger.LogWarning("WhatsApp service is disabled. Cannot send template message.");
            return false;
        }

        // Note: Twilio doesn't use template names the same way as Meta
        // For simplicity, we'll send as a text message with formatted content
        var message = $"Template: {templateName}\n";
        foreach (var param in parameters)
        {
            message += $"{param.Key}: {param.Value}\n";
        }

        return await SendTextMessageAsync(phoneNumber, message.Trim());
    }

    public async Task<bool> SendTextMessageAsync(string phoneNumber, string message)
    {
        if (!IsEnabled)
        {
            _logger.LogWarning("WhatsApp service is disabled. Cannot send text message.");
            return false;
        }

        try
        {
            var formattedPhone = FormatPhoneNumber(phoneNumber);
            var url = $"https://api.twilio.com/2010-04-01/Accounts/{_accountSid}/Messages.json";

            // Twilio requires "whatsapp:" prefix for WhatsApp messages
            var toNumber = formattedPhone.StartsWith("whatsapp:") ? formattedPhone : $"whatsapp:{formattedPhone}";
            var fromNumber = _fromNumber.StartsWith("whatsapp:") ? _fromNumber : $"whatsapp:{_fromNumber}";

            var formData = new Dictionary<string, string>
            {
                { "From", fromNumber },
                { "To", toNumber },
                { "Body", message }
            };

            var content = new FormUrlEncodedContent(formData);
            var response = await _httpClient.PostAsync(url, content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("WhatsApp message sent successfully via Twilio to {PhoneNumber}", phoneNumber);
                return true;
            }
            else
            {
                _logger.LogError("Failed to send WhatsApp message via Twilio. Status: {StatusCode}, Response: {Response}", 
                    response.StatusCode, responseBody);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending WhatsApp message via Twilio to {PhoneNumber}", phoneNumber);
            return false;
        }
    }

    /// <summary>
    /// Format phone number to E.164 format (e.g., +919876543210)
    /// WhatsApp API requires phone numbers without spaces, hyphens, or parentheses
    /// </summary>
    private string FormatPhoneNumber(string phoneNumber)
    {
        if (string.IsNullOrEmpty(phoneNumber))
            return phoneNumber;

        // Remove all non-digit characters except +
        var cleaned = new StringBuilder();
        foreach (var c in phoneNumber)
        {
            if (char.IsDigit(c) || c == '+')
                cleaned.Append(c);
        }

        var formatted = cleaned.ToString();

        // Ensure it starts with +
        if (!formatted.StartsWith("+"))
        {
            // If it's an Indian number starting with 91, add +
            if (formatted.StartsWith("91") && formatted.Length == 12)
            {
                formatted = "+" + formatted;
            }
            // If it's a 10-digit number, assume it's Indian and add +91
            else if (formatted.Length == 10)
            {
                formatted = "+91" + formatted;
            }
            else
            {
                // For other cases, just add +
                formatted = "+" + formatted;
            }
        }

        return formatted;
    }
}
