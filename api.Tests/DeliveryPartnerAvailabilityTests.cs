using Cafe.Api.Helpers;
using Cafe.Api.Models;
using Xunit;

namespace Cafe.Api.Tests;

public sealed class DeliveryPartnerAvailabilityTests
{
    private static Outlet Shop(string opening = "08:00", string closing = "22:00") => new()
    {
        Settings = new OutletSettings { OpeningTime = opening, ClosingTime = closing }
    };

    [Theory]
    [InlineData("2026-11-14T07:59:59+05:30", false)]
    [InlineData("2026-11-14T08:00:00+05:30", true)]
    [InlineData("2026-11-14T21:59:59+05:30", true)]
    [InlineData("2026-11-14T22:00:00+05:30", false)]
    [InlineData("2026-11-14T02:30:00Z", true)]
    public void ShopBoundariesUseIstAndDoNotRequireAShift(string timestamp, bool online)
    {
        var partner = new DeliveryPartner { Status = "offline" };
        DeliveryPartnerAvailability.Apply(partner, Shop(), DateTimeOffset.Parse(timestamp));
        Assert.Equal(online, partner.Availability!.IsOnline);
        Assert.Equal(online ? "available" : "offline", partner.Status);
    }

    [Theory]
    [InlineData("2026-11-14T19:59:59+05:30", false)]
    [InlineData("2026-11-14T20:00:00+05:30", true)]
    [InlineData("2026-11-15T01:59:59+05:30", true)]
    [InlineData("2026-11-15T02:00:00+05:30", false)]
    public void OvernightHoursSpanMidnight(string timestamp, bool online) =>
        Assert.Equal(online, DeliveryPartnerAvailability.Calculate(Shop("20:00", "02:00"), null,
            DateTimeOffset.Parse(timestamp)).IsOnline);

    [Theory]
    [InlineData("08:00", "22:00", "2026-11-14T12:00:00+05:30", "2026-11-15T08:00:00+05:30")]
    [InlineData("20:00", "02:00", "2026-11-15T01:00:00+05:30", "2026-11-15T20:00:00+05:30")]
    public void ManualPauseExpiresAtTheNextOpening(string opening, string closing, string timestamp, string nextOpening)
    {
        var shop = Shop(opening, closing);
        var now = DateTimeOffset.Parse(timestamp);
        var schedule = DeliveryPartnerAvailability.Calculate(shop, null, now);
        Assert.Equal(DateTimeOffset.Parse(nextOpening), schedule.NextOpeningAtUtc);
        var pausedUntil = schedule.NextOpeningAtUtc!.Value.UtcDateTime;
        Assert.False(DeliveryPartnerAvailability.Calculate(shop, pausedUntil, now).IsOnline);
        Assert.False(DeliveryPartnerAvailability.Calculate(shop, pausedUntil,
            schedule.NextOpeningAtUtc.Value.AddSeconds(-1)).IsOnline);
        Assert.True(DeliveryPartnerAvailability.Calculate(shop, pausedUntil, schedule.NextOpeningAtUtc.Value).IsOnline);
    }

    [Fact]
    public void PausingAndClosingPreserveWorkAndCompletionCannotUndoThePause()
    {
        var now = DateTimeOffset.Parse("2026-11-14T12:00:00+05:30");
        var partner = new DeliveryPartner { Status = "on-delivery", CurrentOrderId = "active-order",
            AvailabilityPausedUntilUtc = DateTimeOffset.Parse("2026-11-15T08:00:00+05:30").UtcDateTime };
        DeliveryPartnerAvailability.Apply(partner, Shop(), now);
        Assert.False(partner.Availability!.IsOnline);
        Assert.Equal("active-order", partner.CurrentOrderId);
        partner.AvailabilityPausedUntilUtc = null;
        DeliveryPartnerAvailability.Apply(partner, Shop(), now);
        Assert.True(partner.Availability!.IsOnline);
        Assert.Equal("on-delivery", partner.Status);
        DeliveryPartnerAvailability.Apply(partner, Shop(), now.AddHours(10));
        Assert.False(partner.Availability!.IsOnline);
        Assert.Equal("active-order", partner.CurrentOrderId);
        partner.Status = "available";
        partner.CurrentOrderId = null;
        partner.AvailabilityPausedUntilUtc = now.AddDays(1).UtcDateTime;
        DeliveryPartnerAvailability.Apply(partner, Shop(), now);
        Assert.Equal("offline", partner.Status);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void InactiveAndDeletedOutletsCannotReceiveDeliveries(bool active, bool deleted)
    {
        var shop = Shop();
        shop.IsActive = active;
        shop.IsDeleted = deleted;
        Assert.False(DeliveryPartnerAvailability.Calculate(shop, null, DateTimeOffset.UtcNow).IsOnline);
        Assert.False(DeliveryPartnerAvailability.Calculate(null, null, DateTimeOffset.UtcNow).IsOnline);
    }

    [Theory]
    [InlineData("invalid", "22:00")]
    [InlineData("08:00", "25:00")]
    [InlineData("08:00", "08:00")]
    public void InvalidShopHoursSurfaceAConfigurationError(string opening, string closing) =>
        Assert.Throws<InvalidOperationException>(() =>
            DeliveryPartnerAvailability.Calculate(Shop(opening, closing), null, DateTimeOffset.UtcNow));
}
