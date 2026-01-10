using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Net;
using System.Text.Json;

namespace api.Functions
{
    public class MigrationFunction
    {
        private readonly ILogger _logger;
        private readonly IMongoDatabase _database;

        public MigrationFunction(ILoggerFactory loggerFactory, IMongoDatabase database)
        {
            _logger = loggerFactory.CreateLogger<MigrationFunction>();
            _database = database;
        }

        [Function("MigrateToMultiOutlet")]
        public async Task<HttpResponseData> MigrateToMultiOutlet(
            [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
        {
            _logger.LogInformation("Starting multi-outlet migration");

            try
            {
                // Parse request body
                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var options = JsonSerializer.Deserialize<MigrationOptions>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                var dryRun = options?.DryRun ?? true;
                var defaultOutletId = options?.DefaultOutletId ?? "MTC001";

                // Get the default outlet
                var outletsCollection = _database.GetCollection<BsonDocument>("Outlets");
                var outlet = await outletsCollection.Find(new BsonDocument("outletCode", defaultOutletId)).FirstOrDefaultAsync();

                if (outlet == null)
                {
                    var response = req.CreateResponse(HttpStatusCode.BadRequest);
                    await response.WriteAsJsonAsync(new { error = $"Default outlet '{defaultOutletId}' not found" });
                    return response;
                }

                var outletObjectId = outlet["_id"].AsObjectId.ToString();
                _logger.LogInformation($"Using default outlet: {defaultOutletId} (ID: {outletObjectId})");

                var results = new Dictionary<string, object>();

                // Collections to migrate
                var collections = new[]
                {
                    "Sales", "OnlineSales", "Expenses", "OperationalExpenses",
                    "OverheadCosts", "DailyCashReconciliation", "Inventory", "PriceForecasts", "PriceHistory"
                };

                foreach (var collectionName in collections)
                {
                    var collection = _database.GetCollection<BsonDocument>(collectionName);
                    
                    // Count documents without outletId
                    var filter = Builders<BsonDocument>.Filter.Exists("outletId", false);
                    var count = await collection.CountDocumentsAsync(filter);

                    if (count > 0)
                    {
                        _logger.LogInformation($"{collectionName}: {count} documents need migration");

                        if (!dryRun)
                        {
                            // Update documents to add outletId
                            var update = Builders<BsonDocument>.Update.Set("outletId", new ObjectId(outletObjectId));
                            var result = await collection.UpdateManyAsync(filter, update);
                            results[collectionName] = new { updated = result.ModifiedCount };
                        }
                        else
                        {
                            results[collectionName] = new { toUpdate = count };
                        }
                    }
                    else
                    {
                        results[collectionName] = new { message = "All documents already have outletId" };
                    }
                }

                // Handle Users collection separately
                var usersCollection = _database.GetCollection<BsonDocument>("Users");
                var usersFilter = Builders<BsonDocument>.Filter.Exists("defaultOutletId", false);
                var usersCount = await usersCollection.CountDocumentsAsync(usersFilter);

                if (usersCount > 0)
                {
                    _logger.LogInformation($"Users: {usersCount} documents need migration");

                    if (!dryRun)
                    {
                        var usersUpdate = Builders<BsonDocument>.Update
                            .Set("defaultOutletId", new ObjectId(outletObjectId))
                            .Set("assignedOutlets", new BsonArray { new ObjectId(outletObjectId) });
                        var result = await usersCollection.UpdateManyAsync(usersFilter, usersUpdate);
                        results["Users"] = new { updated = result.ModifiedCount };
                    }
                    else
                    {
                        results["Users"] = new { toUpdate = usersCount };
                    }
                }
                else
                {
                    results["Users"] = new { message = "All users already have outlet assignments" };
                }

                var successResponse = req.CreateResponse(HttpStatusCode.OK);
                await successResponse.WriteAsJsonAsync(new
                {
                    success = true,
                    dryRun,
                    defaultOutlet = new { code = defaultOutletId, id = outletObjectId },
                    results
                });

                return successResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Migration failed");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new { error = ex.Message });
                return errorResponse;
            }
        }

        private class MigrationOptions
        {
            public bool DryRun { get; set; } = true;
            public string DefaultOutletId { get; set; } = "MTC001";
        }
    }
}
