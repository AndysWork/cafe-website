# Backend API Audit Report

**Generated**: 2025-07-15  
**Scope**: `api/` — Functions, Services, Models, Helpers, Program.cs, api.csproj  
**Runtime**: Azure Functions V4, .NET 9.0, MongoDB 3.5.0

---

## Executive Summary

All 50 Function files, 21 Service files, 15 Helper files, and key Model files were reviewed. The codebase is well-structured overall but has **several cross-cutting consistency issues** and a handful of notable bugs. No `TODO`, `FIXME`, `HACK`, or `throw new NotImplementedException` statements were found. The `ReceiptImageUrl` field **IS** properly mapped in `OrderFunction.cs` line 777.

---

## CRITICAL Issues

### 1. Blocking `.Result` Call in RecipeFunction.cs

**File**: [RecipeFunction.cs](api/Functions/RecipeFunction.cs#L251)  
**Severity**: CRITICAL — Can cause thread starvation / deadlocks under load

```csharp
// Line 251 — CalculateRecipePrice method
var requestBody = new StreamReader(req.Body).ReadToEndAsync().Result;
```

The `CalculateRecipePrice` method is declared as `IActionResult` (synchronous return) but calls `.Result` on an async method. This blocks the thread pool thread and can deadlock. Should be `async Task<IActionResult>` with `await`.

---

### 2. `CalculateRecipePrice` Uses Synchronous Return Type

**File**: [RecipeFunction.cs](api/Functions/RecipeFunction.cs#L244)

```csharp
// Line 244
public IActionResult CalculateRecipePrice(
```

Every other function in the codebase uses `async Task<...>` return types. This one is synchronous, compounding the `.Result` deadlock risk above.

---

## HIGH Severity Issues

### 3. Wrong HTTP Pattern: IngredientFunction.cs & RecipeFunction.cs

**Files**:  
- [IngredientFunction.cs](api/Functions/IngredientFunction.cs) — 7 endpoints (lines 38-401)  
- [RecipeFunction.cs](api/Functions/RecipeFunction.cs) — 8 endpoints (lines 34-319)

Both use ASP.NET Core `HttpRequest` / `IActionResult` instead of Azure Functions Worker `HttpRequestData` / `HttpResponseData`:

```csharp
// IngredientFunction.cs / RecipeFunction.cs pattern:
public async Task<IActionResult> GetIngredients(
    [HttpTrigger(AuthorizationLevel.Function, "get", Route = "ingredients")] HttpRequest req)
{
    return new OkObjectResult(result);
}
```

**Every other function file** uses the Worker pattern:

```csharp
// Correct pattern (used by all other 48 function files):
public async Task<HttpResponseData> GetAllSales(
    [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "sales")] HttpRequestData req)
```

This is likely the legacy ASP.NET Core in-process model mixed into an isolated worker project. While it may compile and work due to compatibility shims, it prevents these functions from using the Worker middleware pipeline (SecurityHeaders, RateLimiting, RequestLogging, ApiVersion).

---

### 4. Inconsistent `AuthorizationLevel` Across Functions

Most endpoints use `AuthorizationLevel.Anonymous` with JWT validation via `AuthorizationHelper`. However, several function files use `AuthorizationLevel.Function` which **requires a function key** in addition to any JWT logic:

| File | Endpoints with `AuthorizationLevel.Function` |
|---|---|
| [IngredientFunction.cs](api/Functions/IngredientFunction.cs) | All 7 endpoints |
| [RecipeFunction.cs](api/Functions/RecipeFunction.cs) | All 8 endpoints |
| [FrozenItemFunction.cs](api/Functions/FrozenItemFunction.cs) | All 9 endpoints |
| [InventoryFunction.cs](api/Functions/InventoryFunction.cs) | All endpoints |
| [OverheadCostFunction.cs](api/Functions/OverheadCostFunction.cs) | All endpoints |
| [PlatformChargeFunction.cs](api/Functions/PlatformChargeFunction.cs) | All endpoints |
| [InitializeIngredientsFunction.cs](api/Functions/InitializeIngredientsFunction.cs) | 1 endpoint |
| [MigrationFunction.cs](api/Functions/MigrationFunction.cs) | 1 endpoint |
| [UpdateOutletIdsFunction.cs](api/Functions/UpdateOutletIdsFunction.cs) | 2 endpoints |

**Impact**: Frontend calling these routes needs to include a function key in the URL (`?code=xxx`), while all other routes work with just the JWT Bearer token. This creates a split authentication model.

---

### 5. No JWT/Auth Validation in Several `AuthorizationLevel.Function` Files

Because `IngredientFunction.cs`, `RecipeFunction.cs`, `FrozenItemFunction.cs`, `InventoryFunction.cs`, and `OverheadCostFunction.cs` rely solely on `AuthorizationLevel.Function` (function key), they do **not** call `AuthorizationHelper.ValidateAdminRole()` or `ValidateAuthenticatedUser()`. Anyone with a function key can access these endpoints regardless of user identity or role. This is an authorization gap if the function key is shared or leaked.

---

### 6. Information Disclosure — `ex.Message` Sent to Clients

Multiple functions expose raw exception messages to API consumers:

| File | Lines | Pattern |
|---|---|---|
| [FrozenItemFunction.cs](api/Functions/FrozenItemFunction.cs) | 51,79,115,158,207,243,352,377 | `WriteStringAsync($"Error: {ex.Message}")` |
| [OverheadCostFunction.cs](api/Functions/OverheadCostFunction.cs) | 48,69,97,181,228,274,309,330,369 | `WriteStringAsync($"Error: {ex.Message}")` |
| [InventoryFunction.cs](api/Functions/InventoryFunction.cs) | 65,92,126,147,168,195,248,290,318,367,414,463,490 | `WriteStringAsync($"Error: {ex.Message}")` |
| [SubCategoryFunction.cs](api/Functions/SubCategoryFunction.cs) | Multiple | `WriteAsJsonAsync(new { error = ex.Message })` |
| [OutletFunction.cs](api/Functions/OutletFunction.cs) | Multiple | `WriteAsJsonAsync(new { error = ex.Message })` |

By contrast, most other functions correctly return generic messages: `new { error = "Failed to get X" }`.

Internal exception details (stack traces, database errors, connection strings) should never be sent to clients.

---

## MEDIUM Severity Issues

### 7. Wrong Namespace — MigrationFunction.cs

**File**: [MigrationFunction.cs](api/Functions/MigrationFunction.cs#L9)

```csharp
namespace api.Functions  // ← WRONG
```

Every other file uses `Cafe.Api.Functions`. This won't break at runtime (Azure Functions use `[Function]` attributes for discovery), but it's a significant inconsistency.

---

### 8. Dead Code — PriceUpdateScheduler.cs

**File**: [PriceUpdateScheduler.cs](api/Functions/PriceUpdateScheduler.cs#L27-L28)

```csharp
// [Function("ScheduledPriceUpdate")]   ← COMMENTED OUT
// [TimerTrigger("0 0 6 * * *")]        ← COMMENTED OUT
public async Task Run(object timer)
```

The `[Function]` and `[TimerTrigger]` attributes are commented out. The `Run()` method and its supporting code exist but will never be invoked. This is dead code — either reactivate it or remove it.

---

### 9. Placeholder Implementations — MarketPriceService.cs

**File**: [MarketPriceService.cs](api/Services/MarketPriceService.cs)

Two methods are explicitly marked as placeholder/not-implemented:

- **Line ~118**: `FetchFromAgriMarketAsync()` — Returns unsuccessful result with `"AGMARKNET API integration pending"`
- **Line ~170**: `FetchFromWebScrapingAsync()` — Returns unsuccessful result with `"Web scraping not yet implemented"`

Both contain large blocks of commented-out example code showing intended implementations.

---

### 10. Placeholder Comment — FileUploadService.cs

**File**: [FileUploadService.cs](api/Services/FileUploadService.cs#L455)

```csharp
// Placeholder columns: SwiggyOrderId, OrderDate, CustomerName, Items, Total, Payout, etc.
```

The Swiggy Excel upload `ProcessSwiggyExcel` method has column mapping but the comment suggests it may still be approximate.

---

### 11. SalesItemTypeFunction.cs Uses Different Auth Error Handling

**File**: [SalesItemTypeFunction.cs](api/Functions/SalesItemTypeFunction.cs)

This file catches `UnauthorizedAccessException` to handle auth errors, while all other functions use the tuple-based `AuthorizationHelper.ValidateAdminRole()` pattern:

```csharp
// SalesItemTypeFunction pattern (different):
catch (UnauthorizedAccessException ex)
{
    var errorResponse = req.CreateResponse(HttpStatusCode.Unauthorized);
    await errorResponse.WriteAsJsonAsync(new { error = ex.Message });
    return errorResponse;
}
```

```csharp
// Standard pattern (all other files):
var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
if (!isAuthorized) return errorResponse!;
```

The `ValidateAdminRole()` method doesn't throw exceptions — it returns a tuple. The `SalesItemTypeFunction` catches exceptions that likely won't be thrown, meaning auth errors might not be handled correctly.

---

### 12. `UpdateOutletIdsFunction` Injects `IMongoDatabase` Directly

**File**: [UpdateOutletIdsFunction.cs](api/Functions/UpdateOutletIdsFunction.cs#L14)

```csharp
public class UpdateOutletIdsFunction
{
    private readonly IMongoDatabase _database;

    public UpdateOutletIdsFunction(ILogger<UpdateOutletIdsFunction> logger, IMongoDatabase database)
```

This function bypasses `MongoService` and injects `IMongoDatabase` directly. However, `IMongoDatabase` is **not registered in DI** in `Program.cs`. `MongoService` exposes a `Database` property but the interface itself isn't registered. This function either fails at runtime or requires a DI registration not present in the code.

---

## LOW Severity Issues

### 13. Inconsistent Error Response Format

The codebase has 3 distinct error response formats:

| Format | Used In |
|---|---|
| `WriteStringAsync($"Error: {ex.Message}")` | FrozenItem, Overhead, Inventory functions |
| `WriteAsJsonAsync(new { error = "message" })` | Most functions (Sales, Expenses, etc.) |
| `WriteAsJsonAsync(new { success = false, error = "msg" })` | Staff, UserManagement, UserAnalytics |

A consistent response envelope would simplify frontend error handling.

---

### 14. Inconsistent Logging Styles

| Pattern | Used In |
|---|---|
| `_log.LogError($"Error msg: {ex.Message}")` | Expense, Sales, Loyalty, Offer functions |
| `_log.LogError(ex, "Error msg")` | Staff, StaffPerformance, UserManagement |
| `_logger.LogError(ex, $"Error msg: {Id}", id)` | Overhead, PlatformCharge, Outlet |

The first pattern (`string interpolation without exception`) loses the stack trace in Application Insights. The structured logging pattern (`_log.LogError(ex, "msg {Param}", param)`) is preferred.

---

### 15. No Authentication on RecipeFunction Endpoints

**File**: [RecipeFunction.cs](api/Functions/RecipeFunction.cs)

All Recipe CRUD endpoints use `AuthorizationLevel.Function` but have **no JWT validation or role checks**. Any holder of a function key can create, update, or delete recipes regardless of their user role.

---

### 16. SwaggerFunction.cs is an Empty Class

**File**: [SwaggerFunction.cs](api/Functions/SwaggerFunction.cs)

```csharp
public class SwaggerDocumentation
{
    // No custom functions needed - all endpoints are auto-generated from OpenAPI attributes
}
```

This is a dead file. The OpenAPI extension works without it. Could be removed.

---

### 17. `WarmupFunction` Doesn't Inject MongoService

**File**: [WarmupFunction.cs](api/Functions/WarmupFunction.cs#L12)

```csharp
public WarmupFunction(IMongoDatabase database, ILogger<WarmupFunction> logger)
```

Same issue as #12 — injects `IMongoDatabase` directly. Additionally, the warmup only pings MongoDB but doesn't warm up other services like `AuthService`, `BlobStorageService`, or `EmailService`.

---

## Cross-Cutting Observations

### `ReceiptImageUrl` Mapping — CONFIRMED ✅

**File**: [OrderFunction.cs](api/Functions/OrderFunction.cs#L777)

```csharp
ReceiptImageUrl = order.ReceiptImageUrl
```

Present in `MapToOrderResponse()`. All fields are properly mapped.

---

### Function-to-Service Endpoint Cross-Reference

| Function File | Depends On |
|---|---|
| All standard functions | `MongoService`, `AuthService` |
| `FileUploadFunction` | `FileUploadService`, `MongoService` |
| `ImageUploadFunction` | `BlobStorageService`, `MongoService` |
| `StaffFunction` | `MongoService`, `AuthService`, `IEmailService`, `IWhatsAppService` |
| `PaymentFunction` | `MongoService`, `IRazorpayService` |
| `NotificationFunction` | `MongoService`, `NotificationService` |
| `PriceUpdateFunction/Scheduler` | `MongoService`, `MarketPriceService` |
| `OrderFunction` | `MongoService`, `AuthService`, `NotificationService` |
| `LoyaltyFunction` | `MongoService`, `AuthService`, `IEmailService` |
| `UpdateOutletIdsFunction` | `IMongoDatabase` ⚠️ (not registered in DI) |
| `WarmupFunction` | `IMongoDatabase` ⚠️ (not registered in DI) |

---

### NuGet Packages — All Used

All 17 NuGet packages in `api.csproj` map to code usage:

| Package | Used By |
|---|---|
| MongoDB.Driver 3.5.0 | MongoService |
| Azure.Storage.Blobs 12.22.2 | BlobStorageService |
| BCrypt.Net-Next 4.0.3 | AuthService |
| CsvHelper 33.0.1 | FileUploadService |
| EPPlus 7.5.2 | SalesFunction, MenuUploadFunction, FileUploadFunction |
| JWT 8.15.0 | AuthService |
| MailKit 4.9.0 | EmailService |
| SixLabors.ImageSharp 3.1.12 | ImageCompressor |
| Polly 9.0.6 | Program.cs (WhatsApp/Razorpay HTTP clients) |
| OpenApi 1.5.1 | OpenAPI attributes throughout |

---

## Summary Table

| # | File | Issue | Severity |
|---|---|---|---|
| 1 | RecipeFunction.cs:251 | `.Result` blocking call — deadlock risk | CRITICAL |
| 2 | RecipeFunction.cs:244 | Sync return type on async-capable endpoint | CRITICAL |
| 3 | IngredientFunction.cs, RecipeFunction.cs | Wrong HTTP pattern (`HttpRequest`/`IActionResult`) | HIGH |
| 4 | 9 function files | `AuthorizationLevel.Function` vs `Anonymous` split | HIGH |
| 5 | Ingredient, Recipe, FrozenItem, Inventory, Overhead | No JWT/role validation (relies only on function key) | HIGH |
| 6 | FrozenItem, Overhead, Inventory, SubCategory, Outlet | `ex.Message` leaked to clients | HIGH |
| 7 | MigrationFunction.cs:9 | Wrong namespace `api.Functions` | MEDIUM |
| 8 | PriceUpdateScheduler.cs:27-28 | Dead code — `[Function]` attribute commented out | MEDIUM |
| 9 | MarketPriceService.cs:118,170 | Placeholder implementations | MEDIUM |
| 10 | FileUploadService.cs:455 | Placeholder Swiggy column mapping | MEDIUM |
| 11 | SalesItemTypeFunction.cs | Non-standard auth error handling pattern | MEDIUM |
| 12 | UpdateOutletIdsFunction.cs | `IMongoDatabase` not registered in DI | MEDIUM |
| 13 | Multiple files | 3 different error response formats | LOW |
| 14 | Multiple files | Inconsistent logging (some lose stack traces) | LOW |
| 15 | RecipeFunction.cs | No role-based auth on CRUD endpoints | LOW |
| 16 | SwaggerFunction.cs | Empty dead file | LOW |
| 17 | WarmupFunction.cs | `IMongoDatabase` not in DI; incomplete warmup | LOW |

---

## Recommended Priority Order

1. **Fix the `.Result` deadlock** in RecipeFunction.cs (5 min)
2. **Normalize IngredientFunction + RecipeFunction** to Worker pattern (`HttpRequestData`/`HttpResponseData`) and add JWT auth
3. **Standardize `AuthorizationLevel.Anonymous`** across all endpoints and ensure proper JWT validation via `AuthorizationHelper`
4. **Stop leaking `ex.Message`** — replace all `$"Error: {ex.Message}"` responses with generic error messages
5. **Fix `IMongoDatabase` DI** for UpdateOutletIdsFunction and WarmupFunction (register `IMongoDatabase` from `MongoService.Database` or inject `MongoService` instead)
6. **Fix MigrationFunction namespace** to `Cafe.Api.Functions`
7. **Decide on PriceUpdateScheduler** — reactivate or delete
8. **Complete or remove** MarketPriceService placeholders
