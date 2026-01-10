using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using MongoDB.Bson;
using System.Net;
using System.Text.Json;

namespace Cafe.Api.Functions;

public class UpdateOutletIdsFunction
{
    private readonly ILogger<UpdateOutletIdsFunction> _logger;
    private readonly IMongoDatabase _database;

    public UpdateOutletIdsFunction(
        ILogger<UpdateOutletIdsFunction> logger,
        IMongoDatabase database)
    {
        _logger = logger;
        _database = database;
    }

    [Function("UpdateMissingOutletIds")]
    public async Task<HttpResponseData> UpdateMissingOutletIds(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
    {
        _logger.LogInformation("Starting update of missing OutletId fields");

        try
        {
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var request = JsonSerializer.Deserialize<UpdateOutletIdsRequest>(requestBody, new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true 
            });

            bool dryRun = request?.DryRun ?? true;
            string? defaultOutletId = request?.DefaultOutletId;

            _logger.LogInformation($"DryRun: {dryRun}, DefaultOutletId: {defaultOutletId}");

            // Get default outlet if not provided
            if (string.IsNullOrEmpty(defaultOutletId))
            {
                var outletsCollection = _database.GetCollection<BsonDocument>("Outlets");
                var defaultOutlet = await outletsCollection
                    .Find(Builders<BsonDocument>.Filter.Eq("outletCode", "MTC001"))
                    .FirstOrDefaultAsync();

                if (defaultOutlet == null)
                {
                    var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await errorResponse.WriteStringAsync("Default outlet MTC001 not found");
                    return errorResponse;
                }

                defaultOutletId = defaultOutlet["_id"].ToString();
                _logger.LogInformation($"Using default outlet: {defaultOutletId}");
            }

            var outletObjectId = ObjectId.Parse(defaultOutletId);
            var collections = new[] { "CafeMenu", "PriceForecasts", "Inventory", "CashReconciliations" };
            var results = new Dictionary<string, CollectionUpdateResult>();

            foreach (var collectionName in collections)
            {
                var result = await UpdateCollectionAsync(collectionName, outletObjectId, dryRun);
                results[collectionName] = result;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                dryRun,
                defaultOutletId,
                collections = results,
                totalRecords = results.Values.Sum(r => r.TotalRecords),
                totalUpdated = results.Values.Sum(r => r.UpdatedRecords),
                message = dryRun 
                    ? "DRY RUN - No changes were made" 
                    : $"Updated {results.Values.Sum(r => r.UpdatedRecords)} records across {results.Count} collections"
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating outlet IDs");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteStringAsync($"Error: {ex.Message}");
            return errorResponse;
        }
    }

    private async Task<CollectionUpdateResult> UpdateCollectionAsync(
        string collectionName, 
        ObjectId outletId, 
        bool dryRun)
    {
        try
        {
            var collection = _database.GetCollection<BsonDocument>(collectionName);

            // Filter for documents without OutletId or with null OutletId
            var filter = Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Exists("outletId", false),
                Builders<BsonDocument>.Filter.Eq("outletId", BsonNull.Value)
            );

            var totalRecords = await collection.CountDocumentsAsync(filter);

            if (totalRecords == 0)
            {
                _logger.LogInformation($"{collectionName}: No records need updating");
                return new CollectionUpdateResult
                {
                    CollectionName = collectionName,
                    TotalRecords = 0,
                    UpdatedRecords = 0,
                    Message = "All records already have OutletId"
                };
            }

            if (dryRun)
            {
                _logger.LogInformation($"{collectionName}: Would update {totalRecords} records (DRY RUN)");
                return new CollectionUpdateResult
                {
                    CollectionName = collectionName,
                    TotalRecords = totalRecords,
                    UpdatedRecords = 0,
                    Message = $"Would update {totalRecords} records (DRY RUN)"
                };
            }

            // Update documents
            var update = Builders<BsonDocument>.Update.Set("outletId", outletId);
            var result = await collection.UpdateManyAsync(filter, update);

            _logger.LogInformation($"{collectionName}: Updated {result.ModifiedCount} of {totalRecords} records");

            return new CollectionUpdateResult
            {
                CollectionName = collectionName,
                TotalRecords = totalRecords,
                UpdatedRecords = result.ModifiedCount,
                Message = $"Updated {result.ModifiedCount} records"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error updating collection {collectionName}");
            return new CollectionUpdateResult
            {
                CollectionName = collectionName,
                TotalRecords = 0,
                UpdatedRecords = 0,
                Message = $"Error: {ex.Message}"
            };
        }
    }

    [Function("VerifyOutletIds")]
    public async Task<HttpResponseData> VerifyOutletIds(
        [HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequestData req)
    {
        _logger.LogInformation("Verifying OutletId fields across collections");

        try
        {
            var collections = new[] { "Menu", "PriceForecasts", "Inventory", "DailyCashReconciliation" };
            var results = new Dictionary<string, CollectionStats>();

            foreach (var collectionName in collections)
            {
                var stats = await GetCollectionStatsAsync(collectionName);
                results[collectionName] = stats;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                collections = results,
                summary = new
                {
                    totalRecords = results.Values.Sum(s => s.TotalRecords),
                    withOutletId = results.Values.Sum(s => s.WithOutletId),
                    withoutOutletId = results.Values.Sum(s => s.WithoutOutletId)
                }
            });

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying outlet IDs");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteStringAsync($"Error: {ex.Message}");
            return errorResponse;
        }
    }

    private async Task<CollectionStats> GetCollectionStatsAsync(string collectionName)
    {
        try
        {
            var collection = _database.GetCollection<BsonDocument>(collectionName);

            var total = await collection.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);

            var withOutlet = await collection.CountDocumentsAsync(
                Builders<BsonDocument>.Filter.And(
                    Builders<BsonDocument>.Filter.Exists("outletId", true),
                    Builders<BsonDocument>.Filter.Ne("outletId", BsonNull.Value)
                )
            );

            var withoutOutlet = await collection.CountDocumentsAsync(
                Builders<BsonDocument>.Filter.Or(
                    Builders<BsonDocument>.Filter.Exists("outletId", false),
                    Builders<BsonDocument>.Filter.Eq("outletId", BsonNull.Value)
                )
            );

            return new CollectionStats
            {
                CollectionName = collectionName,
                TotalRecords = total,
                WithOutletId = withOutlet,
                WithoutOutletId = withoutOutlet
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error getting stats for collection {collectionName}");
            return new CollectionStats
            {
                CollectionName = collectionName,
                TotalRecords = 0,
                WithOutletId = 0,
                WithoutOutletId = 0,
                Error = ex.Message
            };
        }
    }
}

public class UpdateOutletIdsRequest
{
    public bool DryRun { get; set; } = true;
    public string? DefaultOutletId { get; set; }
}

public class CollectionUpdateResult
{
    public string CollectionName { get; set; } = string.Empty;
    public long TotalRecords { get; set; }
    public long UpdatedRecords { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class CollectionStats
{
    public string CollectionName { get; set; } = string.Empty;
    public long TotalRecords { get; set; }
    public long WithOutletId { get; set; }
    public long WithoutOutletId { get; set; }
    public string? Error { get; set; }
}
