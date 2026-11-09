using Cafe.Api.Helpers;
using Cafe.Api.Models;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Xunit;

namespace Cafe.Api.Tests;

public class IstDateHelperTests
{
    [Theory]
    [InlineData("08/11/2026", 2026, 11, 8)]
    [InlineData("8/11/2026", 2026, 11, 8)]
    [InlineData("08-11-2026", 2026, 11, 8)]
    [InlineData("2026-11-08", 2026, 11, 8)]
    public void ImportDatesUseDayMonthYear(string input, int year, int month, int day)
    {
        Assert.True(IstDateHelper.TryParseImportDate(input, out var date));
        Assert.Equal(new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Unspecified), date);
        Assert.Equal(DateTimeKind.Unspecified, date.Kind);
    }

    [Theory]
    [InlineData("11/30/2026")]
    [InlineData("2026-11-08T10:00:00Z")]
    [InlineData("not a date")]
    public void ImportDatesRejectUnsupportedValues(string input)
    {
        Assert.False(IstDateHelper.TryParseImportDate(input, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("08/11/2026")]
    [InlineData("2026-11-08T10:00:00Z")]
    public void IsoDateRejectsInvalidDeleteFilters(string input)
    {
        Assert.False(IstDateHelper.TryParseIsoDate(input, out _));
    }

    [Theory]
    [InlineData(true, 6, 25)]
    [InlineData(false, 11, 55)]
    public void SessionElapsedUsesTheCorrectClock(bool isUtc, int hour, int minute)
    {
        var createdAt = new DateTime(2026, 11, 9, hour, minute, 0, DateTimeKind.Utc);
        var now = new DateTime(2026, 11, 9, 6, 30, 0, DateTimeKind.Utc);
        Assert.Equal(5, IstDateHelper.GetSessionElapsedMinutes(createdAt, isUtc, now));
    }

    [Fact]
    public void MissingSessionTimestampMarkerRemainsUnknown()
    {
        var session = BsonSerializer.Deserialize<DineInSession>(new BsonDocument());
        Assert.Null(session.CreatedAtIsUtc);
    }

    [Fact]
    public void TimestampMarkersRoundTrip()
    {
        var order = BsonSerializer.Deserialize<Order>(new Order { CreatedAtIsUtc = true }.ToBson());
        var session = BsonSerializer.Deserialize<DineInSession>(new DineInSession
        {
            CreatedAtIsUtc = true,
            SettledAtIsUtc = true,
            Timeline = new List<DineInSessionEvent> { new() { CreatedAtIsUtc = true } }
        }.ToBson());
        Assert.True(order.CreatedAtIsUtc);
        Assert.True(session.CreatedAtIsUtc);
        Assert.True(session.SettledAtIsUtc);
        Assert.True(Assert.Single(session.Timeline).CreatedAtIsUtc);
    }

    [Fact]
    public void UnknownTopLevelFieldsSurviveRoundTrip()
    {
        var source = new BsonDocument("futureField", "preserve");
        var order = BsonSerializer.Deserialize<Order>(source);
        var session = BsonSerializer.Deserialize<DineInSession>(source);
        Assert.Equal("preserve", order.ToBsonDocument()["futureField"].AsString);
        Assert.Equal("preserve", session.ToBsonDocument()["futureField"].AsString);
    }
}
