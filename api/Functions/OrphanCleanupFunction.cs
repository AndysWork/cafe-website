using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using System.Net;
using Cafe.Api.Models;

namespace Cafe.Api.Functions;

public class OrphanCleanupFunction
{
    private readonly IMongoDatabase _database;
    private readonly ILogger<OrphanCleanupFunction> _logger;

    public OrphanCleanupFunction(IMongoDatabase database, ILogger<OrphanCleanupFunction> logger)
    {
        _database = database;
        _logger = logger;
    }

    /// <summary>
    /// Permanently removes soft-deleted records older than 30 days.
    /// Runs daily at 3 AM UTC via timer trigger, or can be invoked manually via HTTP.
    /// </summary>
    [Function("OrphanCleanup")]
    public async Task<HttpResponseData> RunManual(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "admin/cleanup-orphans")] HttpRequestData req)
    {
        var result = await PerformCleanupAsync();
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(result);
        return response;
    }

    // Uncomment when deploying to Azure with timer trigger support
    // [Function("ScheduledOrphanCleanup")]
    // public async Task RunScheduled([TimerTrigger("0 0 3 * * *")] TimerInfo timerInfo)
    // {
    //     await PerformCleanupAsync();
    // }

    private async Task<object> PerformCleanupAsync()
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-30);
        _logger.LogInformation("Orphan cleanup started. Purging soft-deleted records older than {CutoffDate}", cutoffDate);

        var purged = new Dictionary<string, long>();
        long totalPurged = 0;

        totalPurged += await PurgeCollection<CafeMenuItem>("CafeMenu", cutoffDate, purged);
        totalPurged += await PurgeCollection<MenuCategory>("MenuCategory", cutoffDate, purged);
        totalPurged += await PurgeCollection<MenuSubCategory>("MenuSubCategory", cutoffDate, purged);
        totalPurged += await PurgeCollection<Ingredient>("Ingredients", cutoffDate, purged);
        totalPurged += await PurgeCollection<Order>("Orders", cutoffDate, purged);
        totalPurged += await PurgeCollection<Outlet>("Outlets", cutoffDate, purged);
        totalPurged += await PurgeCollection<Offer>("Offers", cutoffDate, purged);
        totalPurged += await PurgeCollection<Reward>("Rewards", cutoffDate, purged);
        totalPurged += await PurgeCollection<ComboMeal>("ComboMeals", cutoffDate, purged);
        totalPurged += await PurgeCollection<BonusConfiguration>("BonusConfigurations", cutoffDate, purged);
        totalPurged += await PurgeCollection<SubscriptionPlan>("SubscriptionPlans", cutoffDate, purged);
        totalPurged += await PurgeCollection<Sales>("Sales", cutoffDate, purged);
        totalPurged += await PurgeCollection<Expense>("Expenses", cutoffDate, purged);
        totalPurged += await PurgeCollection<DeliveryZone>("DeliveryZones", cutoffDate, purged);

        _logger.LogInformation("Orphan cleanup completed. Total purged: {TotalPurged}", totalPurged);

        return new
        {
            message = "Orphan cleanup completed",
            cutoffDate,
            totalPurged,
            details = purged
        };
    }

    private async Task<long> PurgeCollection<T>(string collectionName, DateTime cutoffDate, Dictionary<string, long> purged) where T : ISoftDeletable
    {
        try
        {
            var collection = _database.GetCollection<T>(collectionName);
            var filter = Builders<T>.Filter.Eq(x => x.IsDeleted, true) &
                         Builders<T>.Filter.Lt(x => x.DeletedAt, cutoffDate);

            var result = await collection.DeleteManyAsync(filter);
            if (result.DeletedCount > 0)
            {
                purged[collectionName] = result.DeletedCount;
                _logger.LogInformation("Purged {Count} soft-deleted records from {Collection}", result.DeletedCount, collectionName);
            }
            return result.DeletedCount;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to purge {Collection}", collectionName);
            return 0;
        }
    }
}
