using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;

namespace Cafe.Api.Functions;

public class PriceUpdateScheduler
{
    private readonly MongoService _mongoService;
    private readonly MarketPriceService _priceService;
    private readonly ILogger<PriceUpdateScheduler> _logger;

    public PriceUpdateScheduler(
        MongoService mongoService,
        MarketPriceService priceService,
        ILogger<PriceUpdateScheduler> logger)
    {
        _mongoService = mongoService;
        _priceService = priceService;
        _logger = logger;
    }

    // Runs daily at 2 AM (0 2 * * *)
    // Note: Uncomment and configure when deploying to Azure
    // [Function("ScheduledPriceUpdate")]
    // public async Task Run([TimerTrigger("0 2 * * *")] TimerInfo timerInfo)
    public async Task Run()
    {
        _logger.LogInformation($"Price update scheduler triggered at: {DateTime.UtcNow}");

        try
        {
            // Check if auto-update is enabled
            var settings = await _mongoService.GetPriceUpdateSettingsAsync();
            if (settings == null || !settings.AutoUpdateEnabled)
            {
                _logger.LogInformation("Auto-update is disabled. Skipping price refresh.");
                return;
            }

            // Check if enough time has passed since last update
            if (settings.LastUpdateRun.HasValue)
            {
                var hoursSinceLastUpdate = (DateTime.UtcNow - settings.LastUpdateRun.Value).TotalHours;
                if (hoursSinceLastUpdate < settings.UpdateFrequencyHours)
                {
                    _logger.LogInformation($"Only {hoursSinceLastUpdate:F2} hours since last update. Skipping.");
                    return;
                }
            }

            // Get ingredients configured for auto-update
            var ingredients = await _mongoService.GetIngredientsForAutoUpdateAsync();
            if (ingredients.Count == 0)
            {
                _logger.LogInformation("No ingredients configured for auto-update.");
                return;
            }

            _logger.LogInformation($"Starting automatic price update for {ingredients.Count} ingredients");

            // Fetch prices in bulk
            var results = await _priceService.FetchBulkPricesAsync(ingredients);
            var updated = 0;
            var failed = 0;

            foreach (var result in results)
            {
                if (result.Success)
                {
                    var ingredient = ingredients.FirstOrDefault(i => i.Name == result.IngredientName);
                    if (ingredient != null)
                    {
                        // Calculate change percentage
                        var changePercentage = ingredient.MarketPrice > 0
                            ? Math.Abs(((result.Price - ingredient.MarketPrice) / ingredient.MarketPrice) * 100)
                            : 0;

                        // Only update if change is significant
                        if (changePercentage >= settings.MinChangePercentageToRecord)
                        {
                            var success = await _mongoService.UpdateIngredientPriceAsync(
                                ingredient.Id!,
                                result.Price,
                                result.Source,
                                result.MarketName
                            );

                            if (success)
                            {
                                updated++;
                                _logger.LogInformation($"Updated {ingredient.Name}: ₹{ingredient.MarketPrice} → ₹{result.Price} ({changePercentage:F2}%)");

                                // Log alert if change exceeds threshold
                                if (changePercentage >= settings.AlertThresholdPercentage)
                                {
                                    _logger.LogWarning($"ALERT: Significant price change for {ingredient.Name}: {changePercentage:F2}%");
                                }
                            }
                            else
                            {
                                failed++;
                                _logger.LogError($"Failed to update price for {ingredient.Name}");
                            }
                        }
                        else
                        {
                            _logger.LogInformation($"Skipped {ingredient.Name}: change ({changePercentage:F2}%) below threshold ({settings.MinChangePercentageToRecord}%)");
                        }
                    }
                }
                else
                {
                    failed++;
                    _logger.LogWarning($"Failed to fetch price for {result.IngredientName}: {result.ErrorMessage}");
                }
            }

            // Update last run time
            settings.LastUpdateRun = DateTime.UtcNow;
            await _mongoService.SavePriceUpdateSettingsAsync(settings);

            _logger.LogInformation($"Automatic price update completed: {updated} updated, {failed} failed, {ingredients.Count} total");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during scheduled price update");
        }
    }
}
