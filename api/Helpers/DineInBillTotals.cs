using Cafe.Api.Models;
using Cafe.Api.Repositories;

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

    public static async Task SynchronizeReconciledOrderAsync(IOrderRepository orders, Order reconciledOrder)
    {
        var sessionId = reconciledOrder.DineInSessionId!;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var session = await orders.GetDineInSessionByIdAsync(sessionId)
                ?? throw new InvalidOperationException("The linked dine-in session was not found.");
            if (!string.Equals(session.OutletId, reconciledOrder.OutletId, StringComparison.Ordinal)
                || !session.OrderIds.Contains(reconciledOrder.Id!))
                throw new InvalidOperationException("The order is not linked to the expected dine-in session.");
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
            if (await orders.TryUpdateDineInSessionTotalsAsync(session, expectedUpdatedAt))
                return;
        }

        throw new InvalidOperationException("The table bill changed while it was being updated.");
    }
}
