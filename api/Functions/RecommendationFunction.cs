using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Models;
using Cafe.Api.Helpers;
using System.Net;

namespace Cafe.Api.Functions;

public class RecommendationFunction
{
    private readonly MongoService _mongo;
    private readonly AuthService _auth;
    private readonly ILogger _log;

    public RecommendationFunction(MongoService mongo, AuthService auth, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _auth = auth;
        _log = loggerFactory.CreateLogger<RecommendationFunction>();
    }

    [Function("GetPersonalRecommendations")]
    public async Task<HttpResponseData> GetPersonalRecommendations(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "recommendations")] HttpRequestData req)
    {
        try
        {
            var (isAuthenticated, userId, _, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthenticated) return errorResponse!;

            var outletId = req.Query["outletId"] ?? "default";
            var limitStr = req.Query["limit"];
            int limit = int.TryParse(limitStr, out var l) ? Math.Min(l, 20) : 10;

            // Get user's order history
            var orders = await _mongo.GetUserOrdersAsync(userId!, 1, 50);

            // Get all menu items
            var menuItems = await _mongo.GetMenuAsync(outletId);
            if (!menuItems.Any())
            {
                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new { recommendations = new List<object>(), reason = "No menu items available" });
                return response;
            }

            var recommendations = new List<object>();

            if (orders.Any())
            {
                // Analyze frequently ordered items
                var itemFrequency = orders
                    .SelectMany(o => o.Items)
                    .GroupBy(i => i.Name.ToLower())
                    .OrderByDescending(g => g.Count())
                    .Select(g => new { Name = g.Key, Count = g.Count() })
                    .ToList();

                var favoriteCategories = orders
                    .SelectMany(o => o.Items)
                    .Where(i => !string.IsNullOrWhiteSpace(i.CategoryName))
                    .GroupBy(i => i.CategoryName!)
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Key)
                    .Take(3)
                    .ToList();

                // Already ordered items (re-order suggestions)
                var reorderSuggestions = itemFrequency
                    .Take(3)
                    .Select(f =>
                    {
                        var menuItem = menuItems.FirstOrDefault(m =>
                            m.Name.Equals(f.Name, StringComparison.OrdinalIgnoreCase));
                        return menuItem != null ? new
                        {
                            id = menuItem.Id,
                            name = menuItem.Name,
                            price = menuItem.OnlinePrice,
                            imageUrl = menuItem.ImageUrl,
                            category = menuItem.Category,
                            reason = $"You've ordered this {f.Count} times",
                            type = "reorder"
                        } : null;
                    })
                    .Where(x => x != null)
                    .ToList();

                recommendations.AddRange(reorderSuggestions!);

                // Category-based recommendations (items from fav categories not yet ordered)
                var orderedNames = itemFrequency.Select(f => f.Name).ToHashSet();
                var categoryRecs = menuItems
                    .Where(m => favoriteCategories.Contains(m.Category ?? "") &&
                                !orderedNames.Contains(m.Name.ToLower()) &&
                                m.IsAvailable)
                    .Take(4)
                    .Select(m => new
                    {
                        id = m.Id,
                        name = m.Name,
                        price = m.OnlinePrice,
                        imageUrl = m.ImageUrl,
                        category = m.Category,
                        reason = $"Popular in {m.Category} (you love this category!)",
                        type = "category"
                    });

                recommendations.AddRange(categoryRecs);
            }

            // Time-based recommendations
            var hour = MongoService.GetIstNow().Hour;
            string mealTime = hour switch
            {
                >= 6 and < 11 => "breakfast",
                >= 11 and < 15 => "lunch",
                >= 15 and < 18 => "snacks",
                >= 18 and < 22 => "dinner",
                _ => "late-night"
            };

            var timeRecs = menuItems
                .Where(m => m.IsAvailable &&
                            (m.Category?.Contains(mealTime, StringComparison.OrdinalIgnoreCase) == true ||
                             m.Description?.Contains(mealTime, StringComparison.OrdinalIgnoreCase) == true))
                .Take(3)
                .Select(m => new
                {
                    id = m.Id,
                    name = m.Name,
                    price = m.OnlinePrice,
                    imageUrl = m.ImageUrl,
                    category = m.Category,
                    reason = $"Great for {mealTime}!",
                    type = "time_based"
                });

            recommendations.AddRange(timeRecs);

            // Popular items (if not enough recommendations)
            if (recommendations.Count < limit)
            {
                var existingIds = recommendations.Select(r => ((dynamic)r).id?.ToString()).ToHashSet();
                var popular = menuItems
                    .Where(m => m.IsAvailable && !existingIds.Contains(m.Id))
                    .Take(limit - recommendations.Count)
                    .Select(m => new
                    {
                        id = m.Id,
                        name = m.Name,
                        price = m.OnlinePrice,
                        imageUrl = m.ImageUrl,
                        category = m.Category,
                        reason = "Popular choice",
                        type = "popular"
                    });

                recommendations.AddRange(popular);
            }

            var result = req.CreateResponse(HttpStatusCode.OK);
            await result.WriteAsJsonAsync(new
            {
                recommendations = recommendations.Take(limit),
                mealTime,
                totalSuggestions = recommendations.Count
            });
            return result;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting recommendations");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("GetTrendingItems")]
    public async Task<HttpResponseData> GetTrendingItems(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "recommendations/trending")] HttpRequestData req)
    {
        try
        {
            var outletId = req.Query["outletId"] ?? "default";
            var menuItems = await _mongo.GetMenuAsync(outletId);

            var trending = menuItems
                .Where(m => m.IsAvailable)
                .Take(10)
                .Select(m => new
                {
                    id = m.Id,
                    name = m.Name,
                    price = m.OnlinePrice,
                    imageUrl = m.ImageUrl,
                    category = m.Category
                });

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(trending);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting trending items");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }
}
