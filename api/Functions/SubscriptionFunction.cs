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
            if (plans.Count == 0)
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

            var outletId = OutletHelper.GetOutletIdForAdmin(req, _auth);

            var plan = new SubscriptionPlan
            {
                OutletId = outletId ?? "default",
                Name = InputSanitizer.Sanitize(request.Name),
                Description = request.Description != null ? InputSanitizer.Sanitize(request.Description) : string.Empty,
                Category = string.IsNullOrWhiteSpace(request.Category) ? "all-day" : request.Category.Trim().ToLowerInvariant(),
                Price = request.Price,
                DurationDays = request.DurationDays > 0 ? request.DurationDays : 30,
                Benefits = request.Benefits?.Select(InputSanitizer.Sanitize).ToList() ?? new List<string>(),
                FreeDelivery = request.FreeDelivery,
                DiscountPercent = request.DiscountPercent,
                DailyItemLimit = request.DailyItemLimit,
                BadgeText = request.BadgeText,
                ImageUrl = request.ImageUrl,
                IncludedItems = request.IncludedItems?.Select(i => new SubscriptionItem
                {
                    MenuItemId = i.MenuItemId,
                    MenuItemName = InputSanitizer.Sanitize(i.MenuItemName),
                    DailyQuantity = i.DailyQuantity,
                    UnitPrice = i.UnitPrice,
                    CategoryName = i.CategoryName,
                    ImageUrl = i.ImageUrl,
                    IsVeg = i.IsVeg
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
            existing.Description = request.Description != null ? InputSanitizer.Sanitize(request.Description) : string.Empty;
            existing.Category = string.IsNullOrWhiteSpace(request.Category) ? existing.Category : request.Category.Trim().ToLowerInvariant();
            existing.Price = request.Price;
            existing.DurationDays = request.DurationDays > 0 ? request.DurationDays : 30;
            existing.Benefits = request.Benefits?.Select(InputSanitizer.Sanitize).ToList() ?? new List<string>();
            existing.FreeDelivery = request.FreeDelivery;
            existing.DiscountPercent = request.DiscountPercent;
            existing.DailyItemLimit = request.DailyItemLimit;
            existing.BadgeText = request.BadgeText;
            existing.ImageUrl = request.ImageUrl;
            existing.IncludedItems = request.IncludedItems?.Select(i => new SubscriptionItem
            {
                MenuItemId = i.MenuItemId,
                MenuItemName = InputSanitizer.Sanitize(i.MenuItemName),
                DailyQuantity = i.DailyQuantity,
                UnitPrice = i.UnitPrice,
                CategoryName = i.CategoryName,
                ImageUrl = i.ImageUrl,
                IsVeg = i.IsVeg
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

            var plan = await _mongo.GetSubscriptionPlanByIdAsync(request.PlanId);
            if (plan == null || !plan.IsActive)
            {
                var plans = await _mongo.GetSubscriptionPlansAsync(request.OutletId ?? "default", activeOnly: true);
                plan = plans.FirstOrDefault(p => p.Id == request.PlanId);
            }

            if (plan == null || !plan.IsActive)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "Subscription plan not found or inactive" });
                return notFound;
            }

            var durationDays = request.DurationDays.HasValue && request.DurationDays.Value > 0
                ? request.DurationDays.Value
                : plan.DurationDays;

            // Calculate price based on duration
            decimal dailyRate = plan.DurationDays > 0 ? (plan.Price / plan.DurationDays) : (plan.Price / 30m);
            decimal basePrice = Math.Round(dailyRate * durationDays, 2);
            decimal discountPct = durationDays >= 30 ? 20m : (durationDays >= 14 ? 15m : 10m);
            decimal discountAmt = Math.Round(basePrice * (discountPct / 100m), 2);
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
                Items = plan.IncludedItems ?? new List<SubscriptionItem>(),
                DeliveryTimeSlot = string.IsNullOrWhiteSpace(request.DeliveryTimeSlot) ? "12:30 PM - 2:00 PM" : request.DeliveryTimeSlot.Trim(),
                DeliveryDays = request.DeliveryDays != null && request.DeliveryDays.Count > 0 ? request.DeliveryDays : new List<string> { "Everyday" },
                DeliveryAddress = InputSanitizer.Sanitize(request.DeliveryAddress ?? string.Empty),
                SpecialInstructions = request.SpecialInstructions != null ? InputSanitizer.Sanitize(request.SpecialInstructions) : null,
                StartDate = now,
                EndDate = now.AddDays(durationDays),
                DurationDays = durationDays,
                Status = "active",
                DailySubtotal = dailyRate,
                DiscountPercent = discountPct,
                DiscountAmount = discountAmt,
                AmountPaid = finalAmount,
                FreeDelivery = true,
                PaymentMethod = string.IsNullOrWhiteSpace(request.PaymentMethod) ? "upi-qr" : request.PaymentMethod,
                PaymentStatus = "paid",
                RazorpayPaymentId = request.RazorpayPaymentId,
                CreatedAt = now,
                UpdatedAt = now
            };

            var subscription = await _mongo.CreateCustomerSubscriptionAsync(sub);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                message = $"Successfully subscribed to {plan.Name}! Valid for {durationDays} days until {subscription.EndDate:dd MMM yyyy}.",
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
                if (mItem == null) continue;

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

            var durationDays = Math.Clamp(request.DurationDays, 3, 365);
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

            // Industry standard recurring subscription discount:
            // >= 30 days: 20% off
            // >= 14 days: 15% off
            // >= 7 days: 10% off
            decimal discountPercent = durationDays >= 30 ? 20m : (durationDays >= 14 ? 15m : 10m);
            decimal discountAmount = Math.Round(grossAmount * (discountPercent / 100m), 2);
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
                Status = "active",
                DailySubtotal = dailySubtotal,
                DiscountPercent = discountPercent,
                DiscountAmount = discountAmount,
                AmountPaid = finalPayable,
                FreeDelivery = true,
                PaymentMethod = string.IsNullOrWhiteSpace(request.PaymentMethod) ? "upi-qr" : request.PaymentMethod,
                PaymentStatus = "paid",
                CreatedAt = now,
                UpdatedAt = now
            };

            var created = await _mongo.CreateCustomerSubscriptionAsync(comboSub);

            var response = req.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(new
            {
                message = $"Your custom recurring combo '{comboSub.PlanName}' is now active for {durationDays} days!",
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

            if (sub.Status != "active")
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = $"Cannot pause a subscription that is currently {sub.Status}." });
                return badReq;
            }

            sub.Status = "paused";
            sub.PausedAt = MongoService.GetIstNow();
            await _mongo.UpdateCustomerSubscriptionAsync(id, sub);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                message = "Subscription paused successfully. You can resume anytime to extend your plan.",
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
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteAsJsonAsync(new { error = $"Subscription is not paused (current status: {sub.Status})." });
                return badReq;
            }

            var now = MongoService.GetIstNow();
            var pausedDays = sub.PausedAt.HasValue
                ? Math.Max(1, (int)Math.Ceiling((now - sub.PausedAt.Value).TotalDays))
                : 1;

            sub.Status = "active";
            sub.EndDate = sub.EndDate.AddDays(pausedDays);
            sub.TotalDaysPaused += pausedDays;
            sub.PausedAt = null;

            await _mongo.UpdateCustomerSubscriptionAsync(id, sub);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                message = $"Welcome back! Your subscription is active again and extended by {pausedDays} day(s) until {sub.EndDate:dd MMM yyyy}.",
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
            await _mongo.UpdateCustomerSubscriptionAsync(id, sub);

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
                Benefits = new List<string> { "Free Daily Doorstep Delivery", "Daily Breakfast Time Window (8:00 AM - 10:00 AM)", "15% Flat Savings vs Menu Pricing", "Pause or Skip Anytime" },
                FreeDelivery = true,
                DiscountPercent = 15m,
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
                DiscountPercent = 20m,
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
                DiscountPercent = 15m,
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
                Benefits = new List<string> { "Dinner Slot (7:30 PM - 9:30 PM)", "Free Priority Delivery", "Gourmet Multi-Course Meals", "Pause on Weekends Option" },
                FreeDelivery = true,
                DiscountPercent = 20m,
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
