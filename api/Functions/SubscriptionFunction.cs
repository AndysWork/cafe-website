using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Cafe.Api.Services;
using Cafe.Api.Repositories;
using Cafe.Api.Models;
using Cafe.Api.Helpers;
using System.Net;

namespace Cafe.Api.Functions;

public partial class SubscriptionFunction
{
    private readonly IOperationsRepository _mongo;
    private readonly IMenuRepository _menuRepo;
    private readonly AuthService _auth;
    private readonly ILogger _log;

    public SubscriptionFunction(
        IOperationsRepository mongo,
        IMenuRepository menuRepo,
        AuthService auth,
        ILoggerFactory loggerFactory)
    {
        _mongo = mongo;
        _menuRepo = menuRepo;
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
            var plans = await _mongo.GetSubscriptionPlansAsync(outletId, activeOnly: true);

            // Auto-seed industry standard curated plans if catalog is currently empty
            if (plans.Count == 0 && (await _mongo.GetSubscriptionPlansAsync(outletId)).Count == 0)
            {
                plans = await SeedDefaultCuratedPlansAsync(outletId);
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(plans);
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error getting subscription plans");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while loading subscription plans" });
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
            var itemsError = await ValidatePlanItems(req, request.IncludedItems);
            if (itemsError != null) return itemsError;

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);

            var plan = new SubscriptionPlan
            {
                OutletId = outletId ?? "default",
                Name = InputSanitizer.Sanitize(request.Name),
                Description = request.Description != null ? InputSanitizer.Sanitize(request.Description) : string.Empty,
                Category = string.IsNullOrWhiteSpace(request.Category) ? "all-day" : request.Category.Trim().ToLowerInvariant(),
                Price = request.Price,
                PricingMode = request.PricingMode,
                IsDraft = request.IsDraft,
                DurationDays = request.DurationDays > 0 ? request.DurationDays : 30,
                Benefits = request.Benefits?.Select(InputSanitizer.Sanitize).ToList() ?? new List<string>(),
                FreeDelivery = request.FreeDelivery,
                DiscountPercent = SubscriptionTerms.ForDuration(request.DurationDays).DiscountPercent,
                DailyItemLimit = request.DailyItemLimit,
                BadgeText = request.BadgeText,
                ImageUrl = request.ImageUrl,
                IncludedItems = CopyPlanItems(request.IncludedItems),
                IsActive = request.IsActive && !request.IsDraft,
                CreatedAt = MongoService.GetIstNow()
            };

            await _mongo.CreateSubscriptionPlanAsync(plan);

            var response = req.CreateResponse(HttpStatusCode.Created);
            response.Headers.Add("Location", $"/api/manage/subscriptions/plans/{plan.Id}");
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
            var itemsError = await ValidatePlanItems(req, request.IncludedItems);
            if (itemsError != null) return itemsError;

            var existing = await _mongo.GetSubscriptionPlanByIdAsync(id);
            if (existing == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Subscription plan not found" });
                return notFound;
            }

            existing.Name = InputSanitizer.Sanitize(request.Name);
            existing.Description = request.Description != null ? InputSanitizer.Sanitize(request.Description) : string.Empty;
            existing.Category = string.IsNullOrWhiteSpace(request.Category) ? existing.Category : request.Category.Trim().ToLowerInvariant();
            existing.Price = request.Price;
            existing.PricingMode = request.PricingMode;
            existing.IsDraft = request.IsDraft;
            existing.IsActive = request.IsActive && !request.IsDraft;
            existing.DurationDays = request.DurationDays > 0 ? request.DurationDays : 30;
            existing.Benefits = request.Benefits?.Select(InputSanitizer.Sanitize).ToList() ?? new List<string>();
            existing.FreeDelivery = request.FreeDelivery;
            existing.DiscountPercent = SubscriptionTerms.ForDuration(request.DurationDays).DiscountPercent;
            existing.DailyItemLimit = request.DailyItemLimit;
            existing.BadgeText = request.BadgeText;
            existing.ImageUrl = request.ImageUrl;
            existing.IncludedItems = CopyPlanItems(request.IncludedItems);

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

    /// <summary>
    /// Subscribes to a curated meal plan.
    /// </summary>
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

            var existingSubscriptions = await _mongo.GetUserSubscriptionsAsync(userId!);
            if (existingSubscriptions.Any(s => s.Status is "active" or "paused" or "pending_payment"))
            {
                var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                await conflict.WriteAsJsonAsync(new { error = "You already have an active or pending subscription" });
                return conflict;
            }

            var plan = await _mongo.GetSubscriptionPlanByIdAsync(request.PlanId);
            if (plan == null || !plan.IsActive || plan.IsDraft)
            {
                var plans = await _mongo.GetSubscriptionPlansAsync(request.OutletId ?? "default", activeOnly: true);
                plan = plans.FirstOrDefault(p => p.Id == request.PlanId);
            }

            if (plan == null || !plan.IsActive || plan.IsDraft)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Subscription plan not found or inactive" });
                return notFound;
            }

            var durationDays = request.DurationDays ?? plan.DurationDays;
            if (!SubscriptionTerms.IsSupportedDuration(durationDays))
            {
                var invalid = req.CreateResponse(HttpStatusCode.BadRequest);
                await invalid.WriteAsJsonAsync(new { error = "Choose a subscription duration of 7, 14 or 30 days." });
                return invalid;
            }

            // Calculate price based on duration
            decimal dailyRate = plan.DurationDays > 0 ? (plan.Price / plan.DurationDays) : (plan.Price / 30m);
            decimal basePrice = Math.Round(dailyRate * durationDays, 2, MidpointRounding.AwayFromZero);
            decimal discountPct = SubscriptionTerms.ForDuration(durationDays).DiscountPercent;
            decimal discountAmt = Math.Round(basePrice * (discountPct / 100m), 2, MidpointRounding.AwayFromZero);
            decimal finalAmount = basePrice - discountAmt;

            var now = MongoService.GetIstNow();
            var sub = new CustomerSubscription
            {
                UserId = userId!,
                CustomerName = InputSanitizer.Sanitize(request.CustomerName ?? string.Empty),
                CustomerPhone = InputSanitizer.Sanitize(request.CustomerPhone ?? string.Empty),
                SubscriptionType = "curated_plan",
                PlanId = plan.Id,
                PlanName = plan.Name,
                OutletId = string.IsNullOrWhiteSpace(request.OutletId) ? plan.OutletId : request.OutletId.Trim(),
                Items = CopyPlanItems(plan.IncludedItems),
                DeliveryTimeSlot = string.IsNullOrWhiteSpace(request.DeliveryTimeSlot) ? "12:30 PM - 2:00 PM" : request.DeliveryTimeSlot.Trim(),
                DeliveryDays = request.DeliveryDays != null && request.DeliveryDays.Count > 0 ? request.DeliveryDays : new List<string> { "Everyday" },
                DeliveryAddress = InputSanitizer.Sanitize(request.DeliveryAddress ?? string.Empty),
                SpecialInstructions = request.SpecialInstructions != null ? InputSanitizer.Sanitize(request.SpecialInstructions) : null,
                StartDate = now,
                EndDate = now.AddDays(durationDays),
                DurationDays = durationDays,
                Status = "pending_payment",
                DailySubtotal = dailyRate,
                DiscountPercent = discountPct,
                DiscountAmount = discountAmt,
                AmountPaid = 0,
                AmountDue = finalAmount,
                FreeDelivery = plan.FreeDelivery,
                PaymentMethod = string.IsNullOrWhiteSpace(request.PaymentMethod) ? "upi-qr" : request.PaymentMethod,
                PaymentStatus = "pending",
                CreatedAt = now,
                UpdatedAt = now
            };

            var subscription = await _mongo.CreateCustomerSubscriptionAsync(sub);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                message = $"Subscription request created for {plan.Name}. It will activate after staff confirms payment.",
                subscription
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error subscribing to curated plan");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while creating your subscription" });
            return res;
        }
    }

    /// <summary>
    /// Creates a customized recurring combo subscription with items hand-picked by the customer.
    /// </summary>
    [Function("CreateCustomComboSubscription")]
    public async Task<HttpResponseData> CreateCustomComboSubscription(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "subscriptions/custom-combo")] HttpRequestData req)
    {
        try
        {
            var (isAuthenticated, userId, _, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthenticated) return errorResponse!;

            var (request, validationError) = await ValidationHelper.ValidateBody<CreateCustomComboSubscriptionRequest>(req);
            if (validationError != null) return validationError;

            var existingSubscriptions = await _mongo.GetUserSubscriptionsAsync(userId!);
            if (existingSubscriptions.Any(s => s.Status is "active" or "paused" or "pending_payment"))
            {
                var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                await conflict.WriteAsJsonAsync(new { error = "You already have an active or pending subscription" });
                return conflict;
            }

            if (request.Items == null || request.Items.Count == 0)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Please select at least one menu item for your meal combo." });
                return badReq;
            }

            var itemIds = request.Items.Select(x => x.MenuItemId).Distinct().ToList();
            var menuItems = await _menuRepo.GetMenuItemsByIdsAsync(itemIds, request.OutletId);

            var subscriptionItems = new List<SubscriptionItem>();
            decimal dailySubtotal = 0m;

            foreach (var reqItem in request.Items)
            {
                var mItem = menuItems.FirstOrDefault(m => m.Id == reqItem.MenuItemId);
                if (mItem == null || !mItem.IsAvailable) continue;

                var unitPrice = mItem.OnlinePrice > 0 ? mItem.OnlinePrice : (mItem.WebPrice > 0 ? mItem.WebPrice : mItem.ShopSellingPrice);
                var qty = Math.Clamp(reqItem.Quantity, 1, 10);
                dailySubtotal += (unitPrice * qty);

                subscriptionItems.Add(new SubscriptionItem
                {
                    MenuItemId = mItem.Id,
                    MenuItemName = mItem.Name,
                    UnitPrice = unitPrice,
                    DailyQuantity = qty,
                    CategoryName = mItem.Category,
                    ImageUrl = mItem.ImageUrl,
                    IsVeg = string.Equals(mItem.DietaryType, "veg", StringComparison.OrdinalIgnoreCase) ||
                            (mItem.Name != null && mItem.Name.ToLowerInvariant().Contains("veg"))
                });
            }

            if (subscriptionItems.Count == 0)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = "Selected menu items could not be found or are unavailable." });
                return badReq;
            }

            var durationDays = request.DurationDays;
            var deliveryDays = request.DeliveryDays != null && request.DeliveryDays.Count > 0
                ? request.DeliveryDays
                : new List<string> { "Everyday" };

            // Calculate active delivery count across the duration
            int daysPerWeek = deliveryDays.Contains("Everyday")
                ? 7
                : deliveryDays.Distinct().Count();
            if (daysPerWeek == 0) daysPerWeek = 7;

            int totalDrops = (int)Math.Round((durationDays / 7.0) * daysPerWeek, MidpointRounding.AwayFromZero);
            if (totalDrops < 1) totalDrops = 1;

            decimal grossAmount = dailySubtotal * totalDrops;

            decimal discountPercent = SubscriptionTerms.ForDuration(durationDays).DiscountPercent;
            decimal discountAmount = Math.Round(grossAmount * (discountPercent / 100m), 2, MidpointRounding.AwayFromZero);
            decimal finalPayable = Math.Round(grossAmount - discountAmount, 2);

            var now = MongoService.GetIstNow();
            var comboSub = new CustomerSubscription
            {
                UserId = userId!,
                CustomerName = InputSanitizer.Sanitize(request.CustomerName ?? string.Empty),
                CustomerPhone = InputSanitizer.Sanitize(request.CustomerPhone ?? string.Empty),
                SubscriptionType = "custom_combo",
                PlanName = string.IsNullOrWhiteSpace(request.ComboName) ? "Custom Meal Combo" : InputSanitizer.Sanitize(request.ComboName),
                OutletId = string.IsNullOrWhiteSpace(request.OutletId) ? "default" : request.OutletId.Trim(),
                Items = subscriptionItems,
                DeliveryTimeSlot = string.IsNullOrWhiteSpace(request.DeliveryTimeSlot) ? "12:30 PM - 2:00 PM" : request.DeliveryTimeSlot.Trim(),
                DeliveryDays = deliveryDays,
                DeliveryAddress = InputSanitizer.Sanitize(request.DeliveryAddress ?? string.Empty),
                SpecialInstructions = request.SpecialInstructions != null ? InputSanitizer.Sanitize(request.SpecialInstructions) : null,
                StartDate = now,
                EndDate = now.AddDays(durationDays),
                DurationDays = durationDays,
                Status = "pending_payment",
                DailySubtotal = dailySubtotal,
                DiscountPercent = discountPercent,
                DiscountAmount = discountAmount,
                AmountPaid = 0,
                AmountDue = finalPayable,
                FreeDelivery = true,
                PaymentMethod = string.IsNullOrWhiteSpace(request.PaymentMethod) ? "upi-qr" : request.PaymentMethod,
                PaymentStatus = "pending",
                CreatedAt = now,
                UpdatedAt = now
            };

            var created = await _mongo.CreateCustomerSubscriptionAsync(comboSub);

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(new
            {
                message = $"Your custom recurring combo '{comboSub.PlanName}' is awaiting payment confirmation.",
                subscription = created
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error creating custom combo subscription");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while creating your custom combo subscription." });
            return res;
        }
    }

    [Function("GetPendingSubscriptionPayments")]
    public async Task<HttpResponseData> GetPendingSubscriptionPayments(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "manage/subscriptions/payments/pending")] HttpRequestData req)
    {
        var (isAuthorized, _, _, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
        if (!isAuthorized) return errorResponse!;

        var outletId = req.Query["outletId"]?.Trim();
        var subscriptions = await _mongo.GetCustomerSubscriptionsAsync(outletId, "pending");
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(subscriptions);
        return response;
    }

    [Function("ConfirmSubscriptionPayment")]
    public async Task<HttpResponseData> ConfirmSubscriptionPayment(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "manage/subscriptions/{id}/payment/confirm")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthorized, userId, _, errorResponse) = await AuthorizationHelper.ValidateAdminOrManagerRole(req, _auth);
            if (!isAuthorized) return errorResponse!;

            var (request, validationError) = await ValidationHelper.ValidateBody<ConfirmSubscriptionPaymentRequest>(req);
            if (validationError != null) return validationError;

            var subscription = await _mongo.GetCustomerSubscriptionByIdAsync(id);
            if (subscription == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Subscription not found" });
                return notFound;
            }

            if (subscription.PaymentStatus == "paid")
            {
                var ok = req.CreateResponse(HttpStatusCode.OK);
                await ok.WriteAsJsonAsync(new { message = "Payment already confirmed", subscription });
                return ok;
            }

            if (subscription.Status != "pending_payment" || subscription.PaymentStatus != "pending")
            {
                var conflict = req.CreateResponse(HttpStatusCode.Conflict);
                await conflict.WriteAsJsonAsync(new { error = "Subscription is not awaiting payment confirmation" });
                return conflict;
            }

            var now = MongoService.GetIstNow();
            subscription.PaymentStatus = "paid";
            subscription.PaymentReference = request.PaymentReference?.Trim();
            subscription.PaymentConfirmedBy = userId;
            subscription.PaymentConfirmedAt = now;
            subscription.AmountPaid = subscription.AmountDue;
            subscription.Status = "active";
            subscription.StartDate = now;
            subscription.EndDate = now.AddDays(subscription.DurationDays);
            subscription.UpdatedAt = now;

            if (!await _mongo.UpdateCustomerSubscriptionAsync(id, subscription))
                return await SubscriptionChanged(req);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { message = "Subscription payment confirmed and plan activated", subscription });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error confirming subscription payment {SubscriptionId}", id);
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { error = "Failed to confirm subscription payment" });
            return response;
        }
    }

    /// <summary>
    /// Pauses an active customer subscription (e.g. customer going out of town).
    /// </summary>
    [Function("PauseSubscription")]
    public async Task<HttpResponseData> PauseSubscription(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "subscriptions/{id}/pause")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthenticated, userId, role, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthenticated) return errorResponse!;

            var (request, validationError) = await ValidationHelper.ValidateBody<PauseSubscriptionRequest>(req);
            if (validationError != null) return validationError;

            var sub = await _mongo.GetCustomerSubscriptionByIdAsync(id);
            if (sub == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Subscription not found" });
                return notFound;
            }

            if (role != "admin" && sub.UserId != userId)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "You are not authorized to pause this subscription." });
                return forbidden;
            }

            var now = MongoService.GetIstNow();
            var pauseError = SubscriptionPausePolicy.ValidatePause(sub, request.Days, now);
            if (pauseError != null)
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = pauseError });
                return badReq;
            }

            SubscriptionPausePolicy.Pause(sub, request.Days, now);
            if (!await _mongo.UpdateCustomerSubscriptionAsync(id, sub))
                return await SubscriptionChanged(req);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                message = $"Deliveries paused for {request.Days} day(s). They will resume automatically; you can also resume early. Your end date will extend by the pause days used.",
                subscription = sub
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error pausing subscription {Id}", id);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while pausing subscription" });
            return res;
        }
    }

    /// <summary>
    /// Resumes a paused subscription and automatically extends the end date by the paused duration.
    /// </summary>
    [Function("ResumeSubscription")]
    public async Task<HttpResponseData> ResumeSubscription(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "subscriptions/{id}/resume")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthenticated, userId, role, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthenticated) return errorResponse!;

            var sub = await _mongo.GetCustomerSubscriptionByIdAsync(id);
            if (sub == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Subscription not found" });
                return notFound;
            }

            if (role != "admin" && sub.UserId != userId)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "You are not authorized to resume this subscription." });
                return forbidden;
            }

            if (sub.Status != "paused")
            {
                if (sub.Status == "active" && sub.TotalDaysPaused > 0)
                {
                    var alreadyResumed = req.CreateResponse(HttpStatusCode.OK);
                    await alreadyResumed.WriteAsJsonAsync(new { message = "Your subscription has already resumed.", subscription = sub });
                    return alreadyResumed;
                }
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = $"Subscription is not paused (current status: {sub.Status})." });
                return badReq;
            }

            var now = MongoService.GetIstNow();
            var pausedDays = SubscriptionPausePolicy.Resume(sub, now);

            if (!await _mongo.UpdateCustomerSubscriptionAsync(id, sub))
                return await SubscriptionChanged(req);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                message = $"Your subscription was extended by {pausedDays} day(s) until {sub.EndDate:dd MMM yyyy}. Current status: {sub.Status}.",
                subscription = sub
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error resuming subscription {Id}", id);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while resuming subscription" });
            return res;
        }
    }

    /// <summary>
    /// Cancels an active or paused subscription.
    /// </summary>
    [Function("CancelSubscription")]
    public async Task<HttpResponseData> CancelSubscription(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "subscriptions/{id}/cancel")] HttpRequestData req,
        string id)
    {
        try
        {
            var (isAuthenticated, userId, role, errorResponse) = await AuthorizationHelper.ValidateAuthenticatedUser(req, _auth);
            if (!isAuthenticated) return errorResponse!;

            var sub = await _mongo.GetCustomerSubscriptionByIdAsync(id);
            if (sub == null)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Subscription not found" });
                return notFound;
            }

            if (role != "admin" && sub.UserId != userId)
            {
                var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbidden.WriteAsJsonAsync(new { error = "You are not authorized to cancel this subscription." });
                return forbidden;
            }

            sub.Status = "cancelled";
            sub.PausedAt = null;
            sub.PauseEndsAtUtc = null;
            if (!await _mongo.UpdateCustomerSubscriptionAsync(id, sub))
                return await SubscriptionChanged(req);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                message = "Subscription has been cancelled.",
                subscription = sub
            });
            return response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error cancelling subscription {Id}", id);
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while cancelling subscription" });
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
            _log.LogError(ex, "Error getting user subscriptions");
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteAsJsonAsync(new { error = "An error occurred while retrieving your subscriptions." });
            return res;
        }
    }

    private static async Task<HttpResponseData> SubscriptionChanged(HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.Conflict);
        await response.WriteAsJsonAsync(new { error = "This subscription changed. Please refresh and try again." });
        return response;
    }

    private async Task<List<SubscriptionPlan>> SeedDefaultCuratedPlansAsync(string outletId)
    {
        var defaultPlans = new List<SubscriptionPlan>
        {
            new SubscriptionPlan
            {
                OutletId = outletId,
                Name = "Rise & Shine Breakfast Combo",
                Description = "Kickstart your morning with fresh artisan coffee or special tea paired with warm sandwiches or handcrafted toasts.",
                Category = "breakfast",
                Price = 2999m,
                DurationDays = 30,
                BadgeText = "Morning Favorite",
                Benefits = new List<string> { "Free Daily Doorstep Delivery", "Daily Breakfast Time Window (8:00 AM - 10:00 AM)", "10% Subscription Discount", "Up to 2 Pauses, 7 Days Each" },
                FreeDelivery = true,
                DiscountPercent = SubscriptionTerms.ForDuration(30).DiscountPercent,
                DailyItemLimit = 2,
                IncludedItems = new List<SubscriptionItem>
                {
                    new SubscriptionItem { MenuItemId = "bf_coffee", MenuItemName = "Artisan Cappuccino / Masala Chai", DailyQuantity = 1, UnitPrice = 80m, IsVeg = true },
                    new SubscriptionItem { MenuItemId = "bf_sandwich", MenuItemName = "Grilled Cheese Toast / Veg Club Sandwich", DailyQuantity = 1, UnitPrice = 120m, IsVeg = true }
                },
                IsActive = true
            },
            new SubscriptionPlan
            {
                OutletId = outletId,
                Name = "Executive Daily Lunch Box",
                Description = "Wholesome, home-style chef-curated lunch delivered hot & fresh to your desk or home every day.",
                Category = "lunch",
                Price = 4499m,
                DurationDays = 30,
                BadgeText = "Most Popular",
                Benefits = new List<string> { "Guaranteed Lunch Delivery (12:30 PM - 2:00 PM)", "Free Fast Delivery", "Chef's Rotating Daily Specials", "Zero Hassle Daily Ordering" },
                FreeDelivery = true,
                DiscountPercent = SubscriptionTerms.ForDuration(30).DiscountPercent,
                DailyItemLimit = 3,
                IncludedItems = new List<SubscriptionItem>
                {
                    new SubscriptionItem { MenuItemId = "lunch_main", MenuItemName = "Gourmet Rice Bowl / Paneer Wrap", DailyQuantity = 1, UnitPrice = 160m, IsVeg = true },
                    new SubscriptionItem { MenuItemId = "lunch_bev", MenuItemName = "Refreshing Mint Mojito / Iced Tea", DailyQuantity = 1, UnitPrice = 90m, IsVeg = true },
                    new SubscriptionItem { MenuItemId = "lunch_sweet", MenuItemName = "Artisanal Brownie / Sweet Treat", DailyQuantity = 1, UnitPrice = 60m, IsVeg = true }
                },
                IsActive = true
            },
            new SubscriptionPlan
            {
                OutletId = outletId,
                Name = "High Tea & Artisanal Snacks",
                Description = "The ultimate 4 PM recharge. Piping hot signature tea or cold brews served with crispy savory snacks.",
                Category = "snacks",
                Price = 2499m,
                DurationDays = 30,
                BadgeText = "Great Value",
                Benefits = new List<string> { "Evening Break Window (4:00 PM - 5:30 PM)", "Free Daily Delivery", "Snack Variety Everyday", "Work & Study Essential" },
                FreeDelivery = true,
                DiscountPercent = SubscriptionTerms.ForDuration(30).DiscountPercent,
                DailyItemLimit = 2,
                IncludedItems = new List<SubscriptionItem>
                {
                    new SubscriptionItem { MenuItemId = "tea_snack_1", MenuItemName = "Signature Ginger Cardamom Chai / Cold Coffee", DailyQuantity = 1, UnitPrice = 75m, IsVeg = true },
                    new SubscriptionItem { MenuItemId = "tea_snack_2", MenuItemName = "Crispy French Fries / Fresh Baked Cookies", DailyQuantity = 1, UnitPrice = 90m, IsVeg = true }
                },
                IsActive = true
            },
            new SubscriptionPlan
            {
                OutletId = outletId,
                Name = "Gourmet Dinner Feast Box",
                Description = "Delicious, restaurant-quality dinners delivered hot so you can relax after a long day without kitchen stress.",
                Category = "dinner",
                Price = 5499m,
                DurationDays = 30,
                BadgeText = "Chef's Choice",
                Benefits = new List<string> { "Dinner Slot (7:30 PM - 9:30 PM)", "Free Priority Delivery", "Gourmet Multi-Course Meals", "Up to 2 Pauses, 7 Days Each" },
                FreeDelivery = true,
                DiscountPercent = SubscriptionTerms.ForDuration(30).DiscountPercent,
                DailyItemLimit = 3,
                IncludedItems = new List<SubscriptionItem>
                {
                    new SubscriptionItem { MenuItemId = "din_main", MenuItemName = "Deluxe Pasta / Biryani Feast Bowl", DailyQuantity = 1, UnitPrice = 200m, IsVeg = false },
                    new SubscriptionItem { MenuItemId = "din_side", MenuItemName = "Garlic Bread with Herb Butter", DailyQuantity = 1, UnitPrice = 80m, IsVeg = true },
                    new SubscriptionItem { MenuItemId = "din_bev", MenuItemName = "Signature Beverage / Shake", DailyQuantity = 1, UnitPrice = 110m, IsVeg = true }
                },
                IsActive = true
            }
        };

        foreach (var p in defaultPlans)
        {
            await _mongo.CreateSubscriptionPlanAsync(p);
        }

        return defaultPlans;
    }
}
