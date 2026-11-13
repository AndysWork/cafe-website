using Cafe.Api.Models;

namespace Cafe.Api.Helpers;

public sealed record OrderPeriodCollectionResult(
    decimal Upi, decimal Cash, decimal Other, decimal Unallocated,
    decimal Total, int UnknownAmountOrders, Dictionary<string, string[]> SessionMethods);

public static class OrderPeriodCollection
{
    public static OrderPeriodCollectionResult Calculate(
        IEnumerable<Order> orders, IReadOnlyDictionary<string, DineInSession> sessions)
    {
        decimal upi = 0, cash = 0, other = 0, unallocated = 0;
        var unknownAmountOrders = 0;
        var selectedSessions = new Dictionary<string, DineInSession>();
        var sessionMethods = new Dictionary<string, string[]>();

        void AddPayment(string? method, decimal amount)
        {
            switch (method?.Trim().ToLowerInvariant())
            {
                case "upi":
                case "upi-qr": upi += amount; break;
                case "cash":
                case "cash_at_counter":
                case "cod": cash += amount; break;
                default: other += amount; break;
            }
        }

        foreach (var order in orders)
        {
            if (!string.IsNullOrWhiteSpace(order.DineInSessionId))
            {
                var sessionId = order.DineInSessionId;
                var visited = new HashSet<string>();
                while (true)
                {
                    if (!visited.Add(sessionId) || !sessions.TryGetValue(sessionId, out var session)
                        || !string.Equals(session.OutletId, order.OutletId, StringComparison.Ordinal))
                        throw new InvalidOperationException("Unable to resolve an order's dine-in session.");
                    if (string.IsNullOrWhiteSpace(session.MergedIntoSessionId))
                    {
                        selectedSessions[sessionId] = session;
                        var methods = (session.PaymentEntries ?? new List<DineInPaymentEntry>())
                            .Where(entry => string.Equals(entry.Status, "confirmed", StringComparison.OrdinalIgnoreCase))
                            .Select(entry => entry.PaymentMethod).Where(method => !string.IsNullOrWhiteSpace(method))
                            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                        sessionMethods[order.DineInSessionId] = methods;
                        break;
                    }
                    sessionId = session.MergedIntoSessionId;
                }
            }
            else if (string.Equals(order.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(order.PaymentMethod, "dine_in_tab", StringComparison.OrdinalIgnoreCase))
            {
                AddPayment(order.PaymentMethod, order.Total);
            }
            else if (string.Equals(order.PaymentStatus, "partially_paid", StringComparison.OrdinalIgnoreCase)
                || string.Equals(order.PaymentStatus, "refunded", StringComparison.OrdinalIgnoreCase)
                || (string.Equals(order.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(order.PaymentMethod, "dine_in_tab", StringComparison.OrdinalIgnoreCase)))
            {
                unknownAmountOrders++;
            }
        }

        var seenEntries = new HashSet<string>();
        foreach (var session in selectedSessions.Values)
        {
            decimal confirmedTotal = 0;
            foreach (var entry in session.PaymentEntries ?? new List<DineInPaymentEntry>())
            {
                if (!string.Equals(entry.Status, "confirmed", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.IsNullOrWhiteSpace(entry.Id) || !seenEntries.Add(entry.Id))
                    throw new InvalidOperationException("Duplicate or missing dine-in payment entry ID.");
                confirmedTotal += entry.Amount;
                AddPayment(entry.PaymentMethod, entry.Amount);
            }
            unallocated += Math.Max(0, session.AmountPaid - confirmedTotal);
        }
        return new OrderPeriodCollectionResult(upi, cash, other, unallocated,
            upi + cash + other + unallocated, unknownAmountOrders, sessionMethods);
    }
}
