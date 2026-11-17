namespace Cafe.Api.Services;

public sealed record SubscriptionTerms(decimal DiscountPercent, int PauseLimit, int MaxPauseDays)
{
    public static bool IsSupportedDuration(int days) => days is 7 or 14 or 30;

    public static SubscriptionTerms ForDuration(int days) => days switch
    {
        7 => new(3m, 1, 3),
        14 => new(5m, 1, 5),
        30 => new(10m, 2, 7),
        _ => throw new ArgumentOutOfRangeException(nameof(days), "Choose a subscription duration of 7, 14 or 30 days.")
    };

    // Non-standard subscriptions purchased before duration restrictions retain their existing pause caps.
    public static SubscriptionTerms ForExistingSubscription(int days) =>
        IsSupportedDuration(days) ? ForDuration(days) : new(0m, 2, 7);
}
