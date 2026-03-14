# WhatsApp Integration with Twilio

## Overview

This guide will help you set up WhatsApp messaging for your Maa Tara Cafe application using Twilio's WhatsApp API. The integration enables sending automated WhatsApp messages for:

- ✅ Order confirmations
- 📦 Order status updates
- ⭐ Loyalty points notifications
- 🎁 Reward redemptions
- 👥 Staff onboarding notifications
- 🎉 Promotional offers

## Why Twilio?

✅ **Easy Setup** - Get started in minutes, no complex business verification  
✅ **Sandbox for Testing** - Free test environment with instant access  
✅ **Simple API** - Straightforward REST API with excellent documentation  
✅ **Reliable** - Enterprise-grade delivery with 99.95% uptime  
✅ **Global Reach** - Send messages to 180+ countries  

## Prerequisites

1. **Twilio Account** - Sign up at [twilio.com](https://www.twilio.com/try-twilio) (free trial available)
2. **Phone Number** - For production (included in trial for testing)
3. **Credit Card** - Required for account verification (free trial credits provided)

## Quick Start (5 Minutes) ⚡

### Step 1: Create Twilio Account

1. Go to [twilio.com/try-twilio](https://www.twilio.com/try-twilio)
2. Sign up with your email
3. Verify your email address
4. Verify your phone number
5. You'll receive **$15 in trial credits** (enough for ~3,000 WhatsApp messages!)

### Step 2: Get Your Credentials

After signing up, you'll land on the Twilio Console dashboard:

1. **Account SID** - Copy from the dashboard (starts with `AC...`)
   - Example: `ACxxxxxxxxxxxxxxxxxxxxxxxxxxxxx`
2. **Auth Token** - Click "Show" and copy the token
   - Located right below Account SID

### Step 3: Access WhatsApp Sandbox

Twilio provides a free WhatsApp sandbox for testing:

1. In Twilio Console, navigate to:
   ```
   Messaging → Try it out → Send a WhatsApp message
   ```

2. You'll see the **sandbox number**: `+1 415 523 8886` (US number)

3. **Join the sandbox** to receive messages on your phone:
   - Open WhatsApp on your phone
   - Send a message to `+1 415 523 8886`
   - Message content: `join <your-sandbox-code>`
   - Example: `join coffee-tiger` (your code will be different - shown in Twilio Console)

4. **Confirmation**: You'll receive "Joined <sandbox-name>" message

### Step 4: Configure Your Application

Update [api/local.settings.json](api/local.settings.json):

```json
{
  "Values": {
    "WhatsAppService__TwilioAccountSid": "ACxxxxxxxxxxxxxxxxxxxxxxxxxxxxx",
    "WhatsAppService__TwilioAuthToken": "your_auth_token_here",
    "WhatsAppService__TwilioFromNumber": "whatsapp:+14155238886"
  }
}
```

**Important Notes:**
- ✅ Keep the `whatsapp:` prefix in FromNumber
- ✅ The sandbox number is `+14155238886` (don't change for testing)
- ✅ Replace Account SID and Auth Token with your actual values

### Step 5: Test the Integration

```powershell
# Navigate to API folder
cd f:\MyProducts\CafeWebsite\cafe-website\api

# Build the project
dotnet build

# Run locally
func start
```

Then:
1. Open your cafe website frontend
2. Create a test order
3. **Use your phone number** (the one that joined the sandbox)
4. You should receive a WhatsApp message! 🎉

## Production Setup

Once testing is complete, move to production:

### Option 1: Use Twilio Phone Number (Recommended) ⭐

**Step 1: Buy a Twilio Phone Number**

1. Go to Twilio Console:
   ```
   Phone Numbers → Manage → Buy a number
   ```

2. **Search for a number:**
   - Select your country
   - Enable "SMS" and "MMS" capabilities (WhatsApp uses these)
   - Choose a number
   - Cost: ~$1-2/month

3. **Purchase the number**

**Step 2: Enable WhatsApp on Your Number**

1. Go to:
   ```
   Messaging → Senders → WhatsApp senders
   ```

2. Click **"New WhatsApp Sender"**

3. **Select your purchased number**

4. **Request WhatsApp Sender Access:**
   - Fill in business details:
     - Business name: Maa Tara Cafe
     - Business website: your cafe website
     - Business description: Cafe and restaurant services
   - Submit for approval

5. **Wait for Approval** (1-3 business days)
   - Twilio will verify your business
   - You'll receive email notification

**Step 3: Update Configuration**

Once approved, update [api/local.settings.json](api/local.settings.json):

```json
{
  "WhatsAppService__TwilioFromNumber": "whatsapp:+1234567890"
}
```
Replace `+1234567890` with your purchased number.

### Option 2: Use Existing WhatsApp Business Number

If you already have a WhatsApp Business API number from Meta:

1. Go to:
   ```
   Twilio Console → Messaging → Senders → WhatsApp senders
   ```

2. Click **"Bring your own WhatsApp number"**

3. Follow the integration steps (requires Meta Business Manager access)

**Note:** Option 1 is simpler and recommended for most businesses.

## Configuration Reference

### Local Development

File: `f:\MyProducts\CafeWebsite\cafe-website\api\local.settings.json`

```json
{
  "IsEncrypted": false,
  "Values": {
    // ... other settings ...
    "WhatsAppService__TwilioAccountSid": "ACxxxxxxxxxxxxxxxxxxxxxxxxxxxxx",
    "WhatsAppService__TwilioAuthToken": "your_32_character_auth_token_here",
    "WhatsAppService__TwilioFromNumber": "whatsapp:+14155238886"
  }
}
```

### Azure Production

File: `f:\MyProducts\CafeWebsite\cafe-website\azure-app-settings.json`

```json
[
  {
    "name": "WhatsAppService__TwilioAccountSid",
    "value": "ACxxxxxxxxxxxxxxxxxxxxxxxxxxxxx",
    "slotSetting": false
  },
  {
    "name": "WhatsAppService__TwilioAuthToken",
    "value": "your_auth_token_here",
    "slotSetting": false
  },
  {
    "name": "WhatsAppService__TwilioFromNumber",
    "value": "whatsapp:+14155238886",
    "slotSetting": false
  }
]
```

**Deploy to Azure:**
```powershell
.\Deploy-ToAzure.ps1
```

## Phone Number Formats

The service accepts various phone number formats and automatically converts to E.164:

### Accepted Formats

✅ **With country code:**
- `+919876543210`
- `919876543210`

✅ **Without country code (Indian numbers):**
- `9876543210` → automatically converted to `+919876543210`

❌ **Not accepted:**
- `98765-43210` (hyphens)
- `(987) 654-3210` (parentheses)
- `987 654 3210` (spaces - will be removed automatically)

### Auto-formatting

The service automatically:
1. Removes spaces, hyphens, parentheses
2. Adds `+91` prefix for 10-digit Indian numbers
3. Adds `+` if missing for country code numbers
4. Converts to E.164 format

## Message Features

### Automatic Messages

The application automatically sends WhatsApp messages for:

| Event | Trigger | Recipient | Content |
|-------|---------|-----------|---------|
| Order Created | New order placed | Customer | Order confirmation with details |
| Order Updated | Status changed | Customer | New status notification |
| Order Delivered | Status = delivered | Customer | Loyalty points earned |
| Reward Redeemed | Loyalty reward used | Customer | Redemption confirmation |
| Staff Created | New staff added | Staff member | Welcome message |

### Message Examples

**Order Confirmation:**
```
Hi John! 🎉

Your order has been confirmed!

📝 Order ID: ORD123456
💰 Total Amount: ₹450.00

📦 Order Details:
- Cappuccino x2 (₹200.00)
- Sandwich x1 (₹250.00)

Thank you for choosing Maa Tara Cafe! ☕
```

**Order Status Update:**
```
Hi John! ✅

Your order status has been updated:

📝 Order ID: ORD123456
📊 Status: Ready

Thank you for your patience! ☕
```

**Loyalty Points:**
```
Hi John! 🎁

You've earned 45 loyalty points!

⭐ Total Points: 450

Keep collecting points for more rewards! ☕
```

**Staff Welcome:**
```
Hi Sarah Kumar! 👋

Welcome to the team!

Employee ID: EMP001
Position: Barista

Your account has been created successfully. You can now access the system using your credentials.

- Maa Tara Cafe Management
```

## Sending Promotional Messages

To send promotional offers to customers, use the service directly in your code:

```csharp
// Example: Send promotional offer
await _whatsAppService.SendPromotionalOfferAsync(
    "+919876543210",
    "John Doe",
    "Weekend Special - 20% Off",
    "Get 20% off on all beverages this weekend!",
    "WEEKEND20",
    DateTime.Now.AddDays(3)
);
```

**Bulk messaging example:**

```csharp
// Send to all active customers
var customers = await _mongo.GetAllActiveCustomersAsync();

foreach (var customer in customers)
{
    if (!string.IsNullOrEmpty(customer.PhoneNumber))
    {
        await _whatsAppService.SendPromotionalOfferAsync(
            customer.PhoneNumber,
            customer.Name,
            "Festival Special",
            "Enjoy 25% off on all items this festive season!",
            "FEST25",
            DateTime.Now.AddDays(7)
        );
        
        // Add delay to avoid rate limiting
        await Task.Delay(1000);
    }
}
```

## Costs & Pricing 💰

### Twilio Pricing (2024)

**Trial Account:**
- 💵 **$15 free credit** (no charges during trial)
- Can send to verified numbers only
- Sandbox access included
- ~3,000 WhatsApp messages with trial credit

**Pay-as-you-go (After trial):**
- 💵 **$0.005 per message** (half a cent!)
- No monthly minimum
- No base fees
- Phone number: ~$1-2/month (if you buy one)

### Cost Examples

| Messages/Month | Cost | Per Customer |
|----------------|------|--------------|
| 100 | $0.50 | $0.005 |
| 1,000 | $5.00 | $0.005 |
| 10,000 | $50.00 | $0.005 |
| 100,000 | $500.00 | $0.005 |

**Example Scenario:**
- 100 orders/day
- 3 messages per order (confirmation, ready, delivered)
- 300 messages/day × 30 days = 9,000 messages/month
- **Cost: $45/month**

### Cost Comparison

| Provider | Per Message | Setup | Monthly | Best For |
|----------|-------------|-------|---------|----------|
| **Twilio** | $0.005 | $0 | $0-2 | Best value! |
| Meta API | $0.40-0.60 | $0 | $0 | High volume |
| SMS | $0.01-0.05 | $0 | $0 | Fallback |
| Email | Free | $0 | $0 | Newsletters |

🏆 **Winner:** Twilio WhatsApp is 80x cheaper than Meta's direct API for low-medium volume!

## Sandbox Limitations

The free sandbox environment has these restrictions:

| Feature | Sandbox | Production |
|---------|---------|------------|
| Recipients | Pre-approved only | Any number |
| Message prefix | "Sent from sandbox" | None |
| Templates | Text only | Full support |
| Rate limit | Limited | Higher |
| Cost | Free (trial credit) | Pay per message |

### Sandbox Rules:

1. **Pre-approved Recipients:**
   - Users must join by sending `join <code>` to sandbox number
   - You can add up to 5 test numbers

2. **Message Prefix:**
   - All messages have "Sent from your Twilio sandbox" prefix
   - Removed in production

3. **No Marketing:**
   - Sandbox is for testing only
   - Production required for customer-facing messages

**Solution:** Use sandbox for development and testing, then upgrade to production before going live.

## Troubleshooting

### Issue: Messages Not Sending

**Check 1: Service Enabled**
```
Look for log: "WhatsApp service is not enabled"
```

**Solution:**
- Verify Account SID and Auth Token are correct
- Ensure no placeholder values (`your-account-sid-here`)
- Restart application after configuration changes

**Check 2: Phone Number Format**
```
Error: "Invalid 'To' Phone Number"
```

**Solution:**
- Use E.164 format: `+919876543210`
- Remove spaces, hyphens, parentheses
- Include country code

**Check 3: Sandbox Join**
```
Error: "Message blocked"
```

**Solution:**
- Recipient must join sandbox: send `join <code>` to `+14155238886`
- Or use production number (no join required)

### Issue: Invalid Credentials

**Error:**
```
Status: 401 Unauthorized
"Authentication failed"
```

**Solution:**
1. Go to Twilio Console
2. Verify Account SID starts with `AC`
3. Show and copy Auth Token again
4. Update configuration
5. Rebuild and restart

### Issue: Rate Limit Exceeded

**Error:**
```
Status: 429 Too Many Requests
```

**Solution:**
- Add delays between messages: `await Task.Delay(1000);`
- Implement message queue with throttling
- Upgrade Twilio plan for higher limits

### Issue: Sandbox Code Not Working

**Problem:** Can't join sandbox

**Solution:**
1. Check you're using the correct sandbox code from Twilio Console
2. Send exactly: `join <code>` (lowercase, no extra spaces)
3. Send to correct number: `+1 415 523 8886`
4. Wait 10 seconds and try again
5. Contact Twilio support if still failing

## Security Best Practices

### 1. Protect Your Credentials

❌ **Never commit credentials to Git:**
```json
// DON'T do this in code
"TwilioAuthToken": "actual-token-here"
```

✅ **Use environment variables or Azure Key Vault:**

**Azure Key Vault:**
```powershell
# Store in Key Vault
az keyvault secret set `
  --vault-name "your-keyvault" `
  --name "TwilioAuthToken" `
  --value "your-auth-token"
```

**Update configuration:**
```json
{
  "WhatsAppService__TwilioAuthToken": "@Microsoft.KeyVault(SecretUri=https://your-vault.vault.azure.net/secrets/TwilioAuthToken/)"
}
```

### 2. Rotate Credentials Regularly

1. Go to Twilio Console → Settings
2. Click "Create New Auth Token"
3. Update your application configuration
4. Test thoroughly
5. Delete old token

### 3. Monitor Usage

Track WhatsApp usage in Twilio Console:
```
Monitor → Logs → Messaging Logs
```

Set up alerts for:
- Unusual message volume
- Failed deliveries
- Budget thresholds

### 4. Implement User Preferences

Allow users to opt-out of WhatsApp notifications:

```csharp
// Check user preferences before sending
if (user.WhatsAppOptIn && !string.IsNullOrEmpty(user.PhoneNumber))
{
    await _whatsAppService.SendOrderConfirmationAsync(...);
}
```

## Advanced Features

### Message Status Tracking

To track message delivery status, set up a webhook:

1. **Create webhook endpoint** in your API
2. **Configure in Twilio Console:**
   ```
   Messaging → Settings → Webhook for Status Callbacks
   ```
3. **Handle status updates:**
   - `sent` - Message sent to WhatsApp
   - `delivered` - Delivered to recipient
   - `read` - Read by recipient
   - `failed` - Delivery failed

### Rich Media Messages

Send images, PDFs, or other media:

```csharp
// Extend IWhatsAppService with media support
public async Task<bool> SendMediaMessageAsync(
    string phoneNumber, 
    string mediaUrl, 
    string caption)
{
    var formData = new Dictionary<string, string>
    {
        { "From", _fromNumber },
        { "To", FormatPhoneNumber(phoneNumber) },
        { "MediaUrl", mediaUrl },
        { "Body", caption }
    };
    // ... send via Twilio API
}
```

### Templates (Production Only)

For pre-approved message templates:

1. Create template in Twilio Console
2. Get template SID
3. Send using template:

```csharp
// Use Twilio Content API
var contentSid = "HXxxxxxxxxxxxxxxxxxxxx";
// Send with variables
```

## Testing Checklist

Before going live, test:

- [ ] Account credentials are correct
- [ ] Sandbox joined successfully
- [ ] Order confirmation messages sending
- [ ] Order status update messages sending
- [ ] Loyalty points messages sending
- [ ] Staff welcome messages sending
- [ ] Phone numbers formatting correctly
- [ ] Error handling working (invalid numbers)
- [ ] Logs showing success/failure
- [ ] Messages received on test phone
- [ ] Message content is correct
- [ ] Emojis displaying properly
- [ ] Links working (if any)

## Production Checklist

Before deploying to production:

- [ ] Twilio account upgraded (trial removed)
- [ ] Production phone number purchased
- [ ] WhatsApp sender approved by Twilio
- [ ] Configuration updated with production number
- [ ] Azure Key Vault configured for credentials
- [ ] Rate limiting implemented
- [ ] Error monitoring set up
- [ ] User opt-in/opt-out implemented
- [ ] Budget alerts configured
- [ ] Message templates approved (if using)
- [ ] Backup SMS failover configured (optional)
- [ ] Load tested with expected volume

## Support & Resources

### Twilio Resources

- **Documentation:** [twilio.com/docs/whatsapp](https://www.twilio.com/docs/whatsapp)
- **API Reference:** [twilio.com/docs/sms/api](https://www.twilio.com/docs/sms/api)
- **Code Examples:** [github.com/twilio](https://github.com/twilio)
- **Console:** [console.twilio.com](https://console.twilio.com)

### Twilio Support

- **Support Portal:** [support.twilio.com](https://support.twilio.com)
- **Community:** [twilio.com/community](https://www.twilio.com/community)
- **Email:** support@twilio.com
- **Phone:** Available for paid plans

### Application Code

- **Service Implementation:** [api/Services/WhatsAppService.cs](api/Services/WhatsAppService.cs)
- **Interface:** [api/Services/IWhatsAppService.cs](api/Services/IWhatsAppService.cs)
- **Configuration:** [api/local.settings.json](api/local.settings.json)

### Get Help

1. **Check application logs** in Azure Application Insights
2. **Review Twilio message logs** in Console
3. **Test with Twilio Debugger** tool
4. **Contact your development team**

## Next Steps

After successful setup:

1. ✅ **Test thoroughly** in sandbox environment
2. 📱 **Upgrade to production** number
3. 📊 **Monitor usage** and delivery rates
4. 🎨 **Customize messages** for your brand
5. 📈 **Add analytics** to track engagement
6. 🔔 **Implement user preferences** for opt-in/out
7. 📝 **Create message templates** for common scenarios
8. 🔄 **Set up backup** SMS for fallback
9. 💰 **Monitor costs** and optimize
10. 🚀 **Scale as needed** with Twilio's infrastructure

## Frequently Asked Questions

**Q: How many messages can I send?**  
A: Sandbox: Limited. Production: Thousands per second (with scaling).

**Q: Can I send to international numbers?**  
A: Yes! Twilio supports 180+ countries.

**Q: Do I need WhatsApp Business account?**  
A: No! Twilio handles all WhatsApp Business API requirements.

**Q: Can recipients reply to messages?**  
A: Yes, but you need to set up webhooks to receive replies.

**Q: Is there a message character limit?**  
A: WhatsApp supports up to 4096 characters per message.

**Q: Can I send to multiple recipients at once?**  
A: Yes, but send individual messages (loop through recipients).

**Q: What happens if message fails?**  
A: Error is logged, operation continues (doesn't fail orders).

**Q: Can I schedule messages?**  
A: Not built-in, but you can implement with Azure Functions Timer trigger.

---

**Last Updated:** February 21, 2026  
**Integration Status:** ✅ Twilio WhatsApp Integration Active  
**Integration Type:** Twilio REST API
**Support:** Twilio Support + Your Development Team

## Summary

🎉 **Congratulations!** You now have WhatsApp messaging integrated using Twilio!

**What you've achieved:**
- ✅ Easy 5-minute setup with sandbox
- ✅ Automatic order confirmations
- ✅ Real-time order updates
- ✅ Loyalty program notifications
- ✅ Staff onboarding messages
- ✅ Cost-effective messaging ($0.005/message)
- ✅ Reliable enterprise-grade delivery

**Start testing now and enhance your customer experience!** 🚀
