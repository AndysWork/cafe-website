using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Cafe.Api.Functions;
using Cafe.Api.Helpers;
using Cafe.Api.Models;
using Cafe.Api.Services;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Moq;
using Xunit;

namespace Cafe.Api.Tests;

public sealed class CustomerJourneyTests
{
    private static readonly DateTime Now = new(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc);
    private const string OutletId = "222222222222222222222222";
    private const string ItemId = "333333333333333333333333";
    private static readonly JourneyReportRange Range = new(Now.AddDays(-1), Now.AddDays(1));
    private static JourneySession Session(string id, bool staff = false, DateTime? last = null, string? visitor = null) => new()
    {
        Id = id, VisitorId = visitor ?? Guid.NewGuid().ToString(), OutletId = OutletId, IsStaff = staff,
        StartedAt = Now.AddHours(-2), LastSeenAt = last ?? Now.AddHours(-1), ExpiresAt = Now.AddDays(1)
    };
    private static JourneyEvent Event(string session, string type, int minute = 0, int count = 0, decimal value = 0) => new()
    {
        Id = Guid.NewGuid().ToString(), SessionId = session, OutletId = OutletId, Type = type,
        Page = type == "page_view" ? "/menu" : "/checkout", OccurredAt = Now.AddHours(-2).AddMinutes(minute),
        CartCount = count, CartValue = value, ExpiresAt = Now.AddDays(1)
    };

    [Fact]
    public void MetricsExcludeStaffCountDistinctVisitorsAndReconcileCancellations()
    {
        var visitor = Guid.NewGuid().ToString();
        var sessions = new[] { Session("one", visitor: visitor), Session("two", visitor: visitor), Session("staff", true) };
        var first = Event("one", "order_placed", 5, 2, 100);
        first.Items.Add(new(ItemId, "Coffee", 2, 90));
        var cancelled = Event("two", "order_placed", 5, 9, 400);
        cancelled.Cancelled = true;
        cancelled.Items.Add(new(ItemId, "Coffee", 9, 380));
        var staff = Event("staff", "order_placed", 5, 100, 9999);
        var result = CustomerJourneyAnalytics.BuildDashboard(sessions, new[]
        {
            Event("one", "page_view"), Event("two", "page_view"), Event("staff", "page_view"), first, cancelled, staff
        }, Range, Now, 12);
        Assert.Equal(1, result.Overview.Visitors);
        Assert.Equal(2, result.Overview.Sessions);
        Assert.Equal(2, result.Overview.Orders);
        Assert.Equal(1, result.Overview.CancelledOrders);
        Assert.Equal(100, result.Overview.Revenue);
        Assert.Equal(100, result.Overview.ConversionRate);
        Assert.Equal(1, result.Overview.StaffSessions);
        Assert.Equal(2, Assert.Single(result.Menu).QuantityOrdered);
        Assert.Equal(12, result.RegisteredAccountsAllOutlets);
    }

    [Fact]
    public void FunnelRequiresOrderedStagesAndDoesNotInventDirectCheckoutStages()
    {
        var events = new[]
        {
            Event("full", "page_view"), Event("full", "menu_item_view", 1),
            Event("full", "cart_add", 2, 1, 100), Event("full", "checkout_started", 3, 1, 100),
            Event("full", "order_placed", 4, 1, 100),
            Event("direct", "checkout_started", 1, 1, 100), Event("direct", "order_placed", 2, 1, 100)
        };
        var result = CustomerJourneyAnalytics.BuildDashboard(new[] { Session("full"), Session("direct") }, events, Range, Now, 0);
        Assert.Equal(new[] { 2, 1, 1, 1, 1 }, result.Funnel.Select(s => s.Sessions));
        Assert.Equal(2, result.Overview.Orders);
        Assert.Equal(100, result.Overview.ConversionRate);
    }

    [Fact]
    public void AbandonmentWaitsForInactivityAndExcludesEmptiedConvertedAndContinuedCarts()
    {
        var sessions = new[]
        {
            Session("abandoned"), Session("active", last: Now.AddMinutes(-29)),
            Session("empty"), Session("converted"), Session("continued")
        };
        var events = sessions.Select(s => Event(s.Id, "cart_add", 1, 2, 150)).ToList();
        events.AddRange(new[]
        {
            Event("abandoned", "checkout_started", 2, 2, 150),
            Event("empty", "cart_update", 3), Event("converted", "order_placed", 3, 2, 150),
            Event("converted", "cart_update", 4), Event("continued", "session_continued", 3)
        });
        var result = CustomerJourneyAnalytics.BuildDashboard(sessions, events, Range, Now, 0);
        var rows = CustomerJourneyAnalytics.Summarize(sessions, events, Now);
        Assert.Equal("Checkout abandoned", rows.Single(s => s.Id == "abandoned").Outcome);
        Assert.Equal("Active cart", rows.Single(s => s.Id == "active").Outcome);
        Assert.Equal("Browsing", rows.Single(s => s.Id == "empty").Outcome);
        var converted = rows.Single(s => s.Id == "converted");
        Assert.Equal("Converted", converted.Outcome);
        Assert.Equal(150, converted.CartValue);
        Assert.DoesNotContain("abandoned", rows.Single(s => s.Id == "continued").Outcome);
        Assert.Equal(33.33, result.Overview.CartAbandonmentRate);
        Assert.Equal(1, Assert.Single(result.CartValues).Orders);
    }

    [Fact]
    public void LaterUnconvertedCartCanBeAbandonedEvenAfterAnEarlierOrder()
    {
        var result = CustomerJourneyAnalytics.Summarize(new[] { Session("repeat") }, new[]
        {
            Event("repeat", "order_placed", 1, 1, 100), Event("repeat", "cart_add", 5, 2, 200),
            Event("repeat", "checkout_started", 6, 2, 200)
        }, Now);
        Assert.Equal("Checkout abandoned", Assert.Single(result).Outcome);
        Assert.Equal(1, result[0].Orders);
    }

    [Fact]
    public void HistoricalBoundaryDoesNotFalselyAbandonSessionThatContinuedAfterTheRange()
    {
        var session = Session("continued", last: Now.AddMinutes(-45));
        var end = Now.AddHours(-1);
        var rows = CustomerJourneyAnalytics.Summarize(new[] { session },
            new[] { Event(session.Id, "cart_add", 1, 1, 100) }, Now, end);
        Assert.DoesNotContain("abandoned", Assert.Single(rows).Outcome);
    }

    [Fact]
    public void ActiveTimeMergesOverlappingIntervalsInsteadOfInflatingEngagement()
    {
        var one = Event("one", "page_engagement");
        one.ActiveSeconds = 20;
        var two = Event("one", "page_engagement");
        two.OccurredAt = one.OccurredAt.AddSeconds(10);
        two.ActiveSeconds = 20;
        Assert.Equal(30, CustomerJourneyAnalytics.ActiveSeconds(new[] { one, two, Event("one", "heartbeat") }));
    }

    [Fact]
    public void ConcurrentVisitorsEngagementIsAddedRatherThanMergedAcrossSessions()
    {
        var first = Event("one", "page_engagement");
        first.ActiveSeconds = 20;
        var second = Event("two", "page_engagement");
        second.ActiveSeconds = 20;
        Assert.Equal(40, CustomerJourneyAnalytics.ActiveSeconds(new[] { first, second }));
    }

    [Fact]
    public void HighInterestUsesUniqueViewersAndOrdersMustFollowTheView()
    {
        var sessions = Enumerable.Range(0, 5).Select(i => Session($"s{i}")).ToArray();
        var events = new List<JourneyEvent>();
        foreach (var session in sessions)
            for (var i = 0; i < 3; i++)
            {
                var view = Event(session.Id, "menu_item_view", 5 + i);
                view.MenuItemId = ItemId;
                view.ItemName = "Coffee";
                events.Add(view);
            }
        var priorOrder = Event("s0", "order_placed", 1, 3, 120);
        priorOrder.Items.Add(new(ItemId, "Coffee", 3, 120));
        events.Add(priorOrder);
        var cancelledOrder = Event("s1", "order_placed", 10, 4, 160);
        cancelledOrder.Cancelled = true;
        cancelledOrder.Items.Add(new(ItemId, "Coffee", 4, 160));
        events.Add(cancelledOrder);
        var item = Assert.Single(CustomerJourneyAnalytics.BuildDashboard(sessions, events, Range, Now, 0).Menu);
        Assert.Equal(15, item.Views);
        Assert.Equal(5, item.Viewers);
        Assert.True(item.HighInterestLowConversion);
        Assert.Equal(0, item.OrderRate);
        Assert.Equal(3, item.QuantityOrdered);
    }

    [Fact]
    public void ReportDatesAreInclusiveIstCalendarDates()
    {
        var now = new DateTime(2026, 11, 17, 20, 0, 0, DateTimeKind.Utc);
        var range = CustomerJourneyFunction.ParseRange("2026-11-18", "2026-11-18", now);
        Assert.Equal(new DateTime(2026, 11, 17, 18, 30, 0, DateTimeKind.Utc), range.From);
        Assert.Equal(range.From.AddDays(1), range.ToExclusive);
        Assert.Throws<ArgumentException>(() => CustomerJourneyFunction.ParseRange("2026-10-01", "2026-11-18", now));
        Assert.Throws<ArgumentException>(() => CustomerJourneyFunction.ParseRange("2026-11-19", "2026-11-19", now));
        Assert.Throws<ArgumentException>(() => CustomerJourneyFunction.ParseRange("not-a-date", null, now));
    }

    [Theory]
    [InlineData("order_placed", "/menu", 0)]
    [InlineData("page_view", "/profile?email=private", 0)]
    [InlineData("page_engagement", "/menu", 31)]
    [InlineData("page_view", "/menu", 10)]
    public void BrowserCannotSupplyConversionsSensitiveUrlsOrUnboundedTime(string type, string page, int active)
    {
        Assert.Throws<ArgumentException>(() => CustomerJourneyService.ValidateEvent(new()
        {
            Id = Guid.NewGuid().ToString(), Type = type, Page = page, ActiveSeconds = active, OccurredAt = Now
        }, Now, Now.AddMinutes(-1)));
    }

    [Fact]
    public void CsvEscapesQuotesAndNeutralizesSpreadsheetFormulas()
    {
        var csv = CustomerJourneyService.Csv(new[] { "Name", "Value" },
            new[] { new object?[] { " =HYPERLINK(\"bad\")", 12.5m }, new object?[] { "@formula", "a,b" } });
        Assert.Contains("\"' =HYPERLINK(\"\"bad\"\")\"", csv);
        Assert.Contains("\"'@formula\"", csv);
        Assert.Contains("\"a,b\"", csv);
        Assert.Contains("\"12.5\"", csv);
    }

    [Fact]
    public async Task ConsentControlsServerIdentityAndNeverReusesAnAnonymousIdForLinkedAccounts()
    {
        var db = new Mock<IMongoDatabase>();
        JourneySession? inserted = null;
        var sessions = Collection<JourneySession>(db, "JourneySessions");
        Collection<JourneyEvent>(db, "JourneyEvents");
        Collection<UserActivityEvent>(db, "UserActivityEvents");
        Collection<UserSession>(db, "UserSessions");
        var outlets = Collection<Outlet>(db, "Outlets");
        sessions.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<JourneySession>>(),
            It.IsAny<FindOptions<JourneySession, JourneySession>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Cursor(inserted == null ? Array.Empty<JourneySession>() : new[] { inserted }));
        sessions.Setup(c => c.InsertOneAsync(It.IsAny<JourneySession>(), It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()))
            .Callback<JourneySession, InsertOneOptions, CancellationToken>((s, _, _) => inserted = s).Returns(Task.CompletedTask);
        outlets.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<Outlet>>(),
            It.IsAny<FindOptions<Outlet, BsonDocument>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Cursor(new[] { new BsonDocument("_id", ObjectId.Parse(OutletId)) }));
        var service = new CustomerJourneyService(db.Object);
        var request = new CreateJourneySessionRequest
        {
            AnalyticsConsent = true, ConsentVersion = "journey-v1", VisitorId = Guid.NewGuid().ToString(),
            OutletId = OutletId, ConsentExpiresAt = DateTimeOffset.UtcNow.AddDays(2)
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "server-account"), new Claim(ClaimTypes.Name, "Consented account"),
            new Claim(ClaimTypes.Role, "user"), new Claim(ClaimTypes.Role, "admin")
        }, "test"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateSessionAsync(request with { AnalyticsConsent = false }, principal, default));
        await service.CreateSessionAsync(request, principal, default);
        Assert.Null(inserted!.AccountId);
        Assert.Null(inserted.AccountLabel);
        Assert.True(inserted.IsStaff);
        Assert.Equal(request.VisitorId, inserted.VisitorId);
        inserted = null;
        var result = await service.CreateSessionAsync(request with { LinkAccount = true }, principal, default);
        Assert.Equal("server-account", inserted!.AccountId);
        Assert.Equal("Consented account", inserted.AccountLabel);
        Assert.NotEqual(request.VisitorId, inserted.VisitorId);
        Assert.NotEqual(result.Context.Token, inserted.TokenHash);
        Assert.Equal(request.ConsentExpiresAt.UtcDateTime, inserted.ExpiresAt);
        Assert.Same(inserted, await service.ValidateAsync(result.Context, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.ValidateAsync(result.Context with { Token = new string('0', 64) }, default));
    }

    [Fact]
    public async Task ExpiryAndOutletAreAppliedInMongoQueriesBeforeTtlCleanup()
    {
        var db = new Mock<IMongoDatabase>();
        var sessions = Collection<JourneySession>(db, "JourneySessions");
        var events = Collection<JourneyEvent>(db, "JourneyEvents");
        Collection<UserActivityEvent>(db, "UserActivityEvents");
        Collection<UserSession>(db, "UserSessions");
        FilterDefinition<JourneySession>? sessionFilter = null;
        FilterDefinition<JourneyEvent>? eventFilter = null;
        sessions.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<JourneySession>>(),
            It.IsAny<FindOptions<JourneySession, JourneySession>>(), It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<JourneySession>, FindOptions<JourneySession, JourneySession>, CancellationToken>((f, _, _) => sessionFilter = f)
            .ReturnsAsync(() => Cursor(Array.Empty<JourneySession>()));
        events.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<JourneyEvent>>(),
            It.IsAny<FindOptions<JourneyEvent, JourneyEvent>>(), It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<JourneyEvent>, FindOptions<JourneyEvent, JourneyEvent>, CancellationToken>((f, _, _) => eventFilter = f)
            .ReturnsAsync(() => Cursor(Array.Empty<JourneyEvent>()));
        var service = new CustomerJourneyService(db.Object);
        var result = await service.GetSessionsAsync(OutletId, new(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow), "customers", null, 1, default);
        Assert.Empty(result.Sessions);
        var registry = BsonSerializer.SerializerRegistry;
        var sessionQuery = sessionFilter!.Render(new RenderArgs<JourneySession>(registry.GetSerializer<JourneySession>(), registry));
        var eventQuery = eventFilter!.Render(new RenderArgs<JourneyEvent>(registry.GetSerializer<JourneyEvent>(), registry));
        Assert.Equal(OutletId, sessionQuery["OutletId"].AsString);
        Assert.Equal(OutletId, eventQuery["OutletId"].AsString);
        Assert.True(sessionQuery["ExpiresAt"].AsBsonDocument.Contains("$gt"));
        Assert.True(eventQuery["ExpiresAt"].AsBsonDocument.Contains("$gt"));
        Assert.True(eventQuery["OccurredAt"].AsBsonDocument.Contains("$lt"));
    }

    [Fact]
    public async Task TimelinePageEngagementIncludesEventsOutsideTheVisibleEventPage()
    {
        var db = new Mock<IMongoDatabase>();
        var sessions = Collection<JourneySession>(db, "JourneySessions");
        var events = Collection<JourneyEvent>(db, "JourneyEvents");
        Collection<UserActivityEvent>(db, "UserActivityEvents");
        Collection<UserSession>(db, "UserSessions");
        var id = Guid.NewGuid().ToString();
        var session = Session(id);
        var activity = Enumerable.Range(1, 101).Select(index => new JourneyEvent
        {
            Id = index.ToString(), SessionId = id, Page = index <= 100 ? "/menu" : "/checkout",
            Type = "page_engagement", ActiveSeconds = 30, OccurredAt = Now.AddHours(-2).AddSeconds(index * 30)
        }).ToList();
        activity.Add(Event(id, "page_view"));
        sessions.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<JourneySession>>(),
            It.IsAny<FindOptions<JourneySession, JourneySession>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Cursor(new[] { session }));
        events.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<JourneyEvent>>(),
            It.IsAny<FindOptions<JourneyEvent, JourneyEvent>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Cursor(activity));
        var result = await new CustomerJourneyService(db.Object).GetTimelineAsync(OutletId, Range, id, 2, default);
        Assert.NotNull(result);
        Assert.Equal(2, result.Events.Count);
        Assert.Equal(102, result.Total);
        Assert.Equal(3000, result.Pages.Single(p => p.Page == "/menu").ActiveSeconds);
        Assert.Equal(30, result.Pages.Single(p => p.Page == "/checkout").ActiveSeconds);
    }

    [Fact]
    public async Task RetryingAnEventUsesTheSameUpsertKeyAndInsertOnlyPayload()
    {
        var db = new Mock<IMongoDatabase>();
        var sessions = Collection<JourneySession>(db, "JourneySessions");
        var events = Collection<JourneyEvent>(db, "JourneyEvents");
        var token = new string('a', 64);
        var session = Session(Guid.NewGuid().ToString());
        session.StartedAt = DateTime.UtcNow.AddMinutes(-1);
        session.LastSeenAt = DateTime.UtcNow;
        session.ExpiresAt = DateTime.UtcNow.AddDays(1);
        session.TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        sessions.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<JourneySession>>(),
            It.IsAny<FindOptions<JourneySession, JourneySession>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Cursor(new[] { session }));
        sessions.Setup(c => c.FindAsync(It.IsAny<FilterDefinition<JourneySession>>(),
            It.IsAny<FindOptions<JourneySession, BsonDocument>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Cursor(new[] { new BsonDocument("_id", session.Id) }));
        var operations = new List<UpdateOneModel<JourneyEvent>>();
        events.Setup(c => c.BulkWriteAsync(It.IsAny<IEnumerable<WriteModel<JourneyEvent>>>(),
            It.IsAny<BulkWriteOptions>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<WriteModel<JourneyEvent>>, BulkWriteOptions, CancellationToken>(
                (batch, _, _) =>
                {
                    foreach (var operation in batch) operations.Add(Assert.IsType<UpdateOneModel<JourneyEvent>>(operation));
                })
            .ReturnsAsync(new BulkWriteResult<JourneyEvent>.Acknowledged(1, 0, 0, 0, 0,
                Array.Empty<WriteModel<JourneyEvent>>(), Array.Empty<BulkWriteUpsert>()));
        var request = new TrackJourneyBatchRequest
        {
            Context = new() { SessionId = session.Id, Token = token },
            Events = [new JourneyEventRequest
            {
                Id = Guid.NewGuid().ToString(), Type = "page_view", Page = "/dine-in", OccurredAt = DateTimeOffset.UtcNow
            }]
        };
        var service = new CustomerJourneyService(db.Object);
        await service.TrackAsync(request, default);
        await service.TrackAsync(request, default);
        var registry = BsonSerializer.SerializerRegistry;
        var args = new RenderArgs<JourneyEvent>(registry.GetSerializer<JourneyEvent>(), registry);
        Assert.Equal(2, operations.Count);
        Assert.All(operations, operation =>
        {
            Assert.True(operation.IsUpsert);
            Assert.Equal($"{session.Id}:{request.Events[0].Id}", operation.Filter.Render(args)["_id"].AsString);
            Assert.True(operation.Update.Render(args).AsBsonDocument.Contains("$setOnInsert"));
        });
    }

    private static Mock<IMongoCollection<T>> Collection<T>(Mock<IMongoDatabase> db, string name)
    {
        var collection = new Mock<IMongoCollection<T>>();
        collection.SetupGet(c => c.Indexes).Returns(Mock.Of<IMongoIndexManager<T>>());
        db.Setup(d => d.GetCollection<T>(name, It.IsAny<MongoCollectionSettings>())).Returns(collection.Object);
        return collection;
    }

    private static IAsyncCursor<T> Cursor<T>(IEnumerable<T> rows)
    {
        var cursor = new Mock<IAsyncCursor<T>>();
        cursor.SetupGet(c => c.Current).Returns(rows);
        cursor.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true).ReturnsAsync(false);
        return cursor.Object;
    }
}
