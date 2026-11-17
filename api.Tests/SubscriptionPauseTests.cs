using System.Net;
using System.Text;
using System.Text.Json;
using System.Reflection;
using System.Runtime.CompilerServices;
using Azure.Core.Serialization;
using Cafe.Api.Functions;
using Cafe.Api.Helpers;
using Cafe.Api.Models;
using Cafe.Api.Repositories;
using Cafe.Api.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Moq;
using Xunit;

namespace Cafe.Api.Tests;

[Collection("Auth environment")]
public sealed class SubscriptionPauseTests
{
    private const string UserId = "111111111111111111111111";
    private static readonly DateTime Now = new(2026, 11, 17, 15, 0, 0);

    private static CustomerSubscription Active(DateTime? now = null) => new()
    {
        Id = "222222222222222222222222",
        UserId = UserId,
        Status = "active",
        StartDate = DateTime.SpecifyKind((now ?? Now).AddDays(-10), DateTimeKind.Utc),
        EndDate = DateTime.SpecifyKind((now ?? Now).AddDays(20), DateTimeKind.Utc)
    };

    [Fact]
    public void CountsPausesAcrossTheWholeSubscriptionWithoutResettingAtMonthBoundaries()
    {
        var now = new DateTime(2026, 12, 31, 23, 0, 0);
        var sub = Active(now);
        SubscriptionPausePolicy.Pause(sub, 1, now.AddDays(-4));
        SubscriptionPausePolicy.Resume(sub, now.AddDays(-3));
        SubscriptionPausePolicy.Pause(sub, 1, now.AddDays(-2));
        SubscriptionPausePolicy.Resume(sub, now.AddDays(-1));
        Assert.NotNull(SubscriptionPausePolicy.ValidatePause(sub, 1, now));
        Assert.Equal(2, SubscriptionPausePolicy.PausesUsed(sub));
        var nextMonth = now.AddHours(1);
        Assert.NotNull(SubscriptionPausePolicy.ValidatePause(sub, 7, nextMonth));
    }

    [Fact]
    public void PauseAtIstMonthBoundaryIsCountedOnlyOnce()
    {
        var start = new DateTime(2026, 12, 1, 0, 15, 0);
        var sub = Active(start);
        SubscriptionPausePolicy.Pause(sub, 1, start);
        Assert.Equal(11, Assert.Single(sub.PauseStartsUtc).Month);
        Assert.Equal(1, SubscriptionPausePolicy.PausesUsed(sub));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(8)]
    public void RejectsDurationsOutsideOneToSevenDays(int days)
    {
        var sub = Active();
        Assert.Throws<InvalidOperationException>(() => SubscriptionPausePolicy.Pause(sub, days, Now));
        Assert.Equal("active", sub.Status);
        Assert.Empty(sub.PauseStartsUtc);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(24, 1)]
    [InlineData(25, 2)]
    [InlineData(168, 7)]
    public void EarlyResumeRoundsUpConsumedDaysWithoutGrantingTheWholeRequestedPause(int hours, int days)
    {
        var sub = Active();
        var end = sub.EndDate;
        SubscriptionPausePolicy.Pause(sub, 7, Now);
        Assert.Equal(end, sub.EndDate);
        Assert.Equal(IstDateHelper.IstWallTimeToUtc(Now.AddDays(7)), sub.PauseEndsAtUtc);
        Assert.Equal(days, SubscriptionPausePolicy.Resume(sub, Now.AddHours(hours)));
        Assert.Equal(end.AddDays(days), sub.EndDate);
        Assert.Equal(days, sub.TotalDaysPaused);
        Assert.Equal("active", sub.Status);
        Assert.Null(sub.PausedAt);
        Assert.Null(sub.PauseEndsAtUtc);
        Assert.Throws<InvalidOperationException>(() => SubscriptionPausePolicy.Resume(sub, Now.AddHours(hours)));
    }

    [Fact]
    public void DelayedAutomaticResumeGrantsOnlyTheSelectedDaysAndPreservesExpiredStatus()
    {
        var sub = Active();
        var end = sub.EndDate;
        SubscriptionPausePolicy.Pause(sub, 3, Now);
        Assert.False(SubscriptionPausePolicy.IsDue(sub, Now.AddDays(3).AddTicks(-1)));
        Assert.True(SubscriptionPausePolicy.IsDue(sub, Now.AddDays(3)));
        Assert.Equal(3, SubscriptionPausePolicy.Resume(sub, Now.AddDays(40)));
        Assert.Equal(end.AddDays(3), sub.EndDate);
        Assert.Equal("expired", sub.Status);
    }

    [Fact]
    public void LegacyPausedSubscriptionsAreCappedAndCountedOnce()
    {
        var sub = Active();
        sub.Status = "paused";
        sub.PausedAt = Now;
        Assert.Equal(1, SubscriptionPausePolicy.PausesUsed(sub));
        Assert.True(SubscriptionPausePolicy.IsDue(sub, Now.AddDays(9)));
        Assert.Equal(7, SubscriptionPausePolicy.Resume(sub, Now.AddDays(9)));
        Assert.Single(sub.PauseStartsUtc);
    }

    [Fact]
    public void PausedSubscriptionRemainsResumablePastItsOriginalEndDateAndSurvivesPersistence()
    {
        var sub = Active();
        sub.EndDate = DateTime.SpecifyKind(Now.AddDays(1), DateTimeKind.Utc);
        var originalEnd = sub.EndDate;
        SubscriptionPausePolicy.Pause(sub, 7, Now);
        var restored = BsonSerializer.Deserialize<CustomerSubscription>(sub.ToBson());
        Assert.Equal(1, SubscriptionPausePolicy.PausesUsed(restored));
        Assert.Equal(SubscriptionPausePolicy.ResumeAt(sub), SubscriptionPausePolicy.ResumeAt(restored));
        Assert.Equal(2, SubscriptionPausePolicy.Resume(restored, Now.AddDays(2)));
        Assert.Equal(originalEnd.AddDays(2), restored.EndDate);
        Assert.Equal("active", restored.Status);
    }

    [Fact]
    public void ExpiredOrNonActiveSubscriptionsCannotPause()
    {
        var sub = Active();
        sub.EndDate = Now;
        Assert.NotNull(SubscriptionPausePolicy.ValidatePause(sub, 1, Now));
        sub.EndDate = Now.AddDays(1);
        foreach (var status in new[] { "paused", "pending_payment", "cancelled", "expired", "completed" })
        {
            sub.Status = status;
            Assert.NotNull(SubscriptionPausePolicy.ValidatePause(sub, 1, Now));
        }
    }

    [Theory]
    [InlineData(7, 3, 1)]
    [InlineData(14, 5, 1)]
    [InlineData(30, 7, 2)]
    public void EnforcesDurationSpecificPauseLengthAndLifetimeCount(int duration, int maxDays, int limit)
    {
        var sub = Active();
        sub.DurationDays = duration;
        var originalEnd = sub.EndDate;
        Assert.NotNull(SubscriptionPausePolicy.ValidatePause(sub, maxDays + 1, Now));
        for (var index = 0; index < limit; index++)
        {
            var start = Now.AddDays(index * 8);
            SubscriptionPausePolicy.Pause(sub, maxDays, start);
            Assert.Equal(maxDays, SubscriptionPausePolicy.Resume(sub, start.AddDays(maxDays)));
        }
        Assert.Equal(originalEnd.AddDays(limit * maxDays), sub.EndDate);
        Assert.Equal(limit, sub.PauseLimit);
        Assert.Equal(0, sub.PausesRemaining);
        Assert.NotNull(SubscriptionPausePolicy.ValidatePause(sub, 1, Now));
    }

    [Theory]
    [InlineData(7, 3)]
    [InlineData(14, 5)]
    public void ExistingPausedSubscriptionsUseTheirNewDurationCapWithoutRepricing(int duration, int cap)
    {
        var sub = Active();
        sub.DurationDays = duration;
        sub.Status = "paused";
        sub.PausedAt = IstDateHelper.IstWallTimeToUtc(Now);
        sub.PauseStartsUtc.Add(sub.PausedAt.Value);
        sub.PauseEndsAtUtc = IstDateHelper.IstWallTimeToUtc(Now.AddDays(7));
        sub.AmountPaid = 1234;
        sub.DiscountPercent = 20;
        Assert.True(SubscriptionPausePolicy.UpdateDeadline(sub));
        Assert.Equal(IstDateHelper.IstWallTimeToUtc(Now.AddDays(cap)), sub.PauseEndsAtUtc);
        Assert.Equal(cap, SubscriptionPausePolicy.Resume(sub, Now.AddDays(7)));
        Assert.Equal(1234, sub.AmountPaid);
        Assert.Equal(20, sub.DiscountPercent);
        Assert.Single(sub.PauseStartsUtc);
    }

    [Theory]
    [InlineData(7, 3)]
    [InlineData(14, 5)]
    [InlineData(30, 10)]
    public async Task PurchaseEndpointsApplyConfiguredDiscountsToCuratedPlansAndCustomCombos(int duration, int percent)
    {
        var repository = new Mock<IOperationsRepository>();
        repository.Setup(r => r.GetUserSubscriptionsAsync(UserId)).ReturnsAsync(new List<CustomerSubscription>());
        repository.Setup(r => r.GetSubscriptionPlanByIdAsync("plan")).ReturnsAsync(new SubscriptionPlan
        {
            Id = "plan",
            Name = "Plan",
            DurationDays = 30,
            Price = 3000,
            IsActive = true
        });
        repository.Setup(r => r.CreateCustomerSubscriptionAsync(It.IsAny<CustomerSubscription>()))
            .ReturnsAsync((CustomerSubscription sub) => sub);
        var menu = new Mock<IMenuRepository>();
        menu.Setup(r => r.GetMenuItemsByIdsAsync(It.IsAny<List<string>>(), It.IsAny<string?>()))
            .ReturnsAsync(new List<CafeMenuItem> { new() { Id = "tea", Name = "Tea", OnlinePrice = 100, IsAvailable = true } });
        var (_, auth) = CreateFunction(repository);
        var function = new SubscriptionFunction(repository.Object, menu.Object, auth, NullLoggerFactory.Instance);
        var curated = await function.Subscribe(Request(new { planId = "plan", durationDays = duration }, auth));
        Assert.Equal(HttpStatusCode.OK, curated.StatusCode);
        var combo = await function.CreateCustomComboSubscription(Request(new
        {
            comboName = "Meal",
            durationDays = duration,
            deliveryAddress = "Cafe road",
            customerPhone = "1234567890",
            deliveryTimeSlot = "Lunch",
            items = new[] { new { menuItemId = "tea", quantity = 1 } }
        }, auth));
        Assert.Equal(HttpStatusCode.Created, combo.StatusCode);
        repository.Verify(r => r.CreateCustomerSubscriptionAsync(It.Is<CustomerSubscription>(sub =>
            sub.DurationDays == duration && sub.DiscountPercent == percent &&
            sub.DiscountAmount == duration * percent && sub.AmountDue == duration * (100 - percent))), Times.Exactly(2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(21)]
    public async Task PurchaseEndpointsRejectUnsupportedDurations(int duration)
    {
        var repository = new Mock<IOperationsRepository>();
        repository.Setup(r => r.GetUserSubscriptionsAsync(UserId)).ReturnsAsync(new List<CustomerSubscription>());
        repository.Setup(r => r.GetSubscriptionPlanByIdAsync("plan")).ReturnsAsync(new SubscriptionPlan
        {
            Id = "plan",
            DurationDays = 30,
            Price = 3000,
            IsActive = true
        });
        var (function, auth) = CreateFunction(repository);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await function.Subscribe(Request(new { planId = "plan", durationDays = duration }, auth))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await function.CreateCustomComboSubscription(Request(new
            {
                comboName = "Meal",
                durationDays = duration,
                deliveryAddress = "Cafe road",
                customerPhone = "1234567890",
                deliveryTimeSlot = "Lunch",
                items = new[] { new { menuItemId = "tea", quantity = 1 } }
            }, auth))).StatusCode);
        repository.Verify(r => r.CreateCustomerSubscriptionAsync(It.IsAny<CustomerSubscription>()), Times.Never);
    }

    [Theory]
    [InlineData(0, false, false, HttpStatusCode.BadRequest)]
    [InlineData(8, false, false, HttpStatusCode.BadRequest)]
    [InlineData(3, true, false, HttpStatusCode.BadRequest)]
    [InlineData(3, false, true, HttpStatusCode.Forbidden)]
    [InlineData(3, false, false, HttpStatusCode.OK)]
    public async Task PauseEndpointEnforcesDurationAllowanceAndOwnership(int days, bool usedBoth, bool otherUser, HttpStatusCode expected)
    {
        var now = MongoService.GetIstNow();
        var sub = Active(now);
        if (usedBoth) sub.PauseStartsUtc = new()
        {
            IstDateHelper.IstWallTimeToUtc(now.AddMinutes(-2)),
            IstDateHelper.IstWallTimeToUtc(now.AddMinutes(-1))
        };
        if (otherUser) sub.UserId = "other";
        var repository = new Mock<IOperationsRepository>();
        repository.Setup(r => r.GetCustomerSubscriptionByIdAsync(sub.Id!)).ReturnsAsync(sub);
        repository.Setup(r => r.UpdateCustomerSubscriptionAsync(sub.Id!, sub)).ReturnsAsync(true);
        var (function, auth) = CreateFunction(repository);
        var response = await function.PauseSubscription(Request(new { days }, auth), sub.Id!);
        Assert.Equal(expected, response.StatusCode);
        repository.Verify(r => r.UpdateCustomerSubscriptionAsync(It.IsAny<string>(), It.IsAny<CustomerSubscription>()),
            expected == HttpStatusCode.OK ? Times.Once() : Times.Never());
        if (expected == HttpStatusCode.OK)
        {
            Assert.Equal("paused", sub.Status);
            Assert.Single(sub.PauseStartsUtc);
            Assert.Equal(now.AddDays(20), sub.EndDate);
        }
    }

    [Fact]
    public async Task ConflictingPauseSaveReturnsConflictRatherThanSuccess()
    {
        var sub = Active(MongoService.GetIstNow());
        var repository = new Mock<IOperationsRepository>();
        repository.Setup(r => r.GetCustomerSubscriptionByIdAsync(sub.Id!)).ReturnsAsync(sub);
        repository.Setup(r => r.UpdateCustomerSubscriptionAsync(sub.Id!, sub)).ReturnsAsync(false);
        var (function, auth) = CreateFunction(repository);
        Assert.Equal(HttpStatusCode.Conflict,
            (await function.PauseSubscription(Request(new { days = 2 }, auth), sub.Id!)).StatusCode);
    }

    [Fact]
    public async Task ResumeEndpointUsesConsumedDaysAndSchedulerInvokesDuePauseProcessing()
    {
        var now = MongoService.GetIstNow();
        var sub = Active(now);
        var end = sub.EndDate;
        SubscriptionPausePolicy.Pause(sub, 7, now.AddHours(-25));
        var repository = new Mock<IOperationsRepository>();
        repository.Setup(r => r.GetCustomerSubscriptionByIdAsync(sub.Id!)).ReturnsAsync(sub);
        repository.Setup(r => r.UpdateCustomerSubscriptionAsync(sub.Id!, sub)).ReturnsAsync(true);
        var (function, auth) = CreateFunction(repository);
        Assert.Equal(HttpStatusCode.OK, (await function.ResumeSubscription(Request(new { }, auth), sub.Id!)).StatusCode);
        Assert.Equal(end.AddDays(2), sub.EndDate);
        var scheduler = new SubscriptionPauseScheduler(repository.Object, NullLogger<SubscriptionPauseScheduler>.Instance);
        await scheduler.Run(new TimerInfo());
        repository.Verify(r => r.ResumeDueSubscriptionPausesAsync(It.IsAny<DateTime>()), Times.Once);
    }

    [Fact]
    public async Task SchedulerPersistsTheCappedExtensionOnceWithAVersionGuardForLegacyDocuments()
    {
        var stored = Active();
        var end = stored.EndDate;
        SubscriptionPausePolicy.Pause(stored, 3, Now.AddDays(-4));
        stored = BsonSerializer.Deserialize<CustomerSubscription>(stored.ToBson());
        var collection = new Mock<IMongoCollection<CustomerSubscription>>();
        FilterDefinition<CustomerSubscription>? savedFilter = null;
        collection.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<CustomerSubscription>>(),
                It.IsAny<FindOptions<CustomerSubscription, CustomerSubscription>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var cursor = new Mock<IAsyncCursor<CustomerSubscription>>();
                cursor.SetupGet(c => c.Current).Returns(stored.Status == "paused"
                    ? new[] { BsonSerializer.Deserialize<CustomerSubscription>(stored.ToBson()) }
                    : Array.Empty<CustomerSubscription>());
                cursor.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true).ReturnsAsync(false);
                return cursor.Object;
            });
        collection.Setup(c => c.ReplaceOneAsync(It.IsAny<FilterDefinition<CustomerSubscription>>(),
                It.IsAny<CustomerSubscription>(), It.IsAny<ReplaceOptions>(), It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<CustomerSubscription>, CustomerSubscription, ReplaceOptions, CancellationToken>((filter, sub, _, _) =>
            {
                savedFilter = filter;
                stored = BsonSerializer.Deserialize<CustomerSubscription>(sub.ToBson());
            })
            .ReturnsAsync(new ReplaceOneResult.Acknowledged(1, 1, null));
        var service = (MongoService)RuntimeHelpers.GetUninitializedObject(typeof(MongoService));
        typeof(MongoService).GetField("_customerSubscriptions", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(service, collection.Object);
        Assert.Equal(1, await service.ResumeDueSubscriptionPausesAsync(Now));
        Assert.Equal(0, await service.ResumeDueSubscriptionPausesAsync(Now));
        Assert.Equal(end.AddDays(3), stored.EndDate);
        Assert.Equal(3, stored.TotalDaysPaused);
        Assert.Equal(1, stored.Revision);
        Assert.Equal("active", stored.Status);
        var registry = BsonSerializer.SerializerRegistry;
        var rendered = savedFilter!.Render(new RenderArgs<CustomerSubscription>(registry.GetSerializer<CustomerSubscription>(), registry));
        var guards = rendered.ToJson();
        Assert.Contains("\"revision\" : 0", guards);
        Assert.Contains("\"$exists\" : false", guards);
        collection.Verify(c => c.ReplaceOneAsync(It.IsAny<FilterDefinition<CustomerSubscription>>(),
            It.IsAny<CustomerSubscription>(), It.IsAny<ReplaceOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void OverviewUsesConfirmedPaymentsAndIstCalendarMonthBoundaries()
    {
        var month = new DateTime(2026, 11, 1);
        var result = SubscriptionPlanAnalytics.Calculate(new[]
        {
            new CustomerSubscription { PaymentStatus = "paid", AmountPaid = 700, CreatedAt = month.AddDays(-3), PaymentConfirmedAt = month },
            new CustomerSubscription { PaymentStatus = "paid", AmountPaid = 900, CreatedAt = month.AddTicks(-1) },
            new CustomerSubscription { PaymentStatus = "pending", AmountPaid = 9999, CreatedAt = month },
            new CustomerSubscription { PaymentStatus = "failed", AmountPaid = 9999, CreatedAt = month },
            new CustomerSubscription { PaymentStatus = "paid", AmountPaid = 100, CreatedAt = month.AddMonths(1) }
        }, month.AddDays(10));
        Assert.Equal(3, result.TotalSubscriptions);
        Assert.Equal(1, result.NewSubscriptionsThisMonth);
        Assert.Equal(1, result.PreviousMonthSubscriptions);
        Assert.Equal(700, result.MonthlyRevenue);
        Assert.Equal(900, result.PreviousMonthRevenue);
    }

    [Fact]
    public void OverviewCountsDistinctCurrentPaidCustomersAndCapsOverduePauses()
    {
        CustomerSubscription Current(string user, string status = "active") => new()
        {
            UserId = user, PlanId = "plan", PaymentStatus = "paid", Status = status,
            EndDate = Now.AddDays(1), DurationDays = 7
        };
        var overdue = Current("overdue", "paused");
        overdue.EndDate = Now.AddDays(-4);
        overdue.PausedAt = IstDateHelper.IstWallTimeToUtc(Now.AddDays(-10));
        overdue.PauseEndsAtUtc = overdue.PausedAt.Value.AddDays(7);
        var pending = Current("pending");
        pending.PaymentStatus = "pending";
        var expired = Current("expired");
        expired.EndDate = Now.AddTicks(-1);
        var result = SubscriptionPlanAnalytics.Calculate(new[]
        {
            Current("same"), Current("same"), Current("paused", "paused"), Current("cancelled", "cancelled"),
            overdue, pending, expired
        }, Now);
        Assert.Equal(2, result.PlanSubscribers["plan"]);
    }

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    public async Task AdminPlanCreationPersistsPricingFeaturesSchedulesAndDraftActivation(bool draft, bool active, bool expectedActive)
    {
        var repository = new Mock<IOperationsRepository>();
        SubscriptionPlan? stored = null;
        repository.Setup(r => r.CreateSubscriptionPlanAsync(It.IsAny<SubscriptionPlan>()))
            .Callback<SubscriptionPlan>(p => stored = BsonSerializer.Deserialize<SubscriptionPlan>(p.ToBson()))
            .ReturnsAsync((SubscriptionPlan p) => p);
        var (function, auth) = CreateFunction(repository);
        var response = await function.CreateSubscriptionPlan(Request(new
        {
            name = "Breakfast", description = "Fresh", durationDays = 14, price = 1400,
            pricingMode = "per_day", isDraft = draft, isActive = active, freeDelivery = false,
            includedItems = new[] { new { menuItemId = "coffee", menuItemName = "Coffee", dailyQuantity = 2,
                unitPrice = 80, categoryName = "Drinks", imageUrl = "/coffee.jpg", servingDays = new[] { "Mon", "Wed" } } }
        }, auth, "admin"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(stored);
        Assert.Equal(expectedActive, stored!.IsActive);
        Assert.Equal(draft, stored.IsDraft);
        Assert.Equal("per_day", stored.PricingMode);
        Assert.Equal(1400, stored.Price);
        Assert.Equal(5, stored.DiscountPercent);
        Assert.False(stored.FreeDelivery);
        var item = Assert.Single(stored.IncludedItems!);
        Assert.Equal(new[] { "Mon", "Wed" }, item.ServingDays);
        Assert.Equal(2, item.DailyQuantity);
        Assert.Equal(80, item.UnitPrice);
        Assert.Equal("/coffee.jpg", item.ImageUrl);
    }

    [Fact]
    public async Task AdminCanDeactivateAndUpdateAnExistingPlan()
    {
        var existing = new SubscriptionPlan { Id = "plan", IsActive = true };
        var repository = new Mock<IOperationsRepository>();
        repository.Setup(r => r.GetSubscriptionPlanByIdAsync("plan")).ReturnsAsync(existing);
        repository.Setup(r => r.UpdateSubscriptionPlanAsync("plan", existing)).ReturnsAsync(true);
        var (function, auth) = CreateFunction(repository);
        var response = await function.UpdateSubscriptionPlan(Request(new
        {
            name = "Updated", durationDays = 7, price = 700, isActive = false,
            includedItems = new[] { new { menuItemId = "coffee", menuItemName = "Coffee" } }
        }, auth, "admin"), "plan");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(existing.IsActive);
        Assert.Equal(new[] { "Everyday" }, Assert.Single(existing.IncludedItems!).ServingDays);
    }

    [Theory]
    [InlineData("Funday", 1)]
    [InlineData("Mon", 0)]
    [InlineData("Tue", 21)]
    public async Task InvalidPlanSchedulesOrQuantitiesAreRejectedBeforePersistence(string day, int quantity)
    {
        var repository = new Mock<IOperationsRepository>();
        var (function, auth) = CreateFunction(repository);
        var response = await function.CreateSubscriptionPlan(Request(new
        {
            name = "Invalid plan", durationDays = 7, price = 700,
            includedItems = new[] { new { menuItemId = "coffee", menuItemName = "Coffee",
                dailyQuantity = quantity, servingDays = new[] { day } } }
        }, auth, "admin"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        repository.Verify(r => r.CreateSubscriptionPlanAsync(It.IsAny<SubscriptionPlan>()), Times.Never);
    }

    [Fact]
    public async Task CuratedPurchaseSnapshotsServingDaysAndCannotPurchaseADraft()
    {
        var plan = new SubscriptionPlan
        {
            Id = "plan", Name = "Breakfast", IsActive = true, DurationDays = 7, Price = 700, FreeDelivery = false,
            IncludedItems = new() { new() { MenuItemId = "coffee", MenuItemName = "Coffee", ServingDays = new() { "Mon", "Fri" } } }
        };
        var repository = new Mock<IOperationsRepository>();
        repository.Setup(r => r.GetUserSubscriptionsAsync(UserId)).ReturnsAsync(new List<CustomerSubscription>());
        repository.Setup(r => r.GetSubscriptionPlanByIdAsync("plan")).ReturnsAsync(plan);
        repository.Setup(r => r.GetSubscriptionPlansAsync("default", true)).ReturnsAsync(new List<SubscriptionPlan> { plan });
        CustomerSubscription? purchased = null;
        repository.Setup(r => r.CreateCustomerSubscriptionAsync(It.IsAny<CustomerSubscription>()))
            .Callback<CustomerSubscription>(s => purchased = s).ReturnsAsync((CustomerSubscription s) => s);
        var (function, auth) = CreateFunction(repository);
        Assert.Equal(HttpStatusCode.OK, (await function.Subscribe(Request(new { planId = "plan" }, auth))).StatusCode);
        Assert.Equal(new[] { "Mon", "Fri" }, Assert.Single(purchased!.Items).ServingDays);
        Assert.False(purchased.FreeDelivery);
        plan.IncludedItems[0].ServingDays.Add("Tue");
        Assert.Equal(2, purchased.Items[0].ServingDays.Count);
        plan.IsDraft = true;
        Assert.Equal(HttpStatusCode.NotFound, (await function.Subscribe(Request(new { planId = "plan" }, auth))).StatusCode);
    }

    [Fact]
    public async Task DraftOnlyCatalogStaysHiddenWithoutSeedingNewCustomerPlans()
    {
        var repository = new Mock<IOperationsRepository>();
        repository.Setup(r => r.GetSubscriptionPlansAsync("default", true)).ReturnsAsync(new List<SubscriptionPlan>());
        repository.Setup(r => r.GetSubscriptionPlansAsync("default", false))
            .ReturnsAsync(new List<SubscriptionPlan> { new() { Name = "Draft", IsDraft = true, IsActive = false } });
        var (function, auth) = CreateFunction(repository);
        var response = await function.GetSubscriptionPlans(Request(new { }, auth));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        repository.Verify(r => r.CreateSubscriptionPlanAsync(It.IsAny<SubscriptionPlan>()), Times.Never);
        response.Body.Position = 0;
        Assert.Empty(JsonSerializer.Deserialize<List<SubscriptionPlan>>(await new StreamReader(response.Body).ReadToEndAsync())!);
    }

    [Fact]
    public async Task SavedMenuSetsAndMetricsRequireAdminAndUseTheSelectedOutlet()
    {
        var repository = new Mock<IOperationsRepository>();
        repository.Setup(r => r.CreateSubscriptionMenuSetAsync(It.IsAny<SubscriptionMenuSet>()))
            .ReturnsAsync((SubscriptionMenuSet s) => { s.Id = "set"; return s; });
        repository.Setup(r => r.DeleteSubscriptionMenuSetAsync("branch", "set")).ReturnsAsync(true);
        repository.Setup(r => r.GetSubscriptionMenuSetsAsync("branch")).ReturnsAsync(new List<SubscriptionMenuSet>());
        repository.Setup(r => r.GetSubscriptionOverviewAsync("branch"))
            .ReturnsAsync(new SubscriptionOverviewResponse(2, 1, 0, 100, 0, new Dictionary<string, int>()));
        var (function, auth) = CreateFunction(repository);
        Assert.Equal(HttpStatusCode.Forbidden, (await function.GetSubscriptionOverview(Request(new { }, auth))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await function.GetSubscriptionMenuSets(Request(new { }, auth))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await function.CreateSubscriptionMenuSet(Request(new { }, auth))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await function.DeleteSubscriptionMenuSet(Request(new { }, auth), "set")).StatusCode);
        var request = Request(new
        {
            name = "Breakfast", items = new[] { new { menuItemId = "coffee", menuItemName = "Coffee",
                dailyQuantity = 2, servingDays = new[] { "Mon" } } }
        }, auth, "admin");
        request.Headers.Add("X-Outlet-Id", "branch");
        var created = await function.CreateSubscriptionMenuSet(request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Contains("/set", Assert.Single(created.Headers.GetValues("Location")));
        repository.Verify(r => r.CreateSubscriptionMenuSetAsync(It.Is<SubscriptionMenuSet>(s =>
            s.OutletId == "branch" && s.Items[0].ServingDays.SequenceEqual(new[] { "Mon" }))), Times.Once);
        Assert.Equal(HttpStatusCode.OK, (await function.GetSubscriptionOverview(request)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await function.GetSubscriptionMenuSets(request)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await function.DeleteSubscriptionMenuSet(request, "set")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await function.DeleteSubscriptionMenuSet(request, "missing")).StatusCode);
        repository.Verify(r => r.GetSubscriptionOverviewAsync("branch"), Times.Once);
        repository.Verify(r => r.GetSubscriptionMenuSetsAsync("branch"), Times.Once);
    }

    private static (SubscriptionFunction, AuthService) CreateFunction(Mock<IOperationsRepository> repository)
    {
        var previous = Environment.GetEnvironmentVariable("Jwt__Secret");
        try
        {
            Environment.SetEnvironmentVariable("Jwt__Secret", Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));
            var auth = new AuthService();
            return (new SubscriptionFunction(repository.Object, Mock.Of<IMenuRepository>(), auth, NullLoggerFactory.Instance), auth);
        }
        finally { Environment.SetEnvironmentVariable("Jwt__Secret", previous); }
    }

    private static HttpRequestData Request(object payload, AuthService auth, string role = "user")
    {
        var services = new ServiceCollection();
        services.AddOptions<WorkerOptions>().Configure(o => o.Serializer = new JsonObjectSerializer(new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var context = new Mock<FunctionContext>();
        context.SetupProperty(c => c.InstanceServices, services.BuildServiceProvider());
        var request = new Mock<HttpRequestData>(context.Object);
        var headers = new HttpHeadersCollection { { "Authorization", $"Bearer {auth.GenerateJwtToken(UserId, "customer", role)}" } };
        request.SetupGet(r => r.Headers).Returns(headers);
        request.SetupGet(r => r.Url).Returns(new Uri("http://localhost/api/subscriptions/plans"));
        request.SetupGet(r => r.Query).Returns(System.Web.HttpUtility.ParseQueryString(string.Empty));
        request.SetupGet(r => r.Body).Returns(new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload))));
        request.Setup(r => r.CreateResponse()).Returns(() =>
        {
            var response = new Mock<HttpResponseData>(context.Object);
            response.SetupProperty(r => r.StatusCode, HttpStatusCode.OK);
            response.SetupProperty(r => r.Headers, new HttpHeadersCollection());
            response.SetupProperty(r => r.Body, new MemoryStream());
            return response.Object;
        });
        return request.Object;
    }
}
