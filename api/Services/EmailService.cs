using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Cafe.Api.Models;

namespace Cafe.Api.Services;

public class EmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;
    private readonly MongoService _mongo;
    private readonly string _smtpHost;
    private readonly int _smtpPort;
    private readonly string _smtpUsername;
    private readonly string _smtpPassword;
    private readonly string _fromEmail;
    private readonly string _fromName;
    private readonly string _baseUrl;
    private readonly bool _useSsl;
    private readonly bool _isEnabled;

    public EmailService(IConfiguration config, ILogger<EmailService> logger, MongoService mongo)
    {
        _config = config;
        _logger = logger;
        _mongo = mongo;
        
        _smtpHost = _config["EmailService:SmtpHost"] ?? "smtp.gmail.com";
        _smtpPort = int.TryParse(_config["EmailService:SmtpPort"], out var port) ? port : 587;
        _smtpUsername = _config["EmailService:SmtpUsername"] ?? string.Empty;
        _smtpPassword = _config["EmailService:SmtpPassword"] ?? string.Empty;
        
        // Parse FromEmail to extract just the email address if it contains display name format
        var fromEmailConfig = _config["EmailService:FromEmail"] ?? "noreply@cafemaatara.com";
        _fromEmail = ExtractEmailAddress(fromEmailConfig);
        
        _fromName = _config["EmailService:FromName"] ?? "Maa Tara Cafe";
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

        var subject = "Password Reset Request - Maa Tara Cafe";
        var resetLink = $"{_baseUrl}/reset-password?token={resetToken}";
        
        var htmlContent = GetPasswordResetTemplate(userName, resetLink);
        var plainTextContent = $@"
Hi {userName},

You requested to reset your password for Maa Tara Cafe.

Click the link below to reset your password (valid for 1 hour):
{resetLink}

If you didn't request this, please ignore this email and your password will remain unchanged.

Best regards,
Maa Tara Cafe Team
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

        var subject = "Password Changed Successfully - Maa Tara Cafe";
        
        var htmlContent = GetPasswordChangedTemplate(userName);
        var plainTextContent = $@"
Hi {userName},

Your password has been changed successfully.

If you didn't make this change, please contact us immediately.

Best regards,
Maa Tara Cafe Team
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

        var subject = "Profile Updated Successfully - Maa Tara Cafe";
        
        var htmlContent = GetProfileUpdatedTemplate(userName);
        var plainTextContent = $@"
Hi {userName},

Your profile has been updated successfully.

If you didn't make this change, please contact us immediately.

Best regards,
Maa Tara Cafe Team
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

        var subject = $"Order Confirmed #{orderId} - Maa Tara Cafe";
        
        var htmlContent = GetOrderConfirmationTemplate(userName, orderId, total);
        var plainTextContent = $@"
Hi {userName},

Your order #{orderId} has been confirmed!

Total: ₹{total:N2}

We'll notify you when your order is ready.

Best regards,
Maa Tara Cafe Team
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

        var subject = $"Order #{orderId} - {status} - Maa Tara Cafe";
        
        var htmlContent = GetOrderStatusUpdateTemplate(userName, orderId, status);
        var plainTextContent = $@"
Hi {userName},

Your order #{orderId} is now: {status}

Best regards,
Maa Tara Cafe Team
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

        var subject = "Welcome to Maa Tara Cafe! 🎉";
        
        var htmlContent = GetWelcomeTemplate(userName);
        var plainTextContent = $@"
Hi {userName},

Welcome to Maa Tara Cafe!

We're excited to have you join our community. Enjoy our delicious menu and earn rewards with every order!

Best regards,
Maa Tara Cafe Team
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
Maa Tara Cafe Team
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
            
            <p>You requested to reset your password for your Maa Tara Cafe account.</p>
            
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
            
            <p>Best regards,<br><strong>Maa Tara Cafe Team</strong></p>
        </div>
        <div class='footer'>
            <p>© 2024 Maa Tara Cafe. All rights reserved.</p>
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
            
            <p>Best regards,<br><strong>Maa Tara Cafe Team</strong></p>
        </div>
        <div class='footer'>
            <p>© 2024 Maa Tara Cafe. All rights reserved.</p>
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
            
            <p>Best regards,<br><strong>Maa Tara Cafe Team</strong></p>
        </div>
        <div class='footer'>
            <p>© 2024 Maa Tara Cafe. All rights reserved.</p>
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
            
            <p>Thank you for choosing Maa Tara Cafe!</p>
            
            <p>Best regards,<br><strong>Maa Tara Cafe Team</strong></p>
        </div>
        <div class='footer'>
            <p>© 2024 Maa Tara Cafe. All rights reserved.</p>
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
            
            <p>Best regards,<br><strong>Maa Tara Cafe Team</strong></p>
        </div>
        <div class='footer'>
            <p>© 2024 Maa Tara Cafe. All rights reserved.</p>
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
    <title>Welcome to Maa Tara Cafe</title>
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
            <h1>☕ Welcome to Maa Tara Cafe!</h1>
        </div>
        <div class='content'>
            <p>Hi <strong>{userName}</strong>,</p>
            
            <p>Welcome to the Maa Tara Cafe family! 🎉</p>
            
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
            
            <p>Happy ordering!<br><strong>Maa Tara Cafe Team</strong></p>
        </div>
        <div class='footer'>
            <p>© 2024 Maa Tara Cafe. All rights reserved.</p>
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
            
            <p>Best regards,<br><strong>Maa Tara Cafe Team</strong></p>
        </div>
        <div class='footer'>
            <p>© 2024 Maa Tara Cafe. All rights reserved.</p>
            <p>To unsubscribe from promotional emails, please contact us.</p>
        </div>
    </div>
</body>
</html>";
    }

    public async Task<bool> SendPriceAlertEmailAsync(string toEmail, string subject, string htmlContent)
    {
        if (!_isEnabled)
        {
            _logger.LogWarning($"Email service disabled. Price alert would be sent to {toEmail}");
            return false;
        }

        var plainTextContent = "Please view this email in HTML format to see the price alert details.";
        return await SendEmailAsync(toEmail, subject, htmlContent, plainTextContent);
    }

    public async Task<bool> SendStaffWelcomeEmailAsync(Staff staff)
    {
        if (!_isEnabled)
        {
            _logger.LogWarning($"Email service disabled. Staff welcome email for {staff.Email}");
            return false;
        }

        var subject = $"Welcome to Maa Tara Cafe Team! 🎉 - {staff.FirstName} {staff.LastName}";
        
        var htmlContent = GetStaffWelcomeTemplate(staff);
        var plainTextContent = GetStaffWelcomePlainText(staff);

        return await SendEmailAsync(staff.Email, subject, htmlContent, plainTextContent);
    }

    private string GetStaffWelcomeTemplate(Staff staff)
    {
        var workingDays = staff.WorkingDays.Any() ? string.Join(", ", staff.WorkingDays) : "To be determined";
        var shiftTime = (!string.IsNullOrEmpty(staff.ShiftStartTime) && !string.IsNullOrEmpty(staff.ShiftEndTime)) 
            ? $"{staff.ShiftStartTime} - {staff.ShiftEndTime}" 
            : "To be determined";
        
        // Get bonus info from database configuration
        var bonusInfo = _mongo.GetBonusDescriptionForStaffAsync(staff).GetAwaiter().GetResult();

        return $@"
<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Welcome to Maa Tara Cafe</title>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; max-width: 700px; margin: 0 auto; padding: 20px; }}
        .container {{ background-color: #f9f9f9; border-radius: 10px; padding: 30px; }}
        .header {{ background-color: #8B4513; color: white; padding: 30px; border-radius: 10px 10px 0 0; text-align: center; }}
        .header h1 {{ margin: 0; font-size: 28px; }}
        .content {{ background-color: white; padding: 30px; border-radius: 0 0 10px 10px; }}
        .info-section {{ margin: 25px 0; padding: 20px; background-color: #f8f9fa; border-left: 4px solid #8B4513; border-radius: 5px; }}
        .info-section h3 {{ margin-top: 0; color: #8B4513; }}
        .info-row {{ display: flex; justify-content: space-between; padding: 10px 0; border-bottom: 1px solid #dee2e6; }}
        .info-row:last-child {{ border-bottom: none; }}
        .info-label {{ font-weight: bold; color: #666; }}
        .info-value {{ color: #333; text-align: right; }}
        .highlight {{ background-color: #fff3cd; padding: 15px; border-radius: 5px; margin: 20px 0; border-left: 4px solid #ffc107; }}
        .footer {{ margin-top: 30px; padding-top: 20px; border-top: 2px solid #dee2e6; text-align: center; color: #666; font-size: 14px; }}
        .welcome-message {{ font-size: 16px; line-height: 1.8; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>🎉 Welcome to Maa Tara Cafe Team!</h1>
        </div>
        <div class='content'>
            <div class='welcome-message'>
                <p>Dear <strong>{staff.FirstName} {staff.LastName}</strong>,</p>
                
                <p>Congratulations and welcome to the Maa Tara Cafe family! We are thrilled to have you join our team as a <strong>{staff.Position}</strong>{(string.IsNullOrEmpty(staff.Department) ? "" : $" in the {staff.Department} department")}.</p>
                
                <p>Your skills and enthusiasm will be a valuable addition to our team. We look forward to working with you and achieving great things together!</p>
            </div>

            <div class='highlight'>
                <strong>📋 Employee ID:</strong> {staff.EmployeeId}
            </div>

            <div class='info-section'>
                <h3>💼 Employment Details</h3>
                <div class='info-row'>
                    <span class='info-label'>Position:</span>
                    <span class='info-value'>{staff.Position}</span>
                </div>
                {(string.IsNullOrEmpty(staff.Department) ? "" : $@"
                <div class='info-row'>
                    <span class='info-label'>Department:</span>
                    <span class='info-value'>{staff.Department}</span>
                </div>")}
                <div class='info-row'>
                    <span class='info-label'>Employment Type:</span>
                    <span class='info-value'>{staff.EmploymentType}</span>
                </div>
                <div class='info-row'>
                    <span class='info-label'>Start Date:</span>
                    <span class='info-value'>{staff.HireDate:MMMM dd, yyyy}</span>
                </div>
                {(staff.ProbationEndDate.HasValue ? $@"
                <div class='info-row'>
                    <span class='info-label'>Probation Period Ends:</span>
                    <span class='info-value'>{staff.ProbationEndDate.Value:MMMM dd, yyyy}</span>
                </div>" : "")}
            </div>

            <div class='info-section'>
                <h3>⏰ Work Schedule</h3>
                <div class='info-row'>
                    <span class='info-label'>Working Days:</span>
                    <span class='info-value'>{workingDays}</span>
                </div>
                <div class='info-row'>
                    <span class='info-label'>Shift Timing:</span>
                    <span class='info-value'>{shiftTime}</span>
                </div>
            </div>

            <div class='info-section'>
                <h3>💰 Compensation & Benefits</h3>
                <div class='info-row'>
                    <span class='info-label'>Salary Type:</span>
                    <span class='info-value'>{staff.SalaryType}</span>
                </div>
                <div class='info-row'>
                    <span class='info-label'>Compensation:</span>
                    <span class='info-value'>₹{staff.Salary:N2} ({staff.SalaryType})</span>
                </div>
                <div class='info-row'>
                    <span class='info-label'>Bonus Calculation:</span>
                    <span class='info-value'>{bonusInfo}</span>
                </div>
                <div class='info-row'>
                    <span class='info-label'>Annual Leave:</span>
                    <span class='info-value'>{staff.AnnualLeaveBalance} days</span>
                </div>
                <div class='info-row'>
                    <span class='info-label'>Sick Leave:</span>
                    <span class='info-value'>{staff.SickLeaveBalance} days</span>
                </div>
                <div class='info-row'>
                    <span class='info-label'>Casual Leave:</span>
                    <span class='info-value'>{staff.CasualLeaveBalance} days</span>
                </div>
            </div>

            <div class='info-section'>
                <h3>📞 Contact Information</h3>
                <div class='info-row'>
                    <span class='info-label'>Email:</span>
                    <span class='info-value'>{staff.Email}</span>
                </div>
                <div class='info-row'>
                    <span class='info-label'>Phone:</span>
                    <span class='info-value'>{staff.PhoneNumber}</span>
                </div>
            </div>

            <div class='highlight'>
                <p><strong>📝 Next Steps:</strong></p>
                <ul style='margin: 10px 0; padding-left: 20px;'>
                    <li>Please arrive 15 minutes early on your first day</li>
                    <li>Bring required documents for verification</li>
                    <li>You'll receive orientation and training schedule</li>
                    <li>Contact HR for any questions or concerns</li>
                </ul>
            </div>

            {(string.IsNullOrEmpty(staff.Notes) ? "" : $@"
            <div style='margin-top: 20px; padding: 15px; background-color: #e7f3ff; border-radius: 5px;'>
                <strong>💡 Additional Information:</strong>
                <p style='margin: 10px 0 0 0;'>{staff.Notes}</p>
            </div>")}

            <p style='margin-top: 30px;'>Once again, welcome aboard! We're excited to have you as part of the Maa Tara Cafe family.</p>
            
            <p>If you have any questions, please don't hesitate to reach out to your manager or HR department.</p>

            <div class='footer'>
                <p><strong>Best Regards,</strong></p>
                <p><strong>Maa Tara Cafe Management Team</strong></p>
                <p style='color: #8B4513; margin-top: 10px;'>☕ Brewing Excellence, One Cup at a Time ☕</p>
            </div>
        </div>
    </div>
</body>
</html>";
    }

    private string GetStaffWelcomePlainText(Staff staff)
    {
        var workingDays = staff.WorkingDays.Any() ? string.Join(", ", staff.WorkingDays) : "To be determined";
        var shiftTime = (!string.IsNullOrEmpty(staff.ShiftStartTime) && !string.IsNullOrEmpty(staff.ShiftEndTime)) 
            ? $"{staff.ShiftStartTime} - {staff.ShiftEndTime}" 
            : "To be determined";
        
        // Get bonus info from database configuration
        var bonusInfo = _mongo.GetBonusDescriptionForStaffAsync(staff).GetAwaiter().GetResult();

        return $@"
Welcome to Maa Tara Cafe Team!

Dear {staff.FirstName} {staff.LastName},

Congratulations and welcome to the Maa Tara Cafe family! We are thrilled to have you join our team as a {staff.Position}{(string.IsNullOrEmpty(staff.Department) ? "" : $" in the {staff.Department} department")}.

Your skills and enthusiasm will be a valuable addition to our team. We look forward to working with you and achieving great things together!

EMPLOYEE ID: {staff.EmployeeId}

--- EMPLOYMENT DETAILS ---
Position: {staff.Position}
{(string.IsNullOrEmpty(staff.Department) ? "" : $"Department: {staff.Department}\n")}Employment Type: {staff.EmploymentType}
Start Date: {staff.HireDate:MMMM dd, yyyy}
{(staff.ProbationEndDate.HasValue ? $"Probation Period Ends: {staff.ProbationEndDate.Value:MMMM dd, yyyy}\n" : "")}
--- WORK SCHEDULE ---
Working Days: {workingDays}
Shift Timing: {shiftTime}

--- COMPENSATION & BENEFITS ---
Salary Type: {staff.SalaryType}
Compensation: ₹{staff.Salary:N2} ({staff.SalaryType})
Bonus Calculation: {bonusInfo}
Annual Leave: {staff.AnnualLeaveBalance} days
Sick Leave: {staff.SickLeaveBalance} days
Casual Leave: {staff.CasualLeaveBalance} days

--- CONTACT INFORMATION ---
Email: {staff.Email}
Phone: {staff.PhoneNumber}

NEXT STEPS:
- Please arrive 15 minutes early on your first day
- Bring required documents for verification
- You'll receive orientation and training schedule
- Contact HR for any questions or concerns
{(string.IsNullOrEmpty(staff.Notes) ? "" : $"\nADDITIONAL INFORMATION:\n{staff.Notes}\n")}
Once again, welcome aboard! We're excited to have you as part of the Maa Tara Cafe family.

If you have any questions, please don't hesitate to reach out to your manager or HR department.

Best Regards,
Maa Tara Cafe Management Team

Brewing Excellence, One Cup at a Time
";
    }

    #endregion
}
