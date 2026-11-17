using Cafe.Api.Models;

namespace Cafe.Api.Services;

public static class SubscriptionPlanAnalytics
{
    public static SubscriptionOverviewResponse Calculate(IEnumerable<CustomerSubscription> subscriptions, DateTime now)
    {
        var paid = subscriptions.Where(s => s.PaymentStatus == "paid").ToList();
        var month = new DateTime(now.Year, now.Month, 1);
        var previous = month.AddMonths(-1);
        var current = paid.Where(s => PaidAt(s) >= month && PaidAt(s) < month.AddMonths(1)).ToList();
        var last = paid.Where(s => PaidAt(s) >= previous && PaidAt(s) < month).ToList();
        var subscribers = paid.Where(s => !string.IsNullOrEmpty(s.PlanId) && IsCurrent(s, now))
            .GroupBy(s => s.PlanId!)
            .ToDictionary(g => g.Key, g => g.Select(s => s.UserId).Distinct().Count());
        return new(paid.Count, current.Count, last.Count,
            current.Sum(s => s.AmountPaid), last.Sum(s => s.AmountPaid), subscribers);
    }

    private static DateTime PaidAt(CustomerSubscription sub) => sub.PaymentConfirmedAt ?? sub.CreatedAt;

    private static bool IsCurrent(CustomerSubscription sub, DateTime now)
    {
        if (sub.Status == "active") return sub.EndDate > now;
        if (sub.Status != "paused") return false;
        if (!SubscriptionPausePolicy.IsDue(sub, now)) return true;
        return sub.EndDate.AddDays(SubscriptionPausePolicy.ConsumedDays(sub, now)) > now;
    }
}
