using Cafe.Api.Models;

namespace Cafe.Api.Repositories;

public interface IMenuRepository
{
    // Menu Items
    Task<List<CafeMenuItem>> GetMenuAsync(string? outletId = null, int? page = null, int? pageSize = null);
    Task<long> GetMenuCountAsync(string? outletId = null);
    Task<List<CafeMenuItem>> GetMenuItemsByCategoryAsync(string categoryId, string? outletId = null);
    Task<List<CafeMenuItem>> GetMenuItemsBySubCategoryAsync(string subCategoryId, string? outletId = null);
    Task<CafeMenuItem?> GetMenuItemAsync(string id, string? outletId = null);
    Task<List<CafeMenuItem>> GetMenuItemsByIdsAsync(List<string> ids, string? outletId = null);
    Task<Dictionary<string, string>> GetCategoriesByIdsAsync(List<string> ids);
    Task<CafeMenuItem?> GetMenuItemsByNameAndOutletAsync(string menuItemName, string outletId);
    Task<CafeMenuItem> CreateMenuItemAsync(CafeMenuItem item);
    Task<bool> UpdateMenuItemAsync(string id, CafeMenuItem item);
    Task<bool> DeleteMenuItemAsync(string id);
    Task<int> BulkInsertMenuItemsAsync(List<CafeMenuItem> items);
    Task ClearMenuItemsAsync();
    Task ClearMenuItemsByOutletAsync(string outletId);
    Task<bool> ToggleMenuItemAvailabilityAsync(string id);
    Task<bool> UpdateMenuItemShopPriceAsync(string menuItemId, decimal shopPrice);

    // Categories
    Task<List<MenuCategory>> GetCategoriesAsync(string? outletId = null);
    Task<MenuCategory?> GetCategoryAsync(string id, string? outletId = null);
    Task<MenuCategory> CreateCategoryAsync(MenuCategory category);
    Task<bool> UpdateCategoryAsync(string id, MenuCategory category, string? outletId = null);
    Task<bool> DeleteCategoryAsync(string id, string? outletId = null);
    Task ClearCategoriesAsync();

    // SubCategories
    Task<List<MenuSubCategory>> GetSubCategoriesAsync(string? outletId = null);
    Task<List<MenuSubCategory>> GetSubCategoriesByCategoryAsync(string categoryId, string? outletId = null);
    Task<MenuSubCategory?> GetSubCategoryAsync(string id, string? outletId = null);
    Task<MenuSubCategory> CreateSubCategoryAsync(MenuSubCategory subCategory);
    Task<bool> UpdateSubCategoryAsync(string id, MenuSubCategory subCategory, string? outletId = null);
    Task<bool> DeleteSubCategoryAsync(string id, string? outletId = null);
    Task ClearSubCategoriesAsync();

    // Combo Meals
    Task<ComboMeal> CreateComboMealAsync(ComboMeal combo);
    Task<List<ComboMeal>> GetComboMealsAsync(string outletId, bool activeOnly = false);
    Task<ComboMeal?> GetComboMealByIdAsync(string id);
    Task<bool> UpdateComboMealAsync(string id, ComboMeal combo);
    Task<bool> DeleteComboMealAsync(string id);

    // Happy Hours
    Task<HappyHourRule> CreateHappyHourRuleAsync(HappyHourRule rule);
    Task<List<HappyHourRule>> GetHappyHourRulesAsync(string outletId);
    Task<HappyHourRule?> GetHappyHourRuleByIdAsync(string id);
    Task<List<HappyHourRule>> GetActiveHappyHoursAsync(string outletId);
    Task<bool> UpdateHappyHourRuleAsync(string id, HappyHourRule rule);
    Task<bool> DeleteHappyHourRuleAsync(string id);
}
