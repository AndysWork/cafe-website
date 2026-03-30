using Cafe.Api.Models;

namespace Cafe.Api.Repositories;

public interface IPricingRepository
{
    // Price Forecasts
    Task<List<PriceForecast>> GetPriceForecastsAsync(string? outletId = null);
    Task<List<PriceForecast>> GetPriceForecastsByMenuItemAsync(string menuItemId);
    Task<PriceForecast?> GetLatestActivePriceForecastByMenuItemAsync(string menuItemId);
    Task<PriceForecast?> GetPriceForecastAsync(string id);
    Task<PriceForecast> CreatePriceForecastAsync(PriceForecast forecast);
    Task<bool> UpdatePriceForecastAsync(string id, PriceForecast forecast);
    Task<bool> DeletePriceForecastAsync(string id);
    Task<bool> FinalizePriceForecastAsync(string forecastId, string userId);

    // Ingredients
    Task<List<Ingredient>> GetIngredientsAsync(string? outletId = null);
    Task<List<Ingredient>> GetAllIngredientsAsync(int? page = null, int? pageSize = null);
    Task<long> GetAllIngredientsCountAsync();
    Task<Ingredient?> GetIngredientByIdAsync(string id, string? outletId = null);
    Task<List<Ingredient>> GetIngredientsByCategoryAsync(string category, string? outletId = null);
    Task<List<Ingredient>> SearchIngredientsAsync(string searchTerm, string? outletId = null);
    Task<Ingredient> CreateIngredientAsync(Ingredient ingredient);
    Task<bool> UpdateIngredientAsync(string id, Ingredient ingredient, string? outletId = null);
    Task<bool> DeleteIngredientAsync(string id, string? outletId = null);

    // Recipes
    Task<List<MenuItemRecipe>> GetRecipesAsync(string? outletId = null);
    Task<MenuItemRecipe?> GetRecipeByIdAsync(string id);
    Task<MenuItemRecipe?> GetRecipeByMenuItemNameAsync(string menuItemName);
    Task<MenuItemRecipe?> GetRecipeByMenuItemNameAndOutletAsync(string menuItemName, string outletId);
    Task<MenuItemRecipe> CreateRecipeAsync(MenuItemRecipe recipe);
    Task<bool> UpdateRecipeAsync(string id, MenuItemRecipe recipe);
    Task<bool> DeleteRecipeAsync(string id);
    Task<MenuItemRecipe?> CopyRecipeFromOutletAsync(string menuItemName, string sourceOutletId, string targetOutletId);
    Task<PriceForecast?> CopyPriceForecastFromOutletAsync(string menuItemName, string sourceOutletId, string targetOutletId);
    Task<bool> UpdateMenuItemFuturePricesAsync(string menuItemId, decimal? futureShopPrice, decimal? futureOnlinePrice);

    // Price History
    Task<List<IngredientPriceHistory>> GetPriceHistoryAsync(string ingredientId, int days = 30);
    Task<List<IngredientPriceHistory>> GetAllPriceHistoryAsync();
    Task<IngredientPriceHistory?> GetLatestPriceHistoryAsync(string ingredientId);
    Task<IngredientPriceHistory> SavePriceHistoryAsync(IngredientPriceHistory history);
    Task<bool> UpdateIngredientPriceAsync(string ingredientId, decimal newPrice, string source, string? marketName = null);
    Task<Dictionary<string, decimal>> GetPriceTrendsAsync(string ingredientId, int days = 30);

    // Price Update Settings
    Task<PriceUpdateSettings?> GetPriceUpdateSettingsAsync();
    Task<PriceUpdateSettings> SavePriceUpdateSettingsAsync(PriceUpdateSettings settings);
    Task<List<Ingredient>> GetIngredientsForAutoUpdateAsync();

    // Overhead Costs
    Task<List<OverheadCost>> GetAllOverheadCostsAsync(string? outletId = null);
    Task<List<OverheadCost>> GetActiveOverheadCostsAsync(string? outletId = null);
    Task<OverheadCost?> GetOverheadCostByIdAsync(string id);
    Task<OverheadCost?> GetOverheadCostByTypeAsync(string costType);
    Task<OverheadCost> CreateOverheadCostAsync(OverheadCost overheadCost);
    Task<bool> UpdateOverheadCostAsync(string id, OverheadCost overheadCost);
    Task<bool> DeleteOverheadCostAsync(string id);
    Task<OverheadAllocation> CalculateOverheadAllocationAsync(int preparationTimeMinutes, string? outletId = null);
    Task InitializeDefaultOverheadCostsAsync();
    Task<int> MigrateRecipeOutletIdsAsync(string? defaultOutletId = null);
    Task<int> MigrateOverheadCostOutletIdsAsync(string targetOutletId);
}
