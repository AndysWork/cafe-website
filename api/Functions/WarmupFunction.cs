using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Cafe.Api.Functions;

public class WarmupFunction
{
    private readonly IMongoDatabase _database;
    private readonly ILogger<WarmupFunction> _logger;

    public WarmupFunction(IMongoDatabase database, ILogger<WarmupFunction> logger)
    {
        _database = database;
        _logger = logger;
    }

    [Function("Warmup")]
    public async Task Run([WarmupTrigger] object warmupContext)
    {
        _logger.LogInformation("Warmup trigger fired — pre-warming services");

        try
        {
            // Ping MongoDB to establish connection pool
            await _database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1));
            _logger.LogInformation("MongoDB connection warmed up successfully");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Warmup: MongoDB ping failed, connection will be established on first request");
        }
    }
}
