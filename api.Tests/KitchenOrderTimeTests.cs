using Cafe.Api.Helpers;
using Cafe.Api.Models;
using MongoDB.Bson;
using Xunit;

namespace Cafe.Api.Tests;

public class KitchenOrderTimeTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    public void PhoneOrdersUseUtcCreationTimeWithoutAddingFiveAndAHalfHours(bool? marker)
    {
        var createdUtc = new DateTime(2026, 11, 10, 14, 50, 0, DateTimeKind.Utc);
        var order = new Order
        {
            Id = PhoneDeliveryOrderRules.IdempotentOrderId("cashier", "outlet", "phone-order"),
            OrderSource = "phone",
            OrderType = "delivery",
            CreatedAt = createdUtc,
            CreatedAtIsUtc = marker
        };

        var createdAt = OrderDateHelper.ToKitchenCreatedAtIst(order);

        Assert.Equal(new DateTimeOffset(2026, 11, 10, 20, 20, 0, TimeSpan.FromMinutes(330)), createdAt);
        Assert.Equal(6, IstDateHelper.GetSessionElapsedMinutes(createdAt.DateTime, false, createdUtc.AddMinutes(6)));
        Assert.EndsWith("+05:30", System.Text.Json.JsonSerializer.Serialize(createdAt).Trim('"'));
    }

    [Fact]
    public void PhoneOrderHashIsNotUsedToGuessTheTimestampTimezone()
    {
        var createdUtc = new DateTime(2026, 11, 10, 14, 50, 0, DateTimeKind.Utc);
        var order = new Order
        {
            Id = ObjectId.GenerateNewId(createdUtc.AddMinutes(-330)).ToString(),
            OrderSource = "phone",
            OrderType = "delivery",
            CreatedAt = createdUtc
        };

        Assert.True(DineInTimestampHelper.IsLegacyWallClock(order.CreatedAt, order.Id));
        Assert.Equal(createdUtc, OrderDateHelper.ToKitchenCreatedAtIst(order).UtcDateTime);
    }

    [Fact]
    public void ExplicitLegacyPhoneOrderMarkerPreservesIstWallClock()
    {
        var order = new Order
        {
            OrderSource = "phone",
            OrderType = "delivery",
            CreatedAt = new DateTime(2026, 11, 10, 20, 20, 0, DateTimeKind.Utc),
            CreatedAtIsUtc = false
        };

        Assert.Equal(new DateTimeOffset(2026, 11, 10, 20, 20, 0, TimeSpan.FromMinutes(330)),
            OrderDateHelper.ToKitchenCreatedAtIst(order));
    }

    [Fact]
    public void PhoneOrderKitchenDateUsesIstAcrossMidnight()
    {
        var order = new Order
        {
            OrderSource = "phone",
            OrderType = "delivery",
            CreatedAt = new DateTime(2026, 11, 10, 18, 35, 0, DateTimeKind.Utc)
        };

        Assert.Equal(new DateTime(2026, 11, 11), OrderDateHelper.ToKitchenCreatedAtIst(order).Date);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(null, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void DineInElapsedMinutesDoNotIncludeTheIstOffset(bool? marker, bool legacyWallClock)
    {
        var createdUtc = new DateTime(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);
        var order = new Order
        {
            Id = ObjectId.GenerateNewId(createdUtc).ToString(),
            OrderType = "dine-in",
            DineInSessionId = "session",
            CreatedAt = legacyWallClock ? createdUtc.AddMinutes(330) : createdUtc,
            CreatedAtIsUtc = marker
        };

        var createdAt = OrderDateHelper.ToKitchenCreatedAtIst(order);

        Assert.Equal(new DateTimeOffset(2026, 11, 9, 17, 30, 0, TimeSpan.FromMinutes(330)), createdAt);
        Assert.Equal(6, IstDateHelper.GetSessionElapsedMinutes(createdAt.DateTime, false, createdUtc.AddMinutes(6)));
        Assert.Equal(0, IstDateHelper.GetSessionElapsedMinutes(createdAt.DateTime, false, createdUtc.AddMinutes(-1)));
        Assert.EndsWith("+05:30", System.Text.Json.JsonSerializer.Serialize(createdAt).Trim('"'));
    }

    [Fact]
    public void ExplicitUtcMarkerOverridesLegacyHeuristic()
    {
        var idTime = new DateTime(2026, 11, 9, 6, 30, 0, DateTimeKind.Utc);
        var order = new Order
        {
            Id = ObjectId.GenerateNewId(idTime).ToString(),
            OrderType = "dine-in",
            DineInSessionId = "session",
            CreatedAt = idTime.AddMinutes(330),
            CreatedAtIsUtc = true
        };

        Assert.Equal(order.CreatedAt, OrderDateHelper.ToKitchenCreatedAtIst(order).UtcDateTime);
    }

    [Theory]
    [InlineData("delivery")]
    [InlineData("pickup")]
    [InlineData("dine-in")]
    public void UnlinkedUnmarkedOrdersRetainIstWallClockSemantics(string orderType)
    {
        var storedTime = new DateTime(2026, 11, 9, 17, 30, 0, DateTimeKind.Utc);
        var order = new Order
        {
            Id = ObjectId.GenerateNewId(storedTime).ToString(),
            OrderType = orderType,
            CreatedAt = storedTime
        };

        Assert.Equal(new DateTimeOffset(2026, 11, 9, 17, 30, 0, TimeSpan.FromMinutes(330)),
            OrderDateHelper.ToKitchenCreatedAtIst(order));
    }

    [Fact]
    public void KitchenCreationDateCrossesMidnightInIst()
    {
        var order = new Order
        {
            OrderType = "dine-in",
            DineInSessionId = "session",
            CreatedAt = new DateTime(2026, 11, 8, 18, 35, 0, DateTimeKind.Utc)
        };

        Assert.Equal(new DateTime(2026, 11, 9), OrderDateHelper.ToKitchenCreatedAtIst(order).Date);
    }
}
