using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Repositories;
using Cafe.Api.Models;
using Cafe.Api.Helpers;
using System.Net;
using System.Linq;
using System.ComponentModel.DataAnnotations;

namespace Cafe.Api.Functions;

public class ReviewFunction
{
    private const decimal BillToPointRate = 0.10m; // ₹1 = 0.1 loyalty points
    private const int ReviewWithItemRatingsBonusPoints = 3;

    private readonly IOrderRepository _mongo;
    private readonly AuthService _auth;
    private readonly OutboxService _outbox;
    private readonly ILogger _log;

    public ReviewFunction(IOrderRepository mongo, AuthService auth, OutboxService outbox, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _auth = auth;
        _outbox = outbox;
        _log = loggerFactory.CreateLogger<ReviewFunction>();
    }

    [Function("CreateReview")]
    public async Task<HttpResponseData> CreateReview(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "reviews")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var (request, validationError) = await ValidationHelper.ValidateBody<CreateReviewRequest>(req);
            if (validationError != null) return validationError;

            if (request.Rating < 1 || request.Rating > 5)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Rating must be between 1 and 5" });
                return badReq;
            }

            var order = await _mongo.GetOrderByIdAsync(request.OrderId);
            if (order == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Order not found" });
                return notFound;
            }

            if (order.UserId != userId)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "You can only review your own orders" });
                return forbidden;
            }

            if (order.Status != "delivered")
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "You can only review delivered orders" });
                return badReq;
            }

            var existing = await _mongo.GetReviewByOrderIdAsync(request.OrderId);
            if (existing != null)
            {
                var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                await conflict.WriteAsJsonAsync(new { error = "You have already reviewed this order" });
                return conflict;
            }

            var review = new CustomerReview
            {
                OrderId = request.OrderId,
                UserId = userId!,
                Username = order.Username,
                OutletId = order.OutletId,
                Rating = request.Rating,
                Comment = InputSanitizer.Sanitize(request.Comment ?? ""),
                ItemRatings = BuildItemRatings(order, request.ItemRatings),
                LoyaltyBonusAwarded = false,
                LoyaltyBonusPoints = 0,
                CreatedAt = MongoService.GetIstNow(),
                UpdatedAt = MongoService.GetIstNow()
            };

            if (HasOrderAndItemRatings(order, review))
            {
                review.LoyaltyBonusAwarded = true;
                review.LoyaltyBonusPoints = ReviewWithItemRatingsBonusPoints;
            }

            var created = await _mongo.CreateReviewAsync(review);
            await TryAwardWorkflowLoyaltyPointsAsync(order, created);

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(new
            {
                success = true,
                message = "Review submitted successfully",
                review = new
                {
                    id = created.Id,
                    orderId = created.OrderId,
                    rating = created.Rating,
                    comment = created.Comment,
                        itemRatings = created.ItemRatings,
                        loyaltyBonusAwarded = created.LoyaltyBonusAwarded,
                        loyaltyBonusPoints = created.LoyaltyBonusPoints,
                    username = created.Username,
                    createdAt = created.CreatedAt
                }
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error creating review");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while creating the review" });
            return res;
        }
    }

    [Function("GetReviewByOrder")]
    public async Task<HttpResponseData> GetReviewByOrder(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "reviews/order/{orderId}")] HttpRequestData req,
        string orderId)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var review = await _mongo.GetReviewByOrderIdAsync(orderId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            if (review == null)
            {
                await response.WriteAsJsonAsync(new { exists = false });
            }
            else
            {
                await response.WriteAsJsonAsync(new
                {
                    exists = true,
                    review = new
                    {
                        id = review.Id,
                        orderId = review.OrderId,
                        rating = review.Rating,
                        comment = review.Comment,
                        itemRatings = review.ItemRatings,
                        loyaltyBonusAwarded = review.LoyaltyBonusAwarded,
                        loyaltyBonusPoints = review.LoyaltyBonusPoints,
                        username = review.Username,
                        createdAt = review.CreatedAt
                    }
                });
            }
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting review for order {OrderId}", orderId);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while retrieving the review" });
            return res;
        }
    }

    [Function("GetAllReviews")]
    public async Task<HttpResponseData> GetAllReviews(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "reviews")] HttpRequestData req)
    {
        try
        {
            var queryParams = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var outletId = queryParams["outletId"];
            var page = int.TryParse(queryParams["page"], out var p) ? p : 1;
            var pageSize = int.TryParse(queryParams["pageSize"], out var ps) ? ps : 50;

            var reviews = await _mongo.GetAllReviewsAsync(outletId, page, pageSize);
            var avgRating = await _mongo.GetAverageRatingAsync(outletId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                success = true,
                data = reviews.Select(r => new
                {
                    id = r.Id,
                    orderId = r.OrderId,
                    rating = r.Rating,
                    comment = r.Comment,
                    itemRatings = r.ItemRatings,
                    loyaltyBonusAwarded = r.LoyaltyBonusAwarded,
                    loyaltyBonusPoints = r.LoyaltyBonusPoints,
                    username = r.Username,
                    createdAt = r.CreatedAt
                }),
                averageRating = Math.Round(avgRating, 1),
                count = reviews.Count
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting all reviews");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while retrieving reviews" });
            return res;
        }
    }

    private async Task TryAwardWorkflowLoyaltyPointsAsync(Order order, CustomerReview review)
    {
        if (order.Status != "delivered") return;

        var issues = await _mongo.GetOrderIssuesAsync(order.Id!);
        var hasOpenIssue = issues.Any(i => i.Status == "open" || i.Status == "in-progress");
        if (hasOpenIssue) return;

        var paidBillValue = Math.Max(0m, order.Total);
        var basePoints = (int)Math.Floor(paidBillValue * BillToPointRate);
        var targetPoints = basePoints;
        if (HasOrderAndItemRatings(order, review))
        {
            targetPoints += ReviewWithItemRatingsBonusPoints;
        }
        var alreadyAwarded = Math.Max(0, order.LoyaltyPointsAwardedValue);
        var pointsToAward = targetPoints - alreadyAwarded;
        if (pointsToAward <= 0) return;

        await _outbox.EnqueueAsync("LoyaltyPointsAwardExact", "Order", order.Id!,
            new { UserId = order.UserId, Points = pointsToAward, Reason = $"Order #{order.Id} completion", OrderId = order.Id });

        if (!string.IsNullOrEmpty(order.PhoneNumber))
        {
            await _outbox.EnqueueAsync("LoyaltyWhatsApp", "Order", order.Id!,
                new { PhoneNumber = order.PhoneNumber, Username = order.Username ?? "Customer", PointsEarned = pointsToAward, TotalPoints = pointsToAward });
        }

        await _outbox.EnqueueAsync("LoyaltyNotification", "Order", order.Id!,
            new { UserId = order.UserId, PointsEarned = pointsToAward, TotalPoints = pointsToAward, Reason = $"Order #{order.Id?[^6..]}" });

        order.LoyaltyPointsAwarded = true;
        order.LoyaltyPointsAwardedValue = alreadyAwarded + pointsToAward;
        order.UpdatedAt = MongoService.GetIstNow();
        await _mongo.UpdateOrderAsync(order);
    }

    private static List<ItemRating> BuildItemRatings(Order order, List<CreateItemRatingRequest>? itemRatings)
    {
        if (itemRatings == null || itemRatings.Count == 0)
        {
            return new List<ItemRating>();
        }

        var menuItemMap = order.Items
            .GroupBy(i => i.MenuItemId)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var normalized = itemRatings
            .Where(r => !string.IsNullOrWhiteSpace(r.MenuItemId))
            .GroupBy(r => r.MenuItemId.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .ToList();

        foreach (var rating in normalized)
        {
            if (rating.Rating < 1 || rating.Rating > 5)
            {
                throw new ValidationException("Item rating must be between 1 and 5");
            }

            if (!menuItemMap.ContainsKey(rating.MenuItemId.Trim()))
            {
                throw new ValidationException($"Item rating contains invalid menu item: {rating.MenuItemId}");
            }
        }

        return normalized
            .Select(r =>
            {
                var key = r.MenuItemId.Trim();
                var orderItem = menuItemMap[key];
                return new ItemRating
                {
                    MenuItemId = orderItem.MenuItemId,
                    ItemName = orderItem.Name,
                    Rating = r.Rating
                };
            })
            .ToList();
    }

    private static bool HasOrderAndItemRatings(Order order, CustomerReview review)
    {
        if (review.Rating < 1) return false;

        var uniqueOrderItems = order.Items
            .Select(i => i.MenuItemId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (uniqueOrderItems.Count == 0) return false;

        var ratedItemIds = (review.ItemRatings ?? new List<ItemRating>())
            .Where(r => !string.IsNullOrWhiteSpace(r.MenuItemId) && r.Rating >= 1)
            .Select(r => r.MenuItemId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return uniqueOrderItems.All(id => ratedItemIds.Contains(id));
    }
}
