using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Models;
using Cafe.Api.Services;
using Cafe.Api.Repositories;
using Cafe.Api.Helpers;
using System.Text.Json;
using System.Net;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace Cafe.Api.Functions
{
    public class RecipeFunction
    {
        private readonly ILogger<RecipeFunction> _logger;
        private readonly IPricingRepository _mongoService;
        private readonly AuthService _authService;

        public RecipeFunction(ILogger<RecipeFunction> logger, IPricingRepository mongoService, AuthService authService)
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

                recipe.CreatedAt = MongoService.GetIstNow();
                recipe.UpdatedAt = MongoService.GetIstNow();

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
                recipe.UpdatedAt = MongoService.GetIstNow();

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
                    calculatedAt = MongoService.GetIstNow()
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

        [Function("SyncRecipePrices")]
        public async Task<HttpResponseData> SyncRecipePrices(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "recipes/sync-prices")] HttpRequestData req)
        {
            try
            {
                var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
                if (!isAuthorized) return errorResponse!;

                _logger.LogInformation("Admin triggered recipe price sync to all menu items");

                var updated = await _mongoService.SyncAllRecipePricesToMenuItemsAsync();

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    message = $"Successfully synced prices for {updated} menu items from their recipes",
                    updatedCount = updated
                });
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing recipe prices to menu items");
                var response = req.CreateResponse(HttpStatusCode.InternalServerError);
                await response.WriteAsJsonAsync(new { error = "An error occurred while syncing recipe prices" });
                return response;
            }
        }

        [Function("DownloadRecipeTemplate")]
        public async Task<HttpResponseData> DownloadRecipeTemplate(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "recipes/template")] HttpRequestData req)
        {
            try
            {
                ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
                using var package = new ExcelPackage();
                var worksheet = package.Workbook.Worksheets.Add("Recipes");

                // Headers
                string[] headers = new[]
                {
                    "MenuItemName",
                    "DietaryType",
                    "IngredientName",
                    "Quantity",
                    "Unit",
                    "UnitPrice",
                    "ProfitMargin",
                    "PackagingCost",
                    "ShopPrice",
                    "OnlinePrice",
                    "Notes"
                };

                for (int col = 1; col <= headers.Length; col++)
                {
                    var cell = worksheet.Cells[1, col];
                    cell.Value = headers[col - 1];
                    cell.Style.Font.Bold = true;
                    cell.Style.Font.Color.SetColor(System.Drawing.Color.White);
                    cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(12, 74, 110)); // #0C4A6E
                    cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                }

                // Sample multi-ingredient recipes
                var sampleRows = new object?[][]
                {
                    // Chicken Biryani (non-veg)
                    new object?[] { "Chicken Biryani", "non-veg", "Chicken Breast (Boneless)", 0.25, "kg", 240.00, 30, 10.00, 250.00, 290.00, "Signature Dum Biryani" },
                    new object?[] { "Chicken Biryani", "non-veg", "Basmati Rice", 0.20, "kg", 80.00, null, null, null, null, null },
                    new object?[] { "Chicken Biryani", "non-veg", "Biryani Masala", 0.02, "kg", 400.00, null, null, null, null, null },
                    new object?[] { "Chicken Biryani", "non-veg", "Ghee", 0.03, "kg", 600.00, null, null, null, null, null },
                    new object?[] { "Chicken Biryani", "non-veg", "Fried Onion (Birista)", 0.04, "kg", 180.00, null, null, null, null, null },

                    // Cold Coffee (veg)
                    new object?[] { "Classic Cold Coffee", "veg", "Fresh Milk", 0.25, "ltr", 56.00, 35, 5.00, 90.00, 120.00, "Thick chilled creamy coffee" },
                    new object?[] { "Classic Cold Coffee", "veg", "Coffee Decoction", 0.05, "ltr", 150.00, null, null, null, null, null },
                    new object?[] { "Classic Cold Coffee", "veg", "Sugar", 0.02, "kg", 44.00, null, null, null, null, null },
                    new object?[] { "Classic Cold Coffee", "veg", "Vanilla Ice Cream", 0.05, "kg", 220.00, null, null, null, null, null },

                    // Paneer Butter Masala (veg)
                    new object?[] { "Paneer Butter Masala", "veg", "Fresh Paneer", 0.20, "kg", 320.00, 30, 8.00, 220.00, 260.00, "Rich tomato makhani gravy" },
                    new object?[] { "Paneer Butter Masala", "veg", "Butter", 0.04, "kg", 540.00, null, null, null, null, null },
                    new object?[] { "Paneer Butter Masala", "veg", "Fresh Cream", 0.05, "ltr", 220.00, null, null, null, null, null },
                    new object?[] { "Paneer Butter Masala", "veg", "Tomato Puree", 0.15, "kg", 60.00, null, null, null, null, null },

                    // French Fries Large (veg)
                    new object?[] { "French Fries Large", "veg", "Frozen French Fries 9mm", 0.25, "kg", 130.00, 40, 5.00, 110.00, 140.00, "Crispy golden french fries" },
                    new object?[] { "French Fries Large", "veg", "Refined Sunflower Oil", 0.03, "ltr", 120.00, null, null, null, null, null },
                    new object?[] { "French Fries Large", "veg", "Peri Peri Seasoning", 0.01, "kg", 350.00, null, null, null, null, null }
                };

                for (int r = 0; r < sampleRows.Length; r++)
                {
                    int rowNum = r + 2;
                    for (int c = 0; c < headers.Length; c++)
                    {
                        worksheet.Cells[rowNum, c + 1].Value = sampleRows[r][c];
                    }
                }

                worksheet.Row(1).Height = 26;
                for (int r = 2; r <= sampleRows.Length + 1; r++)
                {
                    worksheet.Row(r).Height = 20;
                }

                worksheet.Cells.AutoFitColumns();
                for (int col = 1; col <= headers.Length; col++)
                {
                    worksheet.Column(col).Width = Math.Max(worksheet.Column(col).Width + 4, 15);
                }

                var excelBytes = package.GetAsByteArray();
                var res = req.CreateResponse(HttpStatusCode.OK);
                res.Headers.Add("Content-Type", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
                res.Headers.Add("Content-Disposition", "attachment; filename=recipes_template.xlsx");
                await res.WriteBytesAsync(excelBytes);
                return res;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating recipe template");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new { error = "An error occurred while generating recipe template" });
                return errorResponse;
            }
        }

        [Function("UploadRecipesExcel")]
        public async Task<HttpResponseData> UploadRecipesExcel(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "recipes/upload")] HttpRequestData req)
        {
            try
            {
                var (isAuthorized, userId, role, authError) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
                if (!isAuthorized) return authError!;

                var outletId = OutletHelper.GetOutletIdFromRequest(req, _authService);
                if (string.IsNullOrWhiteSpace(outletId))
                {
                    var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badReq.WriteAsJsonAsync(new { error = "Outlet ID is required for recipe upload" });
                    return badReq;
                }

                ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

                byte[] fileBytes;
                using (var memoryStream = new MemoryStream())
                {
                    await req.Body.CopyToAsync(memoryStream);
                    fileBytes = memoryStream.ToArray();
                }

                if (fileBytes == null || fileBytes.Length == 0)
                {
                    var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badReq.WriteAsJsonAsync(new { error = "No file data received in request" });
                    return badReq;
                }

                const int MaxFileSizeBytes = 10 * 1024 * 1024;
                if (fileBytes.Length > MaxFileSizeBytes)
                {
                    var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badReq.WriteAsJsonAsync(new { error = "File size exceeds 10MB limit" });
                    return badReq;
                }

                var excelData = ExtractExcelFromMultipart(fileBytes);
                if (excelData == null || excelData.Length == 0)
                {
                    var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badReq.WriteAsJsonAsync(new { error = "Invalid Excel file format. Please upload a valid .xlsx file" });
                    return badReq;
                }

                var rows = new List<RecipeRowUpload>();

                using (var stream = new MemoryStream(excelData))
                using (var package = new ExcelPackage(stream))
                {
                    if (package.Workbook.Worksheets.Count == 0)
                    {
                        var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                        await badReq.WriteAsJsonAsync(new { error = "Excel workbook contains no worksheets" });
                        return badReq;
                    }

                    var worksheet = package.Workbook.Worksheets[0];
                    int rowCount = worksheet.Dimension?.Rows ?? 0;
                    int colCount = worksheet.Dimension?.Columns ?? 0;

                    if (rowCount < 2)
                    {
                        var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                        await badReq.WriteAsJsonAsync(new { error = "Excel sheet must contain headers in row 1 and at least one data row" });
                        return badReq;
                    }

                    var colMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    for (int col = 1; col <= colCount; col++)
                    {
                        var headerVal = worksheet.Cells[1, col].Text?.Trim();
                        if (!string.IsNullOrEmpty(headerVal))
                        {
                            var normalized = headerVal.Replace(" ", "").Replace("_", "").ToLowerInvariant();
                            colMap[normalized] = col;
                        }
                    }

                    int GetCol(params string[] aliases)
                    {
                        foreach (var a in aliases)
                        {
                            var norm = a.Replace(" ", "").Replace("_", "").ToLowerInvariant();
                            if (colMap.TryGetValue(norm, out int colIdx)) return colIdx;
                        }
                        return -1;
                    }

                    int colMenuItem = GetCol("MenuItemName", "MenuItem", "RecipeName", "Recipe", "ItemName", "Name");
                    int colDietary = GetCol("DietaryType", "Dietary", "Type");
                    int colIngredient = GetCol("IngredientName", "Ingredient", "Item");
                    int colQty = GetCol("Quantity", "Qty", "Portion");
                    int colUnit = GetCol("Unit", "MeasurementUnit", "UOM");
                    int colUnitPrice = GetCol("UnitPrice", "CostPerUnit", "UnitCost", "Price");
                    int colProfitMargin = GetCol("ProfitMargin", "Margin", "Profit");
                    int colPackaging = GetCol("PackagingCost", "PackagingCharge", "Packaging");
                    int colShopPrice = GetCol("ShopPrice", "DineInPrice", "SellingPrice");
                    int colOnlinePrice = GetCol("OnlinePrice", "ZomatoPrice", "SwiggyPrice");
                    int colNotes = GetCol("Notes", "Description", "Instructions");

                    if (colMenuItem == -1) colMenuItem = 1;
                    if (colDietary == -1) colDietary = 2;
                    if (colIngredient == -1) colIngredient = 3;
                    if (colQty == -1) colQty = 4;
                    if (colUnit == -1) colUnit = 5;
                    if (colUnitPrice == -1) colUnitPrice = 6;
                    if (colProfitMargin == -1) colProfitMargin = 7;
                    if (colPackaging == -1) colPackaging = 8;
                    if (colShopPrice == -1) colShopPrice = 9;
                    if (colOnlinePrice == -1) colOnlinePrice = 10;
                    if (colNotes == -1) colNotes = 11;

                    string currentMenuItem = string.Empty;
                    string currentDietary = "veg";

                    for (int row = 2; row <= rowCount; row++)
                    {
                        var rowMenuItem = colMenuItem > 0 ? worksheet.Cells[row, colMenuItem].Text?.Trim() : null;
                        if (!string.IsNullOrEmpty(rowMenuItem))
                        {
                            currentMenuItem = rowMenuItem;
                        }

                        if (string.IsNullOrEmpty(currentMenuItem)) continue;

                        var rowDietary = colDietary > 0 ? worksheet.Cells[row, colDietary].Text?.Trim() : null;
                        if (!string.IsNullOrEmpty(rowDietary))
                        {
                            currentDietary = rowDietary;
                        }

                        var ingName = colIngredient > 0 ? worksheet.Cells[row, colIngredient].Text?.Trim() : null;
                        if (string.IsNullOrEmpty(ingName)) continue;

                        var qtyText = colQty > 0 ? worksheet.Cells[row, colQty].Text?.Trim() : null;
                        if (!decimal.TryParse(qtyText, out var qty) || qty <= 0) continue;

                        var unit = colUnit > 0 ? worksheet.Cells[row, colUnit].Text?.Trim() : null;
                        var unitPriceText = colUnitPrice > 0 ? worksheet.Cells[row, colUnitPrice].Text?.Trim() : null;
                        decimal? unitPrice = decimal.TryParse(unitPriceText, out var up) && up >= 0 ? up : (decimal?)null;

                        var marginText = colProfitMargin > 0 ? worksheet.Cells[row, colProfitMargin].Text?.Trim() : null;
                        decimal? margin = decimal.TryParse(marginText, out var pm) && pm > 0 ? pm : (decimal?)null;

                        var packText = colPackaging > 0 ? worksheet.Cells[row, colPackaging].Text?.Trim() : null;
                        decimal? pack = decimal.TryParse(packText, out var pc) && pc >= 0 ? pc : (decimal?)null;

                        var shopText = colShopPrice > 0 ? worksheet.Cells[row, colShopPrice].Text?.Trim() : null;
                        decimal? shopPr = decimal.TryParse(shopText, out var sp) && sp > 0 ? sp : (decimal?)null;

                        var onlineText = colOnlinePrice > 0 ? worksheet.Cells[row, colOnlinePrice].Text?.Trim() : null;
                        decimal? onlinePr = decimal.TryParse(onlineText, out var op) && op > 0 ? op : (decimal?)null;

                        var notes = colNotes > 0 ? worksheet.Cells[row, colNotes].Text?.Trim() : null;

                        rows.Add(new RecipeRowUpload
                        {
                            MenuItemName = currentMenuItem,
                            DietaryType = currentDietary,
                            IngredientName = ingName,
                            Quantity = qty,
                            Unit = !string.IsNullOrEmpty(unit) ? unit : "kg",
                            UnitPrice = unitPrice,
                            ProfitMargin = margin,
                            PackagingCost = pack,
                            ShopPrice = shopPr,
                            OnlinePrice = onlinePr,
                            Notes = notes
                        });
                    }
                }

                if (rows.Count == 0)
                {
                    var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badReq.WriteAsJsonAsync(new { error = "No valid recipe ingredient rows found in Excel sheet" });
                    return badReq;
                }

                var result = await _mongoService.BulkUploadRecipesAsync(rows, outletId, userId ?? "admin");

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(result);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading recipe Excel");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new { error = "An internal error occurred while processing the Excel file" });
                return errorResponse;
            }
        }

        private static byte[] ExtractExcelFromMultipart(byte[] data)
        {
            try
            {
                var excelSignature = new byte[] { 0x50, 0x4B, 0x03, 0x04 };
                for (int i = 0; i <= data.Length - 4; i++)
                {
                    if (data[i] == excelSignature[0] &&
                        data[i + 1] == excelSignature[1] &&
                        data[i + 2] == excelSignature[2] &&
                        data[i + 3] == excelSignature[3])
                    {
                        var excelData = new byte[data.Length - i];
                        Array.Copy(data, i, excelData, 0, excelData.Length);
                        return excelData;
                    }
                }
                return data;
            }
            catch
            {
                return data;
            }
        }
    }
}
