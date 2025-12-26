using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Models;
using Cafe.Api.Helpers;
using System.Net;
using MongoDB.Bson;

namespace Cafe.Api.Functions;

public class InitializeIngredientsFunction
{
    private readonly MongoService _mongoService;
    private readonly AuthService _authService;
    private readonly ILogger<InitializeIngredientsFunction> _logger;

    public InitializeIngredientsFunction(
        MongoService mongoService,
        AuthService authService,
        ILogger<InitializeIngredientsFunction> logger)
    {
        _mongoService = mongoService;
        _authService = authService;
        _logger = logger;
    }

    // POST: Initialize ingredients database with cafe-specific data
    [Function("InitializeIngredients")]
    public async Task<HttpResponseData> InitializeIngredients(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "ingredients/initialize")] HttpRequestData req)
    {
        try
        {
            // Check authorization
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
            if (!isAuthorized) return errorResponse!;

            _logger.LogInformation("Starting ingredient initialization for Maa Tara Cafe");

            var ingredients = GetCafeIngredients();
            var created = 0;
            var errors = new List<string>();

            foreach (var ingredient in ingredients)
            {
                try
                {
                    var existing = await _mongoService.GetIngredientByIdAsync(ingredient.Id!);
                    if (existing == null)
                    {
                        await _mongoService.CreateIngredientAsync(ingredient);
                        created++;
                        _logger.LogInformation($"Created ingredient: {ingredient.Name}");
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"{ingredient.Name}: {ex.Message}");
                    _logger.LogError(ex, $"Failed to create ingredient: {ingredient.Name}");
                }
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                success = true,
                message = $"Initialized {created} ingredients",
                created,
                total = ingredients.Count,
                errors = errors.Take(10)
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing ingredients");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { success = false, error = ex.Message });
            return response;
        }
    }

    private List<Ingredient> GetCafeIngredients()
    {
        var now = DateTime.UtcNow;
        return new List<Ingredient>
        {
            // TEA & BEVERAGES BASE
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Tea Leaves (Dust)", Category = "beverages", MarketPrice = 400, Unit = "kg", IsActive = true, PriceSource = "manual", CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Tea Leaves (Premium)", Category = "beverages", MarketPrice = 600, Unit = "kg", IsActive = true, PriceSource = "manual", CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Coffee Powder", Category = "beverages", MarketPrice = 500, Unit = "kg", IsActive = true, PriceSource = "manual", CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Milk (Full Cream)", Category = "dairy", MarketPrice = 60, Unit = "ltr", IsActive = true, PriceSource = "manual", AutoUpdateEnabled = true, CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Sugar", Category = "others", MarketPrice = 45, Unit = "kg", IsActive = true, PriceSource = "manual", AutoUpdateEnabled = true, CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Ginger", Category = "vegetables", MarketPrice = 120, Unit = "kg", IsActive = true, PriceSource = "manual", AutoUpdateEnabled = true, CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Cardamom", Category = "spices", MarketPrice = 1500, Unit = "kg", IsActive = true, PriceSource = "manual", CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Tea Masala", Category = "spices", MarketPrice = 400, Unit = "kg", IsActive = true, PriceSource = "manual", CreatedAt = now, UpdatedAt = now, LastUpdated = now },

            // BURGER INGREDIENTS
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Burger Buns", Category = "grains", MarketPrice = 8, Unit = "pc", IsActive = true, PriceSource = "manual", CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Chicken Patty", Category = "meat", MarketPrice = 35, Unit = "pc", IsActive = true, PriceSource = "manual", CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Veg Patty", Category = "vegetables", MarketPrice = 20, Unit = "pc", IsActive = true, PriceSource = "manual", CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Cheese Slices", Category = "dairy", MarketPrice = 200, Unit = "kg", IsActive = true, PriceSource = "manual", CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Lettuce", Category = "vegetables", MarketPrice = 60, Unit = "kg", IsActive = true, PriceSource = "manual", AutoUpdateEnabled = true, CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Tomato", Category = "vegetables", MarketPrice = 50, Unit = "kg", IsActive = true, PriceSource = "manual", AutoUpdateEnabled = true, CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Onion (Sliced)", Category = "vegetables", MarketPrice = 40, Unit = "kg", IsActive = true, PriceSource = "manual", AutoUpdateEnabled = true, CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Mayonnaise", Category = "others", MarketPrice = 180, Unit = "kg", IsActive = true, PriceSource = "manual", CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Ketchup", Category = "others", MarketPrice = 150, Unit = "kg", IsActive = true, PriceSource = "manual", CreatedAt = now, UpdatedAt = now, LastUpdated = now },

            // MOMOS INGREDIENTS
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Momos Flour (Maida)", Category = "grains", MarketPrice = 50, Unit = "kg", IsActive = true, PriceSource = "manual", AutoUpdateEnabled = true, CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Chicken Mince", Category = "meat", MarketPrice = 280, Unit = "kg", IsActive = true, PriceSource = "manual", AutoUpdateEnabled = true, CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Cabbage", Category = "vegetables", MarketPrice = 30, Unit = "kg", IsActive = true, PriceSource = "manual", AutoUpdateEnabled = true, CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Carrot", Category = "vegetables", MarketPrice = 45, Unit = "kg", IsActive = true, PriceSource = "manual", AutoUpdateEnabled = true, CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Spring Onion", Category = "vegetables", MarketPrice = 80, Unit = "kg", IsActive = true, PriceSource = "manual", CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Soy Sauce", Category = "others", MarketPrice = 200, Unit = "ltr", IsActive = true, PriceSource = "manual", CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Vinegar", Category = "others", MarketPrice = 100, Unit = "ltr", IsActive = true, PriceSource = "manual", CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Garlic", Category = "vegetables", MarketPrice = 100, Unit = "kg", IsActive = true, PriceSource = "manual", AutoUpdateEnabled = true, CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Green Chilli", Category = "vegetables", MarketPrice = 80, Unit = "kg", IsActive = true, PriceSource = "manual", AutoUpdateEnabled = true, CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Schezwan Sauce", Category = "others", MarketPrice = 250, Unit = "kg", IsActive = true, PriceSource = "manual", CreatedAt = now, UpdatedAt = now, LastUpdated = now },

            // SANDWICH INGREDIENTS
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Bread Slices", Category = "grains", MarketPrice = 40, Unit = "pc", IsActive = true, PriceSource = "manual", CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Butter", Category = "dairy", MarketPrice = 450, Unit = "kg", IsActive = true, PriceSource = "manual", CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Capsicum", Category = "vegetables", MarketPrice = 60, Unit = "kg", IsActive = true, PriceSource = "manual", AutoUpdateEnabled = true, CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Cucumber", Category = "vegetables", MarketPrice = 35, Unit = "kg", IsActive = true, PriceSource = "manual", AutoUpdateEnabled = true, CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Paneer", Category = "dairy", MarketPrice = 300, Unit = "kg", IsActive = true, PriceSource = "manual", AutoUpdateEnabled = true, CreatedAt = now, UpdatedAt = now, LastUpdated = now },

            // COMMON SPICES & CONDIMENTS
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Salt", Category = "spices", MarketPrice = 20, Unit = "kg", IsActive = true, PriceSource = "manual", CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Black Pepper Powder", Category = "spices", MarketPrice = 600, Unit = "kg", IsActive = true, PriceSource = "manual", CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Red Chilli Powder", Category = "spices", MarketPrice = 250, Unit = "kg", IsActive = true, PriceSource = "manual", CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Turmeric Powder", Category = "spices", MarketPrice = 200, Unit = "kg", IsActive = true, PriceSource = "manual", CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Coriander Powder", Category = "spices", MarketPrice = 180, Unit = "kg", IsActive = true, PriceSource = "manual", CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Cumin Seeds", Category = "spices", MarketPrice = 400, Unit = "kg", IsActive = true, PriceSource = "manual", CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Garam Masala", Category = "spices", MarketPrice = 500, Unit = "kg", IsActive = true, PriceSource = "manual", CreatedAt = now, UpdatedAt = now, LastUpdated = now },

            // OILS & FATS
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Refined Oil", Category = "oils", MarketPrice = 150, Unit = "ltr", IsActive = true, PriceSource = "manual", AutoUpdateEnabled = true, CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Mustard Oil", Category = "oils", MarketPrice = 180, Unit = "ltr", IsActive = true, PriceSource = "manual", CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Ghee", Category = "dairy", MarketPrice = 500, Unit = "kg", IsActive = true, PriceSource = "manual", CreatedAt = now, UpdatedAt = now, LastUpdated = now },

            // BEVERAGES PACKAGED
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Mineral Water Bottle", Category = "beverages", MarketPrice = 20, Unit = "ltr", IsActive = true, PriceSource = "manual", CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Cold Drink (Campa Cola)", Category = "beverages", MarketPrice = 15, Unit = "pc", IsActive = true, PriceSource = "manual", CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Packaged Juice", Category = "beverages", MarketPrice = 30, Unit = "pc", IsActive = true, PriceSource = "manual", CreatedAt = now, UpdatedAt = now, LastUpdated = now },

            // SNACKS & BISCUITS
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Biscuits (Parle-G)", Category = "others", MarketPrice = 10, Unit = "pc", IsActive = true, PriceSource = "manual", CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Chips Packet", Category = "others", MarketPrice = 10, Unit = "pc", IsActive = true, PriceSource = "manual", CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Samosa (Ready)", Category = "others", MarketPrice = 8, Unit = "pc", IsActive = true, PriceSource = "manual", CreatedAt = now, UpdatedAt = now, LastUpdated = now },

            // ADDITIONAL VEGETABLES
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Potato", Category = "vegetables", MarketPrice = 30, Unit = "kg", IsActive = true, PriceSource = "manual", AutoUpdateEnabled = true, CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Coriander Leaves", Category = "vegetables", MarketPrice = 40, Unit = "kg", IsActive = true, PriceSource = "manual", CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Mint Leaves", Category = "vegetables", MarketPrice = 60, Unit = "kg", IsActive = true, PriceSource = "manual", CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Lemon", Category = "vegetables", MarketPrice = 80, Unit = "kg", IsActive = true, PriceSource = "manual", AutoUpdateEnabled = true, CreatedAt = now, UpdatedAt = now, LastUpdated = now },

            // PACKAGING MATERIALS
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Paper Cups (100ml)", Category = "others", MarketPrice = 2, Unit = "pc", IsActive = true, PriceSource = "manual", CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Paper Cups (200ml)", Category = "others", MarketPrice = 3, Unit = "pc", IsActive = true, PriceSource = "manual", CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Disposable Plates", Category = "others", MarketPrice = 3, Unit = "pc", IsActive = true, PriceSource = "manual", CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Food Packaging Box", Category = "others", MarketPrice = 8, Unit = "pc", IsActive = true, PriceSource = "manual", CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Tissue Paper", Category = "others", MarketPrice = 50, Unit = "pc", IsActive = true, PriceSource = "manual", CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Plastic Straws", Category = "others", MarketPrice = 0.5m, Unit = "pc", IsActive = true, PriceSource = "manual", CreatedAt = now, UpdatedAt = now, LastUpdated = now },

            // EGGS & PROTEIN
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Eggs", Category = "meat", MarketPrice = 6, Unit = "pc", IsActive = true, PriceSource = "manual", AutoUpdateEnabled = true, CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            
            // RICE & GRAINS (if you serve food items)
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Basmati Rice", Category = "grains", MarketPrice = 100, Unit = "kg", IsActive = true, PriceSource = "manual", AutoUpdateEnabled = true, CreatedAt = now, UpdatedAt = now, LastUpdated = now },
            new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Atta (Wheat Flour)", Category = "grains", MarketPrice = 40, Unit = "kg", IsActive = true, PriceSource = "manual", AutoUpdateEnabled = true, CreatedAt = now, UpdatedAt = now, LastUpdated = now },
        };
    }
}
