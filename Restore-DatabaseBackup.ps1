<#
.SYNOPSIS
    Restores database backups from Azure Blob Storage backups created by DatabaseBackupFunction.

.DESCRIPTION
    Supports safe restore workflow:
      1) List available backup snapshots
      2) Download selected snapshot blobs (.json.gz)
      3) Decompress to .json
      4) Import collections into MongoDB using mongoimport

    By default, this script lists snapshots when -List is provided.
    To restore, provide -SnapshotPrefix.

.REQUIREMENTS
    - Azure CLI (az) for blob list/download
    - MongoDB Database Tools (mongoimport) for restore/import

.PARAMETER SnapshotPrefix
    Backup snapshot prefix to restore (example: 2026-07-10T203000Z-scheduled).

.PARAMETER List
    Lists available snapshot prefixes and exits.

.PARAMETER DownloadOnly
    Downloads and decompresses snapshot files, but does not import.

.PARAMETER DropCollections
    Drops each target collection before import. Use with caution.

.PARAMETER SettingsPath
    Path to local.settings.json. Defaults to ./api/local.settings.json

.PARAMETER OutputPath
    Folder where downloaded/decompressed backup files will be stored.
    Defaults to ./restore_<timestamp>/<snapshotPrefix>

.PARAMETER BackupContainer
    Blob container name. Defaults to database-backups.

.EXAMPLE
    .\Restore-DatabaseBackup.ps1 -List

.EXAMPLE
    .\Restore-DatabaseBackup.ps1 -SnapshotPrefix "2026-07-10T203000Z-scheduled"

.EXAMPLE
    .\Restore-DatabaseBackup.ps1 -SnapshotPrefix "2026-07-10T203000Z-scheduled" -DropCollections

.EXAMPLE
    .\Restore-DatabaseBackup.ps1 -SnapshotPrefix "2026-07-10T203000Z-scheduled" -DownloadOnly
#>

param(
    [string]$SnapshotPrefix,
    [switch]$List,
    [switch]$DownloadOnly,
    [switch]$DropCollections,
    [string]$SettingsPath = (Join-Path $PSScriptRoot "api\local.settings.json"),
    [string]$OutputPath,
    [string]$BackupContainer = "database-backups"
)

$ErrorActionPreference = "Stop"

function Write-Info([string]$msg) { Write-Host $msg -ForegroundColor Cyan }
function Write-Warn([string]$msg) { Write-Host $msg -ForegroundColor Yellow }
function Write-Ok([string]$msg)   { Write-Host $msg -ForegroundColor Green }
function Write-Err([string]$msg)  { Write-Host $msg -ForegroundColor Red }

function Ensure-Command([string]$command, [string]$installHint)
{
    $cmd = Get-Command $command -ErrorAction SilentlyContinue
    if (-not $cmd)
    {
        Write-Err "$command not found."
        if ($installHint) { Write-Host $installHint -ForegroundColor Yellow }
        exit 1
    }
}

function Get-Settings()
{
    if (-not (Test-Path $SettingsPath))
    {
        Write-Err "Settings file not found: $SettingsPath"
        exit 1
    }

    $settings = Get-Content $SettingsPath -Raw | ConvertFrom-Json
    $mongoCs = $settings.Values.'Mongo__ConnectionString'
    $mongoDb = $settings.Values.'Mongo__Database'
    $blobCs = $settings.Values.'Blob__ConnectionString'

    if ([string]::IsNullOrWhiteSpace($mongoCs))
    {
        Write-Err "Mongo__ConnectionString not found in $SettingsPath"
        exit 1
    }

    if ([string]::IsNullOrWhiteSpace($mongoDb))
    {
        Write-Warn "Mongo__Database not found in $SettingsPath. Falling back to CafeDB"
        $mongoDb = "CafeDB"
    }

    if ([string]::IsNullOrWhiteSpace($blobCs))
    {
        Write-Err "Blob__ConnectionString not found in $SettingsPath"
        exit 1
    }

    return [PSCustomObject]@{
        MongoConnectionString = $mongoCs
        MongoDatabase = $mongoDb
        BlobConnectionString = $blobCs
    }
}

function Get-BackupBlobs([string]$blobConnectionString, [string]$container, [string]$prefix = "")
{
    $args = @(
        "storage", "blob", "list",
        "--container-name", $container,
        "--connection-string", $blobConnectionString,
        "--num-results", "5000",
        "--output", "json"
    )

    if (-not [string]::IsNullOrWhiteSpace($prefix))
    {
        $args += @("--prefix", $prefix)
    }

    $raw = & az @args
    if ($LASTEXITCODE -ne 0)
    {
        Write-Err "Failed to list blobs from container '$container'."
        exit 1
    }

    return ($raw | ConvertFrom-Json)
}

function Show-Snapshots($blobs)
{
    $prefixMap = @{}

    foreach ($b in $blobs)
    {
        if (-not $b.name) { continue }
        $parts = $b.name -split "/"
        if ($parts.Length -lt 2) { continue }

        $prefix = $parts[0]
        if (-not $prefixMap.ContainsKey($prefix))
        {
            $prefixMap[$prefix] = [PSCustomObject]@{
                Prefix = $prefix
                BlobCount = 0
                TotalBytes = 0
                LastModified = $null
            }
        }

        $entry = $prefixMap[$prefix]
        $entry.BlobCount += 1
        if ($b.properties.contentLength)
        {
            $entry.TotalBytes += [int64]$b.properties.contentLength
        }

        $lm = $b.properties.lastModified
        if ($lm)
        {
            $dt = [datetime]$lm
            if ($null -eq $entry.LastModified -or $dt -gt $entry.LastModified)
            {
                $entry.LastModified = $dt
            }
        }
    }

    $items = $prefixMap.Values | Sort-Object Prefix -Descending

    if ($items.Count -eq 0)
    {
        Write-Warn "No backup snapshots found in '$BackupContainer'."
        return
    }

    Write-Host ""
    Write-Info "Available backup snapshots:"
    foreach ($i in $items)
    {
        $mb = [math]::Round(($i.TotalBytes / 1MB), 2)
        Write-Host "- $($i.Prefix) | blobs: $($i.BlobCount) | size: $mb MB | lastModified: $($i.LastModified)"
    }
    Write-Host ""
}

function Download-Blob([string]$blobConnectionString, [string]$container, [string]$blobName, [string]$targetPath)
{
    $targetDir = Split-Path -Path $targetPath -Parent
    if (-not (Test-Path $targetDir))
    {
        New-Item -Path $targetDir -ItemType Directory -Force | Out-Null
    }

    $args = @(
        "storage", "blob", "download",
        "--container-name", $container,
        "--name", $blobName,
        "--file", $targetPath,
        "--connection-string", $blobConnectionString,
        "--overwrite", "true",
        "--output", "none"
    )

    & az @args | Out-Null
    if ($LASTEXITCODE -ne 0)
    {
        Write-Err "Failed to download blob '$blobName'."
        exit 1
    }
}

function Decompress-GzipFile([string]$sourcePath, [string]$targetPath)
{
    $inStream = [System.IO.File]::OpenRead($sourcePath)
    try
    {
        $gzip = New-Object System.IO.Compression.GZipStream($inStream, [System.IO.Compression.CompressionMode]::Decompress)
        try
        {
            $outStream = [System.IO.File]::Create($targetPath)
            try
            {
                $gzip.CopyTo($outStream)
            }
            finally
            {
                $outStream.Dispose()
            }
        }
        finally
        {
            $gzip.Dispose()
        }
    }
    finally
    {
        $inStream.Dispose()
    }
}

function Import-Collection(
    [string]$mongoConnectionString,
    [string]$mongoDatabase,
    [string]$collectionName,
    [string]$jsonFilePath,
    [bool]$drop
)
{
    $args = @(
        "--uri=$mongoConnectionString",
        "--db=$mongoDatabase",
        "--collection=$collectionName",
        "--file=$jsonFilePath",
        "--jsonArray"
    )

    if ($drop)
    {
        $args += "--drop"
    }

    & mongoimport @args
    if ($LASTEXITCODE -ne 0)
    {
        Write-Err "mongoimport failed for collection '$collectionName'."
        exit 1
    }
}

Ensure-Command -command "az" -installHint "Install Azure CLI: https://learn.microsoft.com/cli/azure/install-azure-cli"

$cfg = Get-Settings

Write-Host ""
Write-Info "=== Restore Database Backup ==="
Write-Host "Mongo DB:           $($cfg.MongoDatabase)"
Write-Host "Backup container:   $BackupContainer"
Write-Host "Mode:               $(if ($DownloadOnly) { 'DownloadOnly' } else { 'Restore' })"
Write-Host "Drop collections:   $(if ($DropCollections) { 'Yes' } else { 'No' })"
Write-Host ""

$allBlobs = Get-BackupBlobs -blobConnectionString $cfg.BlobConnectionString -container $BackupContainer

if ($List)
{
    Show-Snapshots -blobs $allBlobs
    exit 0
}

if ([string]::IsNullOrWhiteSpace($SnapshotPrefix))
{
    Write-Warn "SnapshotPrefix is required for restore. Use -List to view available snapshots."
    Show-Snapshots -blobs $allBlobs
    exit 1
}

$prefixWithSlash = "$SnapshotPrefix/"
$snapshotBlobs = $allBlobs | Where-Object { $_.name -like "$prefixWithSlash*" }

if (-not $snapshotBlobs -or $snapshotBlobs.Count -eq 0)
{
    Write-Err "Snapshot '$SnapshotPrefix' not found in container '$BackupContainer'."
    exit 1
}

if ([string]::IsNullOrWhiteSpace($OutputPath))
{
    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $OutputPath = Join-Path $PSScriptRoot "restore_$timestamp\$SnapshotPrefix"
}

if (-not (Test-Path $OutputPath))
{
    New-Item -Path $OutputPath -ItemType Directory -Force | Out-Null
}

Write-Info "Downloading snapshot '$SnapshotPrefix' to: $OutputPath"

$jsonGzFiles = @()
foreach ($blob in $snapshotBlobs)
{
    $name = [string]$blob.name
    $relative = $name.Substring($prefixWithSlash.Length)
    $localFile = Join-Path $OutputPath $relative

    Download-Blob -blobConnectionString $cfg.BlobConnectionString -container $BackupContainer -blobName $name -targetPath $localFile

    if ($localFile.ToLowerInvariant().EndsWith(".json.gz"))
    {
        $jsonGzFiles += $localFile
    }
}

if ($jsonGzFiles.Count -eq 0)
{
    Write-Err "No .json.gz files found in snapshot '$SnapshotPrefix'."
    exit 1
}

Write-Info "Decompressing backup files..."

$jsonFiles = @()
foreach ($gz in $jsonGzFiles)
{
    $jsonPath = $gz.Substring(0, $gz.Length - 3) # remove .gz
    Decompress-GzipFile -sourcePath $gz -targetPath $jsonPath
    $jsonFiles += $jsonPath
}

Write-Ok "Downloaded and decompressed $($jsonFiles.Count) collection files."

if ($DownloadOnly)
{
    Write-Ok "DownloadOnly completed. Files are ready at: $OutputPath"
    exit 0
}

Ensure-Command -command "mongoimport" -installHint "Install MongoDB Database Tools: https://www.mongodb.com/try/download/database-tools"

Write-Info "Starting MongoDB restore/import..."

$imported = 0
foreach ($json in $jsonFiles)
{
    $fileName = Split-Path $json -Leaf
    if ($fileName -eq "_metadata.json") { continue }

    if (-not $fileName.ToLowerInvariant().EndsWith(".json")) { continue }

    $collectionName = [System.IO.Path]::GetFileNameWithoutExtension($fileName)
    if ([string]::IsNullOrWhiteSpace($collectionName)) { continue }

    Write-Host "- Importing $collectionName ..."
    Import-Collection -mongoConnectionString $cfg.MongoConnectionString -mongoDatabase $cfg.MongoDatabase -collectionName $collectionName -jsonFilePath $json -drop:$DropCollections
    $imported++
}

Write-Host ""
Write-Ok "Restore completed successfully."
Write-Host "Snapshot:      $SnapshotPrefix"
Write-Host "Collections:   $imported"
Write-Host "Output folder: $OutputPath"
Write-Host ""