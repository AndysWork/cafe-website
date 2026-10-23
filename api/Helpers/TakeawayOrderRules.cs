using Cafe.Api.Models;

namespace Cafe.Api.Helpers;

public static class TakeawayOrderRules
{
    public const string WaiterSource = "waiter";
    public const string GuestUserId = "guest_takeaway";
    public static readonly string[] ActiveStatuses = { "pending", "confirmed", "preparing", "ready" };
    public static readonly string[] CancellableStatuses = { "pending", "confirmed" };

    public static bool IsWaiterTakeaway(Order? order) =>
        order != null
        && !order.IsDeleted
        && string.Equals(order.OrderType, "pickup", StringComparison.OrdinalIgnoreCase)
        && string.Equals(order.TakeawaySource, WaiterSource, StringComparison.OrdinalIgnoreCase);

    public static string? NormalizePaymentTiming(string? timing)
    {
        var normalized = (timing ?? "handover").Trim().ToLowerInvariant();
        return normalized is "upfront" or "handover" ? normalized : null;
    }

    public static (string? Method, string? Error) ValidatePayment(string? paymentMethod, string? upiReference)
    {
        var method = (paymentMethod ?? string.Empty).Trim().ToLowerInvariant();
        if (method is "cash" or "cash_at_counter") method = "cash_at_counter";
        else if (method is "upi" or "upi-qr") method = "upi-qr";
        else return (null, "Payment method must be cash or UPI");

        if (method == "upi-qr" && string.IsNullOrWhiteSpace(upiReference))
            return (null, "UPI reference is required for UPI payments");

        return (method, null);
    }

    public static decimal GetPackagingCharge(Outlet? outlet)
    {
        var charge = outlet?.Settings?.PackagingCharge ?? 0m;
        return Math.Round(Math.Clamp(charge, 0m, 1000m), 2);
    }

    public static string? GetPaymentBlockReason(Order order)
    {
        if (!IsWaiterTakeaway(order)) return "Waiter takeaway order not found";
        if (string.Equals(order.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase))
            return "Payment is already recorded for this takeaway order";
        if (!ActiveStatuses.Contains(order.Status))
            return "Payment can only be recorded for an active takeaway order";
        return null;
    }

    public static string? GetHandoverBlockReason(Order order)
    {
        if (!IsWaiterTakeaway(order)) return "Waiter takeaway order not found";
        if (!string.Equals(order.Status, "ready", StringComparison.OrdinalIgnoreCase))
            return "The kitchen has not packed this parcel yet";
        if (!string.Equals(order.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase))
            return "Collect payment before handing over the parcel";
        return null;
    }

    public static string? GetCancelBlockReason(Order order)
    {
        if (!IsWaiterTakeaway(order)) return "Waiter takeaway order not found";
        if (!CancellableStatuses.Contains(order.Status))
            return "Only takeaway orders the kitchen has not started can be cancelled";
        if (string.Equals(order.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase))
            return "Paid takeaway orders cannot be cancelled here; ask a manager to refund";
        return null;
    }
}
