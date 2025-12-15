# Azure Deployment Guide - Cafe Website API

Complete guide to deploy the Cafe Website API to Azure Functions.

---

## Table of Contents

1. [Prerequisites](#prerequisites)
2. [Quick Deployment (5 Minutes)](#quick-deployment)
3. [Detailed Deployment Steps](#detailed-deployment-steps)
4. [Configuration](#configuration)
5. [Frontend Integration](#frontend-integration)
6. [Monitoring & Troubleshooting](#monitoring--troubleshooting)
7. [Cost Optimization](#cost-optimization)
8. [Security Best Practices](#security-best-practices)

---

## Prerequisites

### Required Software

1. **Azure CLI** (2.50.0 or later)
   - Download: https://aka.ms/installazurecliwindows
   - Verify: `az --version`

2. **.NET SDK 9.0**
   - Download: https://dotnet.microsoft.com/download/dotnet/9.0
   - Verify: `dotnet --version`

3. **Azure Subscription**
   - Free tier: https://azure.microsoft.com/free/
   - Required permissions: Contributor or Owner role

4. **PowerShell** (Windows) or **Bash** (Linux/Mac)

### Before You Start

- [ ] Azure subscription active
- [ ] Azure CLI installed and logged in
- [ ] Project builds successfully locally
- [ ] MongoDB connection string ready
- [ ] Gmail app password configured
- [ ] Chosen globally unique names for resources

---

## Quick Deployment (5 Minutes)

### Step 1: Prepare Configuration

1. **Update** [azure-app-settings.json](azure-app-settings.json):
   ```json
   {
     "name": "EmailService__SmtpPassword",
     "value": "YOUR_GMAIL_APP_PASSWORD_HERE"
   },
   {
     "name": "EmailService__BaseUrl",
     "value": "https://your-frontend-url.azurestaticapps.net"
   }
   ```

2. **Choose unique names**:
   - Function App: `cafemaatara-api` (globally unique)
   - Storage Account: `cafemaatarastorage` (globally unique, lowercase, no hyphens)
   - Resource Group: `CafeWebsite-RG`

### Step 2: Run Deployment Script

```powershell
# Navigate to project root
cd F:\MyProducts\CafeWebsite\cafe-website

# Run deployment
.\Deploy-ToAzure.ps1 `
    -ResourceGroupName "CafeWebsite-RG" `
    -FunctionAppName "cafemaatara-api" `
    -StorageAccountName "cafemaatarastorage" `
    -Location "eastus"
```

### Step 3: Verify Deployment

```powershell
# Test API endpoint
curl https://cafemaatara-api.azurewebsites.net/api/auth/admin/verify
```

**Expected Response:**
```json
{
  "adminExists": true
}
```

---

## Detailed Deployment Steps

### 1. Login to Azure

```powershell
# Login
az login

# Verify subscription
az account show

# (Optional) Set specific subscription
az account set --subscription "YOUR_SUBSCRIPTION_ID"
```

### 2. Create Resources Manually (Alternative to Script)

#### Create Resource Group
```powershell
az group create `
    --name "CafeWebsite-RG" `
    --location "eastus"
```

#### Create Storage Account
```powershell
az storage account create `
    --name "cafemaatarastorage" `
    --resource-group "CafeWebsite-RG" `
    --location "eastus" `
    --sku Standard_LRS
```

#### Create Function App
```powershell
az functionapp create `
    --name "cafemaatara-api" `
    --resource-group "CafeWebsite-RG" `
    --storage-account "cafemaatarastorage" `
    --consumption-plan-location "eastus" `
    --runtime dotnet-isolated `
    --runtime-version 9.0 `
    --functions-version 4 `
    --os-type Windows
```

### 3. Build and Package

```powershell
# Navigate to API folder
cd api

# Build in Release mode
dotnet publish --configuration Release --output ./publish

# Create deployment package
cd publish
Compress-Archive -Path * -DestinationPath ../deploy.zip -Force
cd ..
```

### 4. Deploy to Azure

```powershell
az functionapp deployment source config-zip `
    --name "cafemaatara-api" `
    --resource-group "CafeWebsite-RG" `
    --src deploy.zip
```

### 5. Configure App Settings

**Option A: Using JSON file**
```powershell
# From project root
$settings = Get-Content azure-app-settings.json | ConvertFrom-Json
foreach ($setting in $settings) {
    az functionapp config appsettings set `
        --name "cafemaatara-api" `
        --resource-group "CafeWebsite-RG" `
        --settings "$($setting.name)=$($setting.value)"
}
```

**Option B: Individual settings**
```powershell
az functionapp config appsettings set `
    --name "cafemaatara-api" `
    --resource-group "CafeWebsite-RG" `
    --settings `
        "Mongo__ConnectionString=YOUR_MONGO_CONNECTION" `
        "Jwt__Secret=YOUR_JWT_SECRET"
```

### 6. Configure CORS

```powershell
az functionapp cors add `
    --name "cafemaatara-api" `
    --resource-group "CafeWebsite-RG" `
    --allowed-origins "https://your-frontend-url.azurestaticapps.net"
```

---

## Configuration

### Required App Settings

| Setting | Description | Example |
|---------|-------------|---------|
| `FUNCTIONS_WORKER_RUNTIME` | Runtime type | `dotnet-isolated` |
| `FUNCTIONS_EXTENSION_VERSION` | Functions version | `~4` |
| `Mongo__ConnectionString` | MongoDB connection | `mongodb+srv://...` |
| `Mongo__Database` | Database name | `CafeDB` |
| `Jwt__Secret` | JWT signing key | `YourSecret32Chars...` |
| `EmailService__SmtpUsername` | Gmail address | `yourmail@gmail.com` |
| `EmailService__SmtpPassword` | Gmail app password | `abcd efgh ijkl mnop` |
| `EmailService__BaseUrl` | Frontend URL | `https://yourapp.com` |

### Update Configuration

**Via Azure Portal:**
1. Go to https://portal.azure.com
2. Navigate to Function App
3. Click **Configuration** → **Application settings**
4. Add/Edit settings
5. Click **Save**

**Via Azure CLI:**
```powershell
az functionapp config appsettings set `
    --name "cafemaatara-api" `
    --resource-group "CafeWebsite-RG" `
    --settings "EmailService__BaseUrl=https://new-url.com"
```

### Sensitive Settings

**Use Azure Key Vault for production:**
```powershell
# Create Key Vault
az keyvault create `
    --name "cafemaatara-kv" `
    --resource-group "CafeWebsite-RG" `
    --location "eastus"

# Add secret
az keyvault secret set `
    --vault-name "cafemaatara-kv" `
    --name "MongoConnectionString" `
    --value "YOUR_CONNECTION_STRING"

# Enable Managed Identity
az functionapp identity assign `
    --name "cafemaatara-api" `
    --resource-group "CafeWebsite-RG"

# Grant access
az keyvault set-policy `
    --name "cafemaatara-kv" `
    --object-id $(az functionapp identity show -n "cafemaatara-api" -g "CafeWebsite-RG" --query principalId -o tsv) `
    --secret-permissions get list

# Reference in app settings
az functionapp config appsettings set `
    --name "cafemaatara-api" `
    --resource-group "CafeWebsite-RG" `
    --settings "Mongo__ConnectionString=@Microsoft.KeyVault(VaultName=cafemaatara-kv;SecretName=MongoConnectionString)"
```

---

## Frontend Integration

### Update Frontend Configuration

**Angular (environment.prod.ts):**
```typescript
export const environment = {
  production: true,
  apiUrl: 'https://cafemaatara-api.azurewebsites.net/api'
};
```

**Update CORS after frontend deployment:**
```powershell
az functionapp cors add `
    --name "cafemaatara-api" `
    --resource-group "CafeWebsite-RG" `
    --allowed-origins "https://actual-frontend-url.azurestaticapps.net"
```

### Test Integration

1. **Test admin verification:**
   ```bash
   curl https://cafemaatara-api.azurewebsites.net/api/auth/admin/verify
   ```

2. **Test registration:**
   ```bash
   curl -X POST https://cafemaatara-api.azurewebsites.net/api/auth/register \
     -H "Content-Type: application/json" \
     -d '{
       "username":"testuser",
       "email":"test@example.com",
       "password":"Test@123",
       "firstName":"Test",
       "lastName":"User",
       "phoneNumber":"1234567890"
     }'
   ```

3. **Test login:**
   ```bash
   curl -X POST https://cafemaatara-api.azurewebsites.net/api/auth/login \
     -H "Content-Type: application/json" \
     -d '{
       "username":"admin",
       "password":"Admin@123"
     }'
   ```

---

## Monitoring & Troubleshooting

### View Logs

**Real-time logs:**
```powershell
az functionapp log tail `
    --name "cafemaatara-api" `
    --resource-group "CafeWebsite-RG"
```

**Azure Portal:**
1. Navigate to Function App
2. Click **Log stream** (left menu)
3. Select **Application Insights Logs** or **File System Logs**

### Application Insights

**Enable Application Insights:**
```powershell
# Create Application Insights
az monitor app-insights component create `
    --app "cafemaatara-insights" `
    --location "eastus" `
    --resource-group "CafeWebsite-RG"

# Get instrumentation key
$instrumentationKey = az monitor app-insights component show `
    --app "cafemaatara-insights" `
    --resource-group "CafeWebsite-RG" `
    --query "instrumentationKey" `
    --output tsv

# Configure Function App
az functionapp config appsettings set `
    --name "cafemaatara-api" `
    --resource-group "CafeWebsite-RG" `
    --settings "APPINSIGHTS_INSTRUMENTATIONKEY=$instrumentationKey"
```

### Common Issues

#### Issue: Function App not responding
**Solutions:**
- Check deployment status in Azure Portal
- Verify app settings are configured
- Check logs for errors
- Restart Function App:
  ```powershell
  az functionapp restart -n "cafemaatara-api" -g "CafeWebsite-RG"
  ```

#### Issue: CORS errors
**Solutions:**
- Verify CORS origins:
  ```powershell
  az functionapp cors show -n "cafemaatara-api" -g "CafeWebsite-RG"
  ```
- Add missing origin:
  ```powershell
  az functionapp cors add -n "cafemaatara-api" -g "CafeWebsite-RG" --allowed-origins "https://your-frontend.com"
  ```

#### Issue: Email not sending
**Solutions:**
- Verify Gmail app password in app settings
- Check `EmailService__SmtpPassword` is correct (no spaces)
- Test Gmail SMTP locally first
- Check Application Insights for errors

#### Issue: Database connection failed
**Solutions:**
- Verify MongoDB connection string
- Check MongoDB Atlas IP whitelist (add `0.0.0.0/0` for Azure)
- Test connection string locally
- Check MongoDB user permissions

---

## Cost Optimization

### Azure Functions Consumption Plan

**Pricing:**
- **Free grant:** 1 million executions/month + 400,000 GB-s compute
- **After free grant:** $0.20 per million executions + $0.000016 per GB-s

**Estimated monthly cost:**
- Low traffic (< 100k requests): **FREE**
- Medium traffic (500k requests): **~$5-10/month**
- High traffic (5M requests): **~$50-100/month**

### Storage Account

**Pricing:**
- Standard LRS: ~$0.02 per GB/month
- Estimated: **$0.10-0.50/month**

### Total Estimated Cost

| Traffic Level | Monthly Cost |
|---------------|--------------|
| Development/Testing | **FREE** |
| Small business | **$5-15** |
| Medium business | **$20-50** |
| Large business | **$50-150** |

### Cost Saving Tips

1. **Use free tier effectively:**
   - 1M executions/month free
   - Monitor usage in Azure Portal

2. **Optimize function execution:**
   - Reduce cold starts (keep functions warm)
   - Optimize database queries
   - Use caching where possible

3. **Monitor and alert:**
   ```powershell
   # Create budget alert
   az consumption budget create `
       --budget-name "CafeAPI-Budget" `
       --resource-group "CafeWebsite-RG" `
       --amount 50 `
       --time-grain Monthly
   ```

---

## Security Best Practices

### 1. Secure Secrets

✅ **Do:**
- Use Azure Key Vault for production
- Rotate secrets quarterly
- Use Managed Identity
- Never commit secrets to Git

❌ **Don't:**
- Store secrets in code
- Use default passwords
- Share secrets via email/chat

### 2. Enable HTTPS Only

```powershell
az functionapp update `
    --name "cafemaatara-api" `
    --resource-group "CafeWebsite-RG" `
    --set httpsOnly=true
```

### 3. Configure Authentication

**Enable Azure AD authentication:**
```powershell
az functionapp auth update `
    --name "cafemaatara-api" `
    --resource-group "CafeWebsite-RG" `
    --enabled true `
    --action LoginWithAzureActiveDirectory
```

### 4. IP Restrictions (Optional)

**Restrict to specific IPs:**
```powershell
az functionapp config access-restriction add `
    --name "cafemaatara-api" `
    --resource-group "CafeWebsite-RG" `
    --rule-name "AllowFrontend" `
    --priority 100 `
    --ip-address "YOUR_FRONTEND_IP/32"
```

### 5. MongoDB Atlas Security

**Whitelist Azure IPs:**
1. Go to MongoDB Atlas
2. Network Access → Add IP Address
3. Add `0.0.0.0/0` (Azure Functions use dynamic IPs)
4. Or use Azure Virtual Network (advanced)

### 6. Enable Diagnostic Logging

```powershell
# Create Log Analytics Workspace
az monitor log-analytics workspace create `
    --resource-group "CafeWebsite-RG" `
    --workspace-name "cafemaatara-logs"

# Get workspace ID
$workspaceId = az monitor log-analytics workspace show `
    --resource-group "CafeWebsite-RG" `
    --workspace-name "cafemaatara-logs" `
    --query "id" `
    --output tsv

# Enable diagnostic settings
az monitor diagnostic-settings create `
    --name "cafe-diagnostics" `
    --resource "/subscriptions/YOUR_SUB_ID/resourceGroups/CafeWebsite-RG/providers/Microsoft.Web/sites/cafemaatara-api" `
    --workspace $workspaceId `
    --logs '[{"category":"FunctionAppLogs","enabled":true}]' `
    --metrics '[{"category":"AllMetrics","enabled":true}]'
```

---

## Deployment Checklist

### Pre-Deployment
- [ ] Project builds successfully
- [ ] All tests passing
- [ ] Configuration values prepared
- [ ] Azure subscription active
- [ ] Resource names chosen (globally unique)
- [ ] MongoDB connection tested
- [ ] Gmail SMTP tested

### Deployment
- [ ] Resource group created
- [ ] Storage account created
- [ ] Function App created
- [ ] Code deployed
- [ ] App settings configured
- [ ] CORS configured
- [ ] HTTPS enforced

### Post-Deployment
- [ ] Test admin verification endpoint
- [ ] Test user registration
- [ ] Test login
- [ ] Test password reset email
- [ ] Verify database connectivity
- [ ] Check Application Insights
- [ ] Monitor logs for errors
- [ ] Update frontend configuration
- [ ] Test frontend integration
- [ ] Set up budget alerts
- [ ] Document deployment details

---

## Useful Commands

### Restart Function App
```powershell
az functionapp restart -n "cafemaatara-api" -g "CafeWebsite-RG"
```

### Stop Function App
```powershell
az functionapp stop -n "cafemaatara-api" -g "CafeWebsite-RG"
```

### Start Function App
```powershell
az functionapp start -n "cafemaatara-api" -g "CafeWebsite-RG"
```

### Get Function App URL
```powershell
az functionapp show `
    --name "cafemaatara-api" `
    --resource-group "CafeWebsite-RG" `
    --query "defaultHostName" `
    --output tsv
```

### List All Functions
```powershell
az functionapp function list `
    --name "cafemaatara-api" `
    --resource-group "CafeWebsite-RG" `
    --output table
```

### Get Function Keys
```powershell
az functionapp keys list `
    --name "cafemaatara-api" `
    --resource-group "CafeWebsite-RG"
```

### Delete Resources
```powershell
# Delete entire resource group (USE WITH CAUTION)
az group delete --name "CafeWebsite-RG" --yes --no-wait
```

---

## Support & Resources

### Official Documentation
- Azure Functions: https://docs.microsoft.com/azure/azure-functions/
- Azure CLI: https://docs.microsoft.com/cli/azure/
- .NET 9: https://docs.microsoft.com/dotnet/

### Pricing Calculators
- Azure Pricing Calculator: https://azure.microsoft.com/pricing/calculator/
- Functions Pricing: https://azure.microsoft.com/pricing/details/functions/

### Azure Portal
- Portal: https://portal.azure.com
- Your Function App: `https://portal.azure.com/#resource/subscriptions/YOUR_SUB/resourceGroups/CafeWebsite-RG/providers/Microsoft.Web/sites/cafemaatara-api`

### Monitoring Tools
- Application Insights: Azure Portal → Your Function App → Application Insights
- Metrics: Azure Portal → Your Function App → Metrics
- Alerts: Azure Portal → Your Function App → Alerts

---

## Next Steps After Deployment

1. **Deploy Frontend:**
   - Use Azure Static Web Apps
   - Update API URL in frontend config
   - Configure CORS with actual frontend URL

2. **Set Up Custom Domain:**
   ```powershell
   az functionapp config hostname add `
       --webapp-name "cafemaatara-api" `
       --resource-group "CafeWebsite-RG" `
       --hostname "api.cafemaatara.com"
   ```

3. **Enable SSL Certificate:**
   - Free via Azure (Let's Encrypt integration)
   - Or upload custom certificate

4. **Configure Monitoring Alerts:**
   - High error rates
   - High latency
   - Budget exceeded

5. **Set Up CI/CD:**
   - GitHub Actions
   - Azure DevOps Pipelines

6. **Performance Optimization:**
   - Enable HTTP/2
   - Configure connection pooling
   - Implement caching strategy

---

**Last Updated:** December 2024  
**Status:** ✅ Production Ready  
**Deployment Time:** 5-15 minutes  
**Estimated Cost:** FREE to $15/month
