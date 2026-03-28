using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cafe.Api.Services;

/// <summary>
/// Runs async initialization for MongoService at application startup
/// without blocking the thread pool (replaces .Wait() anti-pattern).
/// </summary>
public class MongoInitializationService : IHostedService
{
    private readonly MongoService _mongoService;
    private readonly ILogger<MongoInitializationService> _logger;

    public MongoInitializationService(MongoService mongoService, ILogger<MongoInitializationService> logger)
    {
        _mongoService = mongoService;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting MongoDB initialization...");
        await _mongoService.InitializeAsync();
        _logger.LogInformation("MongoDB initialization completed");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
