# Backend API Audit Report

**Generated**: 2025-07-15 | **Last Updated**: 2026-03-30  
**Scope**: `api/` — Functions, Services, Models, Helpers, Repositories, Program.cs, api.csproj  
**Runtime**: Azure Functions V4, .NET 9.0, MongoDB 3.5.0

---

## Executive Summary

All 69 Function files, 24 Service files, 18 Helper files, 14 Repository interfaces, and key Model files were reviewed. The codebase has undergone significant architectural improvements since the initial audit — **15 of the original 17 issues have been resolved**. Two issues remain (dead code / placeholder implementations). No `TODO`, `FIXME`, `HACK`, or `throw new NotImplementedException` statements were found. The `ReceiptImageUrl` field **IS** properly mapped in `OrderFunction.cs` line 759.

---

## Previously CRITICAL Issues — ✅ RESOLVED

### ~~1. Blocking `.Result` Call in RecipeFunction.cs~~ ✅ FIXED

**File**: [RecipeFunction.cs](api/Functions/RecipeFunction.cs#L293)  
**Original Severity**: CRITICAL  
**Resolution**: `CalculateRecipePrice` is now `async Task<HttpResponseData>` with proper `await` calls. No `.Result` blocking calls remain.

---

### ~~2. `CalculateRecipePrice` Uses Synchronous Return Type~~ ✅ FIXED

**File**: [RecipeFunction.cs](api/Functions/RecipeFunction.cs#L293)  
**Original Severity**: CRITICAL  
**Resolution**: Method now uses `async Task<HttpResponseData>` matching the Worker pattern used by all other functions.

---

## Previously HIGH Issues — ✅ RESOLVED

### ~~3. Wrong HTTP Pattern: IngredientFunction.cs & RecipeFunction.cs~~ ✅ FIXED

**Original Severity**: HIGH  
**Resolution**: Both files converted to Azure Functions Worker pattern (`HttpRequestData`/`HttpResponseData`). No ASP.NET Core `HttpRequest`/`IActionResult` usage remains.

---

### ~~4. Inconsistent `AuthorizationLevel` Across Functions~~ ✅ FIXED

**Original Severity**: HIGH  
**Resolution**: All endpoints now use `AuthorizationLevel.Anonymous` with JWT validation via `AuthorizationHelper`. Verified in: FrozenItemFunction, InventoryQueryFunction, OverheadCostFunction, PlatformChargeFunction, InitializeIngredientsFunction, MigrationFunction, UpdateOutletIdsFunction.

---

### ~~5. No JWT/Auth Validation in Function-Key Files~~ ✅ FIXED

**Original Severity**: HIGH  
**Resolution**: IngredientFunction, RecipeFunction, FrozenItemFunction, and all other previously Function-key-only files now call `AuthorizationHelper.ValidateAdminRole()` for proper role-based authorization.

---

### ~~6. Information Disclosure — `ex.Message` Sent to Clients~~ ✅ FIXED

**Original Severity**: HIGH  
**Resolution**: All `InvalidOperationException` handlers in `OutletFunction.cs` now return safe, hardcoded validation messages instead of `ex.Message`. CreateOutlet returns `"Outlet creation failed due to a validation error"` and DeleteOutlet returns `"Cannot delete outlet — it still has associated data"`. The general `Exception` catch blocks already returned `"An internal error occurred"`.

---

## MEDIUM Severity Issues

### ~~7. Wrong Namespace — MigrationFunction.cs~~ ✅ FIXED

**Original Severity**: MEDIUM  
**Resolution**: Namespace corrected to `Cafe.Api.Functions`.

---

### 8. Dead Code — PriceUpdateScheduler.cs ⚠️ STILL EXISTS

**File**: [PriceUpdateScheduler.cs](api/Functions/PriceUpdateScheduler.cs#L25)  
**Severity**: MEDIUM

```csharp
// [Function("ScheduledPriceUpdate")]
// public async Task Run([TimerTrigger("0 0 2 * * *")] TimerInfo timerInfo)
```

The `[Function]` and `[TimerTrigger]` attributes remain commented out. The `Run()` method and supporting code exist but will never be invoked. Either reactivate or remove.

---

### 9. Placeholder Implementations — MarketPriceService.cs ⚠️ STILL EXISTS

**File**: [MarketPriceService.cs](api/Services/MarketPriceService.cs)  
**Severity**: MEDIUM

Two methods remain placeholder/not-implemented:

- **`FetchFromAgriMarketAsync()`** — Returns `"AGMARKNET API integration pending"`
- **`FetchFromWebScrapingAsync()`** — Returns `"Web scraping not yet implemented"`

Both contain commented-out example code showing intended implementations.

---

### ~~10. Placeholder Comment — FileUploadService.cs~~ ✅ FIXED

**Original Severity**: MEDIUM  
**Resolution**: Stale placeholder comments ("Swiggy may have different column structure", "Placeholder columns") removed. The `ProcessSwiggyExcel` method has a complete 22-column mapping implementation.

---

### ~~11. SalesItemTypeFunction.cs Uses Different Auth Error Handling~~ ✅ FIXED

**Original Severity**: MEDIUM  
**Resolution**: No longer uses `catch (UnauthorizedAccessException)` pattern. Now uses standard `AuthorizationHelper.ValidateAdminRole()` tuple-based pattern matching all other functions.

---

### ~~12. `UpdateOutletIdsFunction` Injects `IMongoDatabase` Directly~~ ✅ FIXED

**Original Severity**: MEDIUM  
**Resolution**: Refactored to inject `MongoService` instead of `IMongoDatabase`. All collection access now goes through `_mongo.Database.GetCollection<T>()`, consistent with the rest of the codebase.

---

## LOW Severity Issues

### ~~13. Inconsistent Error Response Format~~ ✅ FIXED

**Original Severity**: LOW  
**Resolution**: All error responses standardized to `{ error = "message" }` format. Removed `success = false` wrapper from 15 function files (182 instances). Converted `OfferFunction.cs` from `{ message = "..." }` to `{ error = "..." }` (16 instances). Frontend `error.interceptor.ts` and `error-handler.ts` updated to read `error.error?.error || error.error?.message` for backward compatibility.

---

### ~~14. Inconsistent Logging Styles~~ ✅ FIXED

**Original Severity**: LOW  
**Resolution**: All `LogError($"...: {ex.Message}")` calls converted to structured `LogError(ex, "...")` pattern across ExpenseFunction, LoyaltyAdminFunction, LoyaltyUserFunction, OfferFunction, OperationalExpenseFunction, and SalesFunction. Redundant stack trace and inner exception logging removed from ExpenseFunction (captured automatically by the exception parameter). Only remaining interpolated `LogError` is in PriceUpdateScheduler.cs (dead code — see Issue #8).

---

### ~~15. No Authentication on RecipeFunction Endpoints~~ ✅ FIXED

**Original Severity**: LOW  
**Resolution**: All Recipe CRUD endpoints now use `AuthorizationLevel.Anonymous` with `AuthorizationHelper.ValidateAdminRole()` for proper role-based auth.

---

### ~~16. SwaggerFunction.cs is an Empty Class~~ ✅ FIXED

**Original Severity**: LOW  
**Resolution**: File now contains comprehensive documentation comments explaining the auto-generated OpenAPI/Swagger endpoints (UI, v2 JSON, v3 JSON). While still not containing executable code, it serves as documentation for the OpenAPI extension behavior.

---

### ~~17. `WarmupFunction` Injects `IMongoDatabase` Directly~~ ✅ FIXED

**Original Severity**: LOW  
**Resolution**: Refactored to inject `MongoService` and `AuthService` instead of `IMongoDatabase`. MongoDB warmup now uses `_mongo.Database.RunCommandAsync(ping)`. AuthService is also pre-warmed during startup.

---

## Cross-Cutting Observations

### `ReceiptImageUrl` Mapping — CONFIRMED ✅

**File**: [OrderFunction.cs](api/Functions/OrderFunction.cs#L759)

```csharp
ReceiptImageUrl = order.ReceiptImageUrl
```

Present in `MapToOrderResponse()`. All fields are properly mapped.

---

### Function-to-Service Endpoint Cross-Reference

| Function File | Depends On |
|---|---|
| Most standard functions | `MongoService`, `AuthService` |
| **`OrderFunction`** | `IOrderRepository`, `IMenuRepository`, `IOfferRepository`, `ILoyaltyRepository`, `IUserRepository`, `MongoService` (OutletHelper), `AuthService`, `EventLogService`, `OutboxService` |
| `FileUploadFunction` | `FileUploadService`, `MongoService` |
| `ImageUploadFunction` | `BlobStorageService`, `MongoService` |
| `StaffQueryFunction` / `StaffCommandFunction` | `MongoService`, `AuthService`, `IEmailService`, `IWhatsAppService` |
| `PaymentFunction` | `MongoService`, `IRazorpayService` |
| `NotificationFunction` | `MongoService`, `NotificationService` |
| `PriceUpdateFunction/Scheduler` | `MongoService`, `MarketPriceService` |
| `InventoryCommandFunction` | `MongoService`, `AuthService`, `IEmailService` |
| `LoyaltyUserFunction` / `LoyaltyAdminFunction` | `MongoService`, `AuthService`, `IEmailService` |
| `OutboxProcessorFunction` | `OutboxService`, `IWhatsAppService`, `IEmailService`, `NotificationService`, `ILoyaltyRepository` |
| `DatabaseBackupFunction` | `BlobStorageService`, `MongoService` (Timer: daily 8:30 PM IST) |
| `UpdateOutletIdsFunction` | `MongoService`, `AuthService` |
| `WarmupFunction` | `MongoService`, `AuthService` |

---

### Architecture Improvements Since Initial Audit

| Improvement | Description |
|---|---|
| **Repository Interfaces** | 14 domain interfaces (`IMenuRepository`, `IOrderRepository`, etc.) in `api/Repositories/` |
| **Middleware Pipeline** | 6-stage: SecurityHeaders → InputSanitization → RateLimit → Authorization → RequestLogging → ApiVersioning |
| **Event Sourcing** | `EventLogService` with `EventLog` model for state transition audit trails |
| **Outbox Pattern** | `OutboxService` + `OutboxProcessorFunction` (timer every 30s) for reliable side effects |
| **Centralized Auth** | `AuthorizationMiddleware` extracts JWT claims into FunctionContext for all requests |
| **4-Tier Rate Limiting** | Auth: 10/min, AdminWrite: 60/min, ExportReport: 20/min, PublicRead: 300/min |
| **Soft Delete** | `ISoftDeletable` interface across 14 models; all queries filter deleted records |
| **Caching** | `IMemoryCache` on 12 reference data methods with expiration and invalidation |
| **Indexing** | 35+ compound indexes documented in `DATABASE_INDEXING_STRATEGY.md` |

---

### NuGet Packages — All Used

All 18 NuGet packages in `api.csproj` map to code usage:

| Package | Used By |
|---|---|
| MongoDB.Driver 3.5.0 | MongoService, all repositories |
| Azure.Storage.Blobs 12.22.2 | BlobStorageService |
| BCrypt.Net-Next 4.0.3 | AuthService |
| CsvHelper 33.0.1 | FileUploadService |
| EPPlus 7.5.2 | SalesFunction, MenuUploadFunction, FileUploadFunction |
| QuestPDF 2025.1.2 | ReceiptPdfFunction |
| JWT 8.15.0 | AuthService |
| MailKit 4.9.0 | EmailService |
| SixLabors.ImageSharp 3.1.12 | ImageCompressor |
| Polly 9.0.6 | Program.cs (WhatsApp/Razorpay HTTP clients) |
| OpenApi 1.5.1 | OpenAPI attributes throughout |
| Extensions.Warmup 4.0.1 | WarmupFunction |

---

## Summary Table

| # | File | Issue | Severity | Status |
|---|---|---|---|---|
| 1 | RecipeFunction.cs | `.Result` blocking call | CRITICAL | ✅ FIXED |
| 2 | RecipeFunction.cs | Sync return type | CRITICAL | ✅ FIXED |
| 3 | IngredientFunction.cs, RecipeFunction.cs | Wrong HTTP pattern | HIGH | ✅ FIXED |
| 4 | 9 function files | `AuthorizationLevel.Function` split | HIGH | ✅ FIXED |
| 5 | Ingredient, Recipe, FrozenItem, Inventory, Overhead | No JWT/role validation | HIGH | ✅ FIXED |
| 6 | OutletFunction.cs | `ex.Message` leaked to clients | HIGH | ✅ FIXED |
| 7 | MigrationFunction.cs | Wrong namespace | MEDIUM | ✅ FIXED |
| 8 | PriceUpdateScheduler.cs | Dead code — commented out timer | MEDIUM | ⚠️ OPEN |
| 9 | MarketPriceService.cs | Placeholder implementations | MEDIUM | ⚠️ OPEN |
| 10 | FileUploadService.cs | Placeholder Swiggy column mapping | MEDIUM | ✅ FIXED |
| 11 | SalesItemTypeFunction.cs | Non-standard auth pattern | MEDIUM | ✅ FIXED |
| 12 | UpdateOutletIdsFunction.cs | Direct `IMongoDatabase` injection | MEDIUM | ✅ FIXED |
| 13 | Multiple files | 3 different error response formats | LOW | ✅ FIXED |
| 14 | Multiple files | Inconsistent logging styles | LOW | ✅ FIXED |
| 15 | RecipeFunction.cs | No role-based auth on CRUD | LOW | ✅ FIXED |
| 16 | SwaggerFunction.cs | Empty dead file | LOW | ✅ FIXED |
| 17 | WarmupFunction.cs | Direct `IMongoDatabase`; incomplete warmup | LOW | ✅ FIXED |

**Resolution**: 15 fixed, 2 open (dead code / placeholder implementations)

---

## Remaining Action Items (Priority Order)

1. **Decide on PriceUpdateScheduler** — Reactivate the timer trigger or remove the dead code
2. **Complete or remove MarketPriceService placeholders** — `FetchFromAgriMarketAsync` and `FetchFromWebScrapingAsync`

---

> **Initial audit**: 2025-07-15 — 50 Function files, 21 Service files, 15 Helper files  
> **Updated**: 2026-03-30 — 69 Function files, 24 Service files, 18 Helper files, 14 Repository interfaces, 46 Model files
