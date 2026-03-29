using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Models;
using Cafe.Api.Services;
using Cafe.Api.Helpers;
using System.Text.Json;
using System.Net;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;

namespace Cafe.Api.Functions
{
    public class RecipeFunction
    {
        private readonly ILogger<RecipeFunction> _logger;
        private readonly MongoService _mongoService;
        private readonly AuthService _authService;

        public RecipeFunction(ILogger<RecipeFunction> logger, MongoService mongoService, AuthService authService)
        {
            _logger = logger;
            _mongoService = mongoService;
            _authService = authService;
        }

        // GET: /api/recipes
        [Function("GetRecipes")]
        [OpenApiOperation(operationId: "GetRecipes", tags: new[] { "Recipes" }, Summary = "Get all recipes", Description = "Retrieves all recipes")]
        [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(List<MenuItemRecipe>), Description = "Successfully retrieved recipes")]
        [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "User not authenticated")]
        public async Task<HttpResponseData> GetRecipes(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "recipes")] HttpRequestData req)
        {
            try
            {
                var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
                if (!isAuthorized) return errorResponse!;

                _logger.LogInformation("Getting all recipes");

                var outletId = OutletHelper.GetOutletIdFromRequest(req, _authService);
                var recipes = await _mongoService.GetRecipesAsync(outletId);

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(recipes);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting recipes");
                var response = req.CreateResponse(HttpStatusCode.InternalServerError);
                await response.WriteAsJsonAsync(new { error = "An error occurred while getting recipes" });
                return response;
            }
        }

        // GET: /api/recipes/{id}
        [Function("GetRecipeById")]
        [OpenApiOperation(operationId: "GetRecipeById", tags: new[] { "Recipes" }, Summary = "Get recipe by ID", Description = "Retrieves a specific recipe by its ID")]
        [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
        [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Recipe ID")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(MenuItemRecipe), Description = "Successfully retrieved recipe")]
        [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.NotFound, Description = "Recipe not found")]
        [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "User not authenticated")]
        public async Task<HttpResponseData> GetRecipeById(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "recipes/{id}")] HttpRequestData req,
            string id)
        {
            try
            {
                var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
                if (!isAuthorized) return errorResponse!;

                _logger.LogInformation("Getting recipe with ID: {Id}", id);

                var recipe = await _mongoService.GetRecipeByIdAsync(id);
                if (recipe == null)
                {
                    var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFound.WriteAsJsonAsync(new { error = "Recipe not found" });
                    return notFound;
                }

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(recipe);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting recipe with ID: {Id}", id);
                var response = req.CreateResponse(HttpStatusCode.InternalServerError);
                await response.WriteAsJsonAsync(new { error = "An error occurred while getting the recipe" });
                return response;
            }
        }

        // GET: /api/recipes/menuitem/{menuItemName}
        [Function("GetRecipeByMenuItemName")]
        [OpenApiOperation(operationId: "GetRecipeByMenuItemName", tags: new[] { "Recipes" }, Summary = "Get recipe by menu item name", Description = "Retrieves a recipe for a specific menu item")]
        [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
        [OpenApiParameter(name: "menuItemName", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Menu item name")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(MenuItemRecipe), Description = "Successfully retrieved recipe")]
        [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.NotFound, Description = "Recipe not found")]
        [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "User not authenticated")]
        public async Task<HttpResponseData> GetRecipeByMenuItemName(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "recipes/menuitem/{menuItemName}")] HttpRequestData req,
            string menuItemName)
        {
            try
            {
                var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
                if (!isAuthorized) return errorResponse!;

                _logger.LogInformation("Getting recipe for menu item: {MenuItemName}", menuItemName);

                var recipe = await _mongoService.GetRecipeByMenuItemNameAsync(menuItemName);
                if (recipe == null)
                {
                    var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFound.WriteAsJsonAsync(new { error = "Recipe not found" });
                    return notFound;
                }

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(recipe);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting recipe for menu item: {MenuItemName}", menuItemName);
                var response = req.CreateResponse(HttpStatusCode.InternalServerError);
                await response.WriteAsJsonAsync(new { error = "An error occurred while getting the recipe" });
                return response;
            }
        }

        // POST: /api/recipes
        [Function("CreateRecipe")]
        public async Task<HttpResponseData> CreateRecipe(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "recipes")] HttpRequestData req)
        {
            try
            {
                var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
                if (!isAuthorized) return errorResponse!;

                _logger.LogInformation("Creating new recipe");

                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var recipe = JsonSerializer.Deserialize<MenuItemRecipe>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (recipe == null || string.IsNullOrEmpty(recipe.MenuItemName))
                {
                    var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequest.WriteAsJsonAsync(new { error = "Invalid recipe data" });
                    return badRequest;
                }

                var outletId = OutletHelper.GetOutletIdFromRequest(req, _authService);
                if (!string.IsNullOrEmpty(outletId))
                {
                    recipe.OutletId = outletId;
                }

                recipe.MenuItemName = InputSanitizer.Sanitize(recipe.MenuItemName);
                if (!string.IsNullOrEmpty(recipe.Notes))
                {
                    recipe.Notes = InputSanitizer.Sanitize(recipe.Notes);
                }

                recipe.CreatedAt = DateTime.UtcNow;
                recipe.UpdatedAt = DateTime.UtcNow;

                var createdRecipe = await _mongoService.CreateRecipeAsync(recipe);

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(createdRecipe);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating recipe");
                var response = req.CreateResponse(HttpStatusCode.InternalServerError);
                await response.WriteAsJsonAsync(new { error = "An error occurred while creating the recipe" });
                return response;
            }
        }

        // PUT: /api/recipes/{id}
        [Function("UpdateRecipe")]
        public async Task<HttpResponseData> UpdateRecipe(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "recipes/{id}")] HttpRequestData req,
            string id)
        {
            try
            {
                var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
                if (!isAuthorized) return errorResponse!;

                _logger.LogInformation("Updating recipe with ID: {Id}", id);

                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var recipe = JsonSerializer.Deserialize<MenuItemRecipe>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (recipe == null)
                {
                    var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequest.WriteAsJsonAsync(new { error = "Invalid recipe data" });
                    return badRequest;
                }

                var outletId = OutletHelper.GetOutletIdFromRequest(req, _authService);
                if (!string.IsNullOrEmpty(outletId))
                {
                    recipe.OutletId = outletId;
                }

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
                    var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFound.WriteAsJsonAsync(new { error = "Recipe not found" });
                    return notFound;
                }

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(recipe);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating recipe with ID: {Id}", id);
                var response = req.CreateResponse(HttpStatusCode.InternalServerError);
                await response.WriteAsJsonAsync(new { error = "An error occurred while updating the recipe" });
                return response;
            }
        }

        // DELETE: /api/recipes/{id}
        [Function("DeleteRecipe")]
        public async Task<HttpResponseData> DeleteRecipe(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "recipes/{id}")] HttpRequestData req,
            string id)
        {
            try
            {
                var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
                if (!isAuthorized) return errorResponse!;

                _logger.LogInformation("Deleting recipe with ID: {Id}", id);

                var deleted = await _mongoService.DeleteRecipeAsync(id);
                if (!deleted)
                {
                    var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFound.WriteAsJsonAsync(new { error = "Recipe not found" });
                    return notFound;
                }

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new { message = "Recipe deleted successfully" });
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting recipe with ID: {Id}", id);
                var response = req.CreateResponse(HttpStatusCode.InternalServerError);
                await response.WriteAsJsonAsync(new { error = "An error occurred while deleting the recipe" });
                return response;
            }
        }

        // POST: /api/recipes/calculate
        [Function("CalculateRecipePrice")]
        public async Task<HttpResponseData> CalculateRecipePrice(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "recipes/calculate")] HttpRequestData req)
        {
            try
            {
                var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
                if (!isAuthorized) return errorResponse!;

                _logger.LogInformation("Calculating recipe price");

                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var recipe = JsonSerializer.Deserialize<MenuItemRecipe>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (recipe == null)
                {
                    var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequest.WriteAsJsonAsync(new { error = "Invalid recipe data" });
                    return badRequest;
                }

                var ingredientSubtotal = recipe.Ingredients.Sum(ing => ing.TotalCost);
                var wastageAmount = (ingredientSubtotal * recipe.OverheadCosts.WastagePercentage) / 100;

                var overheadSubtotal =
                    recipe.OverheadCosts.LabourCharge +
                    recipe.OverheadCosts.RentAllocation +
                    recipe.OverheadCosts.ElectricityCharge +
                    wastageAmount +
                    recipe.OverheadCosts.Miscellaneous;

                var makingCost = ingredientSubtotal + overheadSubtotal;
                var profitAmount = (makingCost * recipe.ProfitMargin) / 100;
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

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(calculation);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating recipe price");
                var response = req.CreateResponse(HttpStatusCode.InternalServerError);
                await response.WriteAsJsonAsync(new { error = "An error occurred while calculating the recipe price" });
                return response;
            }
        }

        // GET: /api/recipes/makingcost/{menuItemName}
        [Function("GetMakingCostByMenuItem")]
        public async Task<HttpResponseData> GetMakingCostByMenuItem(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "recipes/makingcost/{menuItemName}")] HttpRequestData req,
            string menuItemName)
        {
            try
            {
                var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
                if (!isAuthorized) return errorResponse!;

                _logger.LogInformation("Getting making cost for menu item: {MenuItemName}", menuItemName);

                var recipe = await _mongoService.GetRecipeByMenuItemNameAsync(menuItemName);
                if (recipe == null)
                {
                    var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFound.WriteAsJsonAsync(new { error = "Recipe not found" });
                    return notFound;
                }

                var result = new
                {
                    menuItemName = recipe.MenuItemName,
                    makingCost = recipe.TotalMakingCost,
                    sellingPrice = recipe.SuggestedSellingPrice,
                    profitMargin = recipe.ProfitMargin
                };

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(result);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting making cost for menu item: {MenuItemName}", menuItemName);
                var response = req.CreateResponse(HttpStatusCode.InternalServerError);
                await response.WriteAsJsonAsync(new { error = "An error occurred while getting the making cost" });
                return response;
            }
        }

        [Function("MigrateRecipeOutlets")]
        public async Task<HttpResponseData> MigrateRecipeOutlets(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "recipes/migrate-outlets")] HttpRequestData req)
        {
            try
            {
                var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
                if (!isAuthorized) return errorResponse!;

                _logger.LogInformation("Starting migration of recipe outlet IDs");
                
                string? defaultOutletId = null;
                try
                {
                    var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                    if (!string.IsNullOrEmpty(requestBody))
                    {
                        var jsonDoc = JsonDocument.Parse(requestBody);
                        if (jsonDoc.RootElement.TryGetProperty("defaultOutletId", out var outletIdElement))
                        {
                            defaultOutletId = outletIdElement.GetString();
                        }
                    }
                }
                catch
                {
                    // If parsing fails, continue without default outlet ID
                }
                
                var updated = await _mongoService.MigrateRecipeOutletIdsAsync(defaultOutletId);
                
                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new { 
                    success = true, 
                    message = $"Successfully updated {updated} recipes with outlet IDs",
                    updatedCount = updated,
                    defaultOutletIdUsed = defaultOutletId
                });
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error migrating recipe outlet IDs");
                var response = req.CreateResponse(HttpStatusCode.InternalServerError);
                await response.WriteAsJsonAsync(new { error = "An error occurred while migrating recipe outlets" });
                return response;
            }
        }
    }
}