<#
.SYNOPSIS
    Backfills missing order channel values in MongoDB.

.DESCRIPTION
    Sets channel for legacy orders where channel is null/missing/empty:
      - dine-in -> shop
      - otherwise -> web

    Safe by default (dry-run). Use -Apply to persist changes.

.PARAMETER Apply
    Execute updates. Without this switch, script runs in dry-run mode.

.PARAMETER SettingsPath
    Path to local.settings.json. Defaults to ./api/local.settings.json

.EXAMPLE
    .\Backfill-OrderChannels.ps1

.EXAMPLE
    .\Backfill-OrderChannels.ps1 -Apply
#>

param(
    [switch]$Apply,
    [string]$SettingsPath = (Join-Path $PSScriptRoot "api\local.settings.json")
)

$ErrorActionPreference = "Stop"

function Write-Info([string]$msg) { Write-Host $msg -ForegroundColor Cyan }
function Write-Warn([string]$msg) { Write-Host $msg -ForegroundColor Yellow }
function Write-Ok([string]$msg)   { Write-Host $msg -ForegroundColor Green }
function Write-Err([string]$msg)  { Write-Host $msg -ForegroundColor Red }

if (-not (Test-Path $SettingsPath)) {
    Write-Err "Settings file not found: $SettingsPath"
    exit 1
}

$settings = Get-Content $SettingsPath -Raw | ConvertFrom-Json
$connectionString = $settings.Values.'Mongo__ConnectionString'
$databaseName = $settings.Values.'Mongo__Database'

if ([string]::IsNullOrWhiteSpace($connectionString)) {
    Write-Err "Mongo__ConnectionString not found in $SettingsPath"
    exit 1
}
if ([string]::IsNullOrWhiteSpace($databaseName)) {
    Write-Warn "Mongo__Database not found. Falling back to CafeDB"
    $databaseName = "CafeDB"
}

$mongosh = Get-Command mongosh -ErrorAction SilentlyContinue
if (-not $mongosh) {
    Write-Err "mongosh not found. Install MongoDB Shell to run this script."
    exit 1
}

Write-Info "=== Legacy Order Channel Backfill ==="
Write-Host "Database: $databaseName"
Write-Host "Mode:     $(if ($Apply) { 'APPLY' } else { 'DRY-RUN' })"
Write-Host ""

$escapedCs = $connectionString.Replace("\", "\\").Replace("'", "\'")
$escapedDb = $databaseName.Replace("\", "\\").Replace("'", "\'")

$js = @'
const connectionString = '__CS__';
const databaseName = '__DB__';
const apply = __APPLY__;

const conn = new Mongo(connectionString);
const dbRef = conn.getDB(databaseName);
const orders = dbRef.getCollection('Orders');

const missingFilter = {
  $or: [
    { channel: { $exists: false } },
    { channel: null },
    { channel: '' }
  ]
};

const candidates = orders.find(missingFilter).toArray();
const totalCandidates = candidates.length;

let inferredShop = 0;
let inferredWeb = 0;
for (const o of candidates) {
  if ((o.orderType || '').toString().trim().toLowerCase() === 'dine-in') {
    inferredShop += 1;
  } else {
    inferredWeb += 1;
  }
}

if (!apply) {
  print(JSON.stringify({
    ok: true,
    mode: 'dry-run',
    totalCandidates,
    inferredShop,
    inferredWeb
  }));
  quit(0);
}

const updateResult = orders.updateMany(
  missingFilter,
  [
    {
      $set: {
        channel: {
          $cond: [
            { $eq: ['$orderType', 'dine-in'] },
            'shop',
            'web'
          ]
        }
      }
    }
  ]
);

print(JSON.stringify({
  ok: true,
  mode: 'apply',
  totalCandidates,
  inferredShop,
  inferredWeb,
  matchedCount: updateResult.matchedCount,
  modifiedCount: updateResult.modifiedCount
}));
'@

$js = $js.Replace('__CS__', $escapedCs)
$js = $js.Replace('__DB__', $escapedDb)
$js = $js.Replace('__APPLY__', $(if ($Apply) { 'true' } else { 'false' }))

$result = & mongosh --quiet --norc --eval $js
if ($LASTEXITCODE -ne 0) {
    Write-Err "mongosh execution failed"
    exit $LASTEXITCODE
}

try {
    $obj = $result | ConvertFrom-Json
    Write-Host "Candidates: $($obj.totalCandidates)"
    Write-Host "Infer shop: $($obj.inferredShop)"
    Write-Host "Infer web:  $($obj.inferredWeb)"

    if ($Apply) {
        Write-Ok "Updated: $($obj.modifiedCount)"
    } else {
        Write-Warn "Dry-run only. Re-run with -Apply to persist."
    }
}
catch {
    Write-Warn "Raw mongosh output:"
    Write-Host $result
}
