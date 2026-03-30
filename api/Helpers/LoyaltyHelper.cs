namespace Cafe.Api.Helpers;

public static class LoyaltyHelper
{
    public static (string? NextTier, int? PointsToNextTier) CalculateNextTierInfo(int totalPoints)
    {
        if (totalPoints < 500)
            return ("Silver", 500 - totalPoints);
        if (totalPoints < 1500)
            return ("Gold", 1500 - totalPoints);
        if (totalPoints < 3000)
            return ("Platinum", 3000 - totalPoints);
        return (null, null); // Already at max tier
    }
}
