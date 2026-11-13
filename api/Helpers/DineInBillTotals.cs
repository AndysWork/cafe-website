using Cafe.Api.Models;
using Cafe.Api.Repositories;
using Cafe.Api.Services;
using System.Text.Json;
using System.Globalization;

namespace Cafe.Api.Helpers;

public static class DineInBillTotals
{
    public static void Apply(DineInSession session, IEnumerable<Order> orders)
    {
        var activeOrders = orders
            .Where(order => !string.Equals(order.Status, "cancelled", StringComparison.OrdinalIgnoreCase))
            .ToList();
        session.Subtotal = activeOrders.Sum(order => order.Subtotal);
        session.TaxAmount = 0;
        session.GrandTotal = Math.Max(0, activeOrders.Sum(order => order.Total)
            - session.DiscountAmount - session.LoyaltyDiscountAmount);
    }

    public static async Task<DineInSession> GetReconciliationSessionAsync(IOrderRepository orders, Order order)
    {
        var sessionId = order.DineInSessionId!;
        var visited = new HashSet<string>();
        while (visited.Add(sessionId))
        {
            var session = await orders.GetDineInSessionByIdAsync(sessionId)
                ?? throw new InvalidOperationException("The linked dine-in session was not found.");
            if (!string.Equals(session.OutletId, order.OutletId, StringComparison.Ordinal)
                || !session.OrderIds.Contains(order.Id!))
                throw new InvalidOperationException("The order is not linked to the expected dine-in session.");
            if (string.IsNullOrWhiteSpace(session.MergedIntoSessionId))
                return session;
            sessionId = session.MergedIntoSessionId;
        }
        throw new InvalidOperationException("The dine-in session merge chain contains a cycle.");
    }

    public static async Task SynchronizeReconciledOrderAsync(
        IOrderRepository orders, Order reconciledOrder, string? paymentMethod = null,
        string? paidSessionPaymentMethod = null, bool reconcilePaidBill = false)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var session = await GetReconciliationSessionAsync(orders, reconciledOrder);
            if (string.Equals(session.PaymentStatus, "processing", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A table payment is being processed.");

            var expectedUpdatedAt = session.UpdatedAt;
            var rounds = new List<Order>();
            foreach (var id in session.OrderIds)
            {
                var round = await orders.GetOrderByIdAsync(id)
                    ?? throw new InvalidOperationException("A linked dine-in order was not found.");
                rounds.Add(round);
            }

            var previousBill = session.GrandTotal;
            Apply(session, rounds);
            var correctPaidAmount = reconcilePaidBill
                && string.Equals(session.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase)
                && (previousBill != session.GrandTotal || session.AmountPaid != session.GrandTotal
                    || (session.PaymentEntries ?? new()).Where(entry => string.Equals(entry.Status, "confirmed", StringComparison.OrdinalIgnoreCase))
                        .Sum(entry => entry.Amount) != session.GrandTotal);
            if (correctPaidAmount)
            {
                var method = ResolvePaidSessionPaymentMethod(session, paidSessionPaymentMethod ?? paymentMethod);
                ReclassifyPayments(session, reconciledOrder, method, session.GrandTotal);
            }
            else if (paymentMethod != null)
                ReclassifyPayments(session, reconciledOrder, paymentMethod);
            if (await orders.TryUpdateDineInSessionTotalsAsync(session, expectedUpdatedAt, correctPaidAmount || paymentMethod != null))
                return;
        }

        throw new InvalidOperationException("The table bill changed while it was being updated.");
    }

    public static string ResolvePaidSessionPaymentMethod(DineInSession session, string? selectedMethod)
    {
        static string? Normalize(string? method) => method?.Trim().ToLowerInvariant() switch
        {
            "cash" or "cash_at_counter" or "cod" => "cash_at_counter",
            "upi" or "upi-qr" => "upi-qr",
            _ => null
        };
        if (Normalize(selectedMethod) is string selected)
            return selected;
        var methods = (session.PaymentEntries ?? new())
            .Where(entry => string.Equals(entry.Status, "confirmed", StringComparison.OrdinalIgnoreCase))
            .Select(entry => Normalize(entry.PaymentMethod)).Distinct().ToList();
        if (methods.Count == 1 && methods[0] is string recorded)
            return recorded;
        if (methods.Count == 0 && Normalize(session.PaymentMethod) is string legacy)
            return legacy;
        throw new InvalidOperationException("Choose Cash/COD or UPI when reconciling a fully paid session.");
    }

    private static void ReclassifyPayments(DineInSession session, Order order, string method, decimal? correctedTotal = null)
    {
        var now = MongoService.GetUtcNow();
        var previousSessionMethod = session.PaymentMethod;
        var previousAmountPaid = session.AmountPaid;
        session.PaymentEntries ??= new();
        session.Timeline ??= new();
        var confirmed = session.PaymentEntries
            .Where(entry => string.Equals(entry.Status, "confirmed", StringComparison.OrdinalIgnoreCase)).ToList();
        var oldMethods = JsonSerializer.Serialize(confirmed.Select(entry => new { entry.Id, entry.PaymentMethod, entry.Amount }));
        var confirmedTotal = confirmed.Sum(entry => entry.Amount);
        var adjustment = correctedTotal.HasValue
            ? correctedTotal.Value - confirmedTotal
            : Math.Max(0, session.AmountPaid - confirmedTotal);
        foreach (var entry in confirmed)
            entry.PaymentMethod = method;
        if (adjustment < 0)
        {
            var reduction = -adjustment;
            foreach (var entry in confirmed.AsEnumerable().Reverse())
            {
                var deducted = Math.Min(entry.Amount, reduction);
                entry.Amount -= deducted;
                reduction -= deducted;
                if (reduction == 0) break;
            }
        }
        if (adjustment > 0)
        {
            session.PaymentEntries.Add(new DineInPaymentEntry
            {
                Amount = adjustment,
                PaymentMethod = method,
                Status = "confirmed",
                RequestedAt = now,
                ConfirmedAt = now,
                ConfirmedBy = order.LastReconciledByUserId,
                UpiReference = method == "upi-qr" ? order.UpiReference : null,
                Notes = correctedTotal.HasValue
                    ? $"Historical paid bill correction for order {order.Id}; not a new payment receipt."
                    : $"Previously unallocated collection classified by reconciliation of order {order.Id}."
            });
        }
        if (correctedTotal.HasValue)
            session.AmountPaid = correctedTotal.Value;
        session.PaymentMethod = method;
        session.UpiReference = method == "upi-qr" ? order.UpiReference : null;
        session.Timeline.Add(new DineInSessionEvent
        {
            EventType = correctedTotal.HasValue ? "AdminReconciledPaidBill" : "AdminReconciledPaymentMethod",
            Message = correctedTotal.HasValue
                ? $"Historical paid bill and recorded collection corrected to {correctedTotal.Value:0.00} as {method}. {order.LastReconciliationReason}"
                : $"Full-session recorded collection reclassified as {method}. {order.LastReconciliationReason}",
            Actor = order.LastReconciledByUserId,
            CreatedAt = now,
            CreatedAtIsUtc = true,
            Metadata = new()
            {
                ["orderId"] = order.Id!,
                ["previousEntryMethods"] = oldMethods,
                ["paymentMethod"] = method,
                ["previousSessionMethod"] = previousSessionMethod ?? string.Empty,
                ["previousAmountPaid"] = previousAmountPaid.ToString(CultureInfo.InvariantCulture),
                ["correctedAmountPaid"] = session.AmountPaid.ToString(CultureInfo.InvariantCulture)
            }
        });
    }
}
