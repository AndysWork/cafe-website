<#
.SYNOPSIS
    Removes duplicate categories and subcategories in MongoDB.

.DESCRIPTION
    Detects duplicates by case-insensitive name within an outlet:
      - Categories: grouped by (outletId, category name)
      - Subcategories: grouped by (outletId, effective categoryId, subcategory name)

    For each duplicate group:
      - Keeps one canonical record
      - Re-maps CafeMenu categoryId/subCategoryId references to the kept record
      - Soft-deletes duplicate records (isDeleted=true, deletedAt=now)

    Safe by default:
      - Dry-run mode unless -Apply is provided

.PARAMETER Apply
    Execute updates. Without this switch, script only reports what would change.

.PARAMETER OutletId
    Optional outlet ID to limit processing to one outlet.

.PARAMETER SettingsPath
    Path to local.settings.json. Defaults to ./api/local.settings.json

.EXAMPLE
    .\Remove-DuplicateCategories.ps1

.EXAMPLE
    .\Remove-DuplicateCategories.ps1 -Apply

.EXAMPLE
    .\Remove-DuplicateCategories.ps1 -Apply -OutletId "69622ecfda569d156985fdd7"
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
    exit 1
}

Write-Info "=== Category/SubCategory Deduplication ==="
Write-Host "Database: $databaseName"
Write-Host "OutletId: $(if ($OutletId) { $OutletId } else { '<all>' })"
Write-Host "Mode:     $(if ($Apply) { 'APPLY' } else { 'DRY-RUN' })"
Write-Host ""

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
const categoryCol = dbRef.getCollection('MenuCategory');
const subCategoryCol = dbRef.getCollection('MenuSubCategory');
const menuCol = dbRef.getCollection('CafeMenu');

function normalize(v) {
  if (v === null || v === undefined) return '';
  return String(v).trim().toLowerCase().replace(/\s+/g, ' ');
}

function toKey(v) {
  if (v === null || v === undefined) return '';
  return String(v).trim();
}

function sortedById(docs) {
  return docs.slice().sort((a, b) => toKey(a._id).localeCompare(toKey(b._id)));
}

const now = new Date();

const activeFilter = { isDeleted: { $ne: true } };
if (outletId) activeFilter.outletId = outletId;

const categories = categoryCol.find(activeFilter).toArray();
const subCategories = subCategoryCol.find(activeFilter).toArray();

const categoryGroups = new Map();
for (const c of categories) {
  const groupKey = `${toKey(c.outletId)}|${normalize(c.name)}`;
  if (!categoryGroups.has(groupKey)) categoryGroups.set(groupKey, []);
  categoryGroups.get(groupKey).push(c);
}

const categoryDupGroups = [];
const categoryRemap = new Map();
for (const [, docs] of categoryGroups) {
  if (docs.length <= 1) continue;
  const ordered = sortedById(docs);
  const keep = ordered[0];
  const duplicates = ordered.slice(1);

  categoryDupGroups.push({
    keepId: keep._id,
    keepIdStr: toKey(keep._id),
    name: keep.name,
    outletId: keep.outletId,
    duplicateIds: duplicates.map(d => d._id),
    duplicateIdStrs: duplicates.map(d => toKey(d._id))
  });

  for (const d of duplicates) {
    categoryRemap.set(toKey(d._id), keep._id);
  }
}

let remappedMenuCategoryRefs = 0;
let remappedSubCategoryCategoryRefs = 0;
let softDeletedCategories = 0;

if (apply) {
  for (const g of categoryDupGroups) {
    if (g.duplicateIds.length === 0) continue;

    const menuResult = menuCol.updateMany(
      { categoryId: { $in: g.duplicateIds } },
      { $set: { categoryId: g.keepId, lastUpdated: now } }
    );
    remappedMenuCategoryRefs += menuResult.modifiedCount;

    const subResult = subCategoryCol.updateMany(
      { categoryId: { $in: g.duplicateIds }, isDeleted: { $ne: true } },
      { $set: { categoryId: g.keepId } }
    );
    remappedSubCategoryCategoryRefs += subResult.modifiedCount;

    const deleteResult = categoryCol.updateMany(
      { _id: { $in: g.duplicateIds }, isDeleted: { $ne: true } },
      { $set: { isDeleted: true, deletedAt: now } }
    );
    softDeletedCategories += deleteResult.modifiedCount;
  }
}

function effectiveCategoryId(rawCategoryId) {
  const key = toKey(rawCategoryId);
  if (categoryRemap.has(key)) return categoryRemap.get(key);
  return rawCategoryId;
}

const subGroups = new Map();
for (const s of subCategories) {
  const effectiveCat = effectiveCategoryId(s.categoryId);
  const groupKey = `${toKey(s.outletId)}|${toKey(effectiveCat)}|${normalize(s.name)}`;
  if (!subGroups.has(groupKey)) subGroups.set(groupKey, []);
  subGroups.get(groupKey).push({ doc: s, effectiveCategoryId: effectiveCat });
}

const subDupGroups = [];
for (const [, rows] of subGroups) {
  if (rows.length <= 1) continue;

  const ordered = rows
    .slice()
    .sort((a, b) => toKey(a.doc._id).localeCompare(toKey(b.doc._id)));

  const keep = ordered[0];
  const duplicates = ordered.slice(1);

  subDupGroups.push({
    keepId: keep.doc._id,
    keepIdStr: toKey(keep.doc._id),
    keepCategoryId: keep.effectiveCategoryId,
    name: keep.doc.name,
    outletId: keep.doc.outletId,
    duplicateIds: duplicates.map(d => d.doc._id),
    duplicateIdStrs: duplicates.map(d => toKey(d.doc._id))
  });
}

let remappedMenuSubCategoryRefs = 0;
let normalizedKeptSubCategoryParent = 0;
let softDeletedSubCategories = 0;

if (apply) {
  for (const g of subDupGroups) {
    if (g.duplicateIds.length === 0) continue;

    const keepDoc = subCategoryCol.findOne({ _id: g.keepId, isDeleted: { $ne: true } });
    if (keepDoc && toKey(keepDoc.categoryId) !== toKey(g.keepCategoryId)) {
      const keepUpdate = subCategoryCol.updateOne(
        { _id: g.keepId, isDeleted: { $ne: true } },
        { $set: { categoryId: g.keepCategoryId } }
      );
      normalizedKeptSubCategoryParent += keepUpdate.modifiedCount;
    }

    const menuResult = menuCol.updateMany(
      { subCategoryId: { $in: g.duplicateIds } },
      { $set: { subCategoryId: g.keepId, lastUpdated: now } }
    );
    remappedMenuSubCategoryRefs += menuResult.modifiedCount;

    const deleteResult = subCategoryCol.updateMany(
      { _id: { $in: g.duplicateIds }, isDeleted: { $ne: true } },
      { $set: { isDeleted: true, deletedAt: now } }
    );
    softDeletedSubCategories += deleteResult.modifiedCount;
  }
}

print(JSON.stringify({
  ok: true,
  mode: apply ? 'apply' : 'dry-run',
  scanned: {
    categories: categories.length,
    subCategories: subCategories.length
  },
  duplicates: {
    categoryGroups: categoryDupGroups.length,
    categoryRowsToDelete: categoryDupGroups.reduce((s, g) => s + g.duplicateIds.length, 0),
    subCategoryGroups: subDupGroups.length,
    subCategoryRowsToDelete: subDupGroups.reduce((s, g) => s + g.duplicateIds.length, 0)
  },
  applied: {
    remappedMenuCategoryRefs,
    remappedSubCategoryCategoryRefs,
    softDeletedCategories,
    normalizedKeptSubCategoryParent,
    remappedMenuSubCategoryRefs,
    softDeletedSubCategories
  }
}));
'@

$js = $js.Replace('__CS__', $escapedCs)
$js = $js.Replace('__DB__', $escapedDb)
$js = $js.Replace('__OUTLET__', $escapedOutlet)
$js = $js.Replace('__APPLY__', $Apply.IsPresent.ToString().ToLowerInvariant())

$tempJs = Join-Path $env:TEMP ("dedupe-categories-{0}.js" -f ([Guid]::NewGuid().ToString("N")))
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
    } else {
        Write-Ok "Apply complete."
    }

    Write-Host "Scanned categories:              $($result.scanned.categories)"
    Write-Host "Scanned subcategories:           $($result.scanned.subCategories)"
    Write-Host "Duplicate category groups:       $($result.duplicates.categoryGroups)"
    Write-Host "Duplicate category rows:         $($result.duplicates.categoryRowsToDelete)"
    Write-Host "Duplicate subcategory groups:    $($result.duplicates.subCategoryGroups)"
    Write-Host "Duplicate subcategory rows:      $($result.duplicates.subCategoryRowsToDelete)"

    if ($result.mode -eq 'apply') {
        Write-Host ""
        Write-Host "Applied changes:" -ForegroundColor Cyan
        Write-Host "  Menu category refs remapped:   $($result.applied.remappedMenuCategoryRefs)"
        Write-Host "  Subcat category refs remapped: $($result.applied.remappedSubCategoryCategoryRefs)"
        Write-Host "  Categories soft-deleted:       $($result.applied.softDeletedCategories)"
        Write-Host "  Kept subcat parent normalized: $($result.applied.normalizedKeptSubCategoryParent)"
        Write-Host "  Menu subcat refs remapped:     $($result.applied.remappedMenuSubCategoryRefs)"
        Write-Host "  Subcategories soft-deleted:    $($result.applied.softDeletedSubCategories)"
    } else {
        Write-Warn "No data was changed. Use -Apply to execute deduplication."
    }
}
finally {
    if (Test-Path $tempJs) {
        Remove-Item -Path $tempJs -Force -ErrorAction SilentlyContinue
    }
}
