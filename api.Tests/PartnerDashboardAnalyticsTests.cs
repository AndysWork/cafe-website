using Cafe.Api.Helpers;
using Cafe.Api.Models;
using Xunit;

namespace Cafe.Api.Tests;

public sealed class PartnerDashboardAnalyticsTests
{
    private static readonly DateTime Today = new(2025, 11, 9);

    private static Order Delivered(DateTime? completed = null, bool utc = false) => new()
    {
        OrderType = "delivery", Status = "delivered", DeliveryPartnerId = "partner",
        CreatedAt = Today.AddHours(10), CreatedAtIsUtc = false,
        CompletedAt = completed ?? Today.AddHours(12), CompletedAtIsUtc = utc,
        UpdatedAt = Today.AddHours(12)
    };

    [Theory]
    [InlineData(9, 0)]
    [InlineData(10, 50)]
    [InlineData(11, 50)]
    [InlineData(20, 50)]
    public void IncentiveUsesOnlyOwnValidCompletedDeliveriesAndPaysOneMilestone(int count, int reward)
    {
        var orders = Enumerable.Range(0, count).Select(_ => Delivered()).ToList();
        var otherPartner = Delivered();
        otherPartner.DeliveryPartnerId = "someone-else";
        var deleted = Delivered();
        deleted.IsDeleted = true;
        var dineIn = Delivered();
        dineIn.OrderType = "dine-in";
        var unfinished = Delivered();
        unfinished.Status = "out-for-delivery";
        var missingTimestamp = Delivered();
        missingTimestamp.CompletedAt = null;
        orders.AddRange(new[] { otherPartner, deleted, dineIn, unfinished, missingTimestamp });

        var result = PartnerDashboardAnalytics.Calculate(orders, "partner", Today, 24.8m, null, 0);

        Assert.Equal(count, result.Today.Completed);
        Assert.Equal(count, result.Incentive.CompletedDeliveries);
        Assert.Equal(reward, result.Incentive.EarnedAmount);
        Assert.Equal(24.8m, result.Performance.TotalDistanceKm);
    }

    [Fact]
    public void CompletionAndAssignmentDatesRespectUtcAndLegacyIstCalendarBoundaries()
    {
        var midnight = Delivered(Today.AddDays(-1).AddHours(18.5), utc: true);
        midnight.DeliveryAssignedAtUtc = Today.AddDays(-1).AddHours(18.5);
        var priorDay = Delivered(Today.AddTicks(-1));
        priorDay.CreatedAt = Today.AddDays(-1);
        var tomorrow = Delivered(Today.AddHours(18.5), utc: true);
        tomorrow.CreatedAt = Today.AddDays(1);
        var weekStart = Delivered(Today.AddDays(-7).AddHours(18.5), utc: true);
        weekStart.CreatedAt = Today.AddDays(-6);
        var outsideWeek = Delivered(Today.AddDays(-7).AddHours(18.5).AddTicks(-1), utc: true);
        outsideWeek.CreatedAt = Today.AddDays(-7);

        var result = PartnerDashboardAnalytics.Calculate(
            new[] { midnight, priorDay, tomorrow, weekStart, outsideWeek }, "partner", Today, 0, null, 0);

        Assert.Equal(1, result.Today.Completed);
        Assert.Equal(1, result.Today.Accepted);
        Assert.Equal(0, result.Today.LegacyAssignmentDates);
        Assert.Equal(3, result.Performance.Completed);
    }

    [Fact]
    public void PerformanceExcludesMissingEtasAndInProgressFromRateDenominators()
    {
        var onTime = Delivered();
        onTime.DeliveryDueAtUtc = Today.AddHours(6.5); // Noon IST.
        var late = Delivered(Today.AddHours(12).AddMinutes(1));
        late.DeliveryRouteUpdatedAt = Today.AddHours(11);
        late.DeliveryEtaMinutes = 60;
        var withoutEta = Delivered();
        var cancelled = Delivered();
        cancelled.Status = "cancelled";
        var unfinished = Delivered();
        unfinished.Status = "preparing";

        var result = PartnerDashboardAnalytics.Calculate(
            new[] { onTime, late, withoutEta, cancelled, unfinished }, "partner", Today, 168, 4.8, 12);

        Assert.Equal(50m, result.Performance.OnTimeRate);
        Assert.Equal(75m, result.Performance.CompletionRate);
        Assert.Equal(2, result.Performance.TimedDeliveries);
        Assert.Equal(1, result.Performance.OnTimeDeliveries);
        Assert.Equal(1, result.Today.Cancelled);
        Assert.Equal(4.8, result.Performance.CustomerRating);
        Assert.Equal(12, result.Performance.Reviews);
    }

    [Fact]
    public void EmptyHistoryHasUnavailableRatesAndNoInventedDemandOrIncentive()
    {
        var result = PartnerDashboardAnalytics.Calculate(Array.Empty<Order>(), "partner", Today, 0, null, 0);
        Assert.Null(result.Performance.OnTimeRate);
        Assert.Null(result.Performance.CompletionRate);
        Assert.Null(result.Performance.CustomerRating);
        Assert.Empty(result.PeakHours);
        Assert.Equal(0, result.Incentive.EarnedAmount);
    }

    [Fact]
    public void PeakWindowsUseOutletDemandAndReturnTwoNonOverlappingObservedWindows()
    {
        var orders = new List<Order>();
        foreach (var hour in new[] { 12, 12, 13, 14, 19, 20, 21 })
        {
            var order = Delivered();
            order.CreatedAt = Today.AddHours(hour);
            order.DeliveryPartnerId = "another-partner";
            orders.Add(order);
        }
        var cancelled = Delivered();
        cancelled.Status = "CANCELLED";
        cancelled.CreatedAt = Today.AddHours(6);
        orders.Add(cancelled);

        var result = PartnerDashboardAnalytics.Calculate(orders, "partner", Today, 0, null, 0);

        Assert.Collection(result.PeakHours,
            p => { Assert.Equal(12, p.StartHour); Assert.Equal(15, p.EndHour); Assert.Equal(4, p.Orders); },
            p => { Assert.Equal(19, p.StartHour); Assert.Equal(22, p.EndHour); Assert.Equal(3, p.Orders); });
    }
}
