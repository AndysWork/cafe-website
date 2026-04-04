using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Repositories;
using Cafe.Api.Models;
using Cafe.Api.Helpers;
using System.Net;

namespace Cafe.Api.Functions;

public class SubscriptionFunction
{
    private readonly IOperationsRepository _mongo;
    private readonly AuthService _auth;
    private readonly ILogger _log;

    public SubscriptionFunction(IOperationsRepository mongo, AuthService auth, ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _auth = auth;
        _log = loggerFactory.CreateLogger<SubscriptionFunction>();
    }

    [Function("GetSubscriptionPlans")]
    public async Task<HttpResponseData> GetSubscriptionPlans(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "subscriptions/plans")] HttpRequestData req)
    {
        try
        {
            var outletId = req.Query["outletId"] ?? "default";
            var plans = await _mongo.GetSubscriptionPlansAsync(outletId);
            var active = plans.Where(p => p.IsActive).ToList();

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(active);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting subscription plans");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("GetAllSubscriptionPlans")]
    public async Task<HttpResponseData> GetAllSubscriptionPlans(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/subscriptions/plans")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);
            var plans = await _mongo.GetSubscriptionPlansAsync(outletId ?? "default");

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(plans);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting subscription plans");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("CreateSubscriptionPlan")]
    public async Task<HttpResponseData> CreateSubscriptionPlan(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/subscriptions/plans")] HttpRequestData req)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var (request, validationError) = await ValidationHelper.ValidateBody<CreateSubscriptionPlanRequest>(req);
            if (validationError != null) return validationError;

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);

            var plan = new SubscriptionPlan
            {
                OutletId = outletId ?? "default",
                Name = InputSanitizer.Sanitize(request.Name),
                Description = request.Description != null ? InputSanitizer.Sanitize(request.Description) : null,
                Price = request.Price,
                DurationDays = request.DurationDays,
                Benefits = request.Benefits?.Select(InputSanitizer.Sanitize).ToList() ?? new List<string>(),
                FreeDelivery = request.FreeDelivery,
                DiscountPercent = request.DiscountPercent,
                DailyItemLimit = request.DailyItemLimit,
                IncludedItems = request.IncludedItems?.Select(i => new SubscriptionItem
                {
                    MenuItemId = i.MenuItemId,
                    MenuItemName = InputSanitizer.Sanitize(i.MenuItemName),
                    DailyQuantity = i.DailyQuantity
                }).ToList(),
                IsActive = true,
                CreatedAt = MongoService.GetIstNow()
            };

            await _mongo.CreateSubscriptionPlanAsync(plan);

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(plan);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error creating subscription plan");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("UpdateSubscriptionPlan")]
    public async Task<HttpResponseData> UpdateSubscriptionPlan(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "manage/subscriptions/plans/{id}")] HttpRequestData req, string id)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var (request, validationError) = await ValidationHelper.ValidateBody<CreateSubscriptionPlanRequest>(req);
            if (validationError != null) return validationError;

            var existing = await _mongo.GetSubscriptionPlanByIdAsync(id);
            if (existing == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Subscription plan not found" });
                return notFound;
            }

            existing.Name = InputSanitizer.Sanitize(request.Name);
            existing.Description = request.Description != null ? InputSanitizer.Sanitize(request.Description) : null;
            existing.Price = request.Price;
            existing.DurationDays = request.DurationDays;
            existing.Benefits = request.Benefits?.Select(InputSanitizer.Sanitize).ToList() ?? new List<string>();
            existing.FreeDelivery = request.FreeDelivery;
            existing.DiscountPercent = request.DiscountPercent;
            existing.DailyItemLimit = request.DailyItemLimit;
            existing.IncludedItems = request.IncludedItems?.Select(i => new SubscriptionItem
            {
                MenuItemId = i.MenuItemId,
                MenuItemName = InputSanitizer.Sanitize(i.MenuItemName),
                DailyQuantity = i.DailyQuantity
            }).ToList();

            await _mongo.UpdateSubscriptionPlanAsync(id, existing);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(existing);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error updating subscription plan");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("DeleteSubscriptionPlan")]
    public async Task<HttpResponseData> DeleteSubscriptionPlan(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "manage/subscriptions/plans/{id}")] HttpRequestData req, string id)
    {
        try
        {
            var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            try
            {
                await _mongo.DeleteSubscriptionPlanAsync(id);
            }
            catch (InvalidOperationException iex)
            {
                var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                await conflict.WriteAsJsonAsync(new { error = iex.Message });
                return conflict;
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Subscription plan deleted" });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error deleting subscription plan");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("Subscribe")]
    public async Task<HttpResponseData> Subscribe(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "subscriptions/subscribe")] HttpRequestData req)
    {
        try
        {
            var (isAuthenticated, userId, _, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthenticated) return errorResponse!;

            var (request, validationError) = await ValidationHelper.ValidateBody<SubscribeRequest>(req);
            if (validationError != null) return validationError;

            // Check plan exists
            var plans = await _mongo.GetSubscriptionPlansAsync("default");
            var plan = plans.FirstOrDefault(p => p.Id == request.PlanId && p.IsActive);
            if (plan == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Subscription plan not found or inactive" });
                return notFound;
            }

            // Check no active subscription
            var existing = await _mongo.GetActiveSubscriptionAsync(userId!);
            if (existing != null)
            {
                var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                await conflict.WriteAsJsonAsync(new { error = "You already have an active subscription", currentPlan = existing });
                return conflict;
            }

            var now = MongoService.GetIstNow();
            var sub = new CustomerSubscription
            {
                UserId = userId!,
                PlanId = request.PlanId,
                StartDate = now,
                EndDate = now.AddDays(plan.DurationDays),
                Status = "active",
                AmountPaid = plan.Price,
                CreatedAt = now
            };
            var subscription = await _mongo.CreateCustomerSubscriptionAsync(sub);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                message = $"Subscribed to {plan.Name}! Valid until {subscription.EndDate:dd MMM yyyy}",
                subscription
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error subscribing");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }

    [Function("GetMySubscription")]
    public async Task<HttpResponseData> GetMySubscription(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "subscriptions/my")] HttpRequestData req)
    {
        try
        {
            var (isAuthenticated, userId, _, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthenticated) return errorResponse!;

            var active = await _mongo.GetActiveSubscriptionAsync(userId!);
            var history = await _mongo.GetUserSubscriptionsAsync(userId!);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { active, history });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting subscription");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred" });
            return res;
        }
    }
}
