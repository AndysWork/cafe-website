# Fix Publish Profile Authentication Error

## Error
```
Error: Failed to acquire app settings from https://<scmsite>/api/settings with publish-profile
Error: Failed to fetch Kudu App Settings. Unauthorized (CODE: 401)
```

## Root Cause
The publish profile secret in GitHub is **invalid, expired, or incorrectly configured**.

## ✅ Solution: Update Publish Profile Secret

### Step 1: Download Fresh Publish Profile from Azure

#### Option A: Using Azure Portal (Recommended)
1. Go to [Azure Portal](https://portal.azure.com)
2. Navigate to **Function App** → `cafe-api-5560`
3. Click **Get publish profile** (top toolbar)
4. Save the downloaded `.PublishSettings` file
5. Open the file in a text editor

#### Option B: Using Azure CLI
```powershell
# Login to Azure
az login

# Download publish profile
az functionapp deployment list-publishing-profiles `
  --name cafe-api-5560 `
  --resource-group <your-resource-group> `
  --xml
```

### Step 2: Update GitHub Secret

1. **Go to GitHub Repository**
   - Navigate to: https://github.com/your-username/cafe-website

2. **Access Secrets**
   - Click **Settings** → **Secrets and variables** → **Actions**

3. **Update Secret**
   - Find `AZURE_FUNCTIONAPP_PUBLISH_PROFILE`
   - Click **Update** (or create if it doesn't exist)
   - **Copy the ENTIRE contents** of the `.PublishSettings` file
   - Paste into the secret value
   - Click **Update secret**

### Step 3: Trigger Deployment

#### Option A: Push to Main
```bash
git add .
git commit -m "Update workflow configuration"
git push origin main
```

#### Option B: Manual Trigger
1. Go to **Actions** tab in GitHub
2. Select **Deploy API to Azure Functions**
3. Click **Run workflow** → **Run workflow**

---

## Alternative: Use Azure Login with Service Principal

If publish profile continues to fail, use Azure Login instead:

### Setup Service Principal

1. **Create Service Principal**
```powershell
az ad sp create-for-rbac `
  --name "cafe-api-deploy" `
  --role contributor `
  --scopes /subscriptions/<subscription-id>/resourceGroups/<resource-group>/providers/Microsoft.Web/sites/cafe-api-5560 `
  --sdk-auth
```

2. **Copy the JSON output** (looks like this):
```json
{
  "clientId": "xxx",
  "clientSecret": "xxx",
  "subscriptionId": "xxx",
  "tenantId": "xxx",
  ...
}
```

3. **Add to GitHub Secrets**
   - Create secret: `AZURE_CREDENTIALS`
   - Paste the entire JSON

### Update Workflow to Use Azure Login

Create: `.github/workflows/deploy-api-azure-login.yml`

```yaml
name: Deploy API (Azure Login)

on:
  push:
    branches: [main]
    paths: ['api/**', '.github/workflows/deploy-api-azure-login.yml']
  workflow_dispatch:

env:
  AZURE_FUNCTIONAPP_NAME: cafe-api-5560
  DOTNET_VERSION: '9.0.x'

jobs:
  build-and-deploy:
    runs-on: windows-latest
    
    steps:
    - uses: actions/checkout@v4
    
    - name: Setup .NET
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: ${{ env.DOTNET_VERSION }}
    
    - name: Build
      run: |
        cd api
        dotnet build --configuration Release --output ./output
    
    - name: Azure Login
      uses: azure/login@v1
      with:
        creds: ${{ secrets.AZURE_CREDENTIALS }}
    
    - name: Deploy to Azure Functions
      uses: Azure/functions-action@v1
      with:
        app-name: ${{ env.AZURE_FUNCTIONAPP_NAME }}
        package: './api/output'
```

---

## Quick Troubleshooting Checklist

### ✅ Verify Publish Profile is Valid

The publish profile XML should look like:
```xml
<publishData>
  <publishProfile 
    profileName="cafe-api-5560 - Web Deploy" 
    publishMethod="MSDeploy"
    publishUrl="cafe-api-5560.scm.azurewebsites.net:443"
    userName="$cafe-api-5560"
    userPWD="xxx..."
    ...
```

### ✅ Common Issues

| Issue | Solution |
|-------|----------|
| **Empty/missing secret** | Download fresh publish profile |
| **Partial XML copied** | Ensure ENTIRE file is copied |
| **Extra spaces/newlines** | Paste raw XML without formatting |
| **Wrong Function App** | Verify app name matches `cafe-api-5560` |
| **Deployment slots** | Use production slot profile, not staging |
| **Profile expired** | Re-download from Azure Portal |

### ✅ Test Publish Profile Locally

```powershell
# Extract credentials from publish profile
$xml = [xml](Get-Content "path/to/cafe-api-5560.PublishSettings")
$profile = $xml.publishData.publishProfile | Where-Object { $_.publishMethod -eq "MSDeploy" }
$username = $profile.userName
$password = $profile.userPWD
$publishUrl = $profile.publishUrl

Write-Host "Username: $username"
Write-Host "Publish URL: $publishUrl"
Write-Host "Password length: $($password.Length)"

# Test deployment from local
cd api
func azure functionapp publish cafe-api-5560
```

---

## If All Else Fails: Manual Deployment

### Deploy from Local Machine

```powershell
# Build
cd api
dotnet build --configuration Release --output ./output

# Deploy
func azure functionapp publish cafe-api-5560
```

### Deploy via Azure CLI

```powershell
# Build and zip
cd api
dotnet build --configuration Release --output ./output
Compress-Archive -Path ./output/* -DestinationPath ../deploy.zip -Force

# Deploy
az functionapp deployment source config-zip `
  --resource-group <your-resource-group> `
  --name cafe-api-5560 `
  --src ../deploy.zip
```

---

## Verification

After fixing, verify deployment:

```bash
# Wait a few seconds, then test
curl https://cafe-api-5560.azurewebsites.net/api/auth/admin/verify
```

Expected response:
```json
{
  "exists": true,
  "username": "admin",
  "isActive": true
}
```

---

## Prevention

1. **Store publish profile securely** - Don't commit to source control
2. **Rotate regularly** - Download new profile every 90 days
3. **Use Service Principal for production** - More secure and doesn't expire
4. **Enable deployment center logging** - Azure Portal → Deployment Center → Logs
5. **Test locally first** - Always test `func azure functionapp publish` locally

---

## Next Steps After Fix

1. ✅ Update GitHub secret with fresh publish profile
2. ✅ Re-run workflow
3. ✅ Verify API is accessible
4. ✅ Consider switching to Azure Login method for better security
5. ✅ Set up monitoring/alerts in Azure

---

**Need Help?**
- Check Azure Portal → Function App → Deployment Center for deployment status
- Review GitHub Actions logs for detailed error messages
- Verify Function App is running (not stopped) in Azure Portal
