# Setup Azure Service Principal for GitHub Actions Deployment
# This script helps you create a service principal for automated deployments

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Azure Service Principal Setup" -ForegroundColor Cyan
Write-Host "  for GitHub Actions Deployment" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$functionAppName = "cafe-api-5560"

# Step 1: Login to Azure
Write-Host "Step 1: Logging in to Azure..." -ForegroundColor Yellow
try {
    az login
    Write-Host "✓ Logged in successfully" -ForegroundColor Green
} catch {
    Write-Host "✗ Failed to login to Azure" -ForegroundColor Red
    Write-Host "Please install Azure CLI: https://aka.ms/installazurecliwindows" -ForegroundColor Yellow
    exit 1
}

Write-Host ""

# Step 2: Get Function App details
Write-Host "Step 2: Getting Function App details..." -ForegroundColor Yellow
$functionApp = az functionapp show --name $functionAppName --query "{resourceGroup:resourceGroup, subscriptionId:id}" -o json | ConvertFrom-Json

if (-not $functionApp) {
    Write-Host "✗ Function App '$functionAppName' not found" -ForegroundColor Red
    Write-Host "Please verify the Function App name and try again" -ForegroundColor Yellow
    exit 1
}

$resourceGroup = $functionApp.resourceGroup
$subscriptionId = ($functionApp.subscriptionId -split '/')[2]

Write-Host "✓ Function App: $functionAppName" -ForegroundColor Green
Write-Host "✓ Resource Group: $resourceGroup" -ForegroundColor Green
Write-Host "✓ Subscription ID: $subscriptionId" -ForegroundColor Green
Write-Host ""

# Step 3: Create Service Principal
Write-Host "Step 3: Creating Service Principal..." -ForegroundColor Yellow
Write-Host "This may take a moment..." -ForegroundColor Gray

$scope = "/subscriptions/$subscriptionId/resourceGroups/$resourceGroup/providers/Microsoft.Web/sites/$functionAppName"
$spName = "cafe-api-deploy-$(Get-Date -Format 'yyyyMMdd')"

try {
    $credentials = az ad sp create-for-rbac `
        --name $spName `
        --role contributor `
        --scopes $scope `
        --sdk-auth | ConvertFrom-Json
    
    Write-Host "✓ Service Principal created: $spName" -ForegroundColor Green
} catch {
    Write-Host "✗ Failed to create Service Principal" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Yellow
    exit 1
}

Write-Host ""

# Step 4: Display credentials for GitHub
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  GitHub Secret Configuration" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Copy the JSON below and add it to GitHub:" -ForegroundColor Yellow
Write-Host "  1. Go to: https://github.com/<your-username>/cafe-website/settings/secrets/actions" -ForegroundColor Gray
Write-Host "  2. Click 'New repository secret'" -ForegroundColor Gray
Write-Host "  3. Name: AZURE_CREDENTIALS" -ForegroundColor Gray
Write-Host "  4. Value: Paste the JSON below" -ForegroundColor Gray
Write-Host ""
Write-Host "----------------------------------------" -ForegroundColor DarkGray

# Convert back to JSON for display
$credentialsJson = $credentials | ConvertTo-Json -Depth 10
Write-Host $credentialsJson -ForegroundColor White

Write-Host "----------------------------------------" -ForegroundColor DarkGray
Write-Host ""

# Step 5: Test permissions
Write-Host "Step 4: Testing Service Principal permissions..." -ForegroundColor Yellow
Start-Sleep -Seconds 5

try {
    # Login with service principal
    az login --service-principal `
        -u $credentials.clientId `
        -p $credentials.clientSecret `
        --tenant $credentials.tenantId
    
    # Test access
    $testAccess = az functionapp show --name $functionAppName --resource-group $resourceGroup -o json | ConvertFrom-Json
    
    if ($testAccess) {
        Write-Host "✓ Service Principal has correct permissions" -ForegroundColor Green
    }
    
    # Logout service principal
    az logout
    
    # Login back as user
    az login --username $env:USERNAME
    
} catch {
    Write-Host "⚠ Could not verify permissions, but service principal was created" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Next Steps" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "1. ✓ Service Principal created" -ForegroundColor Green
Write-Host "2. □ Add AZURE_CREDENTIALS to GitHub secrets (see JSON above)" -ForegroundColor Yellow
Write-Host "3. □ Rename workflow file:" -ForegroundColor Yellow
Write-Host "     .github/workflows/deploy-api-azure-login.yml -> deploy-api.yml" -ForegroundColor Gray
Write-Host "4. □ Push changes and deployment will use Azure Login" -ForegroundColor Yellow
Write-Host ""
Write-Host "Alternative: Download Publish Profile" -ForegroundColor Cyan
Write-Host "If you prefer to use publish profile instead:" -ForegroundColor Gray
Write-Host "  az functionapp deployment list-publishing-profiles --name $functionAppName --resource-group $resourceGroup --xml" -ForegroundColor Gray
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Setup Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
