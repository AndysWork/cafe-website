using MongoDB.Driver;
using Cafe.Api.Models;
using Microsoft.Extensions.Configuration;

namespace Cafe.Api.Services;

public class MongoService
{
    private readonly IMongoCollection<CafeMenuItem> _menu;
    private readonly IMongoCollection<MenuCategory> _categories;
    private readonly IMongoCollection<MenuSubCategory> _subCategories;
    
    public MongoService(IConfiguration config)
    {
        // Azure Functions loads from local.settings.json Values section as environment variables
        var cs = Environment.GetEnvironmentVariable("Mongo__ConnectionString");
        var dbName = Environment.GetEnvironmentVariable("Mongo__Database");
        
        if (string.IsNullOrEmpty(cs))
        {
            throw new InvalidOperationException("MongoDB connection string not found in environment variables. Please check local.settings.json");
        }
        
        if (string.IsNullOrEmpty(dbName))
        {
            throw new InvalidOperationException("MongoDB database name not found in environment variables. Please check local.settings.json");
        }
        
        var client = new MongoClient(cs);
        var db = client.GetDatabase(dbName);
        _menu = db.GetCollection<CafeMenuItem>("CafeMenu");
        _categories = db.GetCollection<MenuCategory>("MenuCategory");
        _subCategories = db.GetCollection<MenuSubCategory>("MenuSubCategory");
    }

    #region CafeMenuItem Operations
    
    // Get all menu items
    public async Task<List<CafeMenuItem>> GetMenuAsync() =>
        await _menu.Find(_ => true).ToListAsync();

    // Get menu items by CategoryId
    public async Task<List<CafeMenuItem>> GetMenuItemsByCategoryAsync(string categoryId) =>
        await _menu.Find(item => item.CategoryId == categoryId).ToListAsync();

    // Get menu items by SubCategoryId
    public async Task<List<CafeMenuItem>> GetMenuItemsBySubCategoryAsync(string subCategoryId) =>
        await _menu.Find(item => item.SubCategoryId == subCategoryId).ToListAsync();

    // Get single menu item by ID
    public async Task<CafeMenuItem?> GetMenuItemAsync(string id) =>
        await _menu.Find(x => x.Id == id).FirstOrDefaultAsync();

    // Create new menu item
    public async Task<CafeMenuItem> CreateMenuItemAsync(CafeMenuItem item)
    {
        item.CreatedDate = DateTime.UtcNow;
        item.LastUpdated = DateTime.UtcNow;
        await _menu.InsertOneAsync(item);
        return item;
    }

    // Update existing menu item
    public async Task<bool> UpdateMenuItemAsync(string id, CafeMenuItem item)
    {
        item.LastUpdated = DateTime.UtcNow;
        var result = await _menu.ReplaceOneAsync(x => x.Id == id, item);
        return result.ModifiedCount > 0;
    }

    // Delete menu item
    public async Task<bool> DeleteMenuItemAsync(string id)
    {
        var result = await _menu.DeleteOneAsync(x => x.Id == id);
        return result.DeletedCount > 0;
    }
    
    #endregion

    #region MenuCategory Operations
    
    // Get all categories
    public async Task<List<MenuCategory>> GetCategoriesAsync() =>
        await _categories.Find(_ => true).ToListAsync();

    // Get single category by ID
    public async Task<MenuCategory?> GetCategoryAsync(string id) =>
        await _categories.Find(x => x.Id == id).FirstOrDefaultAsync();

    // Create new category
    public async Task<MenuCategory> CreateCategoryAsync(MenuCategory category)
    {
        await _categories.InsertOneAsync(category);
        return category;
    }

    // Update existing category
    public async Task<bool> UpdateCategoryAsync(string id, MenuCategory category)
    {
        var result = await _categories.ReplaceOneAsync(x => x.Id == id, category);
        return result.ModifiedCount > 0;
    }

    // Delete category
    public async Task<bool> DeleteCategoryAsync(string id)
    {
        var result = await _categories.DeleteOneAsync(x => x.Id == id);
        return result.DeletedCount > 0;
    }
    
    #endregion

    #region MenuSubCategory Operations
    
    // Get all subcategories
    public async Task<List<MenuSubCategory>> GetSubCategoriesAsync() =>
        await _subCategories.Find(_ => true).ToListAsync();

    // Get subcategories by category ID
    public async Task<List<MenuSubCategory>> GetSubCategoriesByCategoryAsync(string categoryId) =>
        await _subCategories.Find(x => x.CategoryId == categoryId).ToListAsync();

    // Get single subcategory by ID
    public async Task<MenuSubCategory?> GetSubCategoryAsync(string id) =>
        await _subCategories.Find(x => x.Id == id).FirstOrDefaultAsync();

    // Create new subcategory
    public async Task<MenuSubCategory> CreateSubCategoryAsync(MenuSubCategory subCategory)
    {
        await _subCategories.InsertOneAsync(subCategory);
        return subCategory;
    }

    // Update existing subcategory
    public async Task<bool> UpdateSubCategoryAsync(string id, MenuSubCategory subCategory)
    {
        var result = await _subCategories.ReplaceOneAsync(x => x.Id == id, subCategory);
        return result.ModifiedCount > 0;
    }

    // Delete subcategory
    public async Task<bool> DeleteSubCategoryAsync(string id)
    {
        var result = await _subCategories.DeleteOneAsync(x => x.Id == id);
        return result.DeletedCount > 0;
    }
    
    // Clear all categories (for schema migration)
    public async Task ClearCategoriesAsync()
    {
        await _categories.DeleteManyAsync(_ => true);
    }
    
    // Clear all subcategories (for schema migration)
    public async Task ClearSubCategoriesAsync()
    {
        await _subCategories.DeleteManyAsync(_ => true);
    }
    
    #endregion
}
