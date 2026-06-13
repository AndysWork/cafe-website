<#
.SYNOPSIS
    Copies future menu prices into current menu prices in MongoDB.

.DESCRIPTION
    Updates CafeMenu records so:
      - Current Shop Price    <- Future Shop Price
      - Current Online Price  <- Future Online Price

    Safe by default:
      - Dry-run mode unless -Apply is provided
      - Only applies when future prices are numeric and > 0
      - Supports optional outlet-level targeting

.PARAMETER Apply
    Execute the update. Without this switch, script runs in dry-run mode.

.PARAMETER OutletId
    Optional outlet ID to limit updates to a single outlet.

.PARAMETER ClearFuturePrices
    Optional. When used with -Apply, clears future price fields after copying.

.PARAMETER SettingsPath
    Path to local.settings.json. Defaults to ./api/local.settings.json

.EXAMPLE
    .\Apply-FutureMenuPrices.ps1

.EXAMPLE
    .\Apply-FutureMenuPrices.ps1 -Apply

.EXAMPLE
    .\Apply-FutureMenuPrices.ps1 -Apply -OutletId "678c9f4a1b2c3d4e5f6a7b8c"

.EXAMPLE
    .\Apply-FutureMenuPrices.ps1 -Apply -ClearFuturePrices
#>

param(
    [switch]$Apply,
    [string]$OutletId,
    [switch]$ClearFuturePrices,
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
    Write-Host "Download: https://www.mongodb.com/try/download/shell" -ForegroundColor Yellow
    exit 1
}

Write-Info "=== Future -> Current Menu Price Sync ==="
Write-Host "Database: $databaseName"
Write-Host "OutletId: $(if ($OutletId) { $OutletId } else { '<all>' })"
Write-Host "Mode:     $(if ($Apply) { 'APPLY' } else { 'DRY-RUN' })"
Write-Host "Clear future fields after apply: $($ClearFuturePrices.IsPresent)"
Write-Host ""

# Escape values for JS literal safety.
$escapedCs = $connectionString.Replace("\", "\\").Replace("'", "\'")
$escapedDb = $databaseName.Replace("\", "\\").Replace("'", "\'")
$outletSafe = if ($null -eq $OutletId) { "" } else { $OutletId }
$escapedOutlet = $outletSafe.Replace("\", "\\").Replace("'", "\'")

$js = @'
const connectionString = '__CS__';
const databaseName = '__DB__';
const outletId = '__OUTLET__';
const apply = __APPLY__;
const clearFuturePrices = __CLEAR__;

const conn = new Mongo(connectionString);
const dbRef = conn.getDB(databaseName);
const col = dbRef.getCollection('CafeMenu');

const sample = col.findOne({});
if (!sample) {
  print(JSON.stringify({
    ok: true,
    mode: apply ? 'apply' : 'dry-run',
    message: 'CafeMenu collection has no documents.'
  }));
  quit(0);
}

function pickField(candidates, fallback) {
  for (const f of candidates) {
    if (Object.prototype.hasOwnProperty.call(sample, f)) return f;
  }
  return fallback;
}

const fShop = pickField(['shopSellingPrice', 'ShopSellingPrice'], 'ShopSellingPrice');
const fOnline = pickField(['onlinePrice', 'OnlinePrice'], 'OnlinePrice');
const fFutureShop = pickField(['futureShopPrice', 'FutureShopPrice'], 'futureShopPrice');
const fFutureOnline = pickField(['futureOnlinePrice', 'FutureOnlinePrice'], 'futureOnlinePrice');
const fOutlet = pickField(['outletId', 'OutletId'], 'outletId');
const fDeleted = pickField(['isDeleted', 'IsDeleted'], 'isDeleted');
const fLastUpdated = pickField(['lastUpdated', 'LastUpdated'], 'LastUpdated');

const baseFilter = {};
baseFilter[fDeleted] = { $ne: true };
if (outletId) baseFilter[fOutlet] = outletId;

const shopFutureCond = {};
shopFutureCond[fFutureShop] = { $type: 'number', $gt: 0 };

const onlineFutureCond = {};
onlineFutureCond[fFutureOnline] = { $type: 'number', $gt: 0 };

const eligibleFilter = {
  $and: [
    baseFilter,
    { $or: [shopFutureCond, onlineFutureCond] }
  ]
};

const totalTargeted = col.countDocuments(baseFilter);
const eligibleCount = col.countDocuments(eligibleFilter);

if (!apply) {
  print(JSON.stringify({
    ok: true,
    mode: 'dry-run',
    targetedDocuments: totalTargeted,
    eligibleForUpdate: eligibleCount,
    fieldMap: {
      shopField: fShop,
      onlineField: fOnline,
      futureShopField: fFutureShop,
      futureOnlineField: fFutureOnline,
      outletField: fOutlet,
      deletedField: fDeleted,
      lastUpdatedField: fLastUpdated
    }
  }));
  quit(0);
}

const setStage = {};
setStage[fShop] = {
  $cond: [
    { $and: [ { $ne: [ '$' + fFutureShop, null ] }, { $gt: [ '$' + fFutureShop, 0 ] } ] },
    '$' + fFutureShop,
    '$' + fShop
  ]
};
setStage[fOnline] = {
  $cond: [
    { $and: [ { $ne: [ '$' + fFutureOnline, null ] }, { $gt: [ '$' + fFutureOnline, 0 ] } ] },
    '$' + fFutureOnline,
    '$' + fOnline
  ]
};
setStage[fLastUpdated] = '$$NOW';

if (clearFuturePrices) {
  setStage[fFutureShop] = null;
  setStage[fFutureOnline] = null;
}

const pipeline = [ { $set: setStage } ];
const result = col.updateMany(eligibleFilter, pipeline);

print(JSON.stringify({
  ok: true,
  mode: 'apply',
  targetedDocuments: totalTargeted,
  eligibleForUpdate: eligibleCount,
  matchedCount: result.matchedCount,
  modifiedCount: result.modifiedCount,
  clearFuturePrices,
  fieldMap: {
    shopField: fShop,
    onlineField: fOnline,
    futureShopField: fFutureShop,
    futureOnlineField: fFutureOnline,
    outletField: fOutlet,
    deletedField: fDeleted,
    lastUpdatedField: fLastUpdated
  }
}));
'@

$js = $js.Replace('__CS__', $escapedCs)
$js = $js.Replace('__DB__', $escapedDb)
$js = $js.Replace('__OUTLET__', $escapedOutlet)
$js = $js.Replace('__APPLY__', $Apply.IsPresent.ToString().ToLowerInvariant())
$js = $js.Replace('__CLEAR__', $ClearFuturePrices.IsPresent.ToString().ToLowerInvariant())

$tempJs = Join-Path $env:TEMP ("menu-future-price-sync-{0}.js" -f ([Guid]::NewGuid().ToString("N")))
$js | Set-Content -Path $tempJs -Encoding UTF8

try {
    $output = & mongosh --nodb --quiet --norc --file $tempJs 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Err "mongosh execution failed."
        $output | ForEach-Object { Write-Host $_ }
        exit $LASTEXITCODE
    }

    $jsonLine = $output | Where-Object { $_ -match '^\s*\{.*\}\s*$' } | Select-Object -Last 1
    if (-not $jsonLine) {
        Write-Warn "Could not parse JSON output. Raw output:"
        $output | ForEach-Object { Write-Host $_ }
        exit 0
    }

    $result = $jsonLine | ConvertFrom-Json

    if (-not $result.ok) {
        Write-Err "Operation failed"
        $result | ConvertTo-Json -Depth 8 | Write-Host
        exit 1
    }

    if ($result.mode -eq 'dry-run') {
        Write-Ok "Dry-run complete."
        Write-Host "Targeted docs:      $($result.targetedDocuments)"
        Write-Host "Eligible updates:   $($result.eligibleForUpdate)"
        Write-Host ""
        Write-Warn "No data was changed. Use -Apply to execute updates."
    }
    else {
        Write-Ok "Apply complete."
        Write-Host "Targeted docs:      $($result.targetedDocuments)"
        Write-Host "Eligible updates:   $($result.eligibleForUpdate)"
        Write-Host "Matched docs:       $($result.matchedCount)"
        Write-Host "Modified docs:      $($result.modifiedCount)"
        Write-Host "Cleared futures:    $($result.clearFuturePrices)"
    }

    Write-Host ""
    Write-Host "Field mapping used:" -ForegroundColor Cyan
    Write-Host "  Shop:             $($result.fieldMap.shopField)"
    Write-Host "  Online:           $($result.fieldMap.onlineField)"
    Write-Host "  Future Shop:      $($result.fieldMap.futureShopField)"
    Write-Host "  Future Online:    $($result.fieldMap.futureOnlineField)"
    Write-Host "  Outlet:           $($result.fieldMap.outletField)"
    Write-Host "  Deleted Flag:     $($result.fieldMap.deletedField)"
    Write-Host "  Last Updated:     $($result.fieldMap.lastUpdatedField)"
}
finally {
    if (Test-Path $tempJs) {
        Remove-Item -Path $tempJs -Force -ErrorAction SilentlyContinue
    }
}
