using MongoDB.Driver;
using Cafe.Api.Models;
using Microsoft.Extensions.Configuration;

namespace Cafe.Api.Services;

public class MongoService
{
    private readonly IMongoCollection<CafeMenuItem> _menu;
    private readonly IMongoCollection<MenuCategory> _categories;
    private readonly IMongoCollection<MenuSubCategory> _subCategories;
    private readonly IMongoCollection<User> _users;
    private readonly IMongoCollection<Order> _orders;
    
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
        _users = db.GetCollection<User>("Users");
        _orders = db.GetCollection<Order>("Orders");

        // Ensure default admin user exists
        try
        {
            EnsureDefaultAdminAsync().Wait();
            Console.WriteLine("✓ Default admin user check completed");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error ensuring default admin: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
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

    // Bulk insert menu items (for Excel upload)
    public async Task<int> BulkInsertMenuItemsAsync(List<CafeMenuItem> items)
    {
        if (items == null || items.Count == 0)
            return 0;

        await _menu.InsertManyAsync(items);
        return items.Count;
    }
    
    // Clear all menu items (useful before bulk upload)
    public async Task ClearMenuItemsAsync()
    {
        await _menu.DeleteManyAsync(_ => true);
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

    #region User Operations

    // Get user by username
    public async Task<User?> GetUserByUsernameAsync(string username) =>
        await _users.Find(x => x.Username == username).FirstOrDefaultAsync();

    // Get user by email
    public async Task<User?> GetUserByEmailAsync(string email) =>
        await _users.Find(x => x.Email == email).FirstOrDefaultAsync();

    // Get user by ID
    public async Task<User?> GetUserByIdAsync(string id) =>
        await _users.Find(x => x.Id == id).FirstOrDefaultAsync();

    // Create new user
    public async Task<User> CreateUserAsync(User user)
    {
        await _users.InsertOneAsync(user);
        return user;
    }

    // Update user last login time
    public async Task UpdateUserLastLoginAsync(string userId)
    {
        var update = Builders<User>.Update.Set(x => x.LastLoginAt, DateTime.UtcNow);
        await _users.UpdateOneAsync(x => x.Id == userId, update);
    }

    // Ensure default admin user exists
    private async Task EnsureDefaultAdminAsync()
    {
        Console.WriteLine("Checking for default admin user...");
        
        // Get default admin credentials from environment or use defaults
        var defaultAdminUsername = Environment.GetEnvironmentVariable("DefaultAdmin__Username") ?? "admin";
        var defaultAdminPassword = Environment.GetEnvironmentVariable("DefaultAdmin__Password") ?? "Admin@123";
        var defaultAdminEmail = Environment.GetEnvironmentVariable("DefaultAdmin__Email") ?? "admin@cafemaatara.com";
        
        var adminExists = await _users.Find(x => x.Username == defaultAdminUsername).AnyAsync();
        if (!adminExists)
        {
            Console.WriteLine($"Admin user '{defaultAdminUsername}' not found. Creating default admin...");
            var authService = new AuthService();
            var adminUser = new User
            {
                Username = defaultAdminUsername,
                Email = defaultAdminEmail,
                PasswordHash = authService.HashPassword(defaultAdminPassword),
                Role = "admin",
                FirstName = "System",
                LastName = "Administrator",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            await _users.InsertOneAsync(adminUser);
            Console.WriteLine("✓ Default admin user created successfully!");
            Console.WriteLine($"  Username: {defaultAdminUsername}");
            Console.WriteLine($"  Email: {defaultAdminEmail}");
            Console.WriteLine($"  Password: {defaultAdminPassword}");
            Console.WriteLine("  IMPORTANT: Please change the default password after first login!");
        }
        else
        {
            Console.WriteLine($"✓ Admin user '{defaultAdminUsername}' already exists");
        }
    }

    // Reset admin password (useful for password recovery)
    public async Task<bool> ResetAdminPasswordAsync(string newPassword)
    {
        var defaultAdminUsername = Environment.GetEnvironmentVariable("DefaultAdmin__Username") ?? "admin";
        var admin = await _users.Find(x => x.Username == defaultAdminUsername).FirstOrDefaultAsync();
        
        if (admin == null)
        {
            Console.WriteLine($"Admin user '{defaultAdminUsername}' not found");
            return false;
        }

        var authService = new AuthService();
        var update = Builders<User>.Update.Set(x => x.PasswordHash, authService.HashPassword(newPassword));
        var result = await _users.UpdateOneAsync(x => x.Id == admin.Id, update);
        
        return result.ModifiedCount > 0;
    }

    #endregion

    #region Order Operations

    // Create new order
    public async Task<Order> CreateOrderAsync(Order order)
    {
        await _orders.InsertOneAsync(order);
        return order;
    }

    // Get user's orders
    public async Task<List<Order>> GetUserOrdersAsync(string userId)
    {
        return await _orders
            .Find(x => x.UserId == userId)
            .SortByDescending(x => x.CreatedAt)
            .ToListAsync();
    }

    // Get all orders (admin)
    public async Task<List<Order>> GetAllOrdersAsync()
    {
        return await _orders
            .Find(_ => true)
            .SortByDescending(x => x.CreatedAt)
            .ToListAsync();
    }

    // Get order by ID
    public async Task<Order?> GetOrderByIdAsync(string orderId)
    {
        return await _orders.Find(x => x.Id == orderId).FirstOrDefaultAsync();
    }

    // Update order status
    public async Task<bool> UpdateOrderStatusAsync(string orderId, string status)
    {
        var update = Builders<Order>.Update
            .Set(x => x.Status, status)
            .Set(x => x.UpdatedAt, DateTime.UtcNow);

        // If status is delivered, set completedAt
        if (status == "delivered")
        {
            update = update.Set(x => x.CompletedAt, DateTime.UtcNow);
        }

        var result = await _orders.UpdateOneAsync(x => x.Id == orderId, update);
        return result.ModifiedCount > 0;
    }

    // Delete order (for testing/admin purposes)
    public async Task<bool> DeleteOrderAsync(string orderId)
    {
        var result = await _orders.DeleteOneAsync(x => x.Id == orderId);
        return result.DeletedCount > 0;
    }

    #endregion

}
