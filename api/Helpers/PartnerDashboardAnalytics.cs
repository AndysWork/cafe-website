using Cafe.Api.Models;

namespace Cafe.Api.Helpers;

public static class PartnerDashboardAnalytics
{
    public static (PartnerTodaySummary Today, PartnerPerformanceSummary Performance,
        PartnerIncentiveSummary Incentive, List<PartnerPeakWindow> PeakHours) Calculate(
        IEnumerable<Order> source, string partnerId, DateTime today, decimal distance, double? rating, int reviews)
    {
        var start = today.Date.AddDays(-6);
        var end = today.Date.AddDays(1);
        var orders = source.Where(o => !o.IsDeleted && string.Equals(o.OrderType, "delivery", StringComparison.OrdinalIgnoreCase)).ToList();
        var assigned = orders.Where(o => o.DeliveryPartnerId == partnerId).ToList();
        bool InPeriod(DateTime value) => value >= start && value < end;
        DateTime WallTime(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
        DateTime CompletionTime(Order order) => order.CompletedAtIsUtc == true
            ? IstDateHelper.NormalizeToIst(DateTime.SpecifyKind(order.CompletedAt!.Value, DateTimeKind.Utc))
            : WallTime(order.CompletedAt!.Value);
        var completed = assigned.Where(o => o.Status.Equals("delivered", StringComparison.OrdinalIgnoreCase)
            && o.CompletedAt.HasValue && InPeriod(CompletionTime(o))).ToList();
        var cancelled = assigned.Where(o => o.Status.Equals("cancelled", StringComparison.OrdinalIgnoreCase)
            && InPeriod(WallTime(o.UpdatedAt))).ToList();
        var acceptedToday = assigned.Where(o => (o.DeliveryAssignedAtUtc.HasValue
            ? IstDateHelper.NormalizeToIst(DateTime.SpecifyKind(o.DeliveryAssignedAtUtc.Value, DateTimeKind.Utc))
            : OrderDateHelper.ToIstCreatedAt(o)).Date == today.Date).ToList();
        var summary = new PartnerTodaySummary
        {
            Completed = completed.Count(o => CompletionTime(o).Date == today.Date),
            Accepted = acceptedToday.Count,
            Cancelled = cancelled.Count(o => WallTime(o.UpdatedAt).Date == today.Date),
            LegacyAssignmentDates = acceptedToday.Count(o => !o.DeliveryAssignedAtUtc.HasValue)
        };
        var timed = completed.Where(o => o.DeliveryDueAtUtc.HasValue
            || (o.DeliveryRouteUpdatedAt.HasValue && o.DeliveryEtaMinutes > 0)).ToList();
        var onTime = timed.Count(o => CompletionTime(o) <= (o.DeliveryDueAtUtc.HasValue
            ? IstDateHelper.NormalizeToIst(DateTime.SpecifyKind(o.DeliveryDueAtUtc.Value, DateTimeKind.Utc))
            : WallTime(o.DeliveryRouteUpdatedAt!.Value).AddMinutes(o.DeliveryEtaMinutes!.Value)));
        var performance = new PartnerPerformanceSummary
        {
            OnTimeRate = timed.Count > 0 ? Math.Round(100m * onTime / timed.Count, 1) : null,
            CompletionRate = completed.Count + cancelled.Count > 0
                ? Math.Round(100m * completed.Count / (completed.Count + cancelled.Count), 1) : null,
            CustomerRating = rating,
            TotalDistanceKm = distance,
            Completed = completed.Count,
            Cancelled = cancelled.Count,
            TimedDeliveries = timed.Count,
            OnTimeDeliveries = onTime,
            Reviews = reviews
        };
        var incentive = new PartnerIncentiveSummary
        {
            CompletedDeliveries = summary.Completed,
            EarnedAmount = summary.Completed >= 10 ? 50 : 0
        };
        var hours = new int[24];
        foreach (var order in orders.Where(o => !string.Equals(o.Status, "cancelled", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(o.Status, "rejected", StringComparison.OrdinalIgnoreCase)))
        {
            var created = OrderDateHelper.ToIstCreatedAt(order);
            if (InPeriod(created)) hours[created.Hour]++;
        }
        var windows = Enumerable.Range(0, 22).Select(hour =>
            new PartnerPeakWindow(hour, hour + 3, hours.Skip(hour).Take(3).Sum()))
            .Where(w => w.Orders > 0).OrderByDescending(w => w.Orders).ThenBy(w => w.StartHour).ToList();
        var peaks = new List<PartnerPeakWindow>();
        foreach (var window in windows)
        {
            if (peaks.All(p => window.EndHour <= p.StartHour || window.StartHour >= p.EndHour))
                peaks.Add(window);
            if (peaks.Count == 2) break;
        }
        return (summary, performance, incentive, peaks);
    }
}
