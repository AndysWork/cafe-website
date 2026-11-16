using Cafe.Api.Models;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using System.Text.Json;

namespace Cafe.Api.Services;

public partial class ZomatoSalesPayoutInsightService
{
    /// <summary>
    /// Converts retained parser uploads into per-order online sales for the dashboard.
    /// Orders are IST wall-clock dated, counted once (latest upload wins), and skipped when
    /// the same Zomato order already exists in the recorded online sales.
    /// </summary>
    public static List<OnlineSale> ToDashboardOnlineSales(
        IReadOnlyList<ZomatoSalesPayoutInsightRecord> records, DateTime startDate, DateTime endExclusive,
        IEnumerable<string> existingZomatoOrderIds)
    {
        var settings = new JsonWriterSettings { OutputMode = JsonOutputMode.RelaxedExtendedJson };
        var sources = records
            .Where(r => r.SourceData != null)
            .OrderByDescending(r => r.UpdatedAt)
            .ThenByDescending(r => r.Id, StringComparer.Ordinal)
            .Select(r => JsonSerializer.Deserialize<ZomatoReportSourceData>(r.SourceData!.ToJson(settings)))
            .Where(data => data != null)
            .Select(data => data!)
            .ToList();
        var known = existingZomatoOrderIds.Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var orders = sources.SelectMany(s => s.Orders)
            .Where(o => !string.IsNullOrWhiteSpace(o.OrderId))
            .DistinctBy(o => o.OrderId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(o => o.OrderId, StringComparer.OrdinalIgnoreCase);
        var settlements = sources.SelectMany(s => s.Settlements)
            .Where(o => !string.IsNullOrWhiteSpace(o.OrderId))
            .DistinctBy(o => o.OrderId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(o => o.OrderId, StringComparer.OrdinalIgnoreCase);

        bool InRange(DateTime? date) => date.HasValue && date.Value >= startDate && date.Value < endExclusive;
        static DateTime Ist(DateTime date) => DateTime.SpecifyKind(date, DateTimeKind.Unspecified);

        var result = new List<OnlineSale>();
        foreach (var order in orders.Values)
        {
            if (known.Contains(order.OrderId) || !InRange(order.PlacedAt)
                || !string.Equals(order.Status, "Delivered", StringComparison.OrdinalIgnoreCase))
                continue;
            settlements.TryGetValue(order.OrderId, out var settlement);
            result.Add(new OnlineSale
            {
                Platform = "Zomato",
                OrderId = order.OrderId,
                OrderAt = Ist(order.PlacedAt!.Value),
                Distance = order.DistanceKm,
                OrderedItems = ParseItems(order.ItemsInOrder)
                    .Select(item => new OrderedItem { ItemName = item.Name, Quantity = item.Quantity }).ToList(),
                DiscountCoupon = string.IsNullOrWhiteSpace(order.DiscountConstruct) ? null : order.DiscountConstruct,
                BillSubTotal = order.BillSubtotal,
                PackagingCharges = order.PackagingCharges,
                DiscountAmount = order.RestaurantDiscountPromo + order.RestaurantDiscountOthers
                    + order.GoldDiscount + order.BrandPackDiscount,
                Payout = settlement?.OrderLevelPayout ?? 0,
                PlatformDeduction = settlement?.NetDeductions ?? 0,
                TotalCommissionable = settlement?.TotalCommissionableValue ?? 0,
                Rating = order.Rating,
                Review = string.IsNullOrWhiteSpace(order.Review) ? null : order.Review,
                KPT = order.KptMinutes,
                RWT = order.RiderWaitMinutes,
                OrderMarking = string.IsNullOrWhiteSpace(order.OrderReadyMarked) ? null : order.OrderReadyMarked,
                Complain = string.IsNullOrWhiteSpace(order.CustomerComplaintTag) ? null : order.CustomerComplaintTag
            });
        }

        // Paid settlements without a delivered order row (e.g. compensated cancellations) still credit the cafe.
        foreach (var settlement in settlements.Values)
        {
            if (settlement.OrderLevelPayout == 0 || known.Contains(settlement.OrderId)) continue;
            orders.TryGetValue(settlement.OrderId, out var order);
            if (order != null && string.Equals(order.Status, "Delivered", StringComparison.OrdinalIgnoreCase)) continue;
            var date = settlement.OrderDate ?? order?.PlacedAt;
            if (!InRange(date)) continue;
            result.Add(new OnlineSale
            {
                Platform = "Zomato",
                OrderId = settlement.OrderId,
                OrderAt = Ist(date!.Value),
                BillSubTotal = settlement.Subtotal,
                PackagingCharges = settlement.PackagingCharge,
                DiscountAmount = settlement.PromoDiscount + settlement.OtherRestaurantDiscount,
                Payout = settlement.OrderLevelPayout,
                PlatformDeduction = settlement.NetDeductions,
                TotalCommissionable = settlement.TotalCommissionableValue
            });
        }
        return result.OrderBy(sale => sale.OrderAt).ThenBy(sale => sale.OrderId, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Report-level adjusted additional deductions/additions per uploaded period (latest upload wins per period).
    /// The client applies them only to ranges that fully contain the period; weekly amounts are never prorated.
    /// </summary>
    public static List<DashboardZomatoAdjustmentResponse> ToDashboardAdjustments(
        IReadOnlyList<ZomatoSalesPayoutInsightRecord> records)
    {
        var settings = new JsonWriterSettings { OutputMode = JsonOutputMode.RelaxedExtendedJson };
        return records
            .Where(r => r.SourceData != null)
            .OrderByDescending(r => r.UpdatedAt)
            .ThenByDescending(r => r.Id, StringComparer.Ordinal)
            .DistinctBy(r => (r.PeriodStartDate.Date, r.PeriodEndDate.Date))
            .Select(r =>
            {
                var data = JsonSerializer.Deserialize<ZomatoReportSourceData>(r.SourceData!.ToJson(settings))
                    ?? new ZomatoReportSourceData();
                decimal Adjusted(bool isAddition) =>
                    data.AdditionalTotals.FirstOrDefault(t => t.Label.StartsWith(
                        isAddition ? "Total Additions" : "Total Deductions", StringComparison.OrdinalIgnoreCase))?.AdjustedAmount
                    ?? data.AdditionalDeductions.Where(row => row.IsAddition == isAddition).Sum(row => row.AdjustedAmount);
                return new DashboardZomatoAdjustmentResponse(
                    r.PeriodStartDate.ToString("yyyy-MM-dd"), r.PeriodEndDate.ToString("yyyy-MM-dd"),
                    Round2(Adjusted(false)), Round2(Adjusted(true)));
            })
            .OrderBy(a => a.PeriodStart, StringComparer.Ordinal)
            .ToList();
    }
}
