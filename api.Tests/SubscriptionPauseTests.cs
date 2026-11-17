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

    private static HttpRequestData Request(object payload, AuthService auth)
    {
        var services = new ServiceCollection();
        services.AddOptions<WorkerOptions>().Configure(o => o.Serializer = new JsonObjectSerializer(new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var context = new Mock<FunctionContext>();
        context.SetupProperty(c => c.InstanceServices, services.BuildServiceProvider());
        var request = new Mock<HttpRequestData>(context.Object);
        var headers = new HttpHeadersCollection { { "Authorization", $"Bearer {auth.GenerateJwtToken(UserId, "customer", "user")}" } };
        request.SetupGet(r => r.Headers).Returns(headers);
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
