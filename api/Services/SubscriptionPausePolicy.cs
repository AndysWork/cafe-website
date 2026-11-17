using Cafe.Api.Helpers;
using Cafe.Api.Models;

namespace Cafe.Api.Services;

public static class SubscriptionPausePolicy
{
    public const int MaximumDays = 7;

    public static int PausesUsed(CustomerSubscription sub)
    {
        var count = sub.PauseStartsUtc.Count;
        // Existing paused subscriptions predate the pause history.
        if (sub.Status == "paused" && !sub.PauseStartsUtc.Contains(IstDateHelper.IstWallTimeToUtc(PauseStartedAt(sub))))
            count++;
        return count;
    }

    public static string? ValidatePause(CustomerSubscription sub, int days, DateTime now)
    {
        var terms = SubscriptionTerms.ForExistingSubscription(sub.DurationDays);
        if (days < 1 || days > terms.MaxPauseDays)
            return $"Choose a pause duration between 1 and {terms.MaxPauseDays} days for this subscription.";
        if (sub.Status != "active") return $"Cannot pause a subscription that is currently {sub.Status}.";
        if (sub.EndDate <= now) return "Cannot pause an expired subscription.";
        if (PausesUsed(sub) >= terms.PauseLimit)
            return $"This {sub.DurationDays}-day subscription allows at most {terms.PauseLimit} pause(s) in total.";
        return null;
    }

    public static void Pause(CustomerSubscription sub, int days, DateTime now)
    {
        var error = ValidatePause(sub, days, now);
        if (error != null) throw new InvalidOperationException(error);
        sub.Status = "paused";
        sub.PausedAt = IstDateHelper.IstWallTimeToUtc(now);
        sub.PauseStartsUtc.Add(sub.PausedAt.Value);
        sub.PauseEndsAtUtc = IstDateHelper.IstWallTimeToUtc(now.AddDays(days));
    }

    public static DateTime ResumeAt(CustomerSubscription sub)
    {
        var maximum = PauseStartedAt(sub).AddDays(SubscriptionTerms.ForExistingSubscription(sub.DurationDays).MaxPauseDays);
        var selected = sub.PauseEndsAtUtc.HasValue
            ? IstDateHelper.NormalizeToIst(DateTime.SpecifyKind(sub.PauseEndsAtUtc.Value, DateTimeKind.Utc))
            : maximum;
        return selected < maximum ? selected : maximum;
    }

    public static bool IsDue(CustomerSubscription sub, DateTime now) =>
        sub.Status == "paused" && ResumeAt(sub) <= now;

    public static bool UpdateDeadline(CustomerSubscription sub)
    {
        if (sub.Status != "paused") return false;
        var deadline = IstDateHelper.IstWallTimeToUtc(ResumeAt(sub));
        if (sub.PauseEndsAtUtc == deadline) return false;
        sub.PausedAt = IstDateHelper.IstWallTimeToUtc(PauseStartedAt(sub));
        if (!sub.PauseStartsUtc.Contains(sub.PausedAt.Value)) sub.PauseStartsUtc.Add(sub.PausedAt.Value);
        sub.PauseEndsAtUtc = deadline;
        return true;
    }

    public static int Resume(CustomerSubscription sub, DateTime now)
    {
        if (sub.Status != "paused") throw new InvalidOperationException("Subscription is not paused.");
        var started = PauseStartedAt(sub);
        if (now < started) throw new InvalidOperationException("Pause start is in the future.");
        var end = ResumeAt(sub);
        var consumedUntil = now < end ? now : end;
        var days = Math.Clamp((int)Math.Ceiling((consumedUntil - started).TotalDays), 1,
            SubscriptionTerms.ForExistingSubscription(sub.DurationDays).MaxPauseDays);
        var startedUtc = IstDateHelper.IstWallTimeToUtc(started);
        if (!sub.PauseStartsUtc.Contains(startedUtc)) sub.PauseStartsUtc.Add(startedUtc);
        sub.EndDate = sub.EndDate.AddDays(days);
        sub.TotalDaysPaused += days;
        sub.Status = sub.EndDate > now ? "active" : "expired";
        sub.PausedAt = null;
        sub.PauseEndsAtUtc = null;
        return days;
    }

    private static DateTime PauseStartedAt(CustomerSubscription sub)
    {
        var started = sub.PausedAt ?? sub.UpdatedAt;
        // Old pauses stored IST wall time; new pauses store UTC with an explicit UTC deadline.
        return sub.PauseEndsAtUtc.HasValue
            ? IstDateHelper.NormalizeToIst(DateTime.SpecifyKind(started, DateTimeKind.Utc))
            : DateTime.SpecifyKind(started, DateTimeKind.Unspecified);
    }
}
