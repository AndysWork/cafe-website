using System.Text;
using System.Text.Json;
using Cafe.Api.Services;
using OfficeOpenXml;
using Xunit;

namespace Cafe.Api.Tests;

public sealed class ZomatoKitchenTests
{
    [Theory]
    [InlineData("0", "valid", 0)]
    [InlineData(" 12.5 ", "valid", 12.5)]
    [InlineData("", "missing", -1)]
    [InlineData(" ", "missing", -1)]
    [InlineData("-5", "invalid", -1)]
    [InlineData("bad0", "invalid", -1)]
    [InlineData("NaN", "invalid", -1)]
    [InlineData("1,2", "invalid", -1)]
    [InlineData("10 min", "invalid", -1)]
    public async Task DurationsPreserveZeroAndClassifyMissingAndInvalidInput(string input, string status, double expected)
    {
        var parsed = await Parse($"one,\"06:02 PM, October 27 2026\",Delivered,1 x Tea,100,\"{input}\",\"{input}\",4\n");
        var order = Assert.Single(parsed.Insights.KitchenOrders!);
        Assert.Equal(status, order.KptStatus);
        Assert.Equal(status, order.RiderWaitStatus);
        Assert.Equal(expected < 0 ? (decimal?)null : (decimal)expected, order.KptMinutes);
        Assert.Equal(order.KptMinutes, order.RiderWaitMinutes);
        Assert.True(order.TimingsValidated);
        Assert.Equal(expected < 0 ? 0m : (decimal)expected, parsed.Insights.OrderHistorySummary.AverageKptMinutes);
        Assert.Equal("2026-10-27T18:02:00", order.PlacedAt);
    }

    [Fact]
    public async Task DeliveredFactsConsolidateItemsAndSurviveSourceAndSummarySerialization()
    {
        var parsed = await Parse(
            "one,\"06:02 PM, October 27 2026\",Delivered,\"2 x Tea, 1 x tea, 1 x Coffee\",400,25,0,4\n" +
            "two,\"09:00 PM, October 28 2026\",Delivered,1 x Coffee,100,10,7,9\n" +
            "cancelled,\"09:00 PM, October 28 2026\",Cancelled,1 x Coffee,100,99,99,1\n");
        Assert.Equal(2, parsed.Insights.KitchenOrders!.Count);
        var first = parsed.Insights.KitchenOrders[0];
        Assert.Equal(4, first.Quantity);
        Assert.Equal(2, first.DistinctItems);
        Assert.Equal(3, Assert.Single(first.Items, i => i.Name.Equals("Tea", StringComparison.OrdinalIgnoreCase)).Quantity);
        Assert.Null(parsed.Insights.KitchenOrders[1].Rating);
        var persisted = JsonSerializer.Deserialize<ZomatoReportSourceData>(JsonSerializer.Serialize(parsed.SourceData))!;
        Assert.Equal("valid", persisted.Orders[0].KptDataStatus);
        Assert.Equal(0m, persisted.Orders[0].RiderWaitMinutes);
        var summary = JsonSerializer.Deserialize<ZomatoSalesPayoutInsightResult>(JsonSerializer.Serialize(parsed.Insights))!;
        Assert.Equal(2, summary.KitchenOrders!.Count);
        Assert.True(summary.KitchenOrders[0].TimingsValidated);
    }

    private static async Task<ParsedZomatoReport> Parse(string rows)
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        using var csv = new MemoryStream(Encoding.UTF8.GetBytes(
            "Order ID,Order Placed At,Order Status,Items in order,Total,KPT duration (minutes),Rider wait time (minutes),Rating\n" + rows));
        using var workbook = new ExcelPackage();
        var sheet = workbook.Workbook.Worksheets.Add("Order Level");
        sheet.Cells[1, 1].Value = "S No";
        sheet.Cells[1, 2].Value = "Order ID";
        sheet.Cells[1, 3].Value = "Order Date";
        sheet.Cells[2, 1].Value = 1;
        sheet.Cells[2, 2].Value = "one";
        sheet.Cells[2, 3].Value = "2026-10-27";
        using var excel = new MemoryStream(workbook.GetAsByteArray());
        return await new ZomatoSalesPayoutInsightService().ParseReportAsync(csv, excel);
    }
}
