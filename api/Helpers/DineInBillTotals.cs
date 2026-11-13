using Cafe.Api.Models;
using Cafe.Api.Repositories;
using Cafe.Api.Services;
using System.Text.Json;

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
        IOrderRepository orders, Order reconciledOrder, string? paymentMethod = null)
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

            Apply(session, rounds);
            if (paymentMethod != null)
                ReclassifyPayments(session, reconciledOrder, paymentMethod);
            if (await orders.TryUpdateDineInSessionTotalsAsync(session, expectedUpdatedAt, paymentMethod != null))
                return;
        }

        throw new InvalidOperationException("The table bill changed while it was being updated.");
    }

    private static void ReclassifyPayments(DineInSession session, Order order, string method)
    {
        var now = MongoService.GetUtcNow();
        var previousSessionMethod = session.PaymentMethod;
        session.PaymentEntries ??= new();
        session.Timeline ??= new();
        var confirmed = session.PaymentEntries
            .Where(entry => string.Equals(entry.Status, "confirmed", StringComparison.OrdinalIgnoreCase)).ToList();
        var oldMethods = JsonSerializer.Serialize(confirmed.Select(entry => new { entry.Id, entry.PaymentMethod }));
        var unallocated = Math.Max(0, session.AmountPaid - confirmed.Sum(entry => entry.Amount));
        foreach (var entry in confirmed)
            entry.PaymentMethod = method;
        if (unallocated > 0)
        {
            session.PaymentEntries.Add(new DineInPaymentEntry
            {
                Amount = unallocated,
                PaymentMethod = method,
                Status = "confirmed",
                RequestedAt = now,
                ConfirmedAt = now,
                ConfirmedBy = order.LastReconciledByUserId,
                UpiReference = method == "upi-qr" ? order.UpiReference : null,
                Notes = $"Previously unallocated collection classified by reconciliation of order {order.Id}."
            });
        }
        session.PaymentMethod = method;
        session.UpiReference = method == "upi-qr" ? order.UpiReference : null;
        session.Timeline.Add(new DineInSessionEvent
        {
            EventType = "AdminReconciledPaymentMethod",
            Message = $"Full-session recorded collection reclassified as {method}. {order.LastReconciliationReason}",
            Actor = order.LastReconciledByUserId,
            CreatedAt = now,
            CreatedAtIsUtc = true,
            Metadata = new()
            {
                ["orderId"] = order.Id!,
                ["previousEntryMethods"] = oldMethods,
                ["paymentMethod"] = method,
                ["previousSessionMethod"] = previousSessionMethod ?? string.Empty
            }
        });
    }
}
