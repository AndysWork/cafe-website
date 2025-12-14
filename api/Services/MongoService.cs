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
    private readonly IMongoCollection<LoyaltyAccount> _loyaltyAccounts;
    private readonly IMongoCollection<Reward> _rewards;
    private readonly IMongoCollection<PointsTransaction> _transactions;
    private readonly IMongoCollection<Offer> _offers;
    private readonly IMongoCollection<Sales> _sales;
    private readonly IMongoCollection<Expense> _expenses;
    private readonly IMongoCollection<SalesItemType> _salesItemTypes;
    private readonly IMongoCollection<OfflineExpenseType> _offlineExpenseTypes;
    private readonly IMongoCollection<OnlineExpenseType> _onlineExpenseTypes;
    
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
        _loyaltyAccounts = db.GetCollection<LoyaltyAccount>("LoyaltyAccounts");
        _rewards = db.GetCollection<Reward>("Rewards");
        _transactions = db.GetCollection<PointsTransaction>("PointsTransactions");
        _offers = db.GetCollection<Offer>("Offers");
        _sales = db.GetCollection<Sales>("Sales");
        _expenses = db.GetCollection<Expense>("Expenses");
        _salesItemTypes = db.GetCollection<SalesItemType>("SalesItemTypes");
        _offlineExpenseTypes = db.GetCollection<OfflineExpenseType>("OfflineExpenseTypes");
        _onlineExpenseTypes = db.GetCollection<OnlineExpenseType>("OnlineExpenseTypes");

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

        // Ensure indexes for loyalty collections
        try
        {
            EnsureIndexesAsync().Wait();
            Console.WriteLine("✓ Database indexes check completed");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error ensuring indexes: {ex.Message}");
        }

        // Ensure default rewards exist
        try
        {
            EnsureDefaultRewardsAsync().Wait();
            Console.WriteLine("✓ Default rewards check completed");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error ensuring default rewards: {ex.Message}");
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

    #endregion

    #region Loyalty Operations

    // Get or create loyalty account for user
    public async Task<LoyaltyAccount> GetOrCreateLoyaltyAccountAsync(string userId, string username)
    {
        var account = await _loyaltyAccounts.Find(x => x.UserId == userId).FirstOrDefaultAsync();
        
        if (account == null)
        {
            account = new LoyaltyAccount
            {
                UserId = userId,
                Username = username,
                CurrentPoints = 0,
                TotalPointsEarned = 0,
                TotalPointsRedeemed = 0,
                Tier = "Bronze",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await _loyaltyAccounts.InsertOneAsync(account);
        }
        
        return account;
    }

    // Get loyalty account by user ID
    public async Task<LoyaltyAccount?> GetLoyaltyAccountByUserIdAsync(string userId)
    {
        return await _loyaltyAccounts.Find(x => x.UserId == userId).FirstOrDefaultAsync();
    }

    // Get all loyalty accounts (admin)
    public async Task<List<LoyaltyAccount>> GetAllLoyaltyAccountsAsync()
    {
        return await _loyaltyAccounts
            .Find(_ => true)
            .SortByDescending(x => x.CurrentPoints)
            .ToListAsync();
    }

    // Award points to user
    public async Task<LoyaltyAccount?> AwardPointsAsync(string userId, int points, string description, string? orderId = null)
    {
        var account = await _loyaltyAccounts.Find(x => x.UserId == userId).FirstOrDefaultAsync();
        if (account == null) return null;

        // Update account
        account.CurrentPoints += points;
        account.TotalPointsEarned += points;
        account.UpdatedAt = DateTime.UtcNow;

        // Update tier based on total points earned
        account.Tier = CalculateTier(account.TotalPointsEarned);

        await _loyaltyAccounts.ReplaceOneAsync(x => x.Id == account.Id, account);

        // Create transaction record
        var transaction = new PointsTransaction
        {
            UserId = userId,
            Points = points,
            Type = "earned",
            Description = description,
            OrderId = orderId,
            CreatedAt = DateTime.UtcNow
        };
        await _transactions.InsertOneAsync(transaction);

        return account;
    }

    // Redeem points for reward
    public async Task<(bool Success, string Message, LoyaltyAccount? Account)> RedeemRewardAsync(string userId, string rewardId)
    {
        var account = await _loyaltyAccounts.Find(x => x.UserId == userId).FirstOrDefaultAsync();
        if (account == null)
            return (false, "Loyalty account not found", null);

        var reward = await _rewards.Find(x => x.Id == rewardId && x.IsActive).FirstOrDefaultAsync();
        if (reward == null)
            return (false, "Reward not found or inactive", null);

        if (reward.ExpiresAt.HasValue && reward.ExpiresAt.Value < DateTime.UtcNow)
            return (false, "Reward has expired", null);

        if (account.CurrentPoints < reward.PointsCost)
            return (false, $"Insufficient points. Need {reward.PointsCost}, have {account.CurrentPoints}", null);

        // Deduct points
        account.CurrentPoints -= reward.PointsCost;
        account.TotalPointsRedeemed += reward.PointsCost;
        account.UpdatedAt = DateTime.UtcNow;

        await _loyaltyAccounts.ReplaceOneAsync(x => x.Id == account.Id, account);

        // Create transaction record
        var transaction = new PointsTransaction
        {
            UserId = userId,
            Points = -reward.PointsCost,
            Type = "redeemed",
            Description = $"Redeemed: {reward.Name}",
            RewardId = rewardId,
            CreatedAt = DateTime.UtcNow
        };
        await _transactions.InsertOneAsync(transaction);

        return (true, $"Successfully redeemed {reward.Name}", account);
    }

    // Get user's transaction history
    public async Task<List<PointsTransaction>> GetUserTransactionsAsync(string userId)
    {
        return await _transactions
            .Find(x => x.UserId == userId)
            .SortByDescending(x => x.CreatedAt)
            .ToListAsync();
    }

    // Get all transactions (admin)
    public async Task<List<PointsTransaction>> GetAllTransactionsAsync()
    {
        return await _transactions
            .Find(_ => true)
            .SortByDescending(x => x.CreatedAt)
            .ToListAsync();
    }

    // Get all available rewards
    public async Task<List<Reward>> GetActiveRewardsAsync()
    {
        var now = DateTime.UtcNow;
        return await _rewards
            .Find(x => x.IsActive && (!x.ExpiresAt.HasValue || x.ExpiresAt.Value > now))
            .SortBy(x => x.PointsCost)
            .ToListAsync();
    }

    // Get all rewards (admin)
    public async Task<List<Reward>> GetAllRewardsAsync()
    {
        return await _rewards
            .Find(_ => true)
            .SortBy(x => x.PointsCost)
            .ToListAsync();
    }

    // Create reward (admin)
    public async Task<Reward> CreateRewardAsync(Reward reward)
    {
        reward.CreatedAt = DateTime.UtcNow;
        await _rewards.InsertOneAsync(reward);
        return reward;
    }

    // Update reward (admin)
    public async Task<bool> UpdateRewardAsync(string id, Reward reward)
    {
        var result = await _rewards.ReplaceOneAsync(x => x.Id == id, reward);
        return result.ModifiedCount > 0;
    }

    // Delete reward (admin)
    public async Task<bool> DeleteRewardAsync(string id)
    {
        var result = await _rewards.DeleteOneAsync(x => x.Id == id);
        return result.DeletedCount > 0;
    }

    // Calculate tier based on total points earned
    private string CalculateTier(int totalPoints)
    {
        if (totalPoints >= 3000) return "Platinum";
        if (totalPoints >= 1500) return "Gold";
        if (totalPoints >= 500) return "Silver";
        return "Bronze";
    }

    // Ensure database indexes for performance
    private async Task EnsureIndexesAsync()
    {
        // Index on LoyaltyAccounts.userId for faster user lookups
        var loyaltyUserIdIndex = Builders<LoyaltyAccount>.IndexKeys.Ascending(x => x.UserId);
        await _loyaltyAccounts.Indexes.CreateOneAsync(new CreateIndexModel<LoyaltyAccount>(
            loyaltyUserIdIndex,
            new CreateIndexOptions { Name = "userId_1", Unique = true }
        ));

        // Index on PointsTransactions.userId for faster transaction history queries
        var transactionUserIdIndex = Builders<PointsTransaction>.IndexKeys.Ascending(x => x.UserId);
        await _transactions.Indexes.CreateOneAsync(new CreateIndexModel<PointsTransaction>(
            transactionUserIdIndex,
            new CreateIndexOptions { Name = "userId_1" }
        ));

        // Index on PointsTransactions.createdAt for sorting
        var transactionDateIndex = Builders<PointsTransaction>.IndexKeys.Descending(x => x.CreatedAt);
        await _transactions.Indexes.CreateOneAsync(new CreateIndexModel<PointsTransaction>(
            transactionDateIndex,
            new CreateIndexOptions { Name = "createdAt_-1" }
        ));

        // Index on Rewards.isActive for faster active rewards queries
        var rewardActiveIndex = Builders<Reward>.IndexKeys.Ascending(x => x.IsActive);
        await _rewards.Indexes.CreateOneAsync(new CreateIndexModel<Reward>(
            rewardActiveIndex,
            new CreateIndexOptions { Name = "isActive_1" }
        ));

        Console.WriteLine("✓ Created indexes: LoyaltyAccounts(userId), PointsTransactions(userId, createdAt), Rewards(isActive)");
    }

    // Ensure default rewards exist in database
    private async Task EnsureDefaultRewardsAsync()
    {
        var count = await _rewards.CountDocumentsAsync(_ => true);
        if (count > 0) return;

        var defaultRewards = new List<Reward>
        {
            new Reward
            {
                Name = "Free Coffee",
                Description = "Enjoy a complimentary coffee of your choice",
                PointsCost = 100,
                Icon = "☕",
                IsActive = true
            },
            new Reward
            {
                Name = "10% Off Next Order",
                Description = "Get 10% discount on your next order",
                PointsCost = 150,
                Icon = "🎁",
                IsActive = true
            },
            new Reward
            {
                Name = "Free Dessert",
                Description = "Choose any dessert from our menu",
                PointsCost = 120,
                Icon = "🍰",
                IsActive = true
            },
            new Reward
            {
                Name = "Free Burger",
                Description = "Get a burger of your choice on the house",
                PointsCost = 200,
                Icon = "🍔",
                IsActive = true
            },
            new Reward
            {
                Name = "20% Off Next Order",
                Description = "Save 20% on your next order",
                PointsCost = 300,
                Icon = "💰",
                IsActive = true
            }
        };

        await _rewards.InsertManyAsync(defaultRewards);
        Console.WriteLine($"✓ Inserted {defaultRewards.Count} default rewards");
    }

    #endregion

    #region Offer Operations

    // Get all active offers
    public async Task<List<Offer>> GetActiveOffersAsync()
    {
        var now = DateTime.UtcNow;
        return await _offers.Find(o => 
            o.IsActive && 
            o.ValidFrom <= now && 
            o.ValidTill >= now
        ).ToListAsync();
    }

    // Get all offers (admin)
    public async Task<List<Offer>> GetAllOffersAsync() =>
        await _offers.Find(_ => true).ToListAsync();

    // Get offer by ID
    public async Task<Offer?> GetOfferByIdAsync(string id) =>
        await _offers.Find(o => o.Id == id).FirstOrDefaultAsync();

    // Get offer by code
    public async Task<Offer?> GetOfferByCodeAsync(string code) =>
        await _offers.Find(o => o.Code.ToLower() == code.ToLower()).FirstOrDefaultAsync();

    // Create new offer
    public async Task<Offer> CreateOfferAsync(Offer offer)
    {
        offer.CreatedAt = DateTime.UtcNow;
        offer.UpdatedAt = DateTime.UtcNow;
        await _offers.InsertOneAsync(offer);
        return offer;
    }

    // Update offer
    public async Task<bool> UpdateOfferAsync(string id, Offer offer)
    {
        offer.UpdatedAt = DateTime.UtcNow;
        var result = await _offers.ReplaceOneAsync(o => o.Id == id, offer);
        return result.ModifiedCount > 0;
    }

    // Delete offer
    public async Task<bool> DeleteOfferAsync(string id)
    {
        var result = await _offers.DeleteOneAsync(o => o.Id == id);
        return result.DeletedCount > 0;
    }

    // Increment offer usage count
    public async Task<bool> IncrementOfferUsageAsync(string id)
    {
        var update = Builders<Offer>.Update.Inc(o => o.UsageCount, 1);
        var result = await _offers.UpdateOneAsync(o => o.Id == id, update);
        return result.ModifiedCount > 0;
    }

    // Validate offer
    public async Task<OfferValidationResponse> ValidateOfferAsync(OfferValidationRequest request)
    {
        var offer = await GetOfferByCodeAsync(request.Code);

        if (offer == null)
        {
            return new OfferValidationResponse
            {
                IsValid = false,
                Message = "Invalid offer code"
            };
        }

        if (!offer.IsActive)
        {
            return new OfferValidationResponse
            {
                IsValid = false,
                Message = "This offer is no longer active"
            };
        }

        var now = DateTime.UtcNow;
        if (now < offer.ValidFrom)
        {
            return new OfferValidationResponse
            {
                IsValid = false,
                Message = "This offer is not yet valid"
            };
        }

        if (now > offer.ValidTill)
        {
            return new OfferValidationResponse
            {
                IsValid = false,
                Message = "This offer has expired"
            };
        }

        if (offer.UsageLimit.HasValue && offer.UsageCount >= offer.UsageLimit.Value)
        {
            return new OfferValidationResponse
            {
                IsValid = false,
                Message = "This offer has reached its usage limit"
            };
        }

        if (offer.MinOrderAmount.HasValue && request.OrderAmount < offer.MinOrderAmount.Value)
        {
            return new OfferValidationResponse
            {
                IsValid = false,
                Message = $"Minimum order amount of ₹{offer.MinOrderAmount.Value} required"
            };
        }

        // Calculate discount
        decimal discountAmount = 0;
        switch (offer.DiscountType.ToLower())
        {
            case "percentage":
                discountAmount = (request.OrderAmount * offer.DiscountValue) / 100;
                if (offer.MaxDiscount.HasValue && discountAmount > offer.MaxDiscount.Value)
                {
                    discountAmount = offer.MaxDiscount.Value;
                }
                break;
            case "flat":
                discountAmount = offer.DiscountValue;
                break;
            case "bogo":
                // For BOGO, discount is handled differently, set to 0 for now
                discountAmount = 0;
                break;
        }

        return new OfferValidationResponse
        {
            IsValid = true,
            Message = "Offer applied successfully",
            Offer = offer,
            DiscountAmount = discountAmount
        };
    }

    #endregion

    // Delete order (for testing/admin purposes)
    public async Task<bool> DeleteOrderAsync(string orderId)
    {
        var result = await _orders.DeleteOneAsync(x => x.Id == orderId);
        return result.DeletedCount > 0;
    }

    #region Sales Operations

    // Get all sales records
    public async Task<List<Sales>> GetAllSalesAsync() =>
        await _sales.Find(_ => true).SortByDescending(s => s.Date).ToListAsync();

    // Get sales by date range
    public async Task<List<Sales>> GetSalesByDateRangeAsync(DateTime startDate, DateTime endDate) =>
        await _sales.Find(s => s.Date >= startDate && s.Date <= endDate)
            .SortByDescending(s => s.Date)
            .ToListAsync();

    // Get sales by ID
    public async Task<Sales?> GetSalesByIdAsync(string id) =>
        await _sales.Find(x => x.Id == id).FirstOrDefaultAsync();

    // Create new sales record
    public async Task<Sales> CreateSalesAsync(Sales sales)
    {
        sales.CreatedAt = DateTime.UtcNow;
        sales.UpdatedAt = DateTime.UtcNow;
        await _sales.InsertOneAsync(sales);
        return sales;
    }

    // Update sales record
    public async Task<bool> UpdateSalesAsync(string id, Sales sales)
    {
        sales.UpdatedAt = DateTime.UtcNow;
        var result = await _sales.ReplaceOneAsync(x => x.Id == id, sales);
        return result.ModifiedCount > 0;
    }

    // Delete sales record
    public async Task<bool> DeleteSalesAsync(string id)
    {
        var result = await _sales.DeleteOneAsync(x => x.Id == id);
        return result.DeletedCount > 0;
    }

    // Get sales summary by date
    public async Task<SalesSummary> GetSalesSummaryByDateAsync(DateTime date)
    {
        var startOfDay = date.Date;
        var endOfDay = startOfDay.AddDays(1);

        var salesRecords = await _sales.Find(s => s.Date >= startOfDay && s.Date < endOfDay).ToListAsync();

        var summary = new SalesSummary
        {
            Date = date,
            TotalSales = salesRecords.Sum(s => s.TotalAmount),
            TotalTransactions = salesRecords.Count,
            PaymentMethodBreakdown = salesRecords
                .GroupBy(s => s.PaymentMethod)
                .ToDictionary(g => g.Key, g => g.Sum(s => s.TotalAmount))
        };

        return summary;
    }

    #endregion

    #region Expense Operations

    // Get all expenses
    public async Task<List<Expense>> GetAllExpensesAsync() =>
        await _expenses.Find(_ => true).SortByDescending(e => e.Date).ToListAsync();

    // Get expenses by date range
    public async Task<List<Expense>> GetExpensesByDateRangeAsync(DateTime startDate, DateTime endDate) =>
        await _expenses.Find(e => e.Date >= startDate && e.Date <= endDate)
            .SortByDescending(e => e.Date)
            .ToListAsync();

    // Get expense by ID
    public async Task<Expense?> GetExpenseByIdAsync(string id) =>
        await _expenses.Find(x => x.Id == id).FirstOrDefaultAsync();

    // Create new expense
    public async Task<Expense> CreateExpenseAsync(Expense expense)
    {
        expense.CreatedAt = DateTime.UtcNow;
        expense.UpdatedAt = DateTime.UtcNow;
        await _expenses.InsertOneAsync(expense);
        return expense;
    }

    // Update expense
    public async Task<bool> UpdateExpenseAsync(string id, Expense expense)
    {
        expense.UpdatedAt = DateTime.UtcNow;
        var result = await _expenses.ReplaceOneAsync(x => x.Id == id, expense);
        return result.ModifiedCount > 0;
    }

    // Delete expense
    public async Task<bool> DeleteExpenseAsync(string id)
    {
        var result = await _expenses.DeleteOneAsync(x => x.Id == id);
        return result.DeletedCount > 0;
    }

    // Get expense summary by date
    public async Task<ExpenseSummary> GetExpenseSummaryByDateAsync(DateTime date)
    {
        var startOfDay = date.Date;
        var endOfDay = startOfDay.AddDays(1);

        var expenses = await _expenses.Find(e => e.Date >= startOfDay && e.Date < endOfDay).ToListAsync();

        var summary = new ExpenseSummary
        {
            Date = date,
            TotalExpenses = expenses.Sum(e => e.Amount),
            ExpenseTypeBreakdown = expenses
                .GroupBy(e => e.ExpenseType)
                .ToDictionary(g => g.Key, g => g.Sum(e => e.Amount))
        };

        return summary;
    }

    #endregion

    #region SalesItemType Methods

    public async Task<List<SalesItemType>> GetAllSalesItemTypesAsync() =>
        await _salesItemTypes.Find(_ => true).ToListAsync();

    public async Task<List<SalesItemType>> GetActiveSalesItemTypesAsync() =>
        await _salesItemTypes.Find(s => s.IsActive).ToListAsync();

    public async Task<SalesItemType?> GetSalesItemTypeByIdAsync(string id) =>
        await _salesItemTypes.Find(s => s.Id == id).FirstOrDefaultAsync();

    public async Task<SalesItemType> CreateSalesItemTypeAsync(SalesItemType itemType)
    {
        itemType.CreatedAt = DateTime.UtcNow;
        itemType.UpdatedAt = DateTime.UtcNow;
        await _salesItemTypes.InsertOneAsync(itemType);
        return itemType;
    }

    public async Task<SalesItemType?> UpdateSalesItemTypeAsync(string id, SalesItemType itemType)
    {
        itemType.UpdatedAt = DateTime.UtcNow;
        var result = await _salesItemTypes.ReplaceOneAsync(s => s.Id == id, itemType);
        return result.ModifiedCount > 0 ? itemType : null;
    }

    public async Task<bool> DeleteSalesItemTypeAsync(string id)
    {
        var result = await _salesItemTypes.DeleteOneAsync(s => s.Id == id);
        return result.DeletedCount > 0;
    }

    public async Task InitializeDefaultSalesItemTypesAsync()
    {
        var count = await _salesItemTypes.CountDocumentsAsync(_ => true);
        if (count == 0)
        {
            var defaultItems = new List<SalesItemType>
            {
                new() { ItemName = "Tea - 5", DefaultPrice = 5, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { ItemName = "Tea - 10", DefaultPrice = 10, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { ItemName = "Tea - 20", DefaultPrice = 20, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { ItemName = "Tea - 30", DefaultPrice = 30, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { ItemName = "Black Tea", DefaultPrice = 10, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { ItemName = "Tea Parcel", DefaultPrice = 50, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { ItemName = "Coffee", DefaultPrice = 20, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { ItemName = "Biscuit", DefaultPrice = 10, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { ItemName = "Cigarete", DefaultPrice = 20, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { ItemName = "Snacks", DefaultPrice = 15, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { ItemName = "Water", DefaultPrice = 20, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { ItemName = "Campa", DefaultPrice = 30, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
            };

            await _salesItemTypes.InsertManyAsync(defaultItems);
        }
    }

    #endregion

    #region OfflineExpenseType Methods

    public async Task<List<OfflineExpenseType>> GetAllOfflineExpenseTypesAsync()
    {
        return await _offlineExpenseTypes.Find(_ => true).ToListAsync();
    }

    public async Task<List<OfflineExpenseType>> GetActiveOfflineExpenseTypesAsync()
    {
        return await _offlineExpenseTypes.Find(e => e.IsActive).ToListAsync();
    }

    public async Task<OfflineExpenseType?> GetOfflineExpenseTypeByIdAsync(string id)
    {
        return await _offlineExpenseTypes.Find(e => e.Id == id).FirstOrDefaultAsync();
    }

    public async Task<OfflineExpenseType> CreateOfflineExpenseTypeAsync(CreateOfflineExpenseTypeRequest request)
    {
        var expenseType = new OfflineExpenseType
        {
            ExpenseType = request.ExpenseType,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _offlineExpenseTypes.InsertOneAsync(expenseType);
        return expenseType;
    }

    public async Task<bool> UpdateOfflineExpenseTypeAsync(string id, CreateOfflineExpenseTypeRequest request)
    {
        var update = Builders<OfflineExpenseType>.Update
            .Set(e => e.ExpenseType, request.ExpenseType)
            .Set(e => e.UpdatedAt, DateTime.UtcNow);

        var result = await _offlineExpenseTypes.UpdateOneAsync(e => e.Id == id, update);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> DeleteOfflineExpenseTypeAsync(string id)
    {
        var result = await _offlineExpenseTypes.DeleteOneAsync(e => e.Id == id);
        return result.DeletedCount > 0;
    }

    public async Task InitializeDefaultOfflineExpenseTypesAsync()
    {
        var count = await _offlineExpenseTypes.CountDocumentsAsync(_ => true);
        if (count == 0)
        {
            var defaultExpenseTypes = new List<OfflineExpenseType>
            {
                new() { ExpenseType = "Milk", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { ExpenseType = "Cup", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { ExpenseType = "Cigarete", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { ExpenseType = "Biscuit", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { ExpenseType = "Rent", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { ExpenseType = "Grocerry", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { ExpenseType = "Misc", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { ExpenseType = "Tea", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { ExpenseType = "Water", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { ExpenseType = "Chicken", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { ExpenseType = "Cold Drinks", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { ExpenseType = "Packaging", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { ExpenseType = "Utensils", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { ExpenseType = "Kitkat/Oreo", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { ExpenseType = "Egg", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { ExpenseType = "Veggie", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { ExpenseType = "Sugar", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { ExpenseType = "Paneer", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { ExpenseType = "Bread", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { ExpenseType = "Fund (Save)", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new() { ExpenseType = "Ice cream", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
            };

            await _offlineExpenseTypes.InsertManyAsync(defaultExpenseTypes);
        }
    }

    #endregion

    #region OnlineExpenseType Methods

    public async Task<List<OnlineExpenseType>> GetAllOnlineExpenseTypesAsync()
    {
        return await _onlineExpenseTypes.Find(_ => true).ToListAsync();
    }

    public async Task<List<OnlineExpenseType>> GetActiveOnlineExpenseTypesAsync()
    {
        return await _onlineExpenseTypes.Find(t => t.IsActive).ToListAsync();
    }

    public async Task<OnlineExpenseType?> GetOnlineExpenseTypeByIdAsync(string id)
    {
        return await _onlineExpenseTypes.Find(t => t.Id == id).FirstOrDefaultAsync();
    }

    public async Task<OnlineExpenseType> CreateOnlineExpenseTypeAsync(CreateOnlineExpenseTypeRequest request)
    {
        var expenseType = new OnlineExpenseType
        {
            ExpenseType = request.ExpenseType,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _onlineExpenseTypes.InsertOneAsync(expenseType);
        return expenseType;
    }

    public async Task UpdateOnlineExpenseTypeAsync(string id, CreateOnlineExpenseTypeRequest request)
    {
        var update = Builders<OnlineExpenseType>.Update
            .Set(t => t.ExpenseType, request.ExpenseType)
            .Set(t => t.UpdatedAt, DateTime.UtcNow);

        await _onlineExpenseTypes.UpdateOneAsync(t => t.Id == id, update);
    }

    public async Task DeleteOnlineExpenseTypeAsync(string id)
    {
        await _onlineExpenseTypes.DeleteOneAsync(t => t.Id == id);
    }

    public async Task InitializeDefaultOnlineExpenseTypesAsync()
    {
        var existingCount = await _onlineExpenseTypes.CountDocumentsAsync(_ => true);
        if (existingCount > 0)
        {
            return; // Already initialized
        }

        var defaultExpenseTypes = new List<OnlineExpenseType>
        {
            new() { ExpenseType = "Grocerry", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new() { ExpenseType = "Tea", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new() { ExpenseType = "Buiscuit", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new() { ExpenseType = "Snacks", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new() { ExpenseType = "Sabji & Plate", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new() { ExpenseType = "Print", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new() { ExpenseType = "Cigarette", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new() { ExpenseType = "Water & Cold Drinks", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new() { ExpenseType = "Sabji", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new() { ExpenseType = "Bread & Banner", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new() { ExpenseType = "Vishal Megamart", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new() { ExpenseType = "Bread", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new() { ExpenseType = "Bread & Others", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new() { ExpenseType = "Foils & Others", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new() { ExpenseType = "Grocerry & Chicken", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new() { ExpenseType = "Misc", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new() { ExpenseType = "Campa", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new() { ExpenseType = "Milk", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new() { ExpenseType = "Chicken", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new() { ExpenseType = "Hyperpure", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new() { ExpenseType = "Coffee", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new() { ExpenseType = "Piu Salary", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new() { ExpenseType = "Packaging", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new() { ExpenseType = "Sabji & Others", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new() { ExpenseType = "Ice Cube", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new() { ExpenseType = "Blinkit", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new() { ExpenseType = "Printing", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
        };

        await _onlineExpenseTypes.InsertManyAsync(defaultExpenseTypes);
    }

    #endregion

}
