using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Cafe.Api.Models;
using Cafe.Api.Services;
using System.Text.Json;
using Cafe.Api.Helpers;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;
using System.Net;

namespace Cafe.Api.Functions
{
    public class IngredientFunction
    {
        private readonly ILogger<IngredientFunction> _logger;
        private readonly MongoService _mongoService;
        private readonly IEmailService _emailService;
        private const decimal MAJOR_PRICE_CHANGE_THRESHOLD = 10.0m; // 10% change threshold

        public IngredientFunction(ILogger<IngredientFunction> logger, MongoService mongoService, IEmailService emailService)
        {
            _logger = logger;
            _mongoService = mongoService;
            _emailService = emailService;
        }

        // GET: /api/ingredients
        [Function("GetIngredients")]
        [OpenApiOperation(operationId: "GetIngredients", tags: new[] { "Ingredients" }, Summary = "Get all ingredients", Description = "Retrieves all ingredients")]
        [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(List<Ingredient>), Description = "Successfully retrieved ingredients")]
        [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "User not authenticated")]
        public async Task<IActionResult> GetIngredients(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "ingredients")] HttpRequest req)
        {
            try
            {
                _logger.LogInformation("Getting all ingredients");

                var ingredients = await _mongoService.GetIngredientsAsync();
                return new OkObjectResult(ingredients);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting ingredients");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        // GET: /api/ingredients/{id}
        [Function("GetIngredientById")]
        [OpenApiOperation(operationId: "GetIngredientById", tags: new[] { "Ingredients" }, Summary = "Get ingredient by ID", Description = "Retrieves a specific ingredient by its ID")]
        [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
        [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Ingredient ID")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(Ingredient), Description = "Successfully retrieved ingredient")]
        [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.NotFound, Description = "Ingredient not found")]
        [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "User not authenticated")]
        public async Task<IActionResult> GetIngredientById(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "ingredients/{id}")] HttpRequest req,
            string id)
        {
            try
            {
                _logger.LogInformation($"Getting ingredient with ID: {id}");

                var ingredient = await _mongoService.GetIngredientByIdAsync(id);
                if (ingredient == null)
                {
                    return new NotFoundResult();
                }

                return new OkObjectResult(ingredient);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting ingredient with ID: {id}");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        // POST: /api/ingredients
        [Function("CreateIngredient")]
        [OpenApiOperation(operationId: "CreateIngredient", tags: new[] { "Ingredients" }, Summary = "Create a new ingredient", Description = "Creates a new ingredient")]
        [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
        [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(Ingredient), Required = true, Description = "Ingredient details")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.Created, contentType: "application/json", bodyType: typeof(Ingredient), Description = "Ingredient successfully created")]
        [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.BadRequest, Description = "Invalid ingredient data")]
        [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "User not authenticated")]
        public async Task<IActionResult> CreateIngredient(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "ingredients")] HttpRequest req)
        {
            try
            {
                _logger.LogInformation("Creating new ingredient");

                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var ingredient = JsonSerializer.Deserialize<Ingredient>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (ingredient == null || string.IsNullOrEmpty(ingredient.Name))
                {
                    return new BadRequestObjectResult("Invalid ingredient data");
                }

                // Sanitize inputs
                ingredient.Name = InputSanitizer.Sanitize(ingredient.Name);
                ingredient.Category = InputSanitizer.Sanitize(ingredient.Category);
                ingredient.Unit = InputSanitizer.Sanitize(ingredient.Unit);

                // Set timestamps
                ingredient.CreatedAt = DateTime.UtcNow;
                ingredient.UpdatedAt = DateTime.UtcNow;
                ingredient.LastUpdated = DateTime.UtcNow;
                ingredient.IsActive = true;

                var createdIngredient = await _mongoService.CreateIngredientAsync(ingredient);
                return new OkObjectResult(createdIngredient);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating ingredient");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        // PUT: /api/ingredients/{id}
        [Function("UpdateIngredient")]
        public async Task<IActionResult> UpdateIngredient(
            [HttpTrigger(AuthorizationLevel.Function, "put", Route = "ingredients/{id}")] HttpRequest req,
            string id)
        {
            try
            {
                _logger.LogInformation($"Updating ingredient with ID: {id}");

                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var ingredient = JsonSerializer.Deserialize<Ingredient>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (ingredient == null)
                {
                    return new BadRequestObjectResult("Invalid ingredient data");
                }

                // Get current ingredient to detect price changes
                var currentIngredient = await _mongoService.GetIngredientByIdAsync(id);
                if (currentIngredient == null)
                {
                    return new NotFoundResult();
                }

                // Sanitize inputs
                ingredient.Name = InputSanitizer.Sanitize(ingredient.Name);
                ingredient.Category = InputSanitizer.Sanitize(ingredient.Category);
                ingredient.Unit = InputSanitizer.Sanitize(ingredient.Unit);

                ingredient.Id = id;
                ingredient.UpdatedAt = DateTime.UtcNow;
                ingredient.LastUpdated = DateTime.UtcNow;

                // Check for major price change
                bool isMajorPriceChange = false;
                decimal priceChangePercentage = 0;
                
                if (currentIngredient.MarketPrice > 0 && ingredient.MarketPrice != currentIngredient.MarketPrice)
                {
                    priceChangePercentage = ((ingredient.MarketPrice - currentIngredient.MarketPrice) / currentIngredient.MarketPrice) * 100;
                    isMajorPriceChange = Math.Abs(priceChangePercentage) >= MAJOR_PRICE_CHANGE_THRESHOLD;
                    
                    ingredient.PreviousPrice = currentIngredient.MarketPrice;
                    ingredient.PriceChangePercentage = priceChangePercentage;

                    // Save price history
                    await _mongoService.SavePriceHistoryAsync(new IngredientPriceHistory
                    {
                        IngredientId = id,
                        IngredientName = ingredient.Name,
                        Price = ingredient.MarketPrice,
                        Unit = ingredient.Unit,
                        ChangePercentage = priceChangePercentage,
                        Source = ingredient.PriceSource ?? "manual",
                        RecordedAt = DateTime.UtcNow,
                        Notes = $"Updated by admin. Previous: ₹{currentIngredient.MarketPrice}"
                    });

                    _logger.LogInformation($"Price updated for {ingredient.Name}: {currentIngredient.MarketPrice} -> {ingredient.MarketPrice} ({priceChangePercentage:F2}% change)");
                }

                var updated = await _mongoService.UpdateIngredientAsync(id, ingredient);
                if (!updated)
                {
                    return new NotFoundResult();
                }

                // Send notification for major price changes
                if (isMajorPriceChange)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await NotifyMajorPriceChangeAsync(ingredient, priceChangePercentage);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Failed to send price change notification for {ingredient.Name}");
                        }
                    });
                }

                return new OkObjectResult(new
                {
                    ingredient,
                    priceChangeAlert = isMajorPriceChange ? new
                    {
                        message = $"Major price change detected: {Math.Abs(priceChangePercentage):F2}% {(priceChangePercentage > 0 ? "increase" : "decrease")}",
                        percentage = priceChangePercentage,
                        oldPrice = currentIngredient.MarketPrice,
                        newPrice = ingredient.MarketPrice
                    } : null
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating ingredient with ID: {id}");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        private async Task NotifyMajorPriceChangeAsync(Ingredient ingredient, decimal changePercentage)
        {
            var direction = changePercentage > 0 ? "increased" : "decreased";
            var subject = $"⚠️ Major Price Alert: {ingredient.Name}";
            
            var htmlContent = $@"
<html>
<head>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .alert-box {{ background: #fff3cd; border-left: 4px solid #ff9800; padding: 15px; margin: 20px 0; }}
        .price-details {{ background: #f8f9fa; padding: 15px; border-radius: 5px; margin: 15px 0; }}
        .price-row {{ display: flex; justify-content: space-between; margin: 10px 0; }}
        .label {{ font-weight: bold; }}
        .change {{ font-size: 1.2em; color: {(changePercentage > 0 ? "#d32f2f" : "#388e3c")}; font-weight: bold; }}
    </style>
</head>
<body>
    <div class='container'>
        <h2>🔔 Price Alert Notification</h2>
        <div class='alert-box'>
            <strong>Major price change detected for: {ingredient.Name}</strong>
        </div>
        <div class='price-details'>
            <div class='price-row'>
                <span class='label'>Ingredient:</span>
                <span>{ingredient.Name} ({ingredient.Category})</span>
            </div>
            <div class='price-row'>
                <span class='label'>Previous Price:</span>
                <span>₹{ingredient.PreviousPrice}/{ingredient.Unit}</span>
            </div>
            <div class='price-row'>
                <span class='label'>New Price:</span>
                <span>₹{ingredient.MarketPrice}/{ingredient.Unit}</span>
            </div>
            <div class='price-row'>
                <span class='label'>Change:</span>
                <span class='change'>{Math.Abs(changePercentage):F2}% {direction}</span>
            </div>
            <div class='price-row'>
                <span class='label'>Source:</span>
                <span>{ingredient.PriceSource ?? "manual"}</span>
            </div>
            <div class='price-row'>
                <span class='label'>Updated:</span>
                <span>{DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC</span>
            </div>
        </div>
        <p><strong>Action Required:</strong> Review this price change and consider updating menu prices if necessary.</p>
        <p>This is an automated alert triggered when ingredient prices change by more than {MAJOR_PRICE_CHANGE_THRESHOLD}%.</p>
    </div>
</body>
</html>";

            var plainTextContent = $@"
PRICE ALERT NOTIFICATION
========================

Major price change detected for: {ingredient.Name}

Details:
- Ingredient: {ingredient.Name} ({ingredient.Category})
- Previous Price: ₹{ingredient.PreviousPrice}/{ingredient.Unit}
- New Price: ₹{ingredient.MarketPrice}/{ingredient.Unit}
- Change: {Math.Abs(changePercentage):F2}% {direction}
- Source: {ingredient.PriceSource ?? "manual"}
- Updated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC

Action Required: Review this price change and consider updating menu prices if necessary.

This is an automated alert triggered when ingredient prices change by more than {MAJOR_PRICE_CHANGE_THRESHOLD}%.
";

            // Send to default admin email
            var adminEmail = "admin@cafemaatara.com"; // This should come from configuration or admin users in DB
            await _emailService.SendPriceAlertEmailAsync(adminEmail, subject, htmlContent);
            
            _logger.LogInformation($"Price change notification sent for {ingredient.Name} ({changePercentage:F2}% {direction})");
        }

        // DELETE: /api/ingredients/{id}
        [Function("DeleteIngredient")]
        public async Task<IActionResult> DeleteIngredient(
            [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "ingredients/{id}")] HttpRequest req,
            string id)
        {
            try
            {
                _logger.LogInformation($"Deleting ingredient with ID: {id}");

                var deleted = await _mongoService.DeleteIngredientAsync(id);
                if (!deleted)
                {
                    return new NotFoundResult();
                }

                return new OkObjectResult(new { message = "Ingredient deleted successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting ingredient with ID: {id}");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        // GET: /api/ingredients/category/{category}
        [Function("GetIngredientsByCategory")]
        public async Task<IActionResult> GetIngredientsByCategory(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "ingredients/category/{category}")] HttpRequest req,
            string category)
        {
            try
            {
                _logger.LogInformation($"Getting ingredients for category: {category}");

                var ingredients = await _mongoService.GetIngredientsByCategoryAsync(category);
                return new OkObjectResult(ingredients);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting ingredients for category: {category}");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        // POST: /api/ingredients/search
        [Function("SearchIngredients")]
        public async Task<IActionResult> SearchIngredients(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "ingredients/search")] HttpRequest req)
        {
            try
            {
                _logger.LogInformation("Searching ingredients");

                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var searchRequest = JsonSerializer.Deserialize<Dictionary<string, string>>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (searchRequest == null || !searchRequest.ContainsKey("searchTerm"))
                {
                    return new BadRequestObjectResult("Search term is required");
                }

                var searchTerm = InputSanitizer.Sanitize(searchRequest["searchTerm"]);
                var ingredients = await _mongoService.SearchIngredientsAsync(searchTerm);
                return new OkObjectResult(ingredients);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching ingredients");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }
    }
}
