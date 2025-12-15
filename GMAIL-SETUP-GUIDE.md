# Gmail SMTP Setup Guide - 5 Minutes

## Quick Setup for Email Service

The email service now uses **Gmail SMTP** instead of SendGrid - much easier for development!

---

## Step 1: Enable Gmail App Passwords (2 minutes)

### Option A: Using App Passwords (Recommended - More Secure)

1. **Enable 2-Step Verification** (if not already enabled):
   - Go to [Google Account Security](https://myaccount.google.com/security)
   - Click "2-Step Verification"
   - Follow the setup process

2. **Create App Password**:
   - Visit [App Passwords](https://myaccount.google.com/apppasswords)
   - Select app: "Mail"
   - Select device: "Other (Custom name)" → Enter "Cafe Website"
   - Click "Generate"
   - **Copy the 16-character password** (e.g., `abcd efgh ijkl mnop`)
   - Remove spaces: `abcdefghijklmnop`

### Option B: Less Secure App Access (Not Recommended)

If you don't want to enable 2-Step Verification:
- Visit [Less Secure Apps](https://myaccount.google.com/lesssecureapps)
- Turn ON "Allow less secure apps"
- Use your regular Gmail password

⚠️ **Security Note:** App passwords are much safer!

---

## Step 2: Configure Local Settings (1 minute)

Update `api/local.settings.json`:

```json
{
  "Values": {
    "EmailService__SmtpHost": "smtp.gmail.com",
    "EmailService__SmtpPort": "587",
    "EmailService__SmtpUsername": "your-email@gmail.com",
    "EmailService__SmtpPassword": "abcdefghijklmnop",
    "EmailService__FromEmail": "your-email@gmail.com",
    "EmailService__FromName": "Cafe Maatara",
    "EmailService__BaseUrl": "http://localhost:4200",
    "EmailService__UseSsl": "true"
  }
}
```

**Replace:**
- `your-email@gmail.com` → Your Gmail address
- `abcdefghijklmnop` → Your app password (no spaces)

---

## Step 3: Restart & Test (2 minutes)

### Restart Azure Functions
```bash
# Stop current instance (Ctrl+C)
# Start again
func start
```

### Test Password Reset Email
```bash
curl -X POST http://localhost:7071/api/auth/password/forgot \
  -H "Content-Type: application/json" \
  -d '{"email":"your-email@gmail.com"}'
```

### Check Your Gmail Inbox
- Check inbox (might take 5-10 seconds)
- Check spam folder if not in inbox
- Look for "Password Reset Request - Cafe Maatara"

---

## Configuration Options

| Setting | Description | Default | Example |
|---------|-------------|---------|---------|
| `SmtpHost` | Gmail SMTP server | `smtp.gmail.com` | `smtp.gmail.com` |
| `SmtpPort` | SMTP port | `587` | `587` (TLS) or `465` (SSL) |
| `SmtpUsername` | Your Gmail address | - | `john@gmail.com` |
| `SmtpPassword` | App password | - | `abcdefghijklmnop` |
| `FromEmail` | Sender email | Same as username | `john@gmail.com` |
| `FromName` | Display name | `Cafe Maatara` | `Cafe Maatara` |
| `BaseUrl` | Frontend URL | `http://localhost:4200` | - |
| `UseSsl` | Use SSL/TLS | `true` | `true` |

---

## Gmail Sending Limits

**Free Gmail Account:**
- 500 emails/day
- 2,000 emails/day (Google Workspace)

**Rate Limits:**
- Max 500 recipients per message
- Max 2,000 emails per day (rolling 24 hours)

**Perfect for:**
- Development and testing
- Small to medium applications
- Personal projects

---

## Troubleshooting

### "Authentication failed"

**Cause:** Wrong username or password

**Solutions:**
1. Verify Gmail address is correct
2. Check app password (no spaces)
3. Try regenerating app password
4. Ensure 2-Step Verification is enabled

### "SMTP connection failed"

**Cause:** Firewall or network issue

**Solutions:**
1. Check port 587 is not blocked
2. Try port 465 instead (change `SmtpPort` to `"465"`)
3. Disable VPN temporarily
4. Check antivirus/firewall settings

### Emails in Spam Folder

**Cause:** Gmail spam filters

**Solutions:**
1. Mark email as "Not Spam"
2. Add sender to contacts
3. This is normal for development
4. For production, use a custom domain

### "Daily sending quota exceeded"

**Cause:** Sent more than 500 emails in 24 hours

**Solutions:**
1. Wait 24 hours
2. Use multiple Gmail accounts
3. Upgrade to Google Workspace
4. Use a dedicated email service (SendGrid, etc.)

---

## Testing All Email Types

### 1. Password Reset Email
```bash
curl -X POST http://localhost:7071/api/auth/password/forgot \
  -H "Content-Type: application/json" \
  -d '{"email":"your-email@gmail.com"}'
```

### 2. Welcome Email (Register)
```bash
curl -X POST http://localhost:7071/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{
    "username":"testuser123",
    "email":"your-email@gmail.com",
    "password":"Test@12345",
    "firstName":"Test",
    "lastName":"User",
    "phoneNumber":"9876543210"
  }'
```

### 3. Password Changed Email
```bash
# First login to get token
# Then change password
curl -X POST http://localhost:7071/api/auth/password/change \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "currentPassword":"Test@12345",
    "newPassword":"NewPass@123",
    "confirmPassword":"NewPass@123"
  }'
```

---

## Development vs Production

### Development (Current Setup)
- ✅ Use your personal Gmail
- ✅ Free (500 emails/day)
- ✅ Easy setup (5 minutes)
- ✅ Perfect for testing

### Production (Recommended Options)

**Option 1: Google Workspace**
- Professional email (you@yourdomain.com)
- 2,000 emails/day
- Better deliverability
- $6/user/month

**Option 2: SendGrid**
- 100 emails/day (free)
- 40,000 emails/month (paid)
- Dedicated email service
- Better for high volume

**Option 3: Amazon SES**
- 62,000 emails/month (free tier)
- $0.10 per 1,000 emails after
- Highly scalable
- Requires AWS account

---

## Security Best Practices

### ✅ Do's
- Use app passwords (not regular password)
- Enable 2-Step Verification
- Store credentials in environment variables
- Never commit passwords to Git
- Rotate app passwords quarterly

### ❌ Don'ts
- Don't use "Less Secure Apps"
- Don't share app passwords
- Don't hardcode credentials
- Don't use same password everywhere
- Don't commit local.settings.json

---

## Advantages of Gmail SMTP

**vs SendGrid:**
- ✅ No API key needed
- ✅ Use existing Gmail account
- ✅ Higher free tier (500 vs 100/day)
- ✅ Simpler setup
- ✅ No account approval needed

**Limitations:**
- ❌ Lower daily limits than paid services
- ❌ Sender must be your Gmail address
- ❌ Less detailed analytics
- ❌ Not ideal for high-volume production

---

## Quick Reference

### Minimum Configuration
```json
{
  "EmailService__SmtpUsername": "your-email@gmail.com",
  "EmailService__SmtpPassword": "your-app-password",
  "EmailService__FromEmail": "your-email@gmail.com"
}
```

### Full Configuration
```json
{
  "EmailService__SmtpHost": "smtp.gmail.com",
  "EmailService__SmtpPort": "587",
  "EmailService__SmtpUsername": "your-email@gmail.com",
  "EmailService__SmtpPassword": "your-app-password",
  "EmailService__FromEmail": "your-email@gmail.com",
  "EmailService__FromName": "Cafe Maatara",
  "EmailService__BaseUrl": "http://localhost:4200",
  "EmailService__UseSsl": "true"
}
```

---

## What Changed from SendGrid

**Package:**
- ❌ Removed: `SendGrid` (9.29.3)
- ✅ Added: `MailKit` (4.9.0)

**Configuration:**
- ❌ Removed: `SendGridApiKey`
- ✅ Added: `SmtpHost`, `SmtpPort`, `SmtpUsername`, `SmtpPassword`, `UseSsl`

**Code:**
- Updated `EmailService.cs` to use SMTP
- Same interface (IEmailService) - no changes to other code
- Same 7 email templates
- Same functionality

---

## Next Steps

1. ✅ Create Gmail app password (2 min)
2. ✅ Update `local.settings.json` (1 min)
3. ✅ Restart Azure Functions
4. ✅ Test password reset email
5. ✅ Verify email received in Gmail
6. 🎉 Start using email features!

---

## Support

**Documentation:**
- Full Guide: `EMAIL-SERVICE-DOCUMENTATION.md` (updated)
- Quick Setup: This file
- Troubleshooting: See section above

**Gmail Help:**
- [App Passwords Guide](https://support.google.com/accounts/answer/185833)
- [2-Step Verification](https://support.google.com/accounts/answer/185839)
- [Gmail SMTP Settings](https://support.google.com/mail/answer/7126229)

---

**Last Updated:** December 2024  
**Status:** ✅ Production Ready  
**Setup Time:** 5 minutes  
**Free Tier:** 500 emails/day
