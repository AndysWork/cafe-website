using Cafe.Api.Models;

namespace Cafe.Api.Helpers;

public static class StaffOrderDiscount
{
    public static decimal CouponEligibleSubtotal(IEnumerable<Order> orders) => orders
        .Where(order => !order.IsStaffOrder
            && !string.Equals(order.Status, "cancelled", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(order.Status, "rejected", StringComparison.OrdinalIgnoreCase))
        .Sum(order => order.Subtotal);

    public static void Apply(Order order)
    {
        if (!order.IsStaffOrder) return;

        var gross = Math.Max(0, order.Subtotal + order.Tax + order.PlatformCharge
            + order.DeliveryFee + order.PackagingCharge);
        order.StaffDiscountAmount = Math.Round(gross * 0.20m, 2, MidpointRounding.AwayFromZero);
        // Staff discount is a labelled part of DiscountAmount, not an additional deduction.
        order.DiscountAmount = order.StaffDiscountAmount;
        order.CouponCode = null;
        order.LoyaltyPointsUsed = 0;
        order.LoyaltyDiscountAmount = 0;
        order.Total = Math.Round(gross - order.DiscountAmount, 2, MidpointRounding.AwayFromZero);
    }
}
