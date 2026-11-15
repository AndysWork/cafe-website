using Cafe.Api.Helpers;
using Cafe.Api.Models;
using Cafe.Api.Services;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Moq;
using System.Runtime.CompilerServices;
using Xunit;

namespace Cafe.Api.Tests;

public class OrderDateTests
{
    [Fact]
    public async Task DashboardFinanceQueriesReturnMoreThanFiveThousandRecordsWithoutApplyingLimits()
    {
        var online = new Mock<IMongoCollection<OnlineSale>>();
        var sales = new Mock<IMongoCollection<Sales>>();
        var expenses = new Mock<IMongoCollection<Expense>>();
        var orders = new Mock<IMongoCollection<Order>>();
        var onlineOptions = new List<FindOptions<OnlineSale, OnlineSale>>();
        var salesOptions = new List<FindOptions<Sales, Sales>>();
        var expenseOptions = new List<FindOptions<Expense, Expense>>();
        var orderOptions = new List<FindOptions<Order, Order>>();
        var onlineFilters = new List<FilterDefinition<OnlineSale>>();
        var salesFilters = new List<FilterDefinition<Sales>>();
        var expenseFilters = new List<FilterDefinition<Expense>>();
        var date = new DateTime(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc);
        var manyOnline = Enumerable.Range(0, 5001).Select(_ => new OnlineSale { OrderAt = date }).ToList();
        var manySales = Enumerable.Range(0, 5001).Select(_ => new Sales { Date = date }).ToList();
        var manyExpenses = Enumerable.Range(0, 5001).Select(_ => new Expense { Date = date }).ToList();
        var manyOrders = Enumerable.Range(0, 5001).Select(_ => new Order { CreatedAt = date, Status = "delivered" }).ToList();

        online.Setup(collection => collection.FindAsync(It.IsAny<FilterDefinition<OnlineSale>>(),
                It.IsAny<FindOptions<OnlineSale, OnlineSale>>(), It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<OnlineSale>, FindOptions<OnlineSale, OnlineSale>, CancellationToken>((filter, options, _) => { onlineFilters.Add(filter); onlineOptions.Add(options); })
            .ReturnsAsync(() => Cursor(manyOnline));
        sales.Setup(collection => collection.FindAsync(It.IsAny<FilterDefinition<Sales>>(),
                It.IsAny<FindOptions<Sales, Sales>>(), It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<Sales>, FindOptions<Sales, Sales>, CancellationToken>((filter, options, _) => { salesFilters.Add(filter); salesOptions.Add(options); })
            .ReturnsAsync(() => Cursor(manySales));
        expenses.Setup(collection => collection.FindAsync(It.IsAny<FilterDefinition<Expense>>(),
                It.IsAny<FindOptions<Expense, Expense>>(), It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<Expense>, FindOptions<Expense, Expense>, CancellationToken>((filter, options, _) => { expenseFilters.Add(filter); expenseOptions.Add(options); })
            .ReturnsAsync(() => Cursor(manyExpenses));
        orders.Setup(collection => collection.FindAsync(It.IsAny<FilterDefinition<Order>>(),
                It.IsAny<FindOptions<Order, Order>>(), It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<Order>, FindOptions<Order, Order>, CancellationToken>((_, options, _) => orderOptions.Add(options))
            .ReturnsAsync(() => Cursor(orderOptions.Count == 1 ? manyOrders : new List<Order>()));

        var service = (MongoService)RuntimeHelpers.GetUninitializedObject(typeof(MongoService));
        foreach (var (name, value) in new (string, object)[] {
            ("_onlineSales", online.Object), ("_sales", sales.Object),
            ("_expenses", expenses.Object), ("_orders", orders.Object)
        })
            typeof(MongoService).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(service, value);

        var from = new DateTime(2026, 11, 8);
        var end = from.AddDays(1);
        const string outlet = "222222222222222222222222";
        Assert.Equal(10002, (await service.GetDashboardOnlineSalesAsync(outlet, from, end)).Count);
        Assert.Equal(5001, (await service.GetDashboardSalesAsync(outlet, date, date.AddDays(1))).Count);
        Assert.Equal(5001, (await service.GetDashboardExpensesAsync(outlet, date, date.AddDays(1))).Count);
        Assert.Null(Assert.Single(onlineOptions).Limit);
        Assert.Null(Assert.Single(salesOptions).Limit);
        Assert.Null(Assert.Single(expenseOptions).Limit);
        Assert.Equal(2, orderOptions.Count);
        Assert.All(orderOptions, options => Assert.Null(options.Limit));
        var registry = BsonSerializer.SerializerRegistry;
        var onlineRange = Assert.Single(onlineFilters).Render(new RenderArgs<OnlineSale>(registry.GetSerializer<OnlineSale>(), registry))["orderAt"].AsBsonDocument;
        Assert.Equal(new DateTime(2026, 11, 7, 18, 30, 0, DateTimeKind.Utc), onlineRange["$gte"].ToUniversalTime());
        Assert.Equal(new DateTime(2026, 11, 8, 18, 30, 0, DateTimeKind.Utc), onlineRange["$lt"].ToUniversalTime());
        var salesRange = Assert.Single(salesFilters).Render(new RenderArgs<Sales>(registry.GetSerializer<Sales>(), registry))["date"].AsBsonDocument;
        var expenseRange = Assert.Single(expenseFilters).Render(new RenderArgs<Expense>(registry.GetSerializer<Expense>(), registry))["date"].AsBsonDocument;
        Assert.Equal(date, salesRange["$gte"].ToUniversalTime());
        Assert.Equal(date.AddDays(1), salesRange["$lt"].ToUniversalTime());
        Assert.Equal(date, expenseRange["$gte"].ToUniversalTime());
        Assert.Equal(date.AddDays(1), expenseRange["$lt"].ToUniversalTime());
    }

    private static IAsyncCursor<T> Cursor<T>(IEnumerable<T> rows)
    {
        var cursor = new Mock<IAsyncCursor<T>>();
        cursor.SetupGet(value => value.Current).Returns(rows);
        cursor.SetupSequence(value => value.MoveNextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true).ReturnsAsync(false);
        return cursor.Object;
    }

    [Fact]
    public async Task DashboardOrderQueryCombinesPeriodWithLiveQueuesAndRestrictsTheOutlet()
    {
        var orders = new Mock<IMongoCollection<Order>>();
        FilterDefinition<Order>? capturedFilter = null;
        FindOptions<Order, Order>? capturedOptions = null;
        orders.Setup(collection => collection.FindAsync(It.IsAny<FilterDefinition<Order>>(),
                It.IsAny<FindOptions<Order, Order>>(), It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<Order>, FindOptions<Order, Order>, CancellationToken>(
                (filter, options, _) => { capturedFilter = filter; capturedOptions = options; })
            .ReturnsAsync(() => Cursor(new List<Order>()));
        var service = (MongoService)RuntimeHelpers.GetUninitializedObject(typeof(MongoService));
        typeof(MongoService).GetField("_orders", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(service, orders.Object);
        const string outlet = "222222222222222222222222";
        await service.GetDashboardOrdersAsync(outlet, new DateTime(2026, 11, 8), new DateTime(2026, 11, 9), 2, 500);

        var registry = BsonSerializer.SerializerRegistry;
        var filter = capturedFilter!.Render(new RenderArgs<Order>(registry.GetSerializer<Order>(), registry));
        Assert.Equal(ObjectId.Parse(outlet), filter["outletId"].AsObjectId);
        var text = filter["$or"].ToJson();
        Assert.Contains("createdAt", text);
        Assert.Contains("pending", text);
        Assert.Contains("confirmed", text);
        Assert.Contains("preparing", text);
        Assert.Contains("ready", text);
        Assert.Contains("delivery", text);
        Assert.Contains("pending_verification", text);
        Assert.Equal(500, capturedOptions!.Skip);
        Assert.Equal(500, capturedOptions.Limit);
    }

    [Theory]
    [InlineData(null, "pickup", null, true, "2026-11-09T23:55:00Z", "2026-11-09T23:55:00")]
    [InlineData(false, "pickup", null, true, "2026-11-09T23:55:00Z", "2026-11-09T23:55:00")]
    [InlineData(null, "dine-in", null, true, "2026-11-09T23:55:00Z", "2026-11-09T23:55:00")]
    [InlineData(true, "pickup", null, false, "2026-11-09T18:35:00Z", "2026-11-10T00:05:00")]
    [InlineData(null, "dine-in", null, false, "2026-11-09T18:35:00Z", "2026-11-10T00:05:00")]
    [InlineData(null, "delivery", "phone", false, "2026-11-09T18:35:00Z", "2026-11-10T00:05:00")]
    public void WebSalesSerializeNormalizedIstOrderTime(
        bool? marker, string orderType, string? source, bool legacy, string timestamp, string expected)
    {
        var createdAt = DateTime.Parse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        var order = new Order
        {
            Id = ObjectId.GenerateNewId(legacy ? createdAt.AddMinutes(-330) : createdAt).ToString(),
            CreatedAt = createdAt,
            CreatedAtIsUtc = marker,
            OrderType = orderType,
            OrderSource = source,
            DineInSessionId = orderType == "dine-in" ? "session" : null
        };
        var map = typeof(MongoService).GetMethod("MapOrderToWebSale", BindingFlags.NonPublic | BindingFlags.Static)!
            .CreateDelegate<Func<Order, OnlineSale>>();
        using var response = JsonDocument.Parse(JsonSerializer.Serialize(map(order)));

        Assert.Equal(expected, response.RootElement.GetProperty("OrderAt").GetString());
    }

    [Theory]
    [InlineData(true, "2026-11-07T18:29:59Z", false)]
    [InlineData(true, "2026-11-07T18:30:00Z", true)]
    [InlineData(true, "2026-11-08T18:29:59Z", true)]
    [InlineData(true, "2026-11-08T18:30:00Z", false)]
    [InlineData(false, "2026-11-07T23:59:59Z", false)]
    [InlineData(false, "2026-11-08T00:00:00Z", true)]
    [InlineData(false, "2026-11-08T23:59:59Z", true)]
    [InlineData(false, "2026-11-09T00:00:00Z", false)]
    public void FiltersUseIstCalendarDay(bool isUtc, string timestamp, bool expected)
    {
        var order = new Order { CreatedAt = DateTime.Parse(timestamp).ToUniversalTime(), CreatedAtIsUtc = isUtc ? true : null };
        Assert.Equal(expected, OrderDateHelper.IsCreatedInRange(order, new DateTime(2026, 11, 8), new DateTime(2026, 11, 9)));
    }

    [Fact]
    public void MongoFilterUsesSeparateUtcAndLegacyBounds()
    {
        var filter = OrderDateHelper.CreatedInRange(new DateTime(2026, 11, 8), new DateTime(2026, 11, 9));
        var registry = BsonSerializer.SerializerRegistry;
        var rendered = filter.Render(new RenderArgs<Order>(registry.GetSerializer<Order>(), registry));
        var alternatives = rendered["$or"].AsBsonArray.Select(x => x.AsBsonDocument).ToArray();
        var bounds = alternatives.Select(x => x["createdAt"].AsBsonDocument).ToArray();
        Assert.Contains(bounds, x => x["$gte"].ToUniversalTime() == new DateTime(2026, 11, 7, 18, 30, 0, DateTimeKind.Utc)
            && x["$lt"].ToUniversalTime() == new DateTime(2026, 11, 8, 18, 30, 0, DateTimeKind.Utc));
        Assert.Contains(bounds, x => x["$gte"].ToUniversalTime() == new DateTime(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc)
            && x["$lt"].ToUniversalTime() == new DateTime(2026, 11, 9, 0, 0, 0, DateTimeKind.Utc));
        Assert.Contains("$convert", rendered.ToJson());
        Assert.Contains("orderSource", rendered.ToJson());
        Assert.DoesNotContain("\"$not\"", rendered.ToJson());
        Assert.Contains("\"$nor\"", rendered.ToJson());
    }

    [Fact]
    public void UnmarkedDineInAndPhoneOrdersUseUtcAcrossIstMidnight()
    {
        var timestamp = new DateTime(2026, 11, 8, 18, 35, 0, DateTimeKind.Utc);
        var dineIn = new Order { Id = ObjectId.GenerateNewId(timestamp).ToString(), CreatedAt = timestamp,
            OrderType = "dine-in", DineInSessionId = "session" };
        var phone = new Order { CreatedAt = timestamp, OrderSource = "phone" };
        var start = new DateTime(2026, 11, 9);

        foreach (var order in new[] { dineIn, phone })
        {
            Assert.True(OrderDateHelper.IsCreatedAtUtc(order));
            Assert.Equal(new DateTime(2026, 11, 9, 0, 5, 0), OrderDateHelper.ToIstCreatedAt(order));
            Assert.True(OrderDateHelper.IsCreatedInRange(order, start, start.AddDays(1)));
        }
    }

    [Fact]
    public void UnmarkedLegacyDineInOrderPreservesIstWallClock()
    {
        var createdUtc = new DateTime(2026, 11, 8, 18, 35, 0, DateTimeKind.Utc);
        var legacy = new Order { Id = ObjectId.GenerateNewId(createdUtc).ToString(),
            CreatedAt = createdUtc.AddMinutes(330), OrderType = "dine-in", DineInSessionId = "session" };
        Assert.False(OrderDateHelper.IsCreatedAtUtc(legacy));
        Assert.Equal(new DateTime(2026, 11, 9, 0, 5, 0), OrderDateHelper.ToIstCreatedAt(legacy));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("false", null)]
    [InlineData("true", true)]
    public void RolloutMarkerIsOptIn(string? setting, bool? expected)
    {
        Assert.Equal(expected, IstDateHelper.ParseUtcTimestampMarker(setting));
    }

    [Fact]
    public void InvalidRolloutSettingFailsExplicitly()
    {
        Assert.Throws<InvalidOperationException>(() => IstDateHelper.ParseUtcTimestampMarker("enabled"));
    }

    [Fact]
    public void MarkedSettlementAndEventUseUtcForLegacySession()
    {
        var createdUtc = new DateTime(2026, 11, 8, 16, 0, 0, DateTimeKind.Utc);
        var id = ObjectId.GenerateNewId(createdUtc).ToString();
        var legacyCreated = createdUtc.AddHours(5).AddMinutes(30);
        Assert.True(DineInTimestampHelper.IsLegacyWallClock(legacyCreated, id));
        Assert.Equal(new DateTimeOffset(2026, 11, 8, 21, 30, 0, TimeSpan.FromMinutes(330)),
            DineInTimestampHelper.ToIstOffset(legacyCreated, null, true));

        var laterUtc = new DateTime(2026, 11, 8, 18, 45, 0, DateTimeKind.Utc);
        Assert.Equal(new DateTimeOffset(2026, 11, 9, 0, 15, 0, TimeSpan.FromMinutes(330)),
            DineInTimestampHelper.ToIstOffset(laterUtc, true, true));
    }

    [Fact]
    public void LatestOrdersAreSelectedAfterNormalizingBothRepresentations()
    {
        var marked = new Order { Id = "2", CreatedAt = new DateTime(2026, 11, 8, 17, 0, 0, DateTimeKind.Utc), CreatedAtIsUtc = true };
        var legacy = new Order { Id = "1", CreatedAt = new DateTime(2026, 11, 8, 21, 30, 0, DateTimeKind.Utc) };
        var older = new Order { Id = "3", CreatedAt = new DateTime(2026, 11, 8, 15, 0, 0, DateTimeKind.Utc), CreatedAtIsUtc = true };

        var result = OrderDateHelper.TakeLatest(new[] { legacy, older, marked }, 2);

        Assert.Equal(new[] { marked, legacy }, result);
    }

    [Fact]
    public void MergingPerRepresentationLimitsMatchesSortingAllOrders()
    {
        const int limit = 5000;
        var day = new DateTime(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc);
        var orders = Enumerable.Range(0, 12000).Select(i => new Order
        {
            Id = i.ToString("D24"),
            CreatedAt = day.AddSeconds(i * 3),
            CreatedAtIsUtc = i % 2 == 0 ? true : null
        }).ToList();
        var candidates = orders.Where(o => o.CreatedAtIsUtc == true)
            .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id, StringComparer.Ordinal).Take(limit)
            .Concat(orders.Where(o => o.CreatedAtIsUtc != true)
                .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id, StringComparer.Ordinal).Take(limit));

        Assert.Equal(OrderDateHelper.TakeLatest(orders, limit).Select(o => o.Id),
            OrderDateHelper.TakeLatest(candidates, limit).Select(o => o.Id));
    }

    [Fact]
    public void AuditLimitIsAppliedAfterFilteringAndNormalizing()
    {
        var start = new DateTime(2026, 11, 8);
        var end = start.AddDays(1);
        var selector = new DineInAuditSelector(start, end, end, 2);
        var latestUtc = new DateTime(2026, 11, 8, 17, 0, 0, DateTimeKind.Utc);
        var legacyUtc = latestUtc.AddHours(-1);
        var latest = new EventLog { Id = ObjectId.GenerateNewId(latestUtc).ToString(), Timestamp = latestUtc };
        var legacy = new EventLog { Id = ObjectId.GenerateNewId(legacyUtc).ToString(), Timestamp = legacyUtc.AddMinutes(330) };
        var older = new EventLog { Id = ObjectId.GenerateNewId(legacyUtc.AddHours(-1)).ToString(), Timestamp = legacyUtc.AddHours(-1) };
        selector.Add(legacy);
        selector.Add(older);
        selector.Add(latest);

        var outsideDay = new DateTime(2026, 11, 8, 19, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 1100; i++)
            selector.Add(new EventLog { Id = ObjectId.GenerateNewId(outsideDay).ToString(), Timestamp = outsideDay });

        Assert.Equal(new[] { latest, legacy }, selector.GetLatest());
    }

    [Fact]
    public void UnspecifiedFinancialDatesUseHostLocalTimezoneWhenSerialized()
    {
        var date = new DateTime(2026, 11, 8, 0, 0, 0, DateTimeKind.Unspecified);
        var expectedUtc = date.ToUniversalTime();
        Assert.Equal(expectedUtc, new Expense { Date = date }.ToBsonDocument()["date"].ToUniversalTime());
        Assert.Equal(expectedUtc, new Sales { Date = date }.ToBsonDocument()["date"].ToUniversalTime());
    }
}
