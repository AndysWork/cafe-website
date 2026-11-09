using Cafe.Api.Helpers;
using Cafe.Api.Models;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Xunit;

namespace Cafe.Api.Tests;

public class OrderDateTests
{
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
        var utc = alternatives.Single(x => x["createdAtIsUtc"] == BsonBoolean.True);
        var legacy = alternatives.Single(x => x["createdAtIsUtc"].IsBsonDocument);
        Assert.Equal(new DateTime(2026, 11, 7, 18, 30, 0, DateTimeKind.Utc), utc["createdAt"]["$gte"].ToUniversalTime());
        Assert.Equal(new DateTime(2026, 11, 8, 18, 30, 0, DateTimeKind.Utc), utc["createdAt"]["$lt"].ToUniversalTime());
        Assert.Equal(new DateTime(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc), legacy["createdAt"]["$gte"].ToUniversalTime());
        Assert.Equal(new DateTime(2026, 11, 9, 0, 0, 0, DateTimeKind.Utc), legacy["createdAt"]["$lt"].ToUniversalTime());
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
