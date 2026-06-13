<#
.SYNOPSIS
    Copies prices from Saved Recipes into CafeMenu prices in MongoDB.

.DESCRIPTION
    Uses Recipes.priceForecast as source and updates CafeMenu records so:
      - Current Shop Price    <- priceForecast.shopPrice
      - Current Online Price  <- priceForecast.onlinePrice

    Safe by default:
      - Dry-run mode unless -Apply is provided
      - Only applies when recipe forecast prices are numeric and > 0
      - Supports optional outlet-level targeting

.PARAMETER Apply
    Execute the update. Without this switch, script runs in dry-run mode.

.PARAMETER OutletId
    Optional outlet ID to limit updates to a single outlet.

.PARAMETER SettingsPath
    Path to local.settings.json. Defaults to ./api/local.settings.json

.EXAMPLE
    .\Apply-FutureMenuPrices.ps1

.EXAMPLE
    .\Apply-FutureMenuPrices.ps1 -Apply

.EXAMPLE
    .\Apply-FutureMenuPrices.ps1 -Apply -OutletId "678c9f4a1b2c3d4e5f6a7b8c"
#>

param(
    [switch]$Apply,
    [string]$OutletId,
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

Write-Info "=== Saved Recipe -> Menu Price Sync ==="
Write-Host "Database: $databaseName"
Write-Host "OutletId: $(if ($OutletId) { $OutletId } else { '<all>' })"
Write-Host "Mode:     $(if ($Apply) { 'APPLY' } else { 'DRY-RUN' })"
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

const conn = new Mongo(connectionString);
const dbRef = conn.getDB(databaseName);
const menuCol = dbRef.getCollection('CafeMenu');
const recipeCol = dbRef.getCollection('Recipes');

const menuSample = menuCol.findOne({});
if (!menuSample) {
  print(JSON.stringify({
    ok: true,
    mode: apply ? 'apply' : 'dry-run',
    message: 'CafeMenu collection has no documents.'
  }));
  quit(0);
}

function pickField(candidates, fallback) {
  for (const f of candidates) {
    if (Object.prototype.hasOwnProperty.call(menuSample, f)) return f;
  }
  return fallback;
}

function pickFieldFrom(doc, candidates, fallback) {
  if (!doc) return fallback;
  for (const f of candidates) {
    if (Object.prototype.hasOwnProperty.call(doc, f)) return f;
  }
  return fallback;
}

function normalize(value) {
  if (value === null || value === undefined) return '';
  return String(value).trim().toLowerCase();
}

function toPositiveNumber(value) {
  if (typeof value === 'number' && Number.isFinite(value) && value > 0) return value;
  if (typeof value === 'string') {
    const parsed = Number(value);
    if (Number.isFinite(parsed) && parsed > 0) return parsed;
    return null;
  }
  if (value && typeof value === 'object') {
    if (typeof value.valueOf === 'function') {
      const v = value.valueOf();
      if (typeof v === 'number' && Number.isFinite(v) && v > 0) return v;
      if (typeof v === 'string') {
        const parsedValue = Number(v);
        if (Number.isFinite(parsedValue) && parsedValue > 0) return parsedValue;
      }
    }
    if (typeof value.toString === 'function') {
      const s = value.toString();
      const parsedString = Number(s);
      if (Number.isFinite(parsedString) && parsedString > 0) return parsedString;
    }
    // Support Mongo extended JSON values like { "$numberDecimal": "69" }
    const raw = value.$numberDecimal ?? value.$numberDouble ?? value.$numberInt ?? value.$numberLong;
    if (raw !== undefined && raw !== null) {
      const parsed = Number(raw);
      if (Number.isFinite(parsed) && parsed > 0) return parsed;
    }
  }
  return null;
}

const recipeSample = recipeCol.findOne({}) || {};

const fShop = pickField(['shopSellingPrice', 'ShopSellingPrice'], 'ShopSellingPrice');
const fOnline = pickField(['onlinePrice', 'OnlinePrice'], 'OnlinePrice');
const fOutlet = pickField(['outletId', 'OutletId'], 'outletId');
const fDeleted = pickField(['isDeleted', 'IsDeleted'], 'isDeleted');
const fLastUpdated = pickField(['lastUpdated', 'LastUpdated'], 'LastUpdated');

const rMenuItemId = pickFieldFrom(recipeSample, ['menuItemId', 'MenuItemId'], 'menuItemId');
const rMenuItemName = pickFieldFrom(recipeSample, ['menuItemName', 'MenuItemName'], 'menuItemName');
const rPriceForecast = pickFieldFrom(recipeSample, ['priceForecast', 'PriceForecast'], 'priceForecast');
const rUpdatedAt = pickFieldFrom(recipeSample, ['updatedAt', 'UpdatedAt'], 'updatedAt');
const rOutlet = pickFieldFrom(recipeSample, ['outletId', 'OutletId'], 'outletId');

const baseFilter = {};
baseFilter[fDeleted] = { $ne: true };
if (outletId) baseFilter[fOutlet] = outletId;

const recipeFilter = {};
if (outletId) recipeFilter[rOutlet] = outletId;

const recipeSort = {};
recipeSort[rUpdatedAt] = -1;

const recipes = recipeCol.find(recipeFilter).sort(recipeSort).toArray();

const recipesById = new Map();
const recipesByName = new Map();
let recipeCandidates = 0;

for (const recipe of recipes) {
  const pf = recipe[rPriceForecast] || {};
  const shopPrice = toPositiveNumber(pf.shopPrice ?? pf.ShopPrice);
  const onlinePrice = toPositiveNumber(pf.onlinePrice ?? pf.OnlinePrice);
  if (shopPrice === null && onlinePrice === null) continue;

  recipeCandidates += 1;
  const entry = {
    shopPrice,
    onlinePrice,
    menuItemId: normalize(recipe[rMenuItemId]),
    menuItemName: normalize(recipe[rMenuItemName])
  };

  if (entry.menuItemId && !recipesById.has(entry.menuItemId)) {
    recipesById.set(entry.menuItemId, entry);
  }
  if (entry.menuItemName && !recipesByName.has(entry.menuItemName)) {
    recipesByName.set(entry.menuItemName, entry);
  }
}

const menuDocs = menuCol.find(baseFilter).toArray();
const totalTargeted = menuDocs.length;
let eligibleCount = 0;
let matchedById = 0;
let matchedByName = 0;
let matchedCount = 0;
let modifiedCount = 0;

for (const item of menuDocs) {
  const itemId = normalize(item._id ? item._id.valueOf() : item.id);
  const itemName = normalize(item.name ?? item.Name);

  let source = null;
  let sourceType = '';
  if (itemId && recipesById.has(itemId)) {
    source = recipesById.get(itemId);
    sourceType = 'id';
  } else if (itemName && recipesByName.has(itemName)) {
    source = recipesByName.get(itemName);
    sourceType = 'name';
  }

  if (!source) continue;
  eligibleCount += 1;
  if (sourceType === 'id') matchedById += 1;
  if (sourceType === 'name') matchedByName += 1;

  if (!apply) continue;

  const setDoc = {};
  if (source.shopPrice !== null) setDoc[fShop] = source.shopPrice;
  if (source.onlinePrice !== null) setDoc[fOnline] = source.onlinePrice;
  setDoc[fLastUpdated] = new Date();

  const result = menuCol.updateOne({ _id: item._id }, { $set: setDoc });
  matchedCount += result.matchedCount;
  modifiedCount += result.modifiedCount;
}

if (!apply) {
  print(JSON.stringify({
    ok: true,
    mode: 'dry-run',
    targetedDocuments: totalTargeted,
    eligibleForUpdate: eligibleCount,
    recipeCandidates,
    matchedById,
    matchedByName,
    fieldMap: {
      shopField: fShop,
      onlineField: fOnline,
      recipePriceForecastField: rPriceForecast,
      recipeShopField: 'shopPrice/ShopPrice',
      recipeOnlineField: 'onlinePrice/OnlinePrice',
      recipeMenuItemIdField: rMenuItemId,
      recipeMenuItemNameField: rMenuItemName,
      outletField: fOutlet,
      deletedField: fDeleted,
      lastUpdatedField: fLastUpdated
    }
  }));
  quit(0);
}

print(JSON.stringify({
  ok: true,
  mode: 'apply',
  targetedDocuments: totalTargeted,
  eligibleForUpdate: eligibleCount,
  recipeCandidates,
  matchedById,
  matchedByName,
  matchedCount,
  modifiedCount,
  fieldMap: {
    shopField: fShop,
    onlineField: fOnline,
    recipePriceForecastField: rPriceForecast,
    recipeShopField: 'shopPrice/ShopPrice',
    recipeOnlineField: 'onlinePrice/OnlinePrice',
    recipeMenuItemIdField: rMenuItemId,
    recipeMenuItemNameField: rMenuItemName,
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

$tempJs = Join-Path $env:TEMP ("menu-recipe-price-sync-{0}.js" -f ([Guid]::NewGuid().ToString("N")))
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
      Write-Host "Recipe candidates:  $($result.recipeCandidates)"
      Write-Host "Matched by ID:      $($result.matchedById)"
      Write-Host "Matched by Name:    $($result.matchedByName)"
        Write-Host ""
        Write-Warn "No data was changed. Use -Apply to execute updates."
    }
    else {
        Write-Ok "Apply complete."
        Write-Host "Targeted docs:      $($result.targetedDocuments)"
        Write-Host "Eligible updates:   $($result.eligibleForUpdate)"
      Write-Host "Recipe candidates:  $($result.recipeCandidates)"
      Write-Host "Matched by ID:      $($result.matchedById)"
      Write-Host "Matched by Name:    $($result.matchedByName)"
        Write-Host "Matched docs:       $($result.matchedCount)"
        Write-Host "Modified docs:      $($result.modifiedCount)"
    }

    Write-Host ""
    Write-Host "Field mapping used:" -ForegroundColor Cyan
    Write-Host "  Shop:             $($result.fieldMap.shopField)"
    Write-Host "  Online:           $($result.fieldMap.onlineField)"
    Write-Host "  Recipe Forecast:  $($result.fieldMap.recipePriceForecastField)"
    Write-Host "  Recipe Shop:      $($result.fieldMap.recipeShopField)"
    Write-Host "  Recipe Online:    $($result.fieldMap.recipeOnlineField)"
    Write-Host "  Recipe Menu ID:   $($result.fieldMap.recipeMenuItemIdField)"
    Write-Host "  Recipe Menu Name: $($result.fieldMap.recipeMenuItemNameField)"
    Write-Host "  Outlet:           $($result.fieldMap.outletField)"
    Write-Host "  Deleted Flag:     $($result.fieldMap.deletedField)"
    Write-Host "  Last Updated:     $($result.fieldMap.lastUpdatedField)"
}
finally {
    if (Test-Path $tempJs) {
        Remove-Item -Path $tempJs -Force -ErrorAction SilentlyContinue
    }
}
