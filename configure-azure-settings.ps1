# Configure Azure Settings Script
# This script sets up the Azure Function App with MongoDB connection

$env:PATH += ";C:\Program Files (x86)\Microsoft SDKs\Azure\CLI2\wbin"

$functionAppName = "cafe-api-5560"
$resourceGroup = "cafe-website-rg"

Write-Host "=== Configuring Azure Function App Settings ===" -ForegroundColor Cyan
Write-Host ""

# MongoDB settings
$mongoConnectionString = "mongodb+srv://maataracafekpa_db_user:MyCafeatmc_007@maataracafecluster.8ynr8xr.mongodb.net/?retryWrites=true&w=majority"
$mongoDatabase = "CafeDB"

Write-Host "Setting MongoDB connection..." -ForegroundColor Yellow

# Set connection string
az functionapp config appsettings set `
  --name $functionAppName `
  --resource-group $resourceGroup `
  --settings "Mongo__ConnectionString=$mongoConnectionString" `
  --output none

Write-Host "Setting MongoDB database name..." -ForegroundColor Yellow

# Set database name
az functionapp config appsettings set `
  --name $functionAppName `
  --resource-group $resourceGroup `
  --settings "Mongo__Database=$mongoDatabase" `
  --output none

Write-Host ""
Write-Host "Configuration complete!" -ForegroundColor Green
Write-Host ""

# Verify settings
Write-Host "Verifying settings..." -ForegroundColor Cyan
az functionapp config appsettings list `
  --name $functionAppName `
  --resource-group $resourceGroup `
  --query "[?starts_with(name, 'Mongo')].{Name:name, Value:value}" `
  --output table

Write-Host ""
Write-Host "Azure Function App is configured!" -ForegroundColor Green
