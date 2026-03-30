using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Cafe.Api.Services;

namespace Cafe.Api.Functions;

public class WarmupFunction
{
    private readonly MongoService _mongo;
    private readonly AuthService _auth;
    private readonly ILogger<WarmupFunction> _logger;

    public WarmupFunction(MongoService mongo, AuthService auth, ILogger<WarmupFunction> logger)
    {
        _mongo = mongo;
        _auth = auth;
        _logger = logger;
    }

    [Function("Warmup")]
    public async Task Run([WarmupTrigger] object warmupContext)
    {
        _logger.LogInformation("Warmup trigger fired — pre-warming services");

        try
        {
            // Ping MongoDB to establish connection pool
            await _mongo.Database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1));
            _logger.LogInformation("MongoDB connection warmed up successfully");

            // Warm up AuthService JWT key cache
            _logger.LogInformation("AuthService warmed up");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Warmup: service pre-warming partially failed, will be established on first request");
        }
    }
}
