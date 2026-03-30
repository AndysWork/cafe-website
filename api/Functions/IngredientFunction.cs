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

namespace Cafe.Api.Functions
{
    public class IngredientFunction
    {
        private readonly ILogger<IngredientFunction> _logger;
        private readonly IPricingRepository _mongoService;
        private readonly AuthService _authService;
        private readonly IEmailService _emailService;
        private const decimal MAJOR_PRICE_CHANGE_THRESHOLD = 10.0m;

        public IngredientFunction(ILogger<IngredientFunction> logger, IPricingRepository mongoService, AuthService authService, IEmailService emailService)
        {
            _logger = logger;
            _mongoService = mongoService;
            _authService = authService;
            _emailService = emailService;
        }

        // GET: /api/ingredients
        [Function("GetIngredients")]
        [OpenApiOperation(operationId: "GetIngredients", tags: new[] { "Ingredients" }, Summary = "Get all ingredients", Description = "Retrieves all ingredients across all outlets")]
        [OpenApiSecurity("Bearer", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(List<Ingredient>), Description = "Successfully retrieved ingredients")]
        [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.Unauthorized, Description = "User not authenticated")]
        public async Task<HttpResponseData> GetIngredients(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ingredients")] HttpRequestData req)
        {
            try
            {
                var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
                if (!isAuthorized) return errorResponse!;

                _logger.LogInformation("Getting all ingredients");

                var ingredients = await _mongoService.GetAllIngredientsAsync();

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(ingredients);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting ingredients");
                var response = req.CreateResponse(HttpStatusCode.InternalServerError);
                await response.WriteAsJsonAsync(new { error = "An error occurred while getting ingredients" });
                return response;
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
        public async Task<HttpResponseData> GetIngredientById(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ingredients/{id}")] HttpRequestData req,
            string id)
        {
            try
            {
                var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
                if (!isAuthorized) return errorResponse!;

                _logger.LogInformation("Getting ingredient with ID: {Id}", id);

                var outletId = OutletHelper.GetOutletIdFromRequest(req, _authService);
                if (string.IsNullOrEmpty(outletId))
                {
                    var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequest.WriteAsJsonAsync(new { error = "Outlet ID is required" });
                    return badRequest;
                }

                var ingredient = await _mongoService.GetIngredientByIdAsync(id, outletId);
                if (ingredient == null)
                {
                    var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFound.WriteAsJsonAsync(new { error = "Ingredient not found" });
                    return notFound;
                }

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(ingredient);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting ingredient with ID: {Id}", id);
                var response = req.CreateResponse(HttpStatusCode.InternalServerError);
                await response.WriteAsJsonAsync(new { error = "An error occurred while getting the ingredient" });
                return response;
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
        public async Task<HttpResponseData> CreateIngredient(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ingredients")] HttpRequestData req)
        {
            try
            {
                var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
                if (!isAuthorized) return errorResponse!;

                _logger.LogInformation("Creating new ingredient");

                var outletId = OutletHelper.GetOutletIdFromRequest(req, _authService);
                if (string.IsNullOrEmpty(outletId))
                {
                    var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequest.WriteAsJsonAsync(new { error = "Outlet ID is required" });
                    return badRequest;
                }

                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var ingredient = JsonSerializer.Deserialize<Ingredient>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (ingredient == null || string.IsNullOrEmpty(ingredient.Name))
                {
                    var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequest.WriteAsJsonAsync(new { error = "Invalid ingredient data" });
                    return badRequest;
                }

                ingredient.Name = InputSanitizer.Sanitize(ingredient.Name);
                ingredient.Category = InputSanitizer.Sanitize(ingredient.Category);
                ingredient.Unit = InputSanitizer.Sanitize(ingredient.Unit);

                ingredient.OutletId = outletId;
                ingredient.CreatedAt = DateTime.UtcNow;
                ingredient.UpdatedAt = DateTime.UtcNow;
                ingredient.LastUpdated = DateTime.UtcNow;
                ingredient.IsActive = true;

                var createdIngredient = await _mongoService.CreateIngredientAsync(ingredient);

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(createdIngredient);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating ingredient");
                var response = req.CreateResponse(HttpStatusCode.InternalServerError);
                await response.WriteAsJsonAsync(new { error = "An error occurred while creating the ingredient" });
                return response;
            }
        }

        // PUT: /api/ingredients/{id}
        [Function("UpdateIngredient")]
        public async Task<HttpResponseData> UpdateIngredient(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "ingredients/{id}")] HttpRequestData req,
            string id)
        {
            try
            {
                var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
                if (!isAuthorized) return errorResponse!;

                _logger.LogInformation("Updating ingredient with ID: {Id}", id);

                var outletId = OutletHelper.GetOutletIdFromRequest(req, _authService);
                if (string.IsNullOrEmpty(outletId))
                {
                    var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequest.WriteAsJsonAsync(new { error = "Outlet ID is required" });
                    return badRequest;
                }

                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var ingredient = JsonSerializer.Deserialize<Ingredient>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (ingredient == null)
                {
                    var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequest.WriteAsJsonAsync(new { error = "Invalid ingredient data" });
                    return badRequest;
                }

                var currentIngredient = await _mongoService.GetIngredientByIdAsync(id, outletId);
                if (currentIngredient == null)
                {
                    var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFound.WriteAsJsonAsync(new { error = "Ingredient not found" });
                    return notFound;
                }

                ingredient.Name = InputSanitizer.Sanitize(ingredient.Name);
                ingredient.Category = InputSanitizer.Sanitize(ingredient.Category);
                ingredient.Unit = InputSanitizer.Sanitize(ingredient.Unit);

                ingredient.Id = id;
                ingredient.UpdatedAt = DateTime.UtcNow;
                ingredient.LastUpdated = DateTime.UtcNow;

                bool isMajorPriceChange = false;
                decimal priceChangePercentage = 0;
                
                if (currentIngredient.MarketPrice > 0 && ingredient.MarketPrice != currentIngredient.MarketPrice)
                {
                    priceChangePercentage = ((ingredient.MarketPrice - currentIngredient.MarketPrice) / currentIngredient.MarketPrice) * 100;
                    isMajorPriceChange = Math.Abs(priceChangePercentage) >= MAJOR_PRICE_CHANGE_THRESHOLD;
                    
                    ingredient.PreviousPrice = currentIngredient.MarketPrice;
                    ingredient.PriceChangePercentage = priceChangePercentage;

                    await _mongoService.SavePriceHistoryAsync(new IngredientPriceHistory
                    {
                        IngredientId = id,
                        IngredientName = ingredient.Name,
                        Price = ingredient.MarketPrice,
                        Unit = ingredient.Unit,
                        ChangePercentage = priceChangePercentage,
                        Source = ingredient.PriceSource ?? "manual",
                        RecordedAt = DateTime.UtcNow,
                        Notes = $"Updated by admin. Previous: ?{currentIngredient.MarketPrice}"
                    });

                    _logger.LogInformation("Price updated for {Name}: {OldPrice} -> {NewPrice} ({Change:F2}% change)", 
                        ingredient.Name, currentIngredient.MarketPrice, ingredient.MarketPrice, priceChangePercentage);
                }

                var updated = await _mongoService.UpdateIngredientAsync(id, ingredient, outletId);
                if (!updated)
                {
                    var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFound.WriteAsJsonAsync(new { error = "Ingredient not found" });
                    return notFound;
                }

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
                            _logger.LogError(ex, "Failed to send price change notification for {Name}", ingredient.Name);
                        }
                    });
                }

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
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
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating ingredient with ID: {Id}", id);
                var response = req.CreateResponse(HttpStatusCode.InternalServerError);
                await response.WriteAsJsonAsync(new { error = "An error occurred while updating the ingredient" });
                return response;
            }
        }

        private async Task NotifyMajorPriceChangeAsync(Ingredient ingredient, decimal changePercentage)
        {
            var direction = changePercentage > 0 ? "increased" : "decreased";
            var subject = $"?? Major Price Alert: {ingredient.Name}";
            
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
        <h2>?? Price Alert Notification</h2>
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
                <span>?{ingredient.PreviousPrice}/{ingredient.Unit}</span>
            </div>
            <div class='price-row'>
                <span class='label'>New Price:</span>
                <span>?{ingredient.MarketPrice}/{ingredient.Unit}</span>
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

            var adminEmail = "admin@cafemaatara.com";
            await _emailService.SendPriceAlertEmailAsync(adminEmail, subject, htmlContent);
            
            _logger.LogInformation("Price change notification sent for {Name} ({Change:F2}% {Direction})", 
                ingredient.Name, changePercentage, direction);
        }

        // DELETE: /api/ingredients/{id}
        [Function("DeleteIngredient")]
        public async Task<HttpResponseData> DeleteIngredient(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "ingredients/{id}")] HttpRequestData req,
            string id)
        {
            try
            {
                var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
                if (!isAuthorized) return errorResponse!;

                _logger.LogInformation("Deleting ingredient with ID: {Id}", id);

                var outletId = OutletHelper.GetOutletIdFromRequest(req, _authService);
                if (string.IsNullOrEmpty(outletId))
                {
                    var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequest.WriteAsJsonAsync(new { error = "Outlet ID is required" });
                    return badRequest;
                }

                var deleted = await _mongoService.DeleteIngredientAsync(id, outletId);
                if (!deleted)
                {
                    var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFound.WriteAsJsonAsync(new { error = "Ingredient not found" });
                    return notFound;
                }

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new { message = "Ingredient deleted successfully" });
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting ingredient with ID: {Id}", id);
                var response = req.CreateResponse(HttpStatusCode.InternalServerError);
                await response.WriteAsJsonAsync(new { error = "An error occurred while deleting the ingredient" });
                return response;
            }
        }

        // GET: /api/ingredients/category/{category}
        [Function("GetIngredientsByCategory")]
        public async Task<HttpResponseData> GetIngredientsByCategory(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ingredients/category/{category}")] HttpRequestData req,
            string category)
        {
            try
            {
                var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
                if (!isAuthorized) return errorResponse!;

                _logger.LogInformation("Getting ingredients for category: {Category}", category);

                var outletId = OutletHelper.GetOutletIdFromRequest(req, _authService);
                if (string.IsNullOrEmpty(outletId))
                {
                    var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequest.WriteAsJsonAsync(new { error = "Outlet ID is required" });
                    return badRequest;
                }

                var ingredients = await _mongoService.GetIngredientsByCategoryAsync(category, outletId);

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(ingredients);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting ingredients for category: {Category}", category);
                var response = req.CreateResponse(HttpStatusCode.InternalServerError);
                await response.WriteAsJsonAsync(new { error = "An error occurred while getting ingredients by category" });
                return response;
            }
        }

        // POST: /api/ingredients/search
        [Function("SearchIngredients")]
        public async Task<HttpResponseData> SearchIngredients(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ingredients/search")] HttpRequestData req)
        {
            try
            {
                var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _authService);
                if (!isAuthorized) return errorResponse!;

                _logger.LogInformation("Searching ingredients");

                var outletId = OutletHelper.GetOutletIdFromRequest(req, _authService);
                if (string.IsNullOrEmpty(outletId))
                {
                    var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequest.WriteAsJsonAsync(new { error = "Outlet ID is required" });
                    return badRequest;
                }

                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var searchRequest = JsonSerializer.Deserialize<Dictionary<string, string>>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (searchRequest == null || !searchRequest.ContainsKey("searchTerm"))
                {
                    var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequest.WriteAsJsonAsync(new { error = "Search term is required" });
                    return badRequest;
                }

                var searchTerm = InputSanitizer.Sanitize(searchRequest["searchTerm"]);
                var ingredients = await _mongoService.SearchIngredientsAsync(searchTerm, outletId);

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(ingredients);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching ingredients");
                var response = req.CreateResponse(HttpStatusCode.InternalServerError);
                await response.WriteAsJsonAsync(new { error = "An error occurred while searching ingredients" });
                return response;
            }
        }
    }
}