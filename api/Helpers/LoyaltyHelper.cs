using Cafe.Api.Models;

namespace Cafe.Api.Helpers;

public static class LoyaltyHelper
{
    public static bool IsWaiterOrGuestDineInOrder(Order order) =>
        string.Equals(order.OrderType, "dine-in", StringComparison.OrdinalIgnoreCase)
        && (order.UserId == "guest_dinein"
            || (string.Equals(order.DineInOrderSource, "waiter", StringComparison.OrdinalIgnoreCase)
                && order.UserId == (order.WaiterUserId ?? order.ApprovedByUserId ?? order.UserId)));

    public static int GetDineInPointsToEarn(DineInSession session, IEnumerable<Order> orders)
    {
        if (string.IsNullOrWhiteSpace(session.UserId) || session.UserId == "guest_dinein")
            return 0;

        // Legacy waiter sessions stored the order-taking staff member as the customer.
        if (session.UserId == session.WaiterUserId
            || orders.Any(order =>
                string.Equals(order.DineInOrderSource, "waiter", StringComparison.OrdinalIgnoreCase)
                && session.UserId == (order.WaiterUserId ?? order.ApprovedByUserId ?? order.UserId)))
            return 0;

        return Math.Max(0, (int)Math.Floor(session.GrandTotal * 0.10m));
    }

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
