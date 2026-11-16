using System.Text;
using System.Text.Json;
using Cafe.Api.Models;
using Cafe.Api.Services;
using MongoDB.Bson;
using OfficeOpenXml;
using Xunit;

namespace Cafe.Api.Tests;

public sealed class ZomatoDateRangeTests
{
    private readonly ZomatoSalesPayoutInsightService _service = new();
    private static DateTime Day(int day) => new(2026, 10, day);

    [Fact]
    public void SingleDayRecalculatesOrdersItemsHoursDiscountsPackagingAndRatings()
    {
        var source = Source("week", 26, 31,
            Order("before", 27, 100),
            Order("start", 28, 200),
            Order("end", 28, 300, 23),
            Order("after", 29, 400));
        var result = _service.GenerateRangeInsights([source], Day(28), Day(28));

        Assert.Equal("2026-10-28", result.Period.StartDate);
        Assert.Equal("2026-10-28", result.Period.EndDate);
        Assert.Equal(2, result.OrderHistorySummary.DeliveredOrders);
        Assert.Equal(500m, result.OrderHistorySummary.GrossSales);
        Assert.Equal(250m, result.OrderHistorySummary.AverageOrderValue);
        Assert.Equal(20m, result.OrderHistorySummary.TotalDiscount);
        Assert.Equal(10m, result.OrderHistorySummary.PackagingCharges);
        Assert.Equal(4, Assert.Single(result.ItemSalesInsights).QuantitySold);
        Assert.Equal(new[] { "00:00-01:00", "23:00-00:00" }, result.HourlyOrderInsights.Select(h => h.HourLabel).Order());
        Assert.Equal(2, result.ReviewRatingInsights.RatedOrders);
        Assert.Equal(4m, result.ReviewRatingInsights.AverageRating);
        Assert.Equal(2, result.Reconciliation.MatchedDeliveredOrders);
        Assert.Equal(new[] { "start", "end" }, result.KitchenOrders!.Select(o => o.OrderId));
        Assert.All(result.KitchenOrders!, o => Assert.StartsWith("2026-10-28T", o.PlacedAt));
        Assert.All(result.KitchenOrders!, o => Assert.False(o.TimingsValidated));
        Assert.Equal(400m, result.SettlementSummary.TotalPayout);
        Assert.Null(result.SettlementSummary.ActualPayoutCredited);
        Assert.Null(result.SettlementSummary.AdditionalDeductionsAdjusted);
        Assert.Empty(result.AdditionalDeductionTotals);
    }

    [Fact]
    public void OverlappingUploadsUseLatestOrderAndSettlementWithoutDoubleCounting()
    {
        var old = Source("old", 26, 29, Order("duplicate", 28, 100), Order("old-only", 27, 50));
        var latest = Source("latest", 28, 31, Order("DUPLICATE", 28, 300), Order("new-only", 30, 200));
        latest.UpdatedAt = old.UpdatedAt.AddMinutes(1);

        var result = _service.GenerateRangeInsights([old, latest], Day(26), Day(31));

        Assert.Equal(3, result.OrderHistorySummary.TotalOrders);
        Assert.Equal(550m, result.OrderHistorySummary.GrossSales);
        Assert.Equal(440m, result.SettlementSummary.TotalPayout);
        Assert.Equal(3, result.SettlementSummary.TotalOrders);
        Assert.Equal(3, result.KitchenOrders!.Count);
        Assert.Equal(300m, Assert.Single(result.KitchenOrders!, o => o.OrderId == "DUPLICATE").OrderValue);
        Assert.Null(result.SettlementSummary.ActualPayoutCredited);
    }

    [Fact]
    public void LatestOrderIsDeduplicatedBeforeFilteringItsCorrectedDate()
    {
        var old = Source("old", 26, 31, Order("moved", 28, 100));
        var latest = Source("latest", 27, 31, Order("moved", 29, 300));
        latest.UpdatedAt = old.UpdatedAt.AddMinutes(1);

        var result = _service.GenerateRangeInsights([old, latest], Day(28), Day(28));
        Assert.Equal(0, result.OrderHistorySummary.TotalOrders);
        Assert.Empty(result.KitchenOrders!);
        Assert.Equal(0m, result.SettlementSummary.TotalPayout);
    }

    [Fact]
    public void CompleteAdjacentReportsCombineGrandTotalsWithoutRepeatingSectionSubtotals()
    {
        var first = Source("first", 26, 28, Order("one", 27, 100));
        var second = Source("second", 29, 31, Order("two", 30, 200));
        SetAdjustments(first, 10, 2);
        SetAdjustments(second, 20, 3);

        var result = _service.GenerateRangeInsights([first, second], Day(26), Day(31));

        Assert.Equal(240m, result.SettlementSummary.SettledPayout);
        Assert.Equal(30m, result.SettlementSummary.AdditionalDeductionsAdjusted);
        Assert.Equal(5m, result.SettlementSummary.AdditionalAdditionsAdjusted);
        Assert.Equal(215m, result.SettlementSummary.ActualPayoutCredited);
        Assert.Equal(2, result.AdditionalDeductionTotals.Count);
    }

    [Fact]
    public void MixedGrandTotalsAndDetailFallbackPreserveEachReportsAdjustedAmount()
    {
        var first = Source("first", 26, 28, Order("one", 27, 100));
        SetAdjustments(first, 10, 0);
        var second = Source("second", 29, 31, Order("two", 30, 100));
        var data = ReadSource(second);
        data.AdditionalDeductions.Add(new() { AdjustedAmount = 7 });
        second.SourceData = Serialize(data);

        var result = _service.GenerateRangeInsights([first, second], Day(26), Day(31));
        Assert.Equal(17m, result.SettlementSummary.AdditionalDeductionsAdjusted);
        Assert.Equal(143m, result.SettlementSummary.ActualPayoutCredited);
    }

    [Fact]
    public void CoveredZeroOrderDaysAreValidAndKeepRequestedPeriod()
    {
        var result = _service.GenerateRangeInsights(
            [Source("week", 26, 31, Order("one", 27, 100))], Day(28), Day(28));
        Assert.Empty(result.DailySales);
        Assert.Equal(0, result.OrderHistorySummary.TotalOrders);
        Assert.Equal("2026-10-28", result.Period.StartDate);
    }

    [Fact]
    public void MissingCoverageAndLegacySnapshotsFailInsteadOfReturningPartialTotals()
    {
        var first = Source("first", 26, 27, Order("one", 27, 100));
        var second = Source("second", 29, 31, Order("two", 30, 100));
        var legacy = Source("legacy", 28, 28);
        legacy.SourceData = null;

        var error = Assert.Throws<InvalidOperationException>(() =>
            _service.GenerateRangeInsights([first, legacy, second], Day(26), Day(31)));
        Assert.Contains("2026-10-28", error.Message);
        Assert.Contains("re-uploaded", error.Message);
        Assert.Throws<InvalidOperationException>(() =>
            _service.GenerateRangeInsights([], Day(26), Day(31)));
        Assert.Throws<ArgumentException>(() =>
            _service.GenerateRangeInsights([first], Day(31), Day(26)));
    }

    [Fact]
    public void UndatedSettlementUsesMatchingOrderDateButUnknownDatesAreRejected()
    {
        var source = Source("week", 26, 31, Order("one", 28, 100));
        var data = ReadSource(source);
        data.Settlements[0].OrderDate = null;
        source.SourceData = Serialize(data);
        var result = _service.GenerateRangeInsights([source], Day(28), Day(28));
        Assert.Equal(80m, Assert.Single(result.DailySettlement).Payout);

        data.Orders[0].PlacedAt = null;
        source.SourceData = Serialize(data);
        Assert.Throws<InvalidOperationException>(() =>
            _service.GenerateRangeInsights([source], Day(28), Day(28)));
    }

    [Fact]
    public async Task ParsedSourceRowsSurvivePersistenceAndCanRecalculateDifferentDates()
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        using var csv = new MemoryStream(Encoding.UTF8.GetBytes(
            "Order ID,Order Placed At,Order Status,Items in order,Total,Packaging charges,Rating\n" +
            "one,\"06:02 PM, October 27 2026\",Delivered,2 x Tea,100,5,4\n" +
            "two,\"09:00 PM, October 28 2026\",Delivered,1 x Coffee,200,10,5\n"));
        using var workbook = new ExcelPackage();
        var sheet = workbook.Workbook.Worksheets.Add("Order Level");
        sheet.Cells[1, 1].Value = "S No";
        sheet.Cells[1, 2].Value = "Order ID";
        sheet.Cells[1, 3].Value = "Order Date";
        sheet.Cells[2, 1].Value = 1;
        sheet.Cells[2, 2].Value = "one";
        sheet.Cells[2, 3].Value = "2026-10-27";
        using var excel = new MemoryStream(workbook.GetAsByteArray());
        var parsed = await _service.ParseReportAsync(csv, excel);
        Assert.Equal(2, parsed.Insights.OrderHistorySummary.DeliveredOrders);
        var record = Source("parsed", 27, 28);
        record.SourceData = Serialize(parsed.SourceData);

        var result = _service.GenerateRangeInsights([record], Day(28), Day(28));
        Assert.Equal(200m, result.OrderHistorySummary.GrossSales);
        Assert.Equal(10m, result.OrderHistorySummary.PackagingCharges);
        Assert.Equal("21:00-22:00", result.OrderHistorySummary.PeakOrderHour);
        Assert.Equal("Coffee", Assert.Single(result.ItemSalesInsights).ItemName);
    }

    private static OrderHistoryRow Order(string id, int day, decimal total, int hour = 0) => new()
    {
        OrderId = id, PlacedAt = Day(day).AddHours(hour), Status = "Delivered",
        Total = total, ItemsInOrder = "2 x Tea", PackagingCharges = 5, RestaurantDiscountPromo = 10,
        Rating = 4, KptMinutes = 12
    };

    private static ZomatoSalesPayoutInsightRecord Source(string id, int start, int end, params OrderHistoryRow[] orders) => new()
    {
        Id = id, OutletId = "outlet", PeriodStartDate = Day(start), PeriodEndDate = Day(end),
        UpdatedAt = Day(31),
        SourceData = Serialize(new ZomatoReportSourceData
        {
            Orders = orders.ToList(),
            Settlements = orders.Select(o => new SettlementRow
            {
                OrderId = o.OrderId, OrderDate = o.PlacedAt, NetOrderValue = o.Total,
                OrderLevelPayout = o.Total * 0.8m, NetDeductions = o.Total * 0.2m, SettlementStatus = "settled"
            }).ToList()
        })
    };

    private static void SetAdjustments(ZomatoSalesPayoutInsightRecord record, decimal deductions, decimal additions)
    {
        var source = ReadSource(record);
        source.AdditionalTotals =
        [
            new() { Label = "Total Ads & Other", AdjustedAmount = deductions },
            new() { Label = "Total Deductions", AdjustedAmount = deductions },
            new() { Label = "Total Additions", AdjustedAmount = additions }
        ];
        record.SourceData = Serialize(source);
    }

    private static BsonDocument Serialize(ZomatoReportSourceData source) =>
        BsonDocument.Parse(JsonSerializer.Serialize(source));

    private static ZomatoReportSourceData ReadSource(ZomatoSalesPayoutInsightRecord record) =>
        JsonSerializer.Deserialize<ZomatoReportSourceData>(record.SourceData!.ToJson())!;
}
