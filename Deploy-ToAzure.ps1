<#
.SYNOPSIS
    Deploy Cafe Website API to Azure Functions

.DESCRIPTION
    This script deploys the Cafe Website API to Azure Functions and configures all app settings.
    It handles resource creation, deployment, and configuration.

.PARAMETER ResourceGroupName
    The name of the Azure resource group

.PARAMETER Location
    The Azure region for deployment (default: eastus)

.PARAMETER FunctionAppName
    The name of the Function App (must be globally unique)

.PARAMETER StorageAccountName
    The name of the storage account (must be globally unique)

.PARAMETER SkipBuild
    Skip the build step if already built

.EXAMPLE
    .\Deploy-ToAzure.ps1 -ResourceGroupName "CafeWebsite-RG" -FunctionAppName "cafemaatara-api" -StorageAccountName "cafemaatarastorage"
#>

param(
    [Parameter(Mandatory=$true)]
    [string]$ResourceGroupName,
    
    [Parameter(Mandatory=$false)]
    [string]$Location = "eastus",
    
    [Parameter(Mandatory=$true)]
    [string]$FunctionAppName,
    
    [Parameter(Mandatory=$true)]
    [string]$StorageAccountName,
    
    [Parameter(Mandatory=$false)]
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

# Suppress Azure CLI warnings (including Python 32-bit warning)
$env:AZURE_CORE_ONLY_SHOW_ERRORS = "True"
$env:PYTHONWARNINGS = "ignore"

# Colors for output
function Write-ColorOutput($ForegroundColor) {
    $fc = $host.UI.RawUI.ForegroundColor
    $host.UI.RawUI.ForegroundColor = $ForegroundColor
    if ($args) {
        Write-Output $args
    }
    $host.UI.RawUI.ForegroundColor = $fc
}

function Write-Step {
    param([string]$Message)
    Write-ColorOutput Cyan "`n==> $Message"
}

function Write-Success {
    param([string]$Message)
    Write-ColorOutput Green "[OK] $Message"
}

function Write-Warning {
    param([string]$Message)
    Write-ColorOutput Yellow "[WARNING] $Message"
}

function Write-Error {
    param([string]$Message)
    Write-ColorOutput Red "[ERROR] $Message"
}

# Validate Azure CLI is installed
Write-Step "Validating prerequisites..."
try {
    $azVersion = az version --output json | ConvertFrom-Json
    Write-Success "Azure CLI version: $($azVersion.'azure-cli')"
} catch {
    Write-Error "Azure CLI is not installed. Please install from https://aka.ms/installazurecliwindows"
    exit 1
}

# Check if logged in
Write-Step "Checking Azure login status..."
$account = az account show 2>$null
if (!$account) {
    Write-Warning "Not logged in to Azure. Initiating login..."
    az login
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Azure login failed"
        exit 1
    }
}

$accountInfo = az account show | ConvertFrom-Json
Write-Success "Logged in as: $($accountInfo.user.name)"
Write-Success "Subscription: $($accountInfo.name) ($($accountInfo.id))"

# Create Resource Group
Write-Step "Creating resource group: $ResourceGroupName"
$rgExists = az group exists --name $ResourceGroupName
if ($rgExists -eq "true") {
    Write-Warning "Resource group already exists"
} else {
    az group create --name $ResourceGroupName --location $Location --only-show-errors --output none
    Write-Success "Resource group created"
}

# Create Storage Account
Write-Step "Creating storage account: $StorageAccountName"
$storageExists = az storage account check-name --name $StorageAccountName | ConvertFrom-Json
if ($storageExists.nameAvailable -eq $false) {
    if ($storageExists.reason -eq "AlreadyExists") {
        Write-Warning "Storage account already exists"
    } else {
        Write-Error "Storage account name is invalid: $($storageExists.message)"
        exit 1
    }
} else {
    az storage account create `
        --name $StorageAccountName `
        --resource-group $ResourceGroupName `
        --location $Location `
        --sku Standard_LRS `
        --only-show-errors `
        --output none
    Write-Success "Storage account created"
}

# Create Function App
Write-Step "Creating Function App: $FunctionAppName"
$ErrorActionPreference = "SilentlyContinue"
$functionAppExists = az functionapp show --name $FunctionAppName --resource-group $ResourceGroupName --only-show-errors 2>$null
$ErrorActionPreference = "Stop"
if ($functionAppExists) {
    Write-Warning "Function App already exists"
} else {
    az functionapp create `
        --name $FunctionAppName `
        --resource-group $ResourceGroupName `
        --storage-account $StorageAccountName `
        --consumption-plan-location $Location `
        --runtime dotnet-isolated `
        --runtime-version 9.0 `
        --functions-version 4 `
        --os-type Windows `
        --only-show-errors `
        --output none
    Write-Success "Function App created"
}

# Configure CORS
Write-Step "Configuring CORS..."
$ErrorActionPreference = "SilentlyContinue"
az functionapp cors add `
    --name $FunctionAppName `
    --resource-group $ResourceGroupName `
    --allowed-origins "https://your-frontend-url.azurestaticapps.net" `
    --only-show-errors `
    --output none 2>$null
$ErrorActionPreference = "Stop"
Write-Success "CORS configured"

# Build the project
if (-not $SkipBuild) {
    Write-Step "Building the project..."
    Push-Location api
    try {
        dotnet publish --configuration Release --output ./publish
        if ($LASTEXITCODE -ne 0) {
            throw "Build failed"
        }
        Write-Success "Build completed"
    } finally {
        Pop-Location
    }
} else {
    Write-Warning "Skipping build (using existing publish folder)"
}

# Deploy to Azure
Write-Step "Deploying to Azure Functions..."
Push-Location api/publish
try {
    Compress-Archive -Path * -DestinationPath ../deploy.zip -Force
    Write-Success "Created deployment package"
    
    $ErrorActionPreference = "SilentlyContinue"
    az functionapp deployment source config-zip `
        --name $FunctionAppName `
        --resource-group $ResourceGroupName `
        --src ../deploy.zip `
        --only-show-errors `
        --output none
    $ErrorActionPreference = "Stop"
    
    Remove-Item ../deploy.zip -Force
    Write-Success "Deployment completed"
} finally {
    Pop-Location
}

# Configure App Settings
Write-Step "Configuring app settings..."
$settingsFile = "azure-app-settings.json"
if (Test-Path $settingsFile) {
    $settings = Get-Content $settingsFile | ConvertFrom-Json
    
    Write-Warning "Please update the following settings in azure-app-settings.json:"
    Write-Warning "  - EmailService__SmtpPassword (your Gmail app password)"
    Write-Warning "  - EmailService__BaseUrl (your frontend URL)"
    Write-Warning ""
    
    $confirmation = Read-Host "Have you updated azure-app-settings.json? (y/n)"
    if ($confirmation -ne 'y') {
        Write-Error "Please update azure-app-settings.json before continuing"
        exit 1
    }
    
    foreach ($setting in $settings) {
        $ErrorActionPreference = "SilentlyContinue"
        az functionapp config appsettings set `
            --name $FunctionAppName `
            --resource-group $ResourceGroupName `
            --settings "$($setting.name)=$($setting.value)" `
            --only-show-errors `
            --output none
        $ErrorActionPreference = "Stop"
    }
    Write-Success "App settings configured"
} else {
    Write-Error "azure-app-settings.json not found"
    exit 1
}

# Get Function App URL
$ErrorActionPreference = "SilentlyContinue"
$functionAppUrl = az functionapp show `
    --name $FunctionAppName `
    --resource-group $ResourceGroupName `
    --query "defaultHostName" `
    --output tsv 2>$null
$ErrorActionPreference = "Stop"

Write-Step "Deployment Summary"
Write-Success "Resource Group: $ResourceGroupName"
Write-Success "Function App: $FunctionAppName"
Write-Success "Function App URL: https://$functionAppUrl"
Write-Success "API Base URL: https://$functionAppUrl/api"

Write-ColorOutput Cyan "`n==> Next Steps:"
Write-Output "1. Update your frontend to use: https://$functionAppUrl/api"
Write-Output "2. Update CORS settings if needed: az functionapp cors add -n $FunctionAppName -g $ResourceGroupName --allowed-origins 'YOUR_FRONTEND_URL'"
Write-Output "3. Test the API endpoints: https://$functionAppUrl/api/auth/admin/verify"
Write-Output "4. Monitor logs: az functionapp log tail -n $FunctionAppName -g $ResourceGroupName"
Write-Output "5. View in Azure Portal: https://portal.azure.com/#resource/subscriptions/$($accountInfo.id)/resourceGroups/$ResourceGroupName/providers/Microsoft.Web/sites/$FunctionAppName"

Write-ColorOutput Green "`n[SUCCESS] Deployment completed successfully!"
