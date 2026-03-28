using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Driver;

namespace Cafe.Api.Functions;

public class DatabaseBackupFunction
{
    private readonly IMongoDatabase _database;
    private readonly BlobServiceClient _blobServiceClient;
    private readonly ILogger<DatabaseBackupFunction> _log;
    private readonly Services.AuthService _auth;

    private const string BackupContainer = "database-backups";
    private const int RetentionDays = 30;

    private static readonly string[] CollectionsToBackup = new[]
    {
        "CafeMenu", "MenuCategory", "MenuSubCategory", "Users", "Orders",
        "LoyaltyAccounts", "Rewards", "PointsTransactions", "Offers",
        "Sales", "Expenses", "SalesItemTypes", "OfflineExpenseTypes",
        "OnlineExpenseTypes", "CashReconciliations", "OnlineSales",
        "OperationalExpenses", "PlatformCharges", "PriceForecasts",
        "DiscountCoupons", "Ingredients", "Recipes", "IngredientPriceHistory",
        "PriceUpdateSettings", "Inventory", "InventoryTransactions",
        "StockAlerts", "OverheadCosts", "FrozenItems", "Outlets",
        "Staff", "BonusConfigurations", "StaffPerformanceRecords",
        "DailyPerformanceEntries"
    };

    public DatabaseBackupFunction(
        IMongoDatabase database,
        BlobServiceClient blobServiceClient,
        Services.AuthService auth,
        ILoggerFactory loggerFactory)
    {
        _database = database;
        _blobServiceClient = blobServiceClient;
        _auth = auth;
        _log = loggerFactory.CreateLogger<DatabaseBackupFunction>();
    }

    /// <summary>
    /// Automated daily backup at 2:00 AM IST (UTC 20:30 previous day)
    /// </summary>
    [Function("ScheduledDatabaseBackup")]
    public async Task RunScheduledBackup(
        [TimerTrigger("0 30 20 * * *")] TimerInfo timer)
    {
        _log.LogInformation("Scheduled database backup started at {Time}", DateTime.UtcNow);

        try
        {
            var result = await PerformBackupAsync("scheduled");
            _log.LogInformation("Scheduled backup completed: {Collections} collections, {Size} bytes total",
                result.CollectionCount, result.TotalBytes);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Scheduled database backup failed");
            throw;
        }
    }

    /// <summary>
    /// Manual backup trigger (admin only)
    /// </summary>
    [Function("ManualDatabaseBackup")]
    public async Task<HttpResponseData> RunManualBackup(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "admin/backup")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await Helpers.AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            _log.LogInformation("Manual database backup triggered");
            var result = await PerformBackupAsync("manual");

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                success = true,
                message = "Database backup completed successfully",
                data = new
                {
                    timestamp = result.Timestamp,
                    collectionsBackedUp = result.CollectionCount,
                    totalSizeBytes = result.TotalBytes,
                    blobPrefix = result.BlobPrefix,
                    retentionDays = RetentionDays
                }
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Manual database backup failed");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { success = false, error = "Backup failed: " + ex.Message });
            return error;
        }
    }

    /// <summary>
    /// List available backups (admin only)
    /// </summary>
    [Function("ListDatabaseBackups")]
    public async Task<HttpResponseData> ListBackups(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "admin/backups")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await Helpers.AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var container = _blobServiceClient.GetBlobContainerClient(BackupContainer);
            if (!await container.ExistsAsync())
            {
                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new { success = true, data = Array.Empty<object>() });
                return response;
            }

            var backups = new List<object>();
            var seenPrefixes = new HashSet<string>();

            await foreach (var blob in container.GetBlobsAsync())
            {
                // Extract backup folder prefix (e.g., "2026-03-28T020000Z-scheduled")
                var parts = blob.Name.Split('/');
                if (parts.Length < 2) continue;
                var prefix = parts[0];
                if (!seenPrefixes.Add(prefix)) continue;

                backups.Add(new
                {
                    name = prefix,
                    createdOn = blob.Properties.CreatedOn,
                    sizeBytes = blob.Properties.ContentLength
                });
            }

            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteAsJsonAsync(new { success = true, data = backups.OrderByDescending(b => ((dynamic)b).name) });
            return resp;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to list backups");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { success = false, error = ex.Message });
            return error;
        }
    }

    private async Task<BackupResult> PerformBackupAsync(string trigger)
    {
        var container = _blobServiceClient.GetBlobContainerClient(BackupContainer);
        await container.CreateIfNotExistsAsync();

        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHHmmssZ");
        var prefix = $"{timestamp}-{trigger}";
        long totalBytes = 0;
        int collectionCount = 0;

        foreach (var collectionName in CollectionsToBackup)
        {
            try
            {
                var collection = _database.GetCollection<BsonDocument>(collectionName);
                var documents = await collection.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();

                if (documents.Count == 0)
                {
                    _log.LogDebug("Skipping empty collection: {Collection}", collectionName);
                    continue;
                }

                // Serialize to JSON array
                var jsonWriterSettings = new JsonWriterSettings { OutputMode = JsonOutputMode.RelaxedExtendedJson };
                var jsonArray = new StringBuilder();
                jsonArray.Append('[');
                for (int i = 0; i < documents.Count; i++)
                {
                    if (i > 0) jsonArray.Append(',');
                    jsonArray.Append(documents[i].ToJson(jsonWriterSettings));
                }
                jsonArray.Append(']');

                var jsonBytes = Encoding.UTF8.GetBytes(jsonArray.ToString());
                var blobName = $"{prefix}/{collectionName}.json";
                var blobClient = container.GetBlobClient(blobName);

                using var stream = new MemoryStream(jsonBytes);
                await blobClient.UploadAsync(stream, new BlobHttpHeaders
                {
                    ContentType = "application/json"
                });

                totalBytes += jsonBytes.Length;
                collectionCount++;
                _log.LogDebug("Backed up {Collection}: {Count} documents, {Size} bytes",
                    collectionName, documents.Count, jsonBytes.Length);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to backup collection {Collection}", collectionName);
            }
        }

        // Upload metadata file
        var metadata = new
        {
            timestamp = DateTime.UtcNow,
            trigger,
            database = _database.DatabaseNamespace.DatabaseName,
            collectionsBackedUp = collectionCount,
            totalSizeBytes = totalBytes
        };
        var metaJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        var metaBlob = container.GetBlobClient($"{prefix}/_metadata.json");
        using var metaStream = new MemoryStream(Encoding.UTF8.GetBytes(metaJson));
        await metaBlob.UploadAsync(metaStream, new BlobHttpHeaders { ContentType = "application/json" });

        // Cleanup old backups
        await CleanupOldBackupsAsync(container);

        return new BackupResult
        {
            Timestamp = timestamp,
            CollectionCount = collectionCount,
            TotalBytes = totalBytes,
            BlobPrefix = prefix
        };
    }

    private async Task CleanupOldBackupsAsync(BlobContainerClient container)
    {
        try
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-RetentionDays);
            var blobsToDelete = new List<string>();

            await foreach (var blob in container.GetBlobsAsync())
            {
                if (blob.Properties.CreatedOn < cutoff)
                {
                    blobsToDelete.Add(blob.Name);
                }
            }

            foreach (var blobName in blobsToDelete)
            {
                await container.DeleteBlobIfExistsAsync(blobName);
            }

            if (blobsToDelete.Count > 0)
            {
                _log.LogInformation("Cleaned up {Count} old backup blobs (older than {Days} days)",
                    blobsToDelete.Count, RetentionDays);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to cleanup old backups");
        }
    }

    private class BackupResult
    {
        public string Timestamp { get; set; } = "";
        public int CollectionCount { get; set; }
        public long TotalBytes { get; set; }
        public string BlobPrefix { get; set; } = "";
    }
}
