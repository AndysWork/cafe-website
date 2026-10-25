using System.Security.Cryptography;
using System.Text;
using Cafe.Api.Models;

namespace Cafe.Api.Helpers;

public static class PhoneDeliveryOrderRules
{
    public const string GuestUserId = "guest_phone_delivery";
    public static bool IsPhoneOrder(Order order) => order.OrderSource == "phone";

    public static bool IsValidMapsUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048 || value.Any(char.IsControl)
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo))
            return false;
        var host = uri.IdnHost.ToLowerInvariant();
        return host == "maps.app.goo.gl" && uri.AbsolutePath.Length > 1
            || host == "goo.gl" && uri.AbsolutePath.StartsWith("/maps/", StringComparison.Ordinal)
            || (host is "google.com" or "www.google.com" or "google.co.in" or "www.google.co.in")
                && (uri.AbsolutePath == "/maps" || uri.AbsolutePath.StartsWith("/maps/", StringComparison.Ordinal))
            || host == "maps.google.com" && (uri.AbsolutePath is "/" or "/maps"
                || uri.AbsolutePath.StartsWith("/maps/", StringComparison.Ordinal));
    }

    public static string NewTrackingToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static bool IsValidTrackingToken(string? stored, string? supplied) =>
        stored?.Length == 43 && supplied?.Length == 43
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(stored), Encoding.UTF8.GetBytes(supplied));

    // The MongoDB _id uniqueness constraint gives creation durable idempotency without a TTL window.
    public static string IdempotentOrderId(string userId, string outletId, string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            System.Text.Json.JsonSerializer.Serialize(new[] { "phone-order-v1", userId, outletId, key }))))[..24].ToLowerInvariant();

    public static string? WorkflowBlockReason(Order order, string nextStatus)
    {
        if (!IsPhoneOrder(order)) return null;
        if (nextStatus is "out-for-delivery" or "delivered" or "served")
            return "Use delivery partner pickup and delivery actions for phone orders";
        if (order.Status is "out-for-delivery" or "delivered" or "cancelled")
            return "Phone order is no longer in the kitchen workflow";
        if (nextStatus == "cancelled")
            return order.PaymentStatus == "paid" ? "Refund the paid phone order before cancellation" : null;
        var sequence = new[] { "pending", "confirmed", "preparing", "ready" };
        return Array.IndexOf(sequence, nextStatus) < Array.IndexOf(sequence, order.Status)
            || !sequence.Contains(nextStatus) ? "Phone order cannot move backwards" : null;
    }

    public static string? AssignmentBlockReason(Order order, DeliveryPartner partner)
    {
        if (order.OutletId != partner.OutletId || !partner.IsActive || partner.Status == "offline")
            return "Choose an active delivery partner from this outlet";
        if (order.Status is not ("pending" or "confirmed" or "preparing" or "ready"))
            return "Phone order can only be assigned before pickup";
        return order.PaymentMethod == "cod" && !partner.CodAllowed ? "This partner cannot collect COD" : null;
    }

    public static string? PickupBlockReason(Order order, DeliveryPartner partner)
    {
        if (order.DeliveryPartnerId != partner.Id || order.OutletId != partner.OutletId)
            return "Phone order is not assigned to this partner in this outlet";
        if (order.Status != "ready") return "Kitchen must mark the phone order ready before pickup";
        return order.PaymentMethod != "cod" && order.PaymentStatus != "paid" ? "Prepaid phone order payment must be paid before pickup" : null;
    }

    public static object PublicTracking(Order order) => new
    {
        orderId = order.Id,
        status = order.Status,
        kotNumber = order.KotNumber,
        createdAt = order.CreatedAt,
        updatedAt = order.UpdatedAt,
        paymentStatus = order.PaymentStatus,
        paymentMethod = order.PaymentMethod,
        total = order.Total,
        deliveryPartnerName = order.DeliveryPartnerName,
        etaMinutes = order.DeliveryEtaMinutes
    };
}
