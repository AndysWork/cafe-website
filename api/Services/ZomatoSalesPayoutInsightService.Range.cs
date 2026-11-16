using Cafe.Api.Models;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using System.Text.Json;

namespace Cafe.Api.Services;

/// <summary>Parsed insights and the source rows retained for exact-date recalculation.</summary>
public sealed record ParsedZomatoReport(ZomatoSalesPayoutInsightResult Insights, ZomatoReportSourceData SourceData);

public class ZomatoReportSourceData
{
    public List<OrderHistoryRow> Orders { get; set; } = new();
    public List<SettlementRow> Settlements { get; set; } = new();
    public List<AdditionalDeductionRow> AdditionalDeductions { get; set; } = new();
    public List<AdditionalDeductionTotal> AdditionalTotals { get; set; } = new();
}

public partial class ZomatoSalesPayoutInsightService
{
    public ZomatoSalesPayoutInsightResult GenerateRangeInsights(
        IReadOnlyList<ZomatoSalesPayoutInsightRecord> records, DateTime startDate, DateTime endDate)
    {
        var start = DateOnly.FromDateTime(startDate);
        var end = DateOnly.FromDateTime(endDate);
        if (end < start)
            throw new ArgumentException("End date must be on or after start date.");

        var settings = new JsonWriterSettings { OutputMode = JsonOutputMode.RelaxedExtendedJson };
        var sources = records
            .Where(r => DateOnly.FromDateTime(r.PeriodStartDate) <= end &&
                        DateOnly.FromDateTime(r.PeriodEndDate) >= start && r.SourceData != null)
            .OrderByDescending(r => r.UpdatedAt)
            .ThenByDescending(r => r.Id, StringComparer.Ordinal)
            .Select(r => (Record: r, Data: JsonSerializer.Deserialize<ZomatoReportSourceData>(r.SourceData!.ToJson(settings))
                ?? throw new InvalidOperationException("Saved source data is unreadable. Re-upload the affected reports.")))
            .ToList();

        // Check interval coverage, not daily order counts: a covered day can legitimately have no orders.
        var coveredThrough = start.DayNumber - 1;
        foreach (var source in sources.OrderBy(s => s.Record.PeriodStartDate))
        {
            var sourceStart = DateOnly.FromDateTime(source.Record.PeriodStartDate).DayNumber;
            if (sourceStart > coveredThrough + 1) break;
            coveredThrough = Math.Max(coveredThrough, DateOnly.FromDateTime(source.Record.PeriodEndDate).DayNumber);
        }
        if (coveredThrough < end.DayNumber)
        {
            var missingFrom = DateOnly.FromDayNumber(coveredThrough + 1);
            throw new InvalidOperationException(
                $"No order-level source coverage from {missingFrom:yyyy-MM-dd}. Upload reports covering the selected dates. " +
                "Older summary-only snapshots must be re-uploaded once for custom-date reporting.");
        }

        var orders = sources.SelectMany(s => s.Data.Orders)
            .DistinctBy(o => o.OrderId, StringComparer.OrdinalIgnoreCase).ToList();
        var settlements = sources.SelectMany(s => s.Data.Settlements)
            .DistinctBy(o => o.OrderId, StringComparer.OrdinalIgnoreCase).ToList();
        var orderDates = orders.ToDictionary(o => o.OrderId, o => o.PlacedAt, StringComparer.OrdinalIgnoreCase);
        DateTime? SettlementOrderDate(SettlementRow row) =>
            row.OrderDate ?? orderDates.GetValueOrDefault(row.OrderId);

        if (orders.Any(o => !o.PlacedAt.HasValue) ||
            settlements.Any(o => !SettlementOrderDate(o).HasValue))
            throw new InvalidOperationException(
                "Some saved orders have no valid order date. Correct and re-upload those reports before requesting custom dates.");

        bool InRange(DateTime? date) => date.HasValue &&
            DateOnly.FromDateTime(date.Value) >= start && DateOnly.FromDateTime(date.Value) <= end;
        var selectedOrders = orders.Where(o => InRange(o.PlacedAt)).ToList();
        var selectedSettlements = settlements.Where(o => InRange(SettlementOrderDate(o))).ToList();
        foreach (var row in selectedSettlements)
            row.OrderDate ??= orderDates.GetValueOrDefault(row.OrderId);

        var periods = sources.OrderBy(s => s.Record.PeriodStartDate).ToList();
        var fullNonOverlappingPeriods = periods.All(s =>
                DateOnly.FromDateTime(s.Record.PeriodStartDate) >= start &&
                DateOnly.FromDateTime(s.Record.PeriodEndDate) <= end) &&
            !periods.Zip(periods.Skip(1)).Any(pair =>
                pair.First.Record.PeriodEndDate.Date >= pair.Second.Record.PeriodStartDate.Date);

        var details = fullNonOverlappingPeriods
            ? sources.SelectMany(s => s.Data.AdditionalDeductions).ToList()
            : new List<AdditionalDeductionRow>();
        var totals = new List<AdditionalDeductionTotal>();
        if (fullNonOverlappingPeriods)
        {
            foreach (var isAddition in new[] { false, true })
            {
                var label = isAddition ? "Total Additions" : "Total Deductions";
                var perReport = sources.Select(s =>
                    s.Data.AdditionalTotals.FirstOrDefault(t => t.Label.StartsWith(label, StringComparison.OrdinalIgnoreCase))
                    ?? new AdditionalDeductionTotal
                    {
                        TotalAmount = s.Data.AdditionalDeductions.Where(r => r.IsAddition == isAddition).Sum(r => r.TotalAmount),
                        AdjustedAmount = s.Data.AdditionalDeductions.Where(r => r.IsAddition == isAddition).Sum(r => r.AdjustedAmount),
                        OutstandingAmount = s.Data.AdditionalDeductions.Where(r => r.IsAddition == isAddition).Sum(r => r.OutstandingAmount)
                    }).ToList();
                totals.Add(new AdditionalDeductionTotal
                {
                    Label = label,
                    TotalAmount = perReport.Sum(t => t.TotalAmount),
                    AdjustedAmount = perReport.Sum(t => t.AdjustedAmount),
                    OutstandingAmount = perReport.Sum(t => t.OutstandingAmount)
                });
            }
        }

        var result = BuildInsights(selectedOrders, selectedSettlements, details, totals);
        result.Period = new InsightPeriod { StartDate = start.ToString("yyyy-MM-dd"), EndDate = end.ToString("yyyy-MM-dd") };
        result.Notices.Add("Custom-date view uses order dates, not bank transfer dates. Orders in overlapping uploads are counted once, using the latest upload.");
        if (!fullNonOverlappingPeriods)
        {
            result.SettlementSummary.AdditionalDeductionsAdjusted = null;
            result.SettlementSummary.AdditionalAdditionsAdjusted = null;
            result.SettlementSummary.ActualPayoutCredited = null;
            result.Notices.Add("Additional adjustments and actual bank credit are unavailable for partial or overlapping report periods. Weekly deductions are not prorated. Load a saved full-period snapshot to see its bank credit.");
        }
        return result;
    }
}
