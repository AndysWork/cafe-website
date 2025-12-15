using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Cafe.Api.Services;

public class EmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;
    private readonly string _smtpHost;
    private readonly int _smtpPort;
    private readonly string _smtpUsername;
    private readonly string _smtpPassword;
    private readonly string _fromEmail;
    private readonly string _fromName;
    private readonly string _baseUrl;
    private readonly bool _useSsl;
    private readonly bool _isEnabled;

    public EmailService(IConfiguration config, ILogger<EmailService> logger)
    {
        _config = config;
        _logger = logger;
        
        _smtpHost = _config["EmailService:SmtpHost"] ?? "smtp.gmail.com";
        _smtpPort = int.TryParse(_config["EmailService:SmtpPort"], out var port) ? port : 587;
        _smtpUsername = _config["EmailService:SmtpUsername"] ?? string.Empty;
        _smtpPassword = _config["EmailService:SmtpPassword"] ?? string.Empty;
        
        // Parse FromEmail to extract just the email address if it contains display name format
        var fromEmailConfig = _config["EmailService:FromEmail"] ?? "noreply@cafemaatara.com";
        _fromEmail = ExtractEmailAddress(fromEmailConfig);
        
        _fromName = _config["EmailService:FromName"] ?? "Cafe Maatara";
        _baseUrl = _config["EmailService:BaseUrl"] ?? "http://localhost:4200";
        _useSsl = bool.TryParse(_config["EmailService:UseSsl"], out var useSsl) ? useSsl : true;
        
        _isEnabled = !string.IsNullOrEmpty(_smtpUsername) && 
                     !string.IsNullOrEmpty(_smtpPassword) && 
                     _smtpUsername != "your-gmail@gmail.com" &&
                     _smtpPassword != "your-app-password-here";

        if (!_isEnabled)
        {
            _logger.LogWarning("Email service is disabled. Configure SMTP credentials in local.settings.json to enable.");
        }
    }

    private static string ExtractEmailAddress(string emailConfig)
    {
        // Handle formats like "Display Name <email@example.com>" or just "email@example.com"
        if (emailConfig.Contains('<') && emailConfig.Contains('>'))
        {
            var startIndex = emailConfig.IndexOf('<') + 1;
            var endIndex = emailConfig.IndexOf('>');
            return emailConfig.Substring(startIndex, endIndex - startIndex).Trim();
        }
        return emailConfig.Trim();
    }

    public async Task<bool> SendPasswordResetEmailAsync(string toEmail, string userName, string resetToken)
    {
        if (!_isEnabled)
        {
            _logger.LogWarning($"Email service disabled. Password reset token for {toEmail}: {resetToken}");
            _logger.LogWarning($"Reset link: {_baseUrl}/reset-password?token={resetToken}");
            return false;
        }

        var subject = "Password Reset Request - Cafe Maatara";
        var resetLink = $"{_baseUrl}/reset-password?token={resetToken}";
        
        var htmlContent = GetPasswordResetTemplate(userName, resetLink);
        var plainTextContent = $@"
Hi {userName},

You requested to reset your password for Cafe Maatara.

Click the link below to reset your password (valid for 1 hour):
{resetLink}

If you didn't request this, please ignore this email and your password will remain unchanged.

Best regards,
Cafe Maatara Team
";

        return await SendEmailAsync(toEmail, subject, htmlContent, plainTextContent);
    }

    public async Task<bool> SendPasswordChangedNotificationAsync(string toEmail, string userName)
    {
        if (!_isEnabled)
        {
            _logger.LogWarning($"Email service disabled. Password changed notification for {toEmail}");
            return false;
        }

        var subject = "Password Changed Successfully - Cafe Maatara";
        
        var htmlContent = GetPasswordChangedTemplate(userName);
        var plainTextContent = $@"
Hi {userName},

Your password has been changed successfully.

If you didn't make this change, please contact us immediately.

Best regards,
Cafe Maatara Team
";

        return await SendEmailAsync(toEmail, subject, htmlContent, plainTextContent);
    }

    public async Task<bool> SendProfileUpdatedNotificationAsync(string toEmail, string userName)
    {
        if (!_isEnabled)
        {
            _logger.LogWarning($"Email service disabled. Profile updated notification for {toEmail}");
            return false;
        }

        var subject = "Profile Updated Successfully - Cafe Maatara";
        
        var htmlContent = GetProfileUpdatedTemplate(userName);
        var plainTextContent = $@"
Hi {userName},

Your profile has been updated successfully.

If you didn't make this change, please contact us immediately.

Best regards,
Cafe Maatara Team
";

        return await SendEmailAsync(toEmail, subject, htmlContent, plainTextContent);
    }

    public async Task<bool> SendOrderConfirmationEmailAsync(string toEmail, string userName, string orderId, decimal total)
    {
        if (!_isEnabled)
        {
            _logger.LogWarning($"Email service disabled. Order confirmation for {toEmail}, Order: {orderId}");
            return false;
        }

        var subject = $"Order Confirmed #{orderId} - Cafe Maatara";
        
        var htmlContent = GetOrderConfirmationTemplate(userName, orderId, total);
        var plainTextContent = $@"
Hi {userName},

Your order #{orderId} has been confirmed!

Total: ₹{total:N2}

We'll notify you when your order is ready.

Best regards,
Cafe Maatara Team
";

        return await SendEmailAsync(toEmail, subject, htmlContent, plainTextContent);
    }

    public async Task<bool> SendOrderStatusUpdateEmailAsync(string toEmail, string userName, string orderId, string status)
    {
        if (!_isEnabled)
        {
            _logger.LogWarning($"Email service disabled. Order status update for {toEmail}, Order: {orderId}, Status: {status}");
            return false;
        }

        var subject = $"Order #{orderId} - {status} - Cafe Maatara";
        
        var htmlContent = GetOrderStatusUpdateTemplate(userName, orderId, status);
        var plainTextContent = $@"
Hi {userName},

Your order #{orderId} is now: {status}

Best regards,
Cafe Maatara Team
";

        return await SendEmailAsync(toEmail, subject, htmlContent, plainTextContent);
    }

    public async Task<bool> SendWelcomeEmailAsync(string toEmail, string userName)
    {
        if (!_isEnabled)
        {
            _logger.LogWarning($"Email service disabled. Welcome email for {toEmail}");
            return false;
        }

        var subject = "Welcome to Cafe Maatara! 🎉";
        
        var htmlContent = GetWelcomeTemplate(userName);
        var plainTextContent = $@"
Hi {userName},

Welcome to Cafe Maatara!

We're excited to have you join our community. Enjoy our delicious menu and earn rewards with every order!

Best regards,
Cafe Maatara Team
";

        return await SendEmailAsync(toEmail, subject, htmlContent, plainTextContent);
    }

    public async Task<bool> SendPromotionalEmailAsync(string toEmail, string userName, string subject, string content)
    {
        if (!_isEnabled)
        {
            _logger.LogWarning($"Email service disabled. Promotional email for {toEmail}");
            return false;
        }

        var htmlContent = GetPromotionalTemplate(userName, content);
        var plainTextContent = $@"
Hi {userName},

{content}

Best regards,
Cafe Maatara Team
";

        return await SendEmailAsync(toEmail, subject, htmlContent, plainTextContent);
    }

    private async Task<bool> SendEmailAsync(string toEmail, string subject, string htmlContent, string plainTextContent)
    {
        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_fromName, _fromEmail));
            message.To.Add(new MailboxAddress("", toEmail));
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder
            {
                HtmlBody = htmlContent,
                TextBody = plainTextContent
            };
            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();
            
            // Connect to SMTP server
            await client.ConnectAsync(_smtpHost, _smtpPort, _useSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto);
            
            // Authenticate
            await client.AuthenticateAsync(_smtpUsername, _smtpPassword);
            
            // Send email
            await client.SendAsync(message);
            
            // Disconnect
            await client.DisconnectAsync(true);

            _logger.LogInformation($"Email sent successfully to {toEmail}: {subject}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error sending email to {toEmail}: {subject}");
            return false;
        }
    }

    #region Email Templates

    private string GetPasswordResetTemplate(string userName, string resetLink)
    {
        return $@"
<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Password Reset</title>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px; }}
        .container {{ background-color: #f9f9f9; border-radius: 10px; padding: 30px; }}
        .header {{ background-color: #8B4513; color: white; padding: 20px; border-radius: 10px 10px 0 0; text-align: center; }}
        .content {{ background-color: white; padding: 30px; border-radius: 0 0 10px 10px; }}
        .button {{ display: inline-block; padding: 12px 30px; background-color: #8B4513; color: white; text-decoration: none; border-radius: 5px; margin: 20px 0; }}
        .footer {{ margin-top: 30px; text-align: center; color: #666; font-size: 12px; }}
        .warning {{ background-color: #fff3cd; border-left: 4px solid #ffc107; padding: 12px; margin: 20px 0; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>🔒 Password Reset Request</h1>
        </div>
        <div class='content'>
            <p>Hi <strong>{userName}</strong>,</p>
            
            <p>You requested to reset your password for your Cafe Maatara account.</p>
            
            <p>Click the button below to reset your password:</p>
            
            <div style='text-align: center;'>
                <a href='{resetLink}' class='button'>Reset Password</a>
            </div>
            
            <div class='warning'>
                <strong>⏰ Important:</strong> This link will expire in 1 hour for security reasons.
            </div>
            
            <p>If the button doesn't work, copy and paste this link into your browser:</p>
            <p style='word-break: break-all; background-color: #f5f5f5; padding: 10px; border-radius: 5px;'>{resetLink}</p>
            
            <p style='margin-top: 30px;'>If you didn't request this password reset, please ignore this email. Your password will remain unchanged.</p>
            
            <p>Best regards,<br><strong>Cafe Maatara Team</strong></p>
        </div>
        <div class='footer'>
            <p>© 2024 Cafe Maatara. All rights reserved.</p>
            <p>This is an automated message, please do not reply to this email.</p>
        </div>
    </div>
</body>
</html>";
    }

    private string GetPasswordChangedTemplate(string userName)
    {
        return $@"
<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Password Changed</title>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px; }}
        .container {{ background-color: #f9f9f9; border-radius: 10px; padding: 30px; }}
        .header {{ background-color: #28a745; color: white; padding: 20px; border-radius: 10px 10px 0 0; text-align: center; }}
        .content {{ background-color: white; padding: 30px; border-radius: 0 0 10px 10px; }}
        .alert {{ background-color: #d4edda; border-left: 4px solid #28a745; padding: 12px; margin: 20px 0; }}
        .footer {{ margin-top: 30px; text-align: center; color: #666; font-size: 12px; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>✅ Password Changed Successfully</h1>
        </div>
        <div class='content'>
            <p>Hi <strong>{userName}</strong>,</p>
            
            <div class='alert'>
                <strong>✓ Success!</strong> Your password has been changed successfully.
            </div>
            
            <p>Your account security is important to us. This email confirms that your password was recently changed.</p>
            
            <p><strong>If you made this change:</strong><br>
            No further action is needed. Your account is secure.</p>
            
            <p><strong>If you didn't make this change:</strong><br>
            Please contact us immediately at support@cafemaatara.com or call us to secure your account.</p>
            
            <p>Best regards,<br><strong>Cafe Maatara Team</strong></p>
        </div>
        <div class='footer'>
            <p>© 2024 Cafe Maatara. All rights reserved.</p>
        </div>
    </div>
</body>
</html>";
    }

    private string GetProfileUpdatedTemplate(string userName)
    {
        return $@"
<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Profile Updated</title>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px; }}
        .container {{ background-color: #f9f9f9; border-radius: 10px; padding: 30px; }}
        .header {{ background-color: #17a2b8; color: white; padding: 20px; border-radius: 10px 10px 0 0; text-align: center; }}
        .content {{ background-color: white; padding: 30px; border-radius: 0 0 10px 10px; }}
        .footer {{ margin-top: 30px; text-align: center; color: #666; font-size: 12px; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>📝 Profile Updated</h1>
        </div>
        <div class='content'>
            <p>Hi <strong>{userName}</strong>,</p>
            
            <p>Your profile has been updated successfully.</p>
            
            <p>If you didn't make this change, please contact us immediately.</p>
            
            <p>Best regards,<br><strong>Cafe Maatara Team</strong></p>
        </div>
        <div class='footer'>
            <p>© 2024 Cafe Maatara. All rights reserved.</p>
        </div>
    </div>
</body>
</html>";
    }

    private string GetOrderConfirmationTemplate(string userName, string orderId, decimal total)
    {
        return $@"
<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Order Confirmation</title>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px; }}
        .container {{ background-color: #f9f9f9; border-radius: 10px; padding: 30px; }}
        .header {{ background-color: #8B4513; color: white; padding: 20px; border-radius: 10px 10px 0 0; text-align: center; }}
        .content {{ background-color: white; padding: 30px; border-radius: 0 0 10px 10px; }}
        .order-box {{ background-color: #f8f9fa; border: 2px solid #8B4513; border-radius: 8px; padding: 20px; margin: 20px 0; }}
        .total {{ font-size: 24px; color: #8B4513; font-weight: bold; text-align: center; margin: 20px 0; }}
        .footer {{ margin-top: 30px; text-align: center; color: #666; font-size: 12px; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>🎉 Order Confirmed!</h1>
        </div>
        <div class='content'>
            <p>Hi <strong>{userName}</strong>,</p>
            
            <p>Thank you for your order! We've received your order and we're getting it ready.</p>
            
            <div class='order-box'>
                <p><strong>Order Number:</strong> #{orderId}</p>
                <p class='total'>Total: ₹{total:N2}</p>
            </div>
            
            <p>We'll notify you when your order status changes.</p>
            
            <p>Thank you for choosing Cafe Maatara!</p>
            
            <p>Best regards,<br><strong>Cafe Maatara Team</strong></p>
        </div>
        <div class='footer'>
            <p>© 2024 Cafe Maatara. All rights reserved.</p>
        </div>
    </div>
</body>
</html>";
    }

    private string GetOrderStatusUpdateTemplate(string userName, string orderId, string status)
    {
        var statusEmoji = status.ToLower() switch
        {
            "preparing" => "👨‍🍳",
            "ready" => "✅",
            "delivered" => "🚚",
            "completed" => "🎉",
            "cancelled" => "❌",
            _ => "📦"
        };

        return $@"
<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Order Status Update</title>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px; }}
        .container {{ background-color: #f9f9f9; border-radius: 10px; padding: 30px; }}
        .header {{ background-color: #8B4513; color: white; padding: 20px; border-radius: 10px 10px 0 0; text-align: center; }}
        .content {{ background-color: white; padding: 30px; border-radius: 0 0 10px 10px; }}
        .status-box {{ background-color: #d4edda; border-left: 4px solid #28a745; padding: 20px; margin: 20px 0; text-align: center; }}
        .footer {{ margin-top: 30px; text-align: center; color: #666; font-size: 12px; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>{statusEmoji} Order Update</h1>
        </div>
        <div class='content'>
            <p>Hi <strong>{userName}</strong>,</p>
            
            <p>Your order status has been updated:</p>
            
            <div class='status-box'>
                <p><strong>Order #</strong>{orderId}</p>
                <h2 style='margin: 10px 0; color: #28a745;'>{status.ToUpper()}</h2>
            </div>
            
            <p>Thank you for your patience!</p>
            
            <p>Best regards,<br><strong>Cafe Maatara Team</strong></p>
        </div>
        <div class='footer'>
            <p>© 2024 Cafe Maatara. All rights reserved.</p>
        </div>
    </div>
</body>
</html>";
    }

    private string GetWelcomeTemplate(string userName)
    {
        return $@"
<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Welcome to Cafe Maatara</title>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px; }}
        .container {{ background-color: #f9f9f9; border-radius: 10px; padding: 30px; }}
        .header {{ background-color: #8B4513; color: white; padding: 30px; border-radius: 10px 10px 0 0; text-align: center; }}
        .content {{ background-color: white; padding: 30px; border-radius: 0 0 10px 10px; }}
        .features {{ display: flex; justify-content: space-around; margin: 30px 0; }}
        .feature {{ text-align: center; flex: 1; padding: 10px; }}
        .footer {{ margin-top: 30px; text-align: center; color: #666; font-size: 12px; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>☕ Welcome to Cafe Maatara!</h1>
        </div>
        <div class='content'>
            <p>Hi <strong>{userName}</strong>,</p>
            
            <p>Welcome to the Cafe Maatara family! 🎉</p>
            
            <p>We're thrilled to have you join us. Get ready to enjoy:</p>
            
            <div class='features'>
                <div class='feature'>
                    <h3>☕</h3>
                    <p>Fresh Coffee</p>
                </div>
                <div class='feature'>
                    <h3>🍰</h3>
                    <p>Delicious Food</p>
                </div>
                <div class='feature'>
                    <h3>🎁</h3>
                    <p>Loyalty Rewards</p>
                </div>
            </div>
            
            <p>Start exploring our menu and earn rewards with every order!</p>
            
            <p>If you have any questions, feel free to reach out to us.</p>
            
            <p>Happy ordering!<br><strong>Cafe Maatara Team</strong></p>
        </div>
        <div class='footer'>
            <p>© 2024 Cafe Maatara. All rights reserved.</p>
        </div>
    </div>
</body>
</html>";
    }

    private string GetPromotionalTemplate(string userName, string content)
    {
        return $@"
<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Special Offer</title>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px; }}
        .container {{ background-color: #f9f9f9; border-radius: 10px; padding: 30px; }}
        .header {{ background-color: #ff6b6b; color: white; padding: 20px; border-radius: 10px 10px 0 0; text-align: center; }}
        .content {{ background-color: white; padding: 30px; border-radius: 0 0 10px 10px; }}
        .footer {{ margin-top: 30px; text-align: center; color: #666; font-size: 12px; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>🎉 Special Offer!</h1>
        </div>
        <div class='content'>
            <p>Hi <strong>{userName}</strong>,</p>
            
            {content}
            
            <p>Best regards,<br><strong>Cafe Maatara Team</strong></p>
        </div>
        <div class='footer'>
            <p>© 2024 Cafe Maatara. All rights reserved.</p>
            <p>To unsubscribe from promotional emails, please contact us.</p>
        </div>
    </div>
</body>
</html>";
    }

    #endregion
}
