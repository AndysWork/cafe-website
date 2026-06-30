using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Repositories;
using Cafe.Api.Models;
using Cafe.Api.Helpers;
using System.Net;
using System.Linq;

namespace Cafe.Api.Functions;

public class ReviewFunction
{
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
                CreatedAt = MongoService.GetIstNow(),
                UpdatedAt = MongoService.GetIstNow()
            };

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
        if (order.LoyaltyPointsAwarded || order.Status != "delivered") return;

        var issues = await _mongo.GetOrderIssuesAsync(order.Id!);
        var hasOpenIssue = issues.Any(i => i.Status == "open" || i.Status == "in-progress");
        if (hasOpenIssue) return;

        var basePoints = (int)Math.Floor(order.Total * 0.10m);
        var ratingBonus = review.Rating > 0 ? 2 : 0;
        var pointsToAward = Math.Max(0, basePoints + ratingBonus);
        if (pointsToAward <= 0) return;

        await _outbox.EnqueueAsync("LoyaltyPointsAward", "Order", order.Id!,
            new { UserId = order.UserId, Points = pointsToAward, Reason = $"Order #{order.Id} completion", OrderId = order.Id });

        if (!string.IsNullOrEmpty(order.PhoneNumber))
        {
            await _outbox.EnqueueAsync("LoyaltyWhatsApp", "Order", order.Id!,
                new { PhoneNumber = order.PhoneNumber, Username = order.Username ?? "Customer", PointsEarned = pointsToAward, TotalPoints = pointsToAward });
        }

        await _outbox.EnqueueAsync("LoyaltyNotification", "Order", order.Id!,
            new { UserId = order.UserId, PointsEarned = pointsToAward, TotalPoints = pointsToAward, Reason = $"Order #{order.Id?[^6..]}" });

        order.LoyaltyPointsAwarded = true;
        order.LoyaltyPointsAwardedValue = pointsToAward;
        order.UpdatedAt = MongoService.GetIstNow();
        await _mongo.UpdateOrderAsync(order);
    }
}
