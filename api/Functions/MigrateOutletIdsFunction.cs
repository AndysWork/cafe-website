using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Helpers;
using System.Text.Json;

namespace Cafe.Api.Functions;

public class MigrateOutletIdsFunction
{
    private readonly MongoService _mongoService;
    private readonly AuthService _authService;
    private readonly ILogger<MigrateOutletIdsFunction> _logger;

    public MigrateOutletIdsFunction(MongoService mongoService, AuthService authService, ILogger<MigrateOutletIdsFunction> logger)
    {
        _mongoService = mongoService;
        _authService = authService;
        _logger = logger;
    }

    /// <summary>
    /// Migrates existing records without OutletId to a specified outlet.
    /// POST /api/admin/migrate-outlet-ids
    /// Body: { "outletId": "outlet-id-here" }
    /// </summary>
    [Function("MigrateOutletIds")]
    public async Task<HttpResponseData> MigrateOutletIds(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "admin/migrate-outlet-ids")] HttpRequestData req)
    {
        try
        {
            // Validate admin authorization
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
            if (!isAuthorized)
            {
                _logger.LogWarning("Unauthorized migration attempt");
                return errorResponse!;
            }

            // Parse request body
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var migrationRequest = JsonSerializer.Deserialize<MigrationRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (migrationRequest == null || string.IsNullOrEmpty(migrationRequest.OutletId))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "outletId is required in request body" });
                return badRequest;
            }

            var outletId = migrationRequest.OutletId;
            _logger.LogInformation("Starting migration to outlet {OutletId}", outletId);

            // Verify outlet exists
            var outlet = await _mongoService.GetOutletByIdAsync(outletId);
            if (outlet == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = $"Outlet {outletId} not found" });
                return notFound;
            }

            var results = new MigrationResults
            {
                OutletId = outletId,
                OutletName = outlet.OutletName
            };

            // Migrate FrozenItems
            try
            {
                var frozenItemsUpdated = await _mongoService.MigrateFrozenItemsOutletIdAsync(outletId);
                results.FrozenItemsUpdated = frozenItemsUpdated;
                _logger.LogInformation("Migrated {Count} frozen items", frozenItemsUpdated);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error migrating frozen items");
                results.Errors.Add($"FrozenItems: {ex.Message}");
            }

            // Migrate Ingredients
            try
            {
                var ingredientsUpdated = await _mongoService.MigrateIngredientsOutletIdAsync(outletId);
                results.IngredientsUpdated = ingredientsUpdated;
                _logger.LogInformation("Migrated {Count} ingredients", ingredientsUpdated);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error migrating ingredients");
                results.Errors.Add($"Ingredients: {ex.Message}");
            }

            // Migrate Categories
            try
            {
                var categoriesUpdated = await _mongoService.MigrateCategoriesOutletIdAsync(outletId);
                results.CategoriesUpdated = categoriesUpdated;
                _logger.LogInformation("Migrated {Count} categories", categoriesUpdated);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error migrating categories");
                results.Errors.Add($"Categories: {ex.Message}");
            }

            // Migrate SubCategories
            try
            {
                var subCategoriesUpdated = await _mongoService.MigrateSubCategoriesOutletIdAsync(outletId);
                results.SubCategoriesUpdated = subCategoriesUpdated;
                _logger.LogInformation("Migrated {Count} subcategories", subCategoriesUpdated);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error migrating subcategories");
                results.Errors.Add($"SubCategories: {ex.Message}");
            }

            results.TotalRecordsUpdated = results.FrozenItemsUpdated + results.IngredientsUpdated + results.CategoriesUpdated + results.SubCategoriesUpdated;
            results.Success = results.Errors.Count == 0;
            results.Message = results.Success 
                ? $"Successfully migrated {results.TotalRecordsUpdated} records to outlet '{outlet.OutletName}'"
                : $"Migration completed with {results.Errors.Count} error(s)";

            var response = req.CreateResponse(results.Success ? HttpStatusCode.OK : HttpStatusCode.PartialContent);
            await response.WriteAsJsonAsync(results);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during migration");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = $"Migration failed: {ex.Message}" });
            return errorResponse;
        }
    }

    /// <summary>
    /// Gets migration status - counts records with and without outlet IDs
    /// GET /api/admin/migrate-outlet-ids/status
    /// </summary>
    [Function("GetMigrationStatus")]
    public async Task<HttpResponseData> GetMigrationStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "admin/migrate-outlet-ids/status")] HttpRequestData req)
    {
        try
        {
            // Validate admin authorization
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
            if (!isAuthorized)
            {
                return errorResponse!;
            }

            var status = await _mongoService.GetMigrationStatusAsync();

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(status);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting migration status");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = ex.Message });
            return errorResponse;
        }
    }

    private class MigrationRequest
    {
        public string OutletId { get; set; } = string.Empty;
    }

    private class MigrationResults
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string OutletId { get; set; } = string.Empty;
        public string OutletName { get; set; } = string.Empty;
        public int FrozenItemsUpdated { get; set; }
        public int IngredientsUpdated { get; set; }
        public int CategoriesUpdated { get; set; }
        public int SubCategoriesUpdated { get; set; }
        public int TotalRecordsUpdated { get; set; }
        public List<string> Errors { get; set; } = new();
    }
}
