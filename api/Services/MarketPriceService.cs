using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Cafe.Api.Models;

namespace Cafe.Api.Services;

public class MarketPriceService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MarketPriceService> _logger;

    // Mapping of ingredient names to AGMARKNET commodity codes
    private static readonly Dictionary<string, string> AgriCommodityMapping = new()
    {
        // Vegetables
        { "onion", "onion" },
        { "tomato", "tomato" },
        { "potato", "potato" },
        { "capsicum", "capsicum" },
        { "carrot", "carrot" },
        { "cabbage", "cabbage" },
        { "cauliflower", "cauliflower" },
        { "brinjal", "brinjal" },
        { "eggplant", "brinjal" },
        { "ladyfinger", "bhindi" },
        { "okra", "bhindi" },
        
        // Grains & Pulses
        { "rice", "rice" },
        { "wheat", "wheat" },
        { "dal", "gram dal" },
        { "moong dal", "green gram dal" },
        { "toor dal", "arhar dal" },
        { "chana", "gram dal" },
        
        // Others
        { "ginger", "ginger" },
        { "garlic", "garlic" },
        { "green chilli", "green chilli" }
    };

    public MarketPriceService(IHttpClientFactory httpClientFactory, ILogger<MarketPriceService> logger)
    {
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _logger = logger;
    }

    public async Task<PriceFetchResult> FetchPriceAsync(string ingredientName, string category, string unit)
    {
        var result = new PriceFetchResult
        {
            IngredientName = ingredientName,
            Success = false
        };

        try
        {
            // Try AGMARKNET first for produce items
            if (category.ToLower() is "vegetables" or "grains" or "spices")
            {
                var agriPrice = await FetchFromAgriMarketAsync(ingredientName, unit);
                if (agriPrice.Success)
                {
                    return agriPrice;
                }
            }

            // Fallback: Try web scraping (BigBasket)
            var scrapedPrice = await FetchFromWebScrapingAsync(ingredientName, unit);
            if (scrapedPrice.Success)
            {
                return scrapedPrice;
            }

            result.ErrorMessage = "No price source available for this ingredient";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error fetching price for {ingredientName}");
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private Task<PriceFetchResult> FetchFromAgriMarketAsync(string ingredientName, string unit)
    {
        var result = new PriceFetchResult
        {
            IngredientName = ingredientName,
            Source = "agmarknet",
            Success = false
        };

        try
        {
            // Check if ingredient is mapped
            var normalizedName = ingredientName.ToLower().Trim();
            if (!AgriCommodityMapping.TryGetValue(normalizedName, out var commodityName))
            {
                // Try partial match
                var match = AgriCommodityMapping.FirstOrDefault(x => 
                    normalizedName.Contains(x.Key) || x.Key.Contains(normalizedName));
                if (match.Key != null)
                {
                    commodityName = match.Value;
                }
                else
                {
                    result.ErrorMessage = "Ingredient not found in AGMARKNET mapping";
                    return Task.FromResult(result);
                }
            }

            // Note: AGMARKNET API requires authentication and specific endpoints
            // This is a placeholder implementation - you'll need to:
            // 1. Register on AGMARKNET portal
            // 2. Get API credentials
            // 3. Implement actual API calls

            // For now, returning unsuccessful to try fallback methods
            result.ErrorMessage = "AGMARKNET API integration pending";
            return Task.FromResult(result);

            /* Example AGMARKNET API call structure:
            var apiUrl = $"https://api.data.gov.in/resource/9ef84268-d588-465a-a308-a864a43d0070";
            var queryParams = $"?api-key=YOUR_KEY&format=json&filters[commodity]={commodityName}&limit=1";
            
            var response = await _httpClient.GetAsync(apiUrl + queryParams);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<AgriMarketResponse>(content);
                
                if (data?.Records?.Any() == true)
                {
                    var record = data.Records.First();
                    result.Price = ConvertToUnit(record.ModalPrice, unit);
                    result.MarketName = record.Market;
                    result.Success = true;
                }
            }
            */
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"AGMARKNET fetch failed for {ingredientName}");
            result.ErrorMessage = ex.Message;
        }

        return Task.FromResult(result);
    }

    private Task<PriceFetchResult> FetchFromWebScrapingAsync(string ingredientName, string unit)
    {
        var result = new PriceFetchResult
        {
            IngredientName = ingredientName,
            Source = "scraped",
            Success = false
        };

        try
        {
            // Example: Scrape from BigBasket (requires actual implementation)
            // Note: Web scraping should respect robots.txt and terms of service
            
            // This is a placeholder - actual implementation would:
            // 1. Search for ingredient on e-commerce site
            // 2. Parse HTML to extract price
            // 3. Convert to required unit
            // 4. Handle rate limiting and errors

            result.ErrorMessage = "Web scraping not yet implemented";
            return Task.FromResult(result);

            /* Example scraping logic:
            var searchUrl = $"https://www.bigbasket.com/ps/?q={Uri.EscapeDataString(ingredientName)}";
            var response = await _httpClient.GetAsync(searchUrl);
            
            if (response.IsSuccessStatusCode)
            {
                var html = await response.Content.ReadAsStringAsync();
                var priceMatch = Regex.Match(html, @"Rs\s*(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
                
                if (priceMatch.Success && decimal.TryParse(priceMatch.Groups[1].Value, out var price))
                {
                    result.Price = ConvertToUnit(price, unit);
                    result.MarketName = "BigBasket";
                    result.Success = true;
                }
            }
            */
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Web scraping failed for {ingredientName}");
            result.ErrorMessage = ex.Message;
        }

        return Task.FromResult(result);
    }

    public async Task<List<PriceFetchResult>> FetchBulkPricesAsync(List<Ingredient> ingredients)
    {
        var results = new List<PriceFetchResult>();
        var tasks = new List<Task<PriceFetchResult>>();

        // Batch process with some delay to avoid overwhelming APIs
        foreach (var ingredient in ingredients)
        {
            if (!ingredient.AutoUpdateEnabled)
                continue;

            tasks.Add(Task.Run(async () =>
            {
                await Task.Delay(Random.Shared.Next(100, 500)); // Random delay
                return await FetchPriceAsync(ingredient.Name, ingredient.Category, ingredient.Unit);
            }));

            // Process in batches of 10
            if (tasks.Count >= 10)
            {
                var batchResults = await Task.WhenAll(tasks);
                results.AddRange(batchResults);
                tasks.Clear();
                await Task.Delay(2000); // Delay between batches
            }
        }

        // Process remaining tasks
        if (tasks.Any())
        {
            var batchResults = await Task.WhenAll(tasks);
            results.AddRange(batchResults);
        }

        return results;
    }

    private decimal ConvertToUnit(decimal price, string targetUnit)
    {
        // Conversion logic based on unit
        // This is simplified - adjust based on actual price units from sources
        return targetUnit.ToLower() switch
        {
            "kg" => price,
            "gm" => price / 1000,
            "ltr" => price,
            "ml" => price / 1000,
            "pc" => price,
            _ => price
        };
    }

    public decimal CalculatePriceChange(decimal currentPrice, decimal previousPrice)
    {
        if (previousPrice == 0) return 0;
        return ((currentPrice - previousPrice) / previousPrice) * 100;
    }
}

public class PriceFetchResult
{
    public string IngredientName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? MarketName { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
}
