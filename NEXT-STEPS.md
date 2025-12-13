# 🚀 Cafe Website - Environment Setup & Next Steps

## ✅ What's Already Configured

### Production (Azure)
- ✅ Azure Function App: `cafe-api-5560`
- ✅ MongoDB Connection: Atlas Cluster configured
- ✅ Database: `CafeDB`
- ✅ API URL: `https://cafe-api-5560.azurewebsites.net/api`
- ✅ GitHub CI/CD: Ready (needs publish profile secret)

### Local Development
- ✅ API runs on: `http://localhost:7071/api`
- ✅ Frontend runs on: `http://localhost:4200`
- ✅ MongoDB: Using same Atlas cluster (or can use local MongoDB)

---

## 📋 Next Steps

### Step 1: Deploy API Code to Azure

```powershell
# Navigate to API folder
cd f:\MyProducts\CafeWebsite\cafe-website\api

# Build the project
dotnet build --configuration Release

# Publish to Azure (first time manual deployment)
func azure functionapp publish cafe-api-5560
```

**Expected output:**
- Build successful
- Functions uploaded
- API endpoints listed

### Step 2: Test Production API

```powershell
# Test categories endpoint
curl https://cafe-api-5560.azurewebsites.net/api/categories

# Test menu endpoint  
curl https://cafe-api-5560.azurewebsites.net/api/menu

# Test subcategories endpoint
curl https://cafe-api-5560.azurewebsites.net/api/subcategories
```

### Step 3: Upload Data to MongoDB (If Empty)

Since local and production use the same MongoDB Atlas cluster, you can upload data from your local environment:

```powershell
# Start local API
cd f:\MyProducts\CafeWebsite\cafe-website\api
func start

# In another terminal, start frontend
cd f:\MyProducts\CafeWebsite\cafe-website\frontend
npm start
```

Then:
1. Open browser: `http://localhost:4200/admin/login`
2. Login with admin credentials
3. Go to Category Management → Upload your category Excel file
4. Go to Menu Management → Upload your menu Excel file

**The data will be in MongoDB Atlas and available to both local and production!**

### Step 4: Setup GitHub CI/CD (Automated Deployments)

1. **Copy publish profile to GitHub:**
   - File already generated: `publish-profile.xml`
   - Go to: https://github.com/YOUR_USERNAME/YOUR_REPO/settings/secrets/actions
   - Click "New repository secret"
   - Name: `AZURE_FUNCTIONAPP_PUBLISH_PROFILE`
   - Value: Paste contents of `publish-profile.xml`
   - Click "Add secret"

2. **Commit and push workflow:**
```bash
git add .github/workflows/deploy-api.yml
git commit -m "Add GitHub Actions CI/CD"
git push origin main
```

3. **Delete publish profile (security):**
```powershell
Remove-Item .\publish-profile.xml
```

Now every push to `main` that changes `api/**` files will auto-deploy!

### Step 5: Deploy Frontend to Azure Static Web Apps

```powershell
$env:PATH += ";C:\Program Files (x86)\Microsoft SDKs\Azure\CLI2\wbin"

# Create Static Web App
az staticwebapp create `
  --name cafe-frontend `
  --resource-group cafe-website-rg `
  --location eastus

# Get deployment token
$token = az staticwebapp secrets list `
  --name cafe-frontend `
  --resource-group cafe-website-rg `
  --query "properties.apiKey" -o tsv

# Build frontend
cd frontend
npm run build -- --configuration production

# Deploy (install SWA CLI if needed: npm install -g @azure/static-web-apps-cli)
swa deploy ./dist/cafe-website/browser --deployment-token $token
```

Or use the Azure Static Web Apps GitHub Action (recommended):
- https://docs.microsoft.com/en-us/azure/static-web-apps/get-started-cli

---

## 🔧 Environment Configuration

### Local Development

**API (`api/local.settings.json`):**
```json
{
  "Values": {
    "Mongo__ConnectionString": "mongodb+srv://...",
    "Mongo__Database": "CafeDB"
  },
  "Host": {
    "LocalHttpPort": 7071,
    "CORS": "http://localhost:4200"
  }
}
```

**Frontend (`frontend/src/environments/environment.ts`):**
```typescript
export const environment = {
  production: false,
  apiUrl: 'http://localhost:7071/api'
};
```

**Run locally:**
```powershell
# Terminal 1: API
cd api
func start

# Terminal 2: Frontend
cd frontend
npm start
```

### Production (Azure)

**API (Azure Function App Settings):**
- `Mongo__ConnectionString`: Set via Azure Portal or CLI
- `Mongo__Database`: `CafeDB`
- Auto-configured CORS for your frontend domain

**Frontend (`frontend/src/environments/environment.prod.ts`):**
```typescript
export const environment = {
  production: true,
  apiUrl: 'https://cafe-api-5560.azurewebsites.net/api'
};
```

**Build for production:**
```powershell
cd frontend
npm run build -- --configuration production
```

---

## 🛠️ Useful Commands

### Update MongoDB Connection (Production)
```powershell
.\configure-azure-settings.ps1
```

### View Azure Logs
```powershell
$env:PATH += ";C:\Program Files (x86)\Microsoft SDKs\Azure\CLI2\wbin"
az functionapp log tail --name cafe-api-5560 --resource-group cafe-website-rg
```

### Restart Function App
```powershell
az functionapp restart --name cafe-api-5560 --resource-group cafe-website-rg
```

### Check Function App Status
```powershell
az functionapp show --name cafe-api-5560 --resource-group cafe-website-rg --query state
```

### Manual Deploy API
```powershell
cd api
func azure functionapp publish cafe-api-5560
```

---

## 📊 Monitoring & Testing

### Check if API is Running
```powershell
# Production
curl https://cafe-api-5560.azurewebsites.net/api/categories

# Local
curl http://localhost:7071/api/categories
```

### View Application Insights (Optional)
1. Go to Azure Portal
2. Navigate to your Function App
3. Click "Application Insights" (if enabled)
4. View requests, failures, performance

---

## 🔐 Security Notes

### Secrets Management
- ✅ MongoDB credentials stored in Azure App Settings (encrypted)
- ✅ `local.settings.json` in `.gitignore` (never commit!)
- ✅ Publish profiles should be deleted after adding to GitHub
- ✅ Use environment variables for all sensitive data

### CORS Configuration
**Production:** Update to specific domain after frontend deployment
```powershell
az functionapp cors remove --name cafe-api-5560 --resource-group cafe-website-rg --allowed-origins "*"
az functionapp cors add --name cafe-api-5560 --resource-group cafe-website-rg --allowed-origins "https://your-frontend-domain.azurestaticapps.net"
```

---

## 🎯 Quick Reference

| Environment | API URL | Frontend URL | MongoDB |
|------------|---------|--------------|---------|
| **Local** | http://localhost:7071/api | http://localhost:4200 | Atlas Cluster |
| **Production** | https://cafe-api-5560.azurewebsites.net/api | TBD (Static Web App) | Atlas Cluster |

---

## ❓ Troubleshooting

### API returns 500 errors
1. Check MongoDB connection string
2. Verify database name is correct
3. Check Azure logs: `az functionapp log tail`

### Local API won't start
1. Verify `local.settings.json` exists and is valid
2. Check MongoDB Atlas allows your IP
3. Run `dotnet build` to check for errors

### Frontend can't connect to API
1. Check CORS settings
2. Verify API URL in environment files
3. Check API is running (curl test)

### GitHub Actions deployment fails
1. Verify publish profile secret is correct
2. Check workflow file has correct function app name
3. Review GitHub Actions logs

---

## 📝 Summary

**You're all set!** 🎉

- ✅ Local environment: Ready for development
- ✅ Production API: Deployed to Azure Functions
- ✅ MongoDB: Configured for both environments
- ✅ CI/CD: Ready (add secret to activate)
- 🔄 Next: Deploy frontend to Azure Static Web Apps

Need help? Check Azure Portal or run diagnostic commands above!
