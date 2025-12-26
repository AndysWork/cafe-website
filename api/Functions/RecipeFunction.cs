using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Cafe.Api.Models;
using Cafe.Api.Services;
using System.Text.Json;
using Cafe.Api.Helpers;

namespace Cafe.Api.Functions
{
    public class RecipeFunction
    {
        private readonly ILogger<RecipeFunction> _logger;
        private readonly MongoService _mongoService;

        public RecipeFunction(ILogger<RecipeFunction> logger, MongoService mongoService)
        {
            _logger = logger;
            _mongoService = mongoService;
        }

        // GET: /api/recipes
        [Function("GetRecipes")]
        public async Task<IActionResult> GetRecipes(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "recipes")] HttpRequest req)
        {
            try
            {
                _logger.LogInformation("Getting all recipes");

                var recipes = await _mongoService.GetRecipesAsync();
                return new OkObjectResult(recipes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting recipes");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        // GET: /api/recipes/{id}
        [Function("GetRecipeById")]
        public async Task<IActionResult> GetRecipeById(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "recipes/{id}")] HttpRequest req,
            string id)
        {
            try
            {
                _logger.LogInformation($"Getting recipe with ID: {id}");

                var recipe = await _mongoService.GetRecipeByIdAsync(id);
                if (recipe == null)
                {
                    return new NotFoundResult();
                }

                return new OkObjectResult(recipe);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting recipe with ID: {id}");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        // GET: /api/recipes/menuitem/{menuItemName}
        [Function("GetRecipeByMenuItemName")]
        public async Task<IActionResult> GetRecipeByMenuItemName(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "recipes/menuitem/{menuItemName}")] HttpRequest req,
            string menuItemName)
        {
            try
            {
                _logger.LogInformation($"Getting recipe for menu item: {menuItemName}");

                var recipe = await _mongoService.GetRecipeByMenuItemNameAsync(menuItemName);
                if (recipe == null)
                {
                    return new NotFoundResult();
                }

                return new OkObjectResult(recipe);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting recipe for menu item: {menuItemName}");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        // POST: /api/recipes
        [Function("CreateRecipe")]
        public async Task<IActionResult> CreateRecipe(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "recipes")] HttpRequest req)
        {
            try
            {
                _logger.LogInformation("Creating new recipe");

                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var recipe = JsonSerializer.Deserialize<MenuItemRecipe>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (recipe == null || string.IsNullOrEmpty(recipe.MenuItemName))
                {
                    return new BadRequestObjectResult("Invalid recipe data");
                }

                // Sanitize inputs
                recipe.MenuItemName = InputSanitizer.Sanitize(recipe.MenuItemName);
                if (!string.IsNullOrEmpty(recipe.Notes))
                {
                    recipe.Notes = InputSanitizer.Sanitize(recipe.Notes);
                }

                // Set timestamps
                recipe.CreatedAt = DateTime.UtcNow;
                recipe.UpdatedAt = DateTime.UtcNow;

                var createdRecipe = await _mongoService.CreateRecipeAsync(recipe);
                return new OkObjectResult(createdRecipe);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating recipe");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        // PUT: /api/recipes/{id}
        [Function("UpdateRecipe")]
        public async Task<IActionResult> UpdateRecipe(
            [HttpTrigger(AuthorizationLevel.Function, "put", Route = "recipes/{id}")] HttpRequest req,
            string id)
        {
            try
            {
                _logger.LogInformation($"Updating recipe with ID: {id}");

                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var recipe = JsonSerializer.Deserialize<MenuItemRecipe>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (recipe == null)
                {
                    return new BadRequestObjectResult("Invalid recipe data");
                }

                // Sanitize inputs
                recipe.MenuItemName = InputSanitizer.Sanitize(recipe.MenuItemName);
                if (!string.IsNullOrEmpty(recipe.Notes))
                {
                    recipe.Notes = InputSanitizer.Sanitize(recipe.Notes);
                }

                recipe.Id = id;
                recipe.UpdatedAt = DateTime.UtcNow;

                var updated = await _mongoService.UpdateRecipeAsync(id, recipe);
                if (!updated)
                {
                    return new NotFoundResult();
                }

                return new OkObjectResult(recipe);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating recipe with ID: {id}");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        // DELETE: /api/recipes/{id}
        [Function("DeleteRecipe")]
        public async Task<IActionResult> DeleteRecipe(
            [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "recipes/{id}")] HttpRequest req,
            string id)
        {
            try
            {
                _logger.LogInformation($"Deleting recipe with ID: {id}");

                var deleted = await _mongoService.DeleteRecipeAsync(id);
                if (!deleted)
                {
                    return new NotFoundResult();
                }

                return new OkObjectResult(new { message = "Recipe deleted successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting recipe with ID: {id}");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        // POST: /api/recipes/calculate
        [Function("CalculateRecipePrice")]
        public IActionResult CalculateRecipePrice(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "recipes/calculate")] HttpRequest req)
        {
            try
            {
                _logger.LogInformation("Calculating recipe price");

                var requestBody = new StreamReader(req.Body).ReadToEndAsync().Result;
                var recipe = JsonSerializer.Deserialize<MenuItemRecipe>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (recipe == null)
                {
                    return new BadRequestObjectResult("Invalid recipe data");
                }

                // Calculate total ingredient cost
                var ingredientSubtotal = recipe.Ingredients.Sum(ing => ing.TotalCost);

                // Calculate wastage
                var wastageAmount = (ingredientSubtotal * recipe.OverheadCosts.WastagePercentage) / 100;

                // Calculate overhead costs
                var overheadSubtotal =
                    recipe.OverheadCosts.LabourCharge +
                    recipe.OverheadCosts.RentAllocation +
                    recipe.OverheadCosts.ElectricityCharge +
                    wastageAmount +
                    recipe.OverheadCosts.Miscellaneous;

                // Calculate making cost
                var makingCost = ingredientSubtotal + overheadSubtotal;

                // Calculate profit amount
                var profitAmount = (makingCost * recipe.ProfitMargin) / 100;

                // Calculate selling price
                var sellingPrice = makingCost + profitAmount;

                var calculation = new
                {
                    recipeId = recipe.Id ?? "",
                    recipeName = recipe.MenuItemName,
                    breakdown = new
                    {
                        ingredients = recipe.Ingredients,
                        ingredientSubtotal,
                        labour = recipe.OverheadCosts.LabourCharge,
                        rent = recipe.OverheadCosts.RentAllocation,
                        electricity = recipe.OverheadCosts.ElectricityCharge,
                        wastage = wastageAmount,
                        miscellaneous = recipe.OverheadCosts.Miscellaneous,
                        overheadSubtotal,
                        makingCost,
                        profitAmount,
                        profitPercentage = recipe.ProfitMargin,
                        sellingPrice
                    },
                    calculatedAt = DateTime.UtcNow
                };

                return new OkObjectResult(calculation);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating recipe price");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        // GET: /api/recipes/makingcost/{menuItemName}
        [Function("GetMakingCostByMenuItem")]
        public async Task<IActionResult> GetMakingCostByMenuItem(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "recipes/makingcost/{menuItemName}")] HttpRequest req,
            string menuItemName)
        {
            try
            {
                _logger.LogInformation($"Getting making cost for menu item: {menuItemName}");

                var recipe = await _mongoService.GetRecipeByMenuItemNameAsync(menuItemName);
                if (recipe == null)
                {
                    return new NotFoundResult();
                }

                var result = new
                {
                    menuItemName = recipe.MenuItemName,
                    makingCost = recipe.TotalMakingCost,
                    sellingPrice = recipe.SuggestedSellingPrice,
                    profitMargin = recipe.ProfitMargin
                };

                return new OkObjectResult(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting making cost for menu item: {menuItemName}");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }
    }
}
