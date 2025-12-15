# Email Notification Service Documentation

## Overview

The Email Notification Service provides a comprehensive email delivery system for the Cafe Website application using SendGrid. It supports transactional emails for authentication, orders, and promotional campaigns.

**Implementation Date:** December 2024  
**Status:** ✅ Fully Implemented  
**Provider:** SendGrid  
**Email Templates:** 7 Built-in Templates

---

## Table of Contents

1. [Features](#features)
2. [Setup & Configuration](#setup--configuration)
3. [Email Types](#email-types)
4. [API Reference](#api-reference)
5. [Email Templates](#email-templates)
6. [Testing](#testing)
7. [Production Deployment](#production-deployment)
8. [Troubleshooting](#troubleshooting)

---

## Features

### ✅ Implemented Email Types

1. **Password Reset** - Secure token-based password recovery
2. **Password Changed** - Security notification after password change
3. **Profile Updated** - Confirmation of profile changes
4. **Order Confirmation** - Receipt after successful order
5. **Order Status Updates** - Notifications for order progress
6. **Welcome Email** - Onboarding for new users
7. **Promotional Emails** - Marketing campaigns and offers

### Key Features

- ✅ HTML & Plain Text email support
- ✅ Responsive email templates
- ✅ SendGrid integration
- ✅ Automatic fallback to console logging (development)
- ✅ Error handling and logging
- ✅ Configuration-based enable/disable
- ✅ IST timezone support
- ✅ Branded email design

---

## Setup & Configuration

### 1. Install SendGrid Package

Already included in `api.csproj`:
```xml
<PackageReference Include="SendGrid" Version="9.29.3" />
```

### 2. Get SendGrid API Key

**Option A: Free SendGrid Account**
1. Visit [SendGrid.com](https://sendgrid.com)
2. Sign up for free account (100 emails/day)
3. Navigate to Settings → API Keys
4. Create new API key with "Full Access"
5. Copy the API key (starts with `SG.`)

**Option B: Azure Communication Services**
- Alternative to SendGrid
- Integrated with Azure Functions
- Pay-as-you-go pricing

### 3. Configure Local Settings

Update `api/local.settings.json`:
```json
{
  "Values": {
    "EmailService__SendGridApiKey": "SG.your-actual-api-key-here",
    "EmailService__FromEmail": "noreply@cafemaatara.com",
    "EmailService__FromName": "Cafe Maatara",
    "EmailService__BaseUrl": "http://localhost:4200"
  }
}
```

**Configuration Options:**

| Key | Description | Default | Required |
|-----|-------------|---------|----------|
| `SendGridApiKey` | SendGrid API key | - | Yes* |
| `FromEmail` | Sender email address | `noreply@cafemaatara.com` | No |
| `FromName` | Sender display name | `Cafe Maatara` | No |
| `BaseUrl` | Frontend URL for links | `http://localhost:4200` | No |

*If not configured, service falls back to console logging

### 4. Service Registration

Already registered in `api/Program.cs`:
```csharp
s.AddSingleton<IEmailService, EmailService>();
```

### 5. Verify Email Domain (Production)

For production, verify your domain with SendGrid:
1. Add sender authentication
2. Verify DNS records (SPF, DKIM)
3. Set up domain authentication
4. Use verified domain for `FromEmail`

---

## Email Types

### 1. Password Reset Email

**Triggered:** When user requests password reset  
**Function:** `ForgotPassword` in `AuthFunction.cs`

**Content:**
- Secure reset link with token
- 1-hour expiration warning
- Security instructions
- Branded design

**Code:**
```csharp
await _emailService.SendPasswordResetEmailAsync(
    user.Email!, 
    user.FirstName ?? user.Username, 
    resetToken.Token
);
```

### 2. Password Changed Notification

**Triggered:** After successful password change  
**Function:** `ChangePassword` in `AuthFunction.cs`

**Content:**
- Confirmation of password change
- Security alert instructions
- Contact information if unauthorized

**Code:**
```csharp
await _emailService.SendPasswordChangedNotificationAsync(
    user.Email!, 
    user.FirstName ?? user.Username
);
```

### 3. Profile Updated Notification

**Triggered:** After profile update  
**Function:** Can be added to `UpdateProfile`

**Content:**
- Confirmation of profile changes
- Security notice

**Code:**
```csharp
await _emailService.SendProfileUpdatedNotificationAsync(
    user.Email!, 
    user.FirstName ?? user.Username
);
```

### 4. Order Confirmation

**Triggered:** After successful order placement  
**Function:** Can be added to `OrderFunction`

**Content:**
- Order number
- Total amount
- Estimated delivery/pickup time
- Order details

**Code:**
```csharp
await _emailService.SendOrderConfirmationEmailAsync(
    user.Email!, 
    user.FirstName ?? user.Username,
    order.Id!,
    order.Total
);
```

### 5. Order Status Update

**Triggered:** When order status changes  
**Function:** Can be added to order status update logic

**Content:**
- Order number
- New status (Preparing, Ready, Delivered)
- Status-specific emoji
- Tracking information

**Code:**
```csharp
await _emailService.SendOrderStatusUpdateEmailAsync(
    user.Email!, 
    user.FirstName ?? user.Username,
    order.Id!,
    "Preparing" // or "Ready", "Delivered", etc.
);
```

### 6. Welcome Email

**Triggered:** After successful registration  
**Function:** `Register` in `AuthFunction.cs`

**Content:**
- Welcome message
- Feature highlights (Coffee, Food, Rewards)
- Getting started instructions

**Code:**
```csharp
await _emailService.SendWelcomeEmailAsync(
    user.Email!, 
    user.FirstName ?? user.Username
);
```

### 7. Promotional Email

**Triggered:** Manually or via marketing campaigns  
**Function:** Custom admin function (to be created)

**Content:**
- Custom subject
- Custom content
- Branded template
- Unsubscribe notice

**Code:**
```csharp
await _emailService.SendPromotionalEmailAsync(
    user.Email!, 
    user.FirstName ?? user.Username,
    "Special Offer: 20% Off Today!",
    "<p>Use code <strong>CAFE20</strong> for 20% off your next order!</p>"
);
```

---

## API Reference

### IEmailService Interface

```csharp
public interface IEmailService
{
    Task<bool> SendPasswordResetEmailAsync(string toEmail, string userName, string resetToken);
    Task<bool> SendPasswordChangedNotificationAsync(string toEmail, string userName);
    Task<bool> SendProfileUpdatedNotificationAsync(string toEmail, string userName);
    Task<bool> SendOrderConfirmationEmailAsync(string toEmail, string userName, string orderId, decimal total);
    Task<bool> SendOrderStatusUpdateEmailAsync(string toEmail, string userName, string orderId, string status);
    Task<bool> SendWelcomeEmailAsync(string toEmail, string userName);
    Task<bool> SendPromotionalEmailAsync(string toEmail, string userName, string subject, string content);
}
```

### Return Values

- `true` - Email sent successfully
- `false` - Email failed to send (check logs)

### Error Handling

All methods include comprehensive error handling:
- Logs errors to Application Insights
- Returns `false` on failure
- Does not throw exceptions
- Falls back to console logging if service disabled

---

## Email Templates

### Template Structure

All templates include:
- **Responsive Design** - Works on mobile and desktop
- **Branded Header** - Cafe Maatara logo and colors
- **Content Area** - Main message body
- **Footer** - Copyright and contact info
- **Plain Text Fallback** - For email clients without HTML support

### Template Colors

- **Primary:** `#8B4513` (Saddle Brown - Coffee theme)
- **Success:** `#28a745` (Green)
- **Info:** `#17a2b8` (Teal)
- **Warning:** `#ffc107` (Yellow)
- **Danger:** `#dc3545` (Red)

### Customizing Templates

Templates are defined in `api/Services/EmailService.cs`:

1. **Locate Template Method:**
```csharp
private string GetPasswordResetTemplate(string userName, string resetLink)
```

2. **Modify HTML:**
```html
<div class='header'>
    <h1>🔒 Password Reset Request</h1>
</div>
```

3. **Update Styles:**
```html
<style>
    .header { 
        background-color: #8B4513; 
        color: white; 
    }
</style>
```

### Template Best Practices

- Keep total size under 100KB
- Use inline CSS (better email client support)
- Test across email clients (Gmail, Outlook, Apple Mail)
- Include plain text version
- Add alt text to images
- Use web-safe fonts

---

## Testing

### Development Testing (Console Logging)

If `SendGridApiKey` is not configured, emails log to console:

```
Email service disabled. Password reset token for user@example.com: abc123...
Reset link: http://localhost:4200/reset-password?token=abc123...
```

### Enable Email Sending (Development)

1. Get SendGrid API key (free tier)
2. Update `local.settings.json`
3. Restart Azure Functions
4. Trigger email action (e.g., forgot password)

### Test Endpoints

**1. Test Password Reset Email:**
```bash
curl -X POST http://localhost:7071/api/auth/password/forgot \
  -H "Content-Type: application/json" \
  -d '{"email":"test@example.com"}'
```

**2. Test Welcome Email:**
```bash
curl -X POST http://localhost:7071/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{
    "username":"testuser",
    "email":"test@example.com",
    "password":"Test@123",
    "firstName":"Test",
    "lastName":"User",
    "phoneNumber":"9876543210"
  }'
```

**3. Test Password Changed Email:**
```bash
# First login to get token
TOKEN="your-jwt-token"

curl -X POST http://localhost:7071/api/auth/password/change \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "currentPassword":"Old@123",
    "newPassword":"New@456",
    "confirmPassword":"New@456"
  }'
```

### Verify Email Delivery

1. **SendGrid Dashboard:**
   - Navigate to Activity
   - View email statistics
   - Check delivery status

2. **Check Logs:**
```csharp
_logger.LogInformation($"Email sent successfully to {toEmail}: {subject}");
_logger.LogError($"Failed to send email to {toEmail}. Status: {response.StatusCode}");
```

3. **Email Client:**
   - Check inbox (including spam folder)
   - Verify formatting
   - Test links

---

## Production Deployment

### Pre-Deployment Checklist

- [ ] SendGrid API key configured
- [ ] Domain verified with SendGrid
- [ ] SPF/DKIM records added to DNS
- [ ] Email templates tested
- [ ] Sender email matches verified domain
- [ ] Base URL points to production frontend
- [ ] Error logging configured
- [ ] Email rate limits understood

### Environment Variables (Azure)

Configure in Azure Function App Settings:

```
EmailService__SendGridApiKey=SG.production-key-here
EmailService__FromEmail=noreply@yourdomain.com
EmailService__FromName=Cafe Maatara
EmailService__BaseUrl=https://yourdomain.com
```

### SendGrid Production Setup

1. **Verify Domain:**
   - Settings → Sender Authentication
   - Authenticate Domain
   - Add DNS records provided by SendGrid

2. **Set Up Email Tracking:**
   - Enable click tracking
   - Enable open tracking
   - Configure unsubscribe groups

3. **Configure Templates (Optional):**
   - Use SendGrid's template editor
   - Version control templates
   - A/B test email designs

4. **Monitor Deliverability:**
   - Check bounce rates
   - Monitor spam reports
   - Review email analytics

### Rate Limits

**SendGrid Free Tier:**
- 100 emails/day
- No credit card required

**Paid Plans:**
- Essentials: 40,000 emails/month ($19.95/month)
- Pro: 100,000 emails/month ($89.95/month)

### Security Best Practices

- ✅ Use environment variables (never hardcode API keys)
- ✅ Rotate API keys quarterly
- ✅ Use restricted API keys (minimum permissions)
- ✅ Monitor for unauthorized usage
- ✅ Enable two-factor authentication on SendGrid account

---

## Troubleshooting

### Email Not Sending

**1. Check Service Configuration:**
```csharp
// Look for this log message
"Email service is disabled. Configure SendGridApiKey in local.settings.json to enable."
```

**Solution:** Add valid SendGrid API key

**2. Invalid API Key:**
```csharp
// Error log
"Failed to send email to user@example.com. Status: Unauthorized"
```

**Solution:** 
- Verify API key is correct
- Ensure key has "Full Access" or "Mail Send" permission
- Check key hasn't been revoked

**3. Domain Not Verified:**
```csharp
// Error log
"Failed to send email. Status: Forbidden"
```

**Solution:** Verify sender domain in SendGrid

### Emails Going to Spam

**Causes:**
- Domain not verified
- Missing SPF/DKIM records
- Poor sender reputation
- Spammy content

**Solutions:**
1. Verify domain with SendGrid
2. Add SPF record: `v=spf1 include:sendgrid.net ~all`
3. Add DKIM records from SendGrid
4. Warm up IP address (gradually increase volume)
5. Review email content for spam triggers

### Rate Limit Exceeded

**Error:**
```
Status: TooManyRequests
```

**Solution:**
- Upgrade SendGrid plan
- Implement email queuing
- Batch non-urgent emails

### Template Not Rendering

**Issue:** HTML displays as plain text

**Solution:**
- Verify email client supports HTML
- Check HTML is valid
- Test with web-based email clients first
- Ensure `htmlContent` parameter is used

---

## Integration Examples

### Add Email to Order Function

```csharp
// In OrderFunction.cs
private readonly IEmailService _emailService;

public OrderFunction(MongoService mongo, IEmailService emailService)
{
    _mongo = mongo;
    _emailService = emailService;
}

[Function("CreateOrder")]
public async Task<HttpResponseData> CreateOrder(...)
{
    // ... order creation logic ...
    
    // Send confirmation email
    var user = await _mongo.GetUserByIdAsync(order.UserId);
    await _emailService.SendOrderConfirmationEmailAsync(
        user.Email!,
        user.FirstName ?? user.Username,
        order.Id!,
        order.Total
    );
    
    // ... rest of function ...
}
```

### Add Email to Order Status Update

```csharp
[Function("UpdateOrderStatus")]
public async Task<HttpResponseData> UpdateOrderStatus(...)
{
    // ... status update logic ...
    
    // Send status update email
    var user = await _mongo.GetUserByIdAsync(order.UserId);
    await _emailService.SendOrderStatusUpdateEmailAsync(
        user.Email!,
        user.FirstName ?? user.Username,
        order.Id!,
        newStatus
    );
    
    // ... rest of function ...
}
```

### Bulk Email Campaign (Admin)

```csharp
[Function("SendPromotionalCampaign")]
public async Task<HttpResponseData> SendPromotionalCampaign(...)
{
    // Get all active users
    var users = await _mongo.GetAllUsersAsync();
    var activeUsers = users.Where(u => u.IsActive).ToList();
    
    var subject = "Special Weekend Offer - 20% Off!";
    var content = @"
        <h2>Weekend Special!</h2>
        <p>Enjoy <strong>20% off</strong> on all orders this weekend.</p>
        <p>Use code: <strong>WEEKEND20</strong></p>
    ";
    
    foreach (var user in activeUsers)
    {
        await _emailService.SendPromotionalEmailAsync(
            user.Email!,
            user.FirstName ?? user.Username,
            subject,
            content
        );
        
        // Add delay to avoid rate limits
        await Task.Delay(100);
    }
    
    return req.CreateResponse(HttpStatusCode.OK);
}
```

---

## Monitoring & Analytics

### SendGrid Dashboard

**Key Metrics:**
- Delivered emails
- Bounce rate
- Spam reports
- Open rate (if tracking enabled)
- Click rate (if tracking enabled)

### Application Logs

All email operations are logged:

```csharp
// Success
_logger.LogInformation($"Email sent successfully to {toEmail}: {subject}");

// Failure
_logger.LogError($"Failed to send email to {toEmail}. Status: {response.StatusCode}, Body: {body}");

// Disabled
_logger.LogWarning($"Email service disabled. Password reset token for {toEmail}: {resetToken}");
```

### Custom Tracking

Add custom tracking IDs:

```csharp
var msg = MailHelper.CreateSingleEmail(from, to, subject, plainText, htmlContent);
msg.AddCustomArg("userId", user.Id);
msg.AddCustomArg("emailType", "passwordReset");
```

---

## Future Enhancements

### Planned Features

1. **Email Templates in Database**
   - Admin interface to edit templates
   - Version control for templates
   - A/B testing support

2. **Email Preferences**
   - User-configurable notifications
   - Unsubscribe management
   - Frequency controls

3. **Email Queue**
   - Batch processing
   - Retry logic
   - Priority queue

4. **Advanced Analytics**
   - Custom email reports
   - Conversion tracking
   - ROI measurement

5. **Multi-Language Support**
   - Template localization
   - User language preference
   - Dynamic content

---

## Additional Resources

### SendGrid Documentation
- [SendGrid API Reference](https://docs.sendgrid.com/api-reference)
- [Email Best Practices](https://sendgrid.com/resource/email-best-practices/)
- [Deliverability Guide](https://sendgrid.com/resource/email-deliverability-guide/)

### Email Design
- [Email on Acid](https://www.emailonacid.com/) - Email testing
- [Litmus](https://litmus.com/) - Email preview across clients
- [Can I Email](https://www.caniemail.com/) - HTML/CSS support reference

### Email Regulations
- CAN-SPAM Act compliance
- GDPR email requirements
- Unsubscribe best practices

---

## Support

For issues or questions:
1. Check logs for error messages
2. Review SendGrid dashboard
3. Consult this documentation
4. Contact development team

---

**Last Updated:** December 2024  
**Version:** 1.0  
**Status:** ✅ Production Ready
