<#
.SYNOPSIS
    Manual database backup script using mongodump.
    
.DESCRIPTION
    Creates a local backup of the MongoDB Atlas database.
    Requires MongoDB Database Tools installed (mongodump).
    Download: https://www.mongodb.com/try/download/database-tools

.PARAMETER OutputPath
    Directory to store backups. Defaults to ./backup_<timestamp>

.EXAMPLE
    .\Backup-Database.ps1
    .\Backup-Database.ps1 -OutputPath "D:\Backups\CafeDB"
#>

param(
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"

# ─── Load connection settings ───
$settingsPath = Join-Path $PSScriptRoot "api\local.settings.json"
if (-not (Test-Path $settingsPath)) {
    Write-Error "local.settings.json not found at $settingsPath"
    exit 1
}

$settings = Get-Content $settingsPath -Raw | ConvertFrom-Json
$connectionString = $settings.Values.'Mongo__ConnectionString'
$databaseName = $settings.Values.'Mongo__Database'

if ([string]::IsNullOrEmpty($connectionString)) {
    Write-Error "Mongo__ConnectionString not found in local.settings.json"
    exit 1
}
if ([string]::IsNullOrEmpty($databaseName)) {
    $databaseName = "CafeDB"
}

# ─── Check mongodump is available ───
$mongodump = Get-Command mongodump -ErrorAction SilentlyContinue
if (-not $mongodump) {
    Write-Host ""
    Write-Host "ERROR: mongodump not found." -ForegroundColor Red
    Write-Host "Install MongoDB Database Tools from:" -ForegroundColor Yellow
    Write-Host "  https://www.mongodb.com/try/download/database-tools" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Alternative: Use the automated Azure Function backup instead:" -ForegroundColor Yellow
    Write-Host "  POST /api/admin/backup  (requires admin auth)" -ForegroundColor Cyan
    exit 1
}

# ─── Set output path ───
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
if ([string]::IsNullOrEmpty($OutputPath)) {
    $OutputPath = Join-Path $PSScriptRoot "backup_$timestamp"
}

Write-Host ""
Write-Host "=== Cafe Database Backup ===" -ForegroundColor Cyan
Write-Host "Database:    $databaseName"
Write-Host "Output:      $OutputPath"
Write-Host "Timestamp:   $timestamp"
Write-Host ""

# ─── Run mongodump ───
Write-Host "Starting backup..." -ForegroundColor Yellow

$dumpPath = Join-Path $OutputPath $databaseName

& mongodump `
    --uri="$connectionString" `
    --db="$databaseName" `
    --out="$OutputPath" `
    --gzip

if ($LASTEXITCODE -ne 0) {
    Write-Error "mongodump failed with exit code $LASTEXITCODE"
    exit 1
}

# ─── Report results ───
$files = Get-ChildItem -Path $dumpPath -Recurse -File
$totalSize = ($files | Measure-Object -Property Length -Sum).Sum
$sizeFormatted = if ($totalSize -gt 1MB) { 
    "{0:N2} MB" -f ($totalSize / 1MB) 
} else { 
    "{0:N2} KB" -f ($totalSize / 1KB) 
}

Write-Host ""
Write-Host "=== Backup Complete ===" -ForegroundColor Green
Write-Host "Collections: $($files.Count) files"
Write-Host "Total size:  $sizeFormatted (gzipped)"
Write-Host "Location:    $dumpPath"
Write-Host ""

# ─── Cleanup old backups (keep last 5) ───
$backupDirs = Get-ChildItem -Path $PSScriptRoot -Directory -Filter "backup_*" | 
    Sort-Object Name -Descending | 
    Select-Object -Skip 5

if ($backupDirs.Count -gt 0) {
    Write-Host "Cleaning up old backups (keeping last 5)..." -ForegroundColor Yellow
    foreach ($dir in $backupDirs) {
        Remove-Item -Path $dir.FullName -Recurse -Force
        Write-Host "  Removed: $($dir.Name)" -ForegroundColor DarkGray
    }
    Write-Host ""
}

Write-Host "Done!" -ForegroundColor Green
